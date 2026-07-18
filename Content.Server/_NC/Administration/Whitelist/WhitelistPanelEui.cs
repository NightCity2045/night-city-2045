using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._NC.Administration.Whitelist;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared.Players;
using Robust.Server.Player;
using Robust.Shared.Network;

namespace Content.Server._NC.Administration.Whitelist;

/// <summary>
/// Server-authoritative administration interface for the global server whitelist.
/// </summary>
public sealed class WhitelistPanelEui : BaseEui
{
    private const int PageSize = 50;
    private const int MaxInputLength = 128;

    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playtime = default!;
    [Dependency] private readonly ILogManager _log = default!;

    private readonly List<WhitelistPanelEntry> _entries = new();
    private readonly ISawmill _sawmill;
    private string _search = string.Empty;
    private int _page;
    private int _total;
    private int _loadRevision;
    private bool _operationInProgress;
    private WhitelistPanelStatus _status;
    private string _statusTarget = string.Empty;

    public WhitelistPanelEui()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill("admin.nc_whitelist_panel");
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;

        if (!CanUsePanel())
            return;

        LoadPage();
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
        _loadRevision++;
    }

    public override EuiStateBase GetNewState()
    {
        return new WhitelistPanelEuiState(
            new List<WhitelistPanelEntry>(_entries),
            _search,
            _page,
            PageSize,
            _total,
            _status,
            _statusTarget);
    }

    public override void HandleMessage(EuiMessageBase message)
    {
        base.HandleMessage(message);

        if (!CanUsePanel())
            return;

        switch (message)
        {
            case WhitelistPanelSearchMessage search:
                if (search.Search.Length > MaxInputLength)
                {
                    SetStatus(WhitelistPanelStatus.InvalidRequest);
                    return;
                }

                _search = search.Search.Trim();
                _page = 0;
                ClearStatus();
                LoadPage();
                break;

            case WhitelistPanelSetPageMessage page:
                var maxPage = Math.Max(0, (_total - 1) / PageSize);
                _page = Math.Clamp(page.Page, 0, maxPage);
                ClearStatus();
                LoadPage();
                break;

            case WhitelistPanelAddMessage add:
                AddPlayer(add.Player);
                break;

            case WhitelistPanelRemoveMessage remove:
                RemovePlayer(remove.UserId);
                break;

            case WhitelistPanelRefreshMessage:
                ClearStatus();
                LoadPage();
                break;
        }
    }

    private bool CanUsePanel()
    {
        if (_admins.HasAdminFlag(Player, AdminFlags.Whitelist))
            return true;

        _sawmill.Warning($"{Player.Name} ({Player.UserId}) tried to use the whitelist panel without the whitelist flag");
        Close();
        return false;
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player)
            CanUsePanel();
    }

    private async void LoadPage()
    {
        var revision = ++_loadRevision;

        try
        {
            var result = await _db.GetWhitelistEntriesAsync(_search, _page * PageSize, PageSize);
            if (revision != _loadRevision)
                return;

            _total = result.Total;
            var maxPage = Math.Max(0, (_total - 1) / PageSize);
            if (_page > maxPage)
            {
                _page = maxPage;
                LoadPage();
                return;
            }

            _entries.Clear();
            foreach (var entry in result.Entries)
            {
                // Accounts that never joined locally have no stored username, so expose their GUID as a stable fallback.
                _entries.Add(new WhitelistPanelEntry(
                    entry.UserId,
                    entry.LastSeenUserName ?? entry.UserId.ToString()));
            }

            StateDirty();
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Failed to load whitelist panel entries: {exception}");
            SetStatus(WhitelistPanelStatus.InvalidRequest);
        }
    }

    private async void AddPlayer(string player)
    {
        var target = player.Trim();
        if (_operationInProgress || target.Length == 0 || target.Length > MaxInputLength)
        {
            SetStatus(WhitelistPanelStatus.InvalidRequest);
            return;
        }

        _operationInProgress = true;
        try
        {
            var located = await _locator.LookupIdByNameOrIdAsync(target);
            if (located is null)
            {
                SetStatus(WhitelistPanelStatus.PlayerNotFound, target);
                return;
            }

            if (await _db.GetWhitelistStatusAsync(located.UserId))
            {
                SetStatus(WhitelistPanelStatus.AlreadyWhitelisted, located.Username);
                return;
            }

            await _db.AddToWhitelistAsync(located.UserId);
            UpdateOnlineWhitelistStatus(located.UserId, true);
            _sawmill.Info($"{Player.Name} ({Player.UserId}) added {located.Username} ({located.UserId}) to the server whitelist");

            SetStatus(WhitelistPanelStatus.Added, located.Username);
            LoadPage();
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Failed to add '{target}' through the whitelist panel: {exception}");
            SetStatus(WhitelistPanelStatus.InvalidRequest, target);
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private async void RemovePlayer(NetUserId userId)
    {
        if (_operationInProgress)
            return;

        _operationInProgress = true;
        try
        {
            if (!await _db.GetWhitelistStatusAsync(userId))
            {
                SetStatus(WhitelistPanelStatus.NotWhitelisted, userId.ToString());
                LoadPage();
                return;
            }

            var located = await _locator.LookupIdAsync(userId);
            var displayName = located?.Username ?? userId.ToString();

            await _db.RemoveFromWhitelistAsync(userId);
            UpdateOnlineWhitelistStatus(userId, false);
            _sawmill.Info($"{Player.Name} ({Player.UserId}) removed {displayName} ({userId}) from the server whitelist");

            SetStatus(WhitelistPanelStatus.Removed, displayName);
            LoadPage();
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Failed to remove '{userId}' through the whitelist panel: {exception}");
            SetStatus(WhitelistPanelStatus.InvalidRequest, userId.ToString());
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private void UpdateOnlineWhitelistStatus(NetUserId userId, bool whitelisted)
    {
        if (!_players.TryGetSessionById(userId, out var session) ||
            !_players.TryGetPlayerDataByUsername(session.Name, out var playerData))
            return;

        // Keep the live session cache synchronized with the database just like the legacy console commands do.
        playerData.ContentData()!.Whitelisted = whitelisted;
        _playtime.QueueSendWhitelist(session);
    }

    private void SetStatus(WhitelistPanelStatus status, string target = "")
    {
        _status = status;
        _statusTarget = target;
        StateDirty();
    }

    private void ClearStatus()
    {
        _status = WhitelistPanelStatus.None;
        _statusTarget = string.Empty;
    }
}
