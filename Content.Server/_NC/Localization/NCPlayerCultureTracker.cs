using Content.Shared._NC.Chat.Translation;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._NC.Localization;

/// <summary>
///     Reads per-player language and chat translation preferences from replicated client CVars.
/// </summary>
public sealed class NCPlayerCultureTracker : EntitySystem
{
    [Dependency] private readonly INetConfigurationManager _netConfig = default!;

    public string? GetCulture(ICommonSession session)
    {
        return _netConfig.GetClientCVar(session.Channel, CVars.LocCultureName);
    }

    public string? GetCulture(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor)
            ? GetCulture(actor.PlayerSession)
            : null;
    }

    public string? ResolveLanguageCode(ICommonSession session)
    {
        return ResolveLanguageCodeFromCulture(GetCulture(session));
    }

    public string? ResolveLanguageCode(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor)
            ? ResolveLanguageCode(actor.PlayerSession)
            : null;
    }

    public bool IsChatTranslationEnabled(ICommonSession session)
    {
        return _netConfig.GetClientCVar(session.Channel, Content.Shared.CCVar.CCVars.NCChatTranslationPreferenceEnabled);
    }

    public string? ResolveChatLanguageCode(ICommonSession session)
    {
        var preferredLanguage = _netConfig.GetClientCVar(session.Channel, Content.Shared.CCVar.CCVars.NCChatTranslationPreferenceLanguage);
        return NCChatTranslationMarkup.NormalizeLanguageCode(preferredLanguage) ?? ResolveLanguageCode(session);
    }

    public bool TryResolveChatLanguageCode(ICommonSession session, out string? languageCode)
    {
        languageCode = null;

        if (!IsChatTranslationEnabled(session))
            return false;

        languageCode = ResolveChatLanguageCode(session);
        return languageCode != null;
    }

    private static string? ResolveLanguageCodeFromCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return null;

        if (cultureName.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return NCChatTranslationMarkup.RussianLanguageCode;

        if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return NCChatTranslationMarkup.EnglishLanguageCode;

        return null;
    }
}
