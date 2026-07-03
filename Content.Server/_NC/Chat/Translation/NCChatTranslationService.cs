using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._NC.Chat.Translation;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server._NC.Chat.Translation;

public interface INCChatTranslationService
{
    bool IsConfiguredForChannel(ChatChannel channel);

    bool IsConfiguredForAHelp();

    uint AllocateMessageId();

    Task<NCChatTranslationDispatch> TranslateWithSoftHoldAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default);

    Task<NCChatTranslationPayload?> TranslateAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default);

    Task<NCChatTranslationPayload?> TranslateAHelpAsync(
        string text,
        string? fallbackLanguage,
        CancellationToken cancel = default);
}

public sealed record NCChatTranslationDispatch(
    NCChatTranslationPayload? ImmediateTranslation,
    Task<NCChatTranslationPayload?>? PendingTranslation);

public sealed record NCChatTranslationPayload(
    string OriginalText,
    string SourceLanguage,
    IReadOnlyDictionary<string, string> Translations)
{
    public static NCChatTranslationPayload CreatePlaceholder(string originalText, string sourceLanguage)
    {
        var normalizedSource = NCChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage)
            ?? throw new ArgumentException($"Unsupported source language '{sourceLanguage}'.", nameof(sourceLanguage));

        return new NCChatTranslationPayload(
            originalText,
            normalizedSource,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public string GetVisibleText(string? targetLanguage)
    {
        var normalized = NCChatTranslationMarkup.NormalizeLanguageCode(targetLanguage);
        if (normalized == null || normalized == SourceLanguage)
            return OriginalText;

        return Translations.TryGetValue(normalized, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated
            : OriginalText;
    }
}

public sealed class NCChatTranslationService : INCChatTranslationService
{
    private const string ServiceAutoDetectSourceCacheKey = "AUTO";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IConfigurationManager _config;
    private readonly HttpClient _http;
    private readonly ISawmill _sawmill;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    private DateTimeOffset _failureBackoffUntil;
    private int _nextMessageId;

    public NCChatTranslationService(
        IConfigurationManager config,
        IHttpClientHolder http,
        ILogManager logManager)
    {
        _config = config;
        _http = http.Client;
        _sawmill = logManager.GetSawmill("nc.chat.translation");
    }

    public bool IsConfiguredForChannel(ChatChannel channel)
    {
        return IsTranslationConfigured() && IsChannelEnabled(channel switch
        {
            ChatChannel.Local => "local",
            ChatChannel.Whisper => "whisper",
            ChatChannel.Radio => "radio",
            ChatChannel.LOOC => "looc",
            ChatChannel.Dead => "dead",
            ChatChannel.OOC => "ooc",
            _ => null,
        });
    }

    public bool IsConfiguredForAHelp()
    {
        return IsTranslationConfigured() && IsChannelEnabled("ahelp");
    }

    public uint AllocateMessageId()
    {
        return unchecked((uint) Interlocked.Increment(ref _nextMessageId));
    }

    public async Task<NCChatTranslationDispatch> TranslateWithSoftHoldAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default)
    {
        try
        {
            var translationTask = TranslateAsync(text, fallbackLanguage, channel, cancel);
            var softHoldMs = Math.Max(0, _config.GetCVar(CCVars.NCChatTranslationSoftHoldMs));
            if (softHoldMs <= 0)
                return new NCChatTranslationDispatch(null, translationTask);

            if (await Task.WhenAny(translationTask, Task.Delay(softHoldMs, CancellationToken.None)) == translationTask)
                return new NCChatTranslationDispatch(await translationTask, null);

            return new NCChatTranslationDispatch(null, translationTask);
        }
        catch (Exception e)
        {
            RegisterFailure($"Translation soft-hold failed for {channel}: {FlattenExceptionMessage(e)}");
            return new NCChatTranslationDispatch(null, null);
        }
    }

    public Task<NCChatTranslationPayload?> TranslateAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default)
    {
        return TranslateSafeAsync(text, fallbackLanguage, IsConfiguredForChannel(channel), channel.ToString(), cancel);
    }

    public Task<NCChatTranslationPayload?> TranslateAHelpAsync(
        string text,
        string? fallbackLanguage,
        CancellationToken cancel = default)
    {
        return TranslateSafeAsync(text, fallbackLanguage, IsConfiguredForAHelp(), "AHelp", cancel);
    }

    private async Task<NCChatTranslationPayload?> TranslateSafeAsync(
        string text,
        string? fallbackLanguage,
        bool enabled,
        string channelName,
        CancellationToken cancel)
    {
        try
        {
            return await TranslateCoreAsync(text, fallbackLanguage, enabled, channelName, cancel);
        }
        catch (Exception e) when (!cancel.IsCancellationRequested)
        {
            RegisterFailure($"Translation task failed for {channelName}: {FlattenExceptionMessage(e)}");
            return null;
        }
    }

    private bool IsChannelEnabled(string? channelKey)
    {
        if (string.IsNullOrWhiteSpace(channelKey))
            return false;

        var configuredChannels = _config.GetCVar(CCVars.NCChatTranslationChannels);
        if (string.IsNullOrWhiteSpace(configuredChannels))
            return false;

        var tokens = configuredChannels.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (token == "*" ||
                token.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                token.Equals(channelKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<NCChatTranslationPayload?> TranslateCoreAsync(
        string text,
        string? fallbackLanguage,
        bool enabled,
        string channelName,
        CancellationToken cancel)
    {
        if (!enabled || DateTimeOffset.UtcNow < _failureBackoffUntil || string.IsNullOrWhiteSpace(text))
            return null;

        if (text.Length > _config.GetCVar(CCVars.NCChatTranslationMaxMessageLength))
            return null;

        var normalizedText = NCChatTranslationMarkup.NormalizeTranslationText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            return null;

        var provider = GetProvider();
        string? sourceLanguage = null;
        string cacheSourceLanguage;
        IReadOnlyList<string> targetLanguages;

        if (provider == NCChatTranslationProviderSettings.ServiceProvider)
        {
            cacheSourceLanguage = ServiceAutoDetectSourceCacheKey;
            targetLanguages =
            [
                NCChatTranslationMarkup.RussianLanguageCode,
                NCChatTranslationMarkup.EnglishLanguageCode
            ];
        }
        else
        {
            sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(text, fallbackLanguage);
            if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
                return null;

            cacheSourceLanguage = sourceLanguage!;
            targetLanguages = sourceLanguage == NCChatTranslationMarkup.RussianLanguageCode
                ? [NCChatTranslationMarkup.EnglishLanguageCode]
                : [NCChatTranslationMarkup.RussianLanguageCode];
        }

        var cacheKey = BuildCacheKey(BuildProviderCacheSegment(provider, cacheSourceLanguage, targetLanguages), cacheSourceLanguage, normalizedText);
        if (TryGetCachedTranslation(cacheKey, out var cached))
            return cached;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(_config.GetCVar(CCVars.NCChatTranslationTimeoutMs)));

        try
        {
            var result = provider switch
            {
                NCChatTranslationProviderSettings.DeepLProvider => await TranslateWithDeepLAsync(
                    normalizedText,
                    sourceLanguage!,
                    targetLanguages,
                    channelName,
                    linkedCts.Token),
                _ => await TranslateWithServiceAsync(
                    normalizedText,
                    sourceLanguage,
                    targetLanguages,
                    channelName,
                    _config.GetCVar(CCVars.NCChatTranslationServiceUrl).TrimEnd('/'),
                    linkedCts.Token),
            };

            if (result == null)
                return null;

            StoreCachedTranslation(cacheKey, result);
            return result;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            RegisterFailure($"Translation timed out for {channelName}.");
            return null;
        }
        catch (Exception e)
        {
            RegisterFailure($"Translation request failed for {channelName}: {e.Message}");
            return null;
        }
    }

    private async Task<NCChatTranslationPayload?> TranslateWithServiceAsync(
        string normalizedText,
        string? sourceLanguage,
        IReadOnlyList<string> targetLanguages,
        string channelName,
        string baseUrl,
        CancellationToken cancel)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/translate")
        {
            Content = JsonContent.Create(new TranslateRequest(normalizedText, sourceLanguage, targetLanguages, channelName), options: JsonOptions)
        };

        var apiKey = _config.GetCVar(CCVars.NCChatTranslationApiKey);
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpRequest.Headers.Add("X-Api-Key", apiKey);

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(httpRequest, cancel);
        if (!response.IsSuccessStatusCode)
        {
            RegisterFailure($"Translation service returned {(int) response.StatusCode} for {channelName}.");
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<TranslateResponse>(JsonOptions, cancel);
        if (payload == null)
        {
            RegisterFailure("Translation service returned an empty payload.");
            return null;
        }

        var normalizedSource = NCChatTranslationMarkup.NormalizeLanguageCode(payload.SourceLanguage)
            ?? NCChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage);
        if (normalizedSource == null)
            return null;

        var normalizedOriginal = NCChatTranslationMarkup.NormalizeTranslationText(payload.OriginalText ?? normalizedText);
        if (string.IsNullOrWhiteSpace(normalizedOriginal))
            normalizedOriginal = normalizedText;

        var translations = NormalizeTranslations(payload.Translations);
        return translations.Count == 0
            ? null
            : new NCChatTranslationPayload(normalizedOriginal, normalizedSource, translations);
    }

    private async Task<NCChatTranslationPayload?> TranslateWithDeepLAsync(
        string normalizedText,
        string sourceLanguage,
        IReadOnlyList<string> targetLanguages,
        string channelName,
        CancellationToken cancel)
    {
        var authKey = _config.GetCVar(CCVars.NCChatTranslationDeepLAuthKey).Trim();
        if (string.IsNullOrWhiteSpace(authKey))
            return null;

        var baseUrl = NCChatTranslationProviderSettings.ResolveDeepLBaseUrl(
            _config.GetCVar(CCVars.NCChatTranslationDeepLBaseUrl),
            authKey);
        var modelType = NCChatTranslationProviderSettings.NormalizeDeepLModelType(
            _config.GetCVar(CCVars.NCChatTranslationDeepLModelType));
        var splitSentences = NCChatTranslationProviderSettings.NormalizeDeepLSplitSentences(
            _config.GetCVar(CCVars.NCChatTranslationDeepLSplitSentences));
        var context = _config.GetCVar(CCVars.NCChatTranslationDeepLContext).Trim();
        var preserveFormatting = _config.GetCVar(CCVars.NCChatTranslationDeepLPreserveFormatting);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var targetLanguage in targetLanguages)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/translate")
            {
                Content = JsonContent.Create(new DeepLTranslateRequest
                {
                    Text = [normalizedText],
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    ModelType = modelType,
                    PreserveFormatting = preserveFormatting,
                    SplitSentences = splitSentences,
                    Context = string.IsNullOrWhiteSpace(context) ? null : context,
                    GlossaryId = NCChatTranslationProviderSettings.ResolveDeepLGlossaryId(
                        sourceLanguage,
                        targetLanguage,
                        _config.GetCVar(CCVars.NCChatTranslationDeepLGlossaryRuToEn),
                        _config.GetCVar(CCVars.NCChatTranslationDeepLGlossaryEnToRu)),
                }, options: JsonOptions)
            };

            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {authKey}");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(httpRequest, cancel);
            if (!response.IsSuccessStatusCode)
            {
                RegisterFailure($"DeepL returned {(int) response.StatusCode} for {channelName}.");
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<DeepLTranslateResponse>(JsonOptions, cancel);
            var translatedText = payload?.Translations?.FirstOrDefault()?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                RegisterFailure($"DeepL returned an empty translation for {channelName}.");
                return null;
            }

            translations[targetLanguage] = translatedText;
        }

        return translations.Count == 0
            ? null
            : new NCChatTranslationPayload(normalizedText, sourceLanguage, translations);
    }

    private bool IsTranslationConfigured()
    {
        if (!_config.GetCVar(CCVars.NCChatTranslationEnabled))
            return false;

        return IsProviderConfigured(GetProvider());
    }

    private bool IsProviderConfigured(string provider)
    {
        return provider switch
        {
            NCChatTranslationProviderSettings.DeepLProvider =>
                !string.IsNullOrWhiteSpace(_config.GetCVar(CCVars.NCChatTranslationDeepLAuthKey)),
            _ => !string.IsNullOrWhiteSpace(_config.GetCVar(CCVars.NCChatTranslationServiceUrl)),
        };
    }

    private string GetProvider()
    {
        return NCChatTranslationProviderSettings.NormalizeProvider(_config.GetCVar(CCVars.NCChatTranslationProvider));
    }

    private bool TryGetCachedTranslation(string cacheKey, out NCChatTranslationPayload? payload)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(cacheKey, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _cache.Remove(cacheKey);
                payload = null;
                return false;
            }

            payload = entry.Payload;
            return true;
        }
    }

    private void StoreCachedTranslation(string cacheKey, NCChatTranslationPayload payload)
    {
        var ttl = Math.Max(1, _config.GetCVar(CCVars.NCChatTranslationCacheTtlSeconds));
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttl);

        lock (_cacheLock)
        {
            _cache[cacheKey] = new CacheEntry(payload, expiresAt, DateTimeOffset.UtcNow);
            PruneCacheLocked();
        }
    }

    private void PruneCacheLocked()
    {
        var now = DateTimeOffset.UtcNow;
        var maxEntries = Math.Max(64, _config.GetCVar(CCVars.NCChatTranslationCacheMaxEntries));

        if (_cache.Count <= maxEntries)
            return;

        var expired = _cache.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray();
        foreach (var key in expired)
        {
            _cache.Remove(key);
        }

        if (_cache.Count <= maxEntries)
            return;

        foreach (var key in _cache.OrderBy(pair => pair.Value.StoredAt).Take(_cache.Count - maxEntries).Select(pair => pair.Key).ToArray())
        {
            _cache.Remove(key);
        }
    }

    private void RegisterFailure(string reason)
    {
        var backoffSeconds = Math.Max(1, _config.GetCVar(CCVars.NCChatTranslationFailureBackoffSeconds));
        _failureBackoffUntil = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
        _sawmill.Warning(reason);
    }

    private static string FlattenExceptionMessage(Exception exception)
    {
        if (exception is AggregateException aggregateException)
            return string.Join("; ", aggregateException.Flatten().InnerExceptions.Select(e => e.Message));

        return exception.Message;
    }

    private string BuildProviderCacheSegment(string provider, string sourceLanguage, IReadOnlyList<string> targetLanguages)
    {
        return provider switch
        {
            NCChatTranslationProviderSettings.DeepLProvider => $"{provider}|{sourceLanguage}|{string.Join(",", targetLanguages)}",
            _ => $"{provider}|{_config.GetCVar(CCVars.NCChatTranslationServiceUrl).Trim().TrimEnd('/')}",
        };
    }

    private static string BuildCacheKey(string providerSegment, string sourceLanguage, string normalizedText)
    {
        return $"{providerSegment}|{sourceLanguage}|{normalizedText}";
    }

    private static Dictionary<string, string> NormalizeTranslations(Dictionary<string, string>? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw == null)
            return result;

        foreach (var (key, value) in raw)
        {
            var normalizedKey = NCChatTranslationMarkup.NormalizeLanguageCode(key);
            if (normalizedKey == null || string.IsNullOrWhiteSpace(value))
                continue;

            result[normalizedKey] = value.Trim();
        }

        return result;
    }

    private sealed record CacheEntry(
        NCChatTranslationPayload Payload,
        DateTimeOffset ExpiresAt,
        DateTimeOffset StoredAt);

    private sealed record TranslateRequest(
        string Text,
        string? SourceLanguage,
        IReadOnlyList<string> TargetLanguages,
        string Channel);

    private sealed class TranslateResponse
    {
        public string? SourceLanguage { get; set; }
        public string? OriginalText { get; set; }
        public Dictionary<string, string>? Translations { get; set; }
    }

    private sealed class DeepLTranslateRequest
    {
        [JsonPropertyName("text")]
        public required string[] Text { get; init; }

        [JsonPropertyName("source_lang")]
        public required string SourceLanguage { get; init; }

        [JsonPropertyName("target_lang")]
        public required string TargetLanguage { get; init; }

        [JsonPropertyName("model_type")]
        public string? ModelType { get; init; }

        [JsonPropertyName("preserve_formatting")]
        public bool PreserveFormatting { get; init; }

        [JsonPropertyName("split_sentences")]
        public string? SplitSentences { get; init; }

        [JsonPropertyName("context")]
        public string? Context { get; init; }

        [JsonPropertyName("glossary_id")]
        public string? GlossaryId { get; init; }
    }

    private sealed class DeepLTranslateResponse
    {
        [JsonPropertyName("translations")]
        public List<DeepLTranslation>? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
