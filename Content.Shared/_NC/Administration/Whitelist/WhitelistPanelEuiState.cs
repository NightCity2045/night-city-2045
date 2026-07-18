using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Administration.Whitelist;

/// <summary>
/// One account shown in the server whitelist administration panel.
/// </summary>
[Serializable, NetSerializable]
public sealed class WhitelistPanelEntry
{
    public readonly NetUserId UserId;
    public readonly string Username;

    public WhitelistPanelEntry(NetUserId userId, string username)
    {
        UserId = userId;
        Username = username;
    }
}

/// <summary>
/// Result of the most recent whitelist mutation.
/// </summary>
public enum WhitelistPanelStatus : byte
{
    None,
    Added,
    Removed,
    AlreadyWhitelisted,
    NotWhitelisted,
    PlayerNotFound,
    InvalidRequest,
}

/// <summary>
/// Paginated state sent to whitelist administrators.
/// </summary>
[Serializable, NetSerializable]
public sealed class WhitelistPanelEuiState : EuiStateBase
{
    public readonly List<WhitelistPanelEntry> Entries;
    public readonly string Search;
    public readonly int Page;
    public readonly int PageSize;
    public readonly int Total;
    public readonly WhitelistPanelStatus Status;
    public readonly string StatusTarget;

    public WhitelistPanelEuiState(
        List<WhitelistPanelEntry> entries,
        string search,
        int page,
        int pageSize,
        int total,
        WhitelistPanelStatus status,
        string statusTarget)
    {
        Entries = entries;
        Search = search;
        Page = page;
        PageSize = pageSize;
        Total = total;
        Status = status;
        StatusTarget = statusTarget;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelSearchMessage : EuiMessageBase
{
    public readonly string Search;

    public WhitelistPanelSearchMessage(string search)
    {
        Search = search;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelSetPageMessage : EuiMessageBase
{
    public readonly int Page;

    public WhitelistPanelSetPageMessage(int page)
    {
        Page = page;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelAddMessage : EuiMessageBase
{
    public readonly string Player;

    public WhitelistPanelAddMessage(string player)
    {
        Player = player;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelRemoveMessage : EuiMessageBase
{
    public readonly NetUserId UserId;

    public WhitelistPanelRemoveMessage(NetUserId userId)
    {
        UserId = userId;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelRefreshMessage : EuiMessageBase
{
}
