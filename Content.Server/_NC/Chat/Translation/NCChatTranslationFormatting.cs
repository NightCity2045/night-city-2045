using Content.Shared._NC.Chat.Translation;
using Robust.Shared.Utility;

namespace Content.Server._NC.Chat.Translation;

public static class NCChatTranslationFormatting
{
    public static bool ShouldPreserveOriginalText(string? senderLanguage, string? recipientLanguage)
    {
        var normalizedSender = NCChatTranslationMarkup.NormalizeLanguageCode(senderLanguage);
        var normalizedRecipient = NCChatTranslationMarkup.NormalizeLanguageCode(recipientLanguage);
        return normalizedSender != null &&
               normalizedRecipient != null &&
               normalizedSender == normalizedRecipient;
    }

    public static string ResolveVisibleText(
        NCChatTranslationPayload translation,
        string originalMessage,
        string? senderLanguage,
        string? recipientLanguage,
        bool preserveOriginal = false)
    {
        return preserveOriginal || ShouldPreserveOriginalText(senderLanguage, recipientLanguage)
            ? originalMessage
            : translation.GetVisibleText(recipientLanguage);
    }

    public static string ResolveOriginalTextForTag(
        NCChatTranslationPayload translation,
        string originalMessage,
        string? senderLanguage,
        string? recipientLanguage,
        bool preserveOriginal = false)
    {
        return preserveOriginal || ShouldPreserveOriginalText(senderLanguage, recipientLanguage)
            ? string.Empty
            : translation.OriginalText;
    }

    public static string PrefixWithLanguageTag(string wrappedMessage, string? recipientLanguage, string sourceLanguage, string originalText)
    {
        var normalizedSource = NCChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage);
        var normalizedRecipient = NCChatTranslationMarkup.NormalizeLanguageCode(recipientLanguage);

        if (normalizedSource == null ||
            normalizedRecipient == normalizedSource ||
            string.IsNullOrWhiteSpace(originalText))
        {
            return wrappedMessage;
        }

        return $"{NCChatTranslationMarkup.BuildTagMarkup(sourceLanguage, originalText)} {wrappedMessage}";
    }

    public static string BuildPlainWrappedMessage(string senderMarkup, string visibleText, string? statusPrefix = null)
    {
        var escapedMessage = FormattedMessage.EscapeText(visibleText);
        return string.IsNullOrWhiteSpace(statusPrefix)
            ? $"{senderMarkup}: {escapedMessage}"
            : $"{statusPrefix} {senderMarkup}: {escapedMessage}";
    }
}
