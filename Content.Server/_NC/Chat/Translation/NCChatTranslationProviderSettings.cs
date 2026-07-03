using Content.Shared._NC.Chat.Translation;

namespace Content.Server._NC.Chat.Translation;

public static class NCChatTranslationProviderSettings
{
    public const string ServiceProvider = "service";
    public const string DeepLProvider = "deepl";
    public const string DeepLFreeBaseUrl = "https://api-free.deepl.com";
    public const string DeepLProBaseUrl = "https://api.deepl.com";
    public const string DeepLLatencyOptimizedModel = "latency_optimized";
    public const string DeepLQualityOptimizedModel = "quality_optimized";
    public const string DeepLPreferQualityOptimizedModel = "prefer_quality_optimized";

    public static string NormalizeProvider(string? provider)
    {
        return provider?.Trim().ToLowerInvariant() switch
        {
            DeepLProvider => DeepLProvider,
            ServiceProvider => ServiceProvider,
            _ => ServiceProvider,
        };
    }

    public static string NormalizeDeepLModelType(string? modelType)
    {
        return modelType?.Trim().ToLowerInvariant() switch
        {
            DeepLQualityOptimizedModel => DeepLQualityOptimizedModel,
            DeepLPreferQualityOptimizedModel => DeepLPreferQualityOptimizedModel,
            _ => DeepLLatencyOptimizedModel,
        };
    }

    public static string NormalizeDeepLSplitSentences(string? splitSentences)
    {
        return splitSentences?.Trim().ToLowerInvariant() switch
        {
            "1" => "1",
            "nonewlines" => "nonewlines",
            _ => "0",
        };
    }

    public static string ResolveDeepLBaseUrl(string? configuredBaseUrl, string? authKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            return configuredBaseUrl.Trim().TrimEnd('/');

        return !string.IsNullOrWhiteSpace(authKey) && authKey.Trim().EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? DeepLFreeBaseUrl
            : DeepLProBaseUrl;
    }

    public static string? ResolveDeepLGlossaryId(
        string sourceLanguage,
        string targetLanguage,
        string? ruToEnGlossaryId,
        string? enToRuGlossaryId)
    {
        var normalizedSource = NCChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage);
        var normalizedTarget = NCChatTranslationMarkup.NormalizeLanguageCode(targetLanguage);

        if (normalizedSource == null || normalizedTarget == null)
            return null;

        var glossaryId = (normalizedSource, normalizedTarget) switch
        {
            (NCChatTranslationMarkup.RussianLanguageCode, NCChatTranslationMarkup.EnglishLanguageCode) => ruToEnGlossaryId,
            (NCChatTranslationMarkup.EnglishLanguageCode, NCChatTranslationMarkup.RussianLanguageCode) => enToRuGlossaryId,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(glossaryId) ? null : glossaryId.Trim();
    }
}
