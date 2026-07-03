using System.Diagnostics.CodeAnalysis;
using Content.Shared._NC.Chat.Translation;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client._NC.Chat.Translation;

[UsedImplicitly]
public sealed class NCChatTranslationTag : IMarkupTag
{
    private static readonly Color RussianColor = Color.FromHex("#C98C8C");
    private static readonly Color EnglishColor = Color.FromHex("#7FAEDC");

    public string Name => NCChatTranslationMarkup.TagName;

    public bool TryGetControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!node.Attributes.TryGetValue("lang", out var langParam) ||
            !langParam.TryGetString(out var langRaw))
        {
            return false;
        }

        var language = NCChatTranslationMarkup.NormalizeLanguageCode(langRaw);
        if (language == null)
            return false;

        string? tooltip = null;
        if (node.Attributes.TryGetValue("original", out var originalParam) &&
            originalParam.TryGetString(out var encodedOriginal) &&
            NCChatTranslationMarkup.TryDecodeOriginalText(encodedOriginal, out var decodedOriginal) &&
            !string.IsNullOrWhiteSpace(decodedOriginal))
        {
            tooltip = decodedOriginal;
        }

        control = new Label
        {
            Text = $"[{language}]",
            ToolTip = tooltip,
            MouseFilter = Control.MouseFilterMode.Stop,
            FontColorOverride = language == NCChatTranslationMarkup.RussianLanguageCode ? RussianColor : EnglishColor,
            Margin = new Thickness(0f, 0f, 6f, 0f)
        };

        return true;
    }
}
