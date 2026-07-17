using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client.Stylesheets;

/// <summary>
/// Night City specific style class names for new cyberpunk UI.
/// </summary>
public static class StyleNightCity
{
    public const string Window = "NightCityWindow";
    public const string Panel = "NightCityPanel";
    public const string PanelDark = "NightCityPanelDark";
    public const string PanelInset = "NightCityPanelInset";
    public const string TerminalPanel = "NightCityTerminalPanel";
    public const string Button = "NightCityButton";
    public const string ButtonActive = "NightCityButtonActive";
    public const string ButtonDanger = "NightCityButtonDanger";
    public const string Input = "NightCityInput";
    public const string GlowText = "NightCityGlowText";
    public const string MutedText = "NightCityMutedText";
    public const string StatusGood = "NightCityStatusGood";
    public const string StatusWarning = "NightCityStatusWarning";
    public const string StatusDanger = "NightCityStatusDanger";
}

/// <summary>
/// Runtime stylesheet implementation for Night City interfaces.
/// </summary>
public sealed class NightCityStylesheet : StyleBase
{
    public override Stylesheet Stylesheet { get; }

    public NightCityStylesheet(IResourceCache resCache) : base(resCache)
    {
        var windowPanel = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#080b0d"),
            BorderColor = Color.FromHex("#00d7e8"),
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 10,
            ContentMarginBottomOverride = 10
        };

        var mainPanel = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#121719"),
            BorderColor = Color.FromHex("#26383b"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8
        };

        var darkPanel = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#090d0f"),
            BorderColor = Color.FromHex("#183d43"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 8,
            ContentMarginBottomOverride = 8
        };

        var insetPanel = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#050708"),
            BorderColor = Color.FromHex("#3b232c"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 6,
            ContentMarginRightOverride = 6,
            ContentMarginTopOverride = 6,
            ContentMarginBottomOverride = 6
        };

        var normalButton = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#14272b"),
            BorderColor = Color.FromHex("#0b8793"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 5,
            ContentMarginBottomOverride = 5
        };

        var normalButtonHover = new StyleBoxFlat(normalButton)
        {
            BackgroundColor = Color.FromHex("#1b3a40"),
            BorderColor = Color.FromHex("#22d7e6")
        };

        var normalButtonPressed = new StyleBoxFlat(normalButton)
        {
            BackgroundColor = Color.FromHex("#0c1b1f"),
            BorderColor = Color.FromHex("#ff2f66")
        };

        var activeButton = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#2a1d12"),
            BorderColor = Color.FromHex("#ffbf54"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 5,
            ContentMarginBottomOverride = 5
        };

        var dangerButton = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#4b1418"),
            BorderColor = Color.FromHex("#ff4b5f"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4
        };

        var dangerButtonHover = new StyleBoxFlat(dangerButton)
        {
            BackgroundColor = Color.FromHex("#642025")
        };

        var dangerButtonPressed = new StyleBoxFlat(dangerButton)
        {
            BackgroundColor = Color.FromHex("#381013")
        };

        var optionBackground = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#080c0e"),
            BorderColor = Color.FromHex("#00d7e8"),
            BorderThickness = new Thickness(1)
        };

        var inputBox = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#030607"),
            BorderColor = Color.FromHex("#00a8b8"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 5,
            ContentMarginBottomOverride = 5
        };

        Stylesheet = new Stylesheet(BaseRules.Concat(new StyleRule[]
        {
            Element<PanelContainer>().Class(StyleNightCity.TerminalPanel)
                .Prop(PanelContainer.StylePropertyPanel, windowPanel),

            Element<PanelContainer>().Class(StyleNightCity.Panel)
                .Prop(PanelContainer.StylePropertyPanel, mainPanel),

            Element<PanelContainer>().Class(StyleNightCity.PanelDark)
                .Prop(PanelContainer.StylePropertyPanel, darkPanel),

            Element<PanelContainer>().Class(StyleNightCity.PanelInset)
                .Prop(PanelContainer.StylePropertyPanel, insetPanel),

            Element<Label>().Class(StyleNightCity.GlowText)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#78f6ff")),

            Element<Label>().Class(StyleNightCity.MutedText)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#829295")),

            Element<Label>().Class(StyleNightCity.StatusGood)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#76ff8d")),

            Element<Label>().Class(StyleNightCity.StatusWarning)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#ffbf54")),

            Element<Label>().Class(StyleNightCity.StatusDanger)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#ff6678")),

            Element<Button>().Class(StyleNightCity.Button)
                .Prop(Button.StylePropertyStyleBox, normalButton)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#d7fdff")),

            Element<Button>().Class(StyleNightCity.Button).Pseudo(Button.StylePseudoClassHover)
                .Prop(Button.StylePropertyStyleBox, normalButtonHover),

            Element<Button>().Class(StyleNightCity.Button).Pseudo(Button.StylePseudoClassPressed)
                .Prop(Button.StylePropertyStyleBox, normalButtonPressed),

            Element<Button>().Class(StyleNightCity.ButtonActive)
                .Prop(Button.StylePropertyStyleBox, activeButton)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#ffe6a3")),

            Element<Button>().Class(StyleNightCity.ButtonActive).Pseudo(Button.StylePseudoClassHover)
                .Prop(Button.StylePropertyStyleBox, activeButton),

            Element<Button>().Class(StyleNightCity.ButtonDanger)
                .Prop(Button.StylePropertyStyleBox, dangerButton)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#ffe5e8")),

            Element<Button>().Class(StyleNightCity.ButtonDanger).Pseudo(Button.StylePseudoClassHover)
                .Prop(Button.StylePropertyStyleBox, dangerButtonHover),

            Element<Button>().Class(StyleNightCity.ButtonDanger).Pseudo(Button.StylePseudoClassPressed)
                .Prop(Button.StylePropertyStyleBox, dangerButtonPressed),

            Element<OptionButton>()
                .Prop(ContainerButton.StylePropertyStyleBox, normalButton),

            Element<OptionButton>().Pseudo(Button.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, normalButtonHover),

            Element<OptionButton>().Pseudo(Button.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, normalButtonPressed),

            Element<LineEdit>().Class(StyleNightCity.Input)
                .Prop(LineEdit.StylePropertyStyleBox, inputBox)
                .Prop("font-color", Color.FromHex("#d7fdff")),

            Element<LineEdit>().Class(StyleNightCity.Input).Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", Color.FromHex("#52666a")),

            Element<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground)
                .Prop(PanelContainer.StylePropertyPanel, optionBackground),
        }).ToList());
    }
}
