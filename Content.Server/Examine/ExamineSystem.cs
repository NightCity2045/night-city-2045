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

            var text = BuildLocalizedExamineResponse(player, entity, playerEnt, request.GetVerbs, out var verbs);
            RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                request.NetEntity, request.Id, text, verbs?.ToList()), channel);
        }

        private FormattedMessage BuildLocalizedExamineResponse(
            ICommonSession session,
            EntityUid entity,
            EntityUid player,
            bool getVerbs,
            out SortedSet<Verb>? verbs)
        {
            var previousCulture = _localization.DefaultCulture;
            var cultureName = _playerCulture.GetCulture(session);

            if (!string.IsNullOrWhiteSpace(cultureName))
            {
                try
                {
                    // Full examine text is built on the server, so use the requesting client's locale.
                    _localization.SetCulture(CultureInfo.GetCultureInfo(cultureName, predefinedOnly: false));
                }
                catch (CultureNotFoundException)
                {
                    // Keep server default culture if the client somehow reports an invalid culture.
                }
            }

            try
            {
                verbs = getVerbs
                    ? _verbSystem.GetLocalVerbs(entity, player, typeof(ExamineVerb))
                    : null;

                return GetExamineText(entity, player);
            }
            finally
            {
                if (previousCulture != null)
                    _localization.SetCulture(previousCulture);
            }
        }
    }
}
