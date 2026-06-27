using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables translated chat for this client when the server-side NC translation pipeline is active.
    /// </summary>
    public static readonly CVarDef<bool> NCChatTranslationPreferenceEnabled =
        CVarDef.Create("nc.chat_translation.preference.enabled", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Preferred incoming chat translation language for this client.
    ///     Empty value means "follow the game language".
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationPreferenceLanguage =
        CVarDef.Create("nc.chat_translation.preference.language", string.Empty, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Enables the NC automatic RU/EN chat translation pipeline.
    /// </summary>
    public static readonly CVarDef<bool> NCChatTranslationEnabled =
        CVarDef.Create("nc.chat_translation.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Translation backend provider. Supported values: service, deepl.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationProvider =
        CVarDef.Create("nc.chat_translation.provider", "service", CVar.SERVERONLY);

    /// <summary>
    ///     Base URL of the external NC translation service when provider=service.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationServiceUrl =
        CVarDef.Create("nc.chat_translation.service_url", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional API key forwarded as X-Api-Key to the translation service when provider=service.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationApiKey =
        CVarDef.Create("nc.chat_translation.api_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL authentication key used when provider=deepl.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLAuthKey =
        CVarDef.Create("nc.chat_translation.deepl.auth_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL API base URL override.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLBaseUrl =
        CVarDef.Create("nc.chat_translation.deepl.base_url", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL model preference. Supported values: latency_optimized, quality_optimized, prefer_quality_optimized.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLModelType =
        CVarDef.Create("nc.chat_translation.deepl.model_type", "latency_optimized", CVar.SERVERONLY);

    /// <summary>
    ///     If true, asks DeepL to preserve the source formatting as much as possible.
    /// </summary>
    public static readonly CVarDef<bool> NCChatTranslationDeepLPreserveFormatting =
        CVarDef.Create("nc.chat_translation.deepl.preserve_formatting", true, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL sentence splitting mode. Supported values: 0, 1, nonewlines.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLSplitSentences =
        CVarDef.Create("nc.chat_translation.deepl.split_sentences", "0", CVar.SERVERONLY);

    /// <summary>
    ///     Optional unbilled context string forwarded to DeepL to improve short chat translations.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLContext =
        CVarDef.Create("nc.chat_translation.deepl.context", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL glossary id for RU -> EN requests.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLGlossaryRuToEn =
        CVarDef.Create("nc.chat_translation.deepl.glossary_id.ru_en", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL glossary id for EN -> RU requests.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationDeepLGlossaryEnToRu =
        CVarDef.Create("nc.chat_translation.deepl.glossary_id.en_ru", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum time spent waiting for a translation before falling back to original text.
    /// </summary>
    public static readonly CVarDef<int> NCChatTranslationTimeoutMs =
        CVarDef.Create("nc.chat_translation.timeout_ms", 1000, CVar.SERVERONLY);

    /// <summary>
    ///     Soft wait window before the original message is sent and translation continues in the background.
    /// </summary>
    public static readonly CVarDef<int> NCChatTranslationSoftHoldMs =
        CVarDef.Create("nc.chat_translation.soft_hold_ms", 100, CVar.SERVERONLY);

    /// <summary>
    ///     Backoff window after a translation failure to avoid stalling every chat line.
    /// </summary>
    public static readonly CVarDef<int> NCChatTranslationFailureBackoffSeconds =
        CVarDef.Create("nc.chat_translation.failure_backoff_seconds", 5, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum message length eligible for translation.
    /// </summary>
    public static readonly CVarDef<int> NCChatTranslationMaxMessageLength =
        CVarDef.Create("nc.chat_translation.max_message_length", 256, CVar.SERVERONLY);

    /// <summary>
    ///     Local translation cache entry lifetime in seconds.
    /// </summary>
    public static readonly CVarDef<int> NCChatTranslationCacheTtlSeconds =
        CVarDef.Create("nc.chat_translation.cache_ttl_seconds", 1800, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum amount of cached translation entries stored locally on the game server.
    /// </summary>
    public static readonly CVarDef<int> NCChatTranslationCacheMaxEntries =
        CVarDef.Create("nc.chat_translation.cache_max_entries", 4096, CVar.SERVERONLY);

    /// <summary>
    ///     Comma-separated chat channel ids translated by the NC server-side pipeline.
    ///     Supported values: local, whisper, radio, looc, dead, ooc, ahelp. Use '*' or 'all' to enable every listed channel.
    /// </summary>
    public static readonly CVarDef<string> NCChatTranslationChannels =
        CVarDef.Create("nc.chat_translation.channels", "local,whisper,radio,looc,dead,ooc,ahelp", CVar.SERVERONLY);
}
