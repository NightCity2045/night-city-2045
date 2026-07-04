using System.Globalization;
using System.Linq;
using Content.Server._NC.Localization;
using Content.Server.Verbs;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using JetBrains.Annotations;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Examine
{
    [UsedImplicitly]
    public sealed class ExamineSystem : ExamineSystemShared
    {
        [Dependency] private readonly ILocalizationManager _localization = default!;
        [Dependency] private readonly NCPlayerCultureTracker _playerCulture = default!;
        [Dependency] private readonly VerbSystem _verbSystem = default!;

        private readonly FormattedMessage _entityNotFoundMessage = new();
        private readonly FormattedMessage _entityOutOfRangeMessage = new();

        public override void Initialize()
        {
            base.Initialize();
            _entityNotFoundMessage.AddText(Loc.GetString("examine-system-entity-does-not-exist"));
            _entityOutOfRangeMessage.AddText(Loc.GetString("examine-system-cant-see-entity"));

            SubscribeNetworkEvent<ExamineSystemMessages.RequestExamineInfoMessage>(ExamineInfoRequest);
        }

        public override void SendExamineTooltip(EntityUid player, EntityUid target, FormattedMessage message, bool getVerbs, bool centerAtCursor)
        {
            if (!TryComp<ActorComponent>(player, out var actor))
                return;

            var session = actor.PlayerSession;

            SortedSet<Verb>? verbs = null;
            if (getVerbs)
                verbs = _verbSystem.GetLocalVerbs(target, player, typeof(ExamineVerb));

            var ev = new ExamineSystemMessages.ExamineInfoResponseMessage(
                GetNetEntity(target), 0, message, verbs?.ToList(), centerAtCursor
            );

            RaiseNetworkEvent(ev, session.Channel);
        }

        private void ExamineInfoRequest(ExamineSystemMessages.RequestExamineInfoMessage request, EntitySessionEventArgs eventArgs)
        {
            var player = eventArgs.SenderSession;
            var session = eventArgs.SenderSession;
            var channel = player.Channel;
            var entity = GetEntity(request.NetEntity);

            if (session.AttachedEntity is not {Valid: true} playerEnt
                || !EntityManager.EntityExists(entity))
            {
                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, _entityNotFoundMessage), channel);
                return;
            }

            if (!CanExamine(playerEnt, entity))
            {
                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, _entityOutOfRangeMessage, knowTarget: false), channel);
                return;
            }

            var previousCulture = ApplyClientCulture(player, request.CultureName);
            try
            {
                var verbs = request.GetVerbs
                    ? _verbSystem.GetLocalVerbs(entity, playerEnt, typeof(ExamineVerb))
                    : null;

                var text = GetExamineText(entity, player.AttachedEntity);

                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, text, verbs?.ToList()), channel);
            }
            finally
            {
                RestoreCulture(previousCulture);
            }
        }

        private CultureInfo? ApplyClientCulture(ICommonSession session, string? requestedCultureName)
        {
            var previousCulture = _localization.DefaultCulture;
            var cultureName = requestedCultureName ?? _playerCulture.GetCulture(session);

            if (string.IsNullOrWhiteSpace(cultureName))
                return previousCulture;

            try
            {
                // Full examine text and returned verbs are serialized on the server, so use the client's locale.
                _localization.SetCulture(CultureInfo.GetCultureInfo(cultureName, predefinedOnly: false));
                VerbCategory.RefreshStaticLocalizations();
            }
            catch (CultureNotFoundException)
            {
                // Keep server default culture if the client somehow reports an invalid culture.
            }

            return previousCulture;
        }

        private void RestoreCulture(CultureInfo? previousCulture)
        {
            if (previousCulture == null)
                return;

            _localization.SetCulture(previousCulture);
            VerbCategory.RefreshStaticLocalizations();
        }
    }
}
