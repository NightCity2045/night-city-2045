# StyleNightCity UI Rules for Space Station 14 / Robust Toolbox

This document defines the mandatory UI rules for creating new Night City / Cyberpunk RED interfaces in this Space Station 14 fork.

All AI tools, Codex agents, contributors, and generated code must follow this document when creating or editing UI.

## 1. Purpose

`StyleNightCity` is the custom UI style system for this project.

It is used for:

- cyberpunk terminals;
- police consoles;
- corporate panels;
- medical scanners;
- implant interfaces;
- HUD widgets;
- netrunner interfaces;
- Night City themed admin, player, and gameplay UI.

The goal is to create UI that looks like Night City / Cyberpunk RED 2045 while staying compatible with Robust Toolbox UI architecture.

## 2. Core Rule

Robust Toolbox UI is not WPF, UWP, Avalonia, HTML, CSS, Unity UI, or Godot UI.

XAML in Robust Toolbox is used only for:

- control hierarchy;
- layout structure;
- basic control properties;
- assigning names;
- assigning style classes.

XAML must not be used for complex visual effects or animations.

Visual styling belongs in `.sw` styles, textures, shaders, and custom C# drawing code.

Animations and dynamic behavior belong in C# code-behind.

## 3. Absolute Ban on StyleNano

`StyleNano` is legacy UI style code and must not be used for new Night City UI.

Never use:

```csharp
StyleNano
```

Never use:

```csharp
StyleNano.StyleClass...
```

Never add:

```csharp
using Content.Client.Stylesheets;
```

if the only reason is to access `StyleNano`.

Never use `StyleNano` as:

- a base style;
- a fallback;
- an example architecture;
- a source of style class names;
- a reference for new UI design;
- a copy-paste source.

If existing code already uses `StyleNano`, treat it as legacy code. Do not expand it.

For new UI, always use `StyleNightCity` and Night City style classes.

## 4. Required Style Class Naming

Use clear Night City style class names.

Recommended style classes:

```text
NightCityWindow
NightCityPanel
NightCityPanelDark
NightCityPanelInset
NightCityTerminalPanel
NightCityTerminalHeader
NightCityTerminalBody
NightCityTerminalFooter
NightCityButton
NightCityButtonDanger
NightCityButtonGhost
NightCityButtonSmall
NightCityInput
NightCityDivider
NightCityGlowText
NightCityMutedText
NightCityStatusGood
NightCityStatusWarning
NightCityStatusDanger
NightCityScanlineOverlay
NightCityDataRow
NightCityDataCell
```

Do not use Nano-themed names for new UI.

Bad:

```text
NanoButton
NanoPanel
StyleClassButtonBig
StyleNanoButton
```

Good:

```text
NightCityButton
NightCityTerminalPanel
NightCityStatusDanger
```

## 5. Forbidden XAML Concepts

Never generate or write Robust XAML using WPF/UWP-only concepts.

Forbidden:

```text
ControlTemplate
Storyboard
DoubleAnimation
DropShadowEffect
Border
Grid
StackPanel
DockPanel
ResourceDictionary
StaticResource
DynamicResource
DataTemplate
Trigger
VisualStateManager
Binding in WPF style
CornerRadius unless the target Robust control explicitly supports it
```

Use Robust UI controls instead.

Correct replacements:

| Forbidden | Use instead |
|---|---|
| `StackPanel` | `BoxContainer` |
| `Grid` | `GridContainer` |
| `Border` | `PanelContainer` with style class |
| `ControlTemplate` | custom control + `.sw` style + textures |
| `Storyboard` | `FrameUpdate` in C# |
| `DoubleAnimation` | `MathHelper.Lerp` in C# |
| `DropShadowEffect` | texture, shader, or custom `Draw` |
| CSS shadow | texture, shader, or custom `Draw` |

## 6. Allowed Common Robust UI Controls

Prefer safe, known Robust UI controls.

Allowed baseline controls:

```text
Control
PanelContainer
BoxContainer
GridContainer
Label
RichTextLabel
Button
LineEdit
TextureRect
ProgressBar
ScrollContainer
```

Use other controls only when they are known to exist in the project.

If unsure whether a control exists, do not invent it. Use a safe baseline control instead.

## 7. XAML Layout Rules

Use XAML for structure only.

### Vertical layout

```xml
<BoxContainer Orientation="Vertical">
</BoxContainer>
```

### Horizontal layout

```xml
<BoxContainer Orientation="Horizontal">
</BoxContainer>
```

### Panel background

```xml
<PanelContainer StyleClasses="NightCityPanel">
</PanelContainer>
```

### Terminal panel

```xml
<PanelContainer StyleClasses="NightCityTerminalPanel">
</PanelContainer>
```

### Button

```xml
<Button Text="CONNECT" StyleClasses="NightCityButton" />
```

### Dangerous button

```xml
<Button Text="PURGE DATA" StyleClasses="NightCityButtonDanger" />
```

### Expansion

Use expansion only inside containers that support it, such as `BoxContainer`, `SplitContainer`, and `GridContainer`.

```xml
HorizontalExpand="True"
VerticalExpand="True"
```

Do not place expansion properties randomly.

## 8. XAML Must Stay Clean

XAML should not contain heavy visual design.

Do not put the entire visual identity into XAML.

Bad:

```xml
<Button Text="CONNECT" Background="#00FFFF" BorderBrush="#FF00FF" />
```

Good:

```xml
<Button Text="CONNECT" StyleClasses="NightCityButton" />
```

The style class decides how the button looks.

## 9. Code-Behind Rules

Each complex XAML UI should have a matching code-behind file.

Use:

```text
[Name].xaml
[Name].xaml.cs
```

The code-behind class must be `partial`.

If XAML names are used from C#, add:

```csharp
[GenerateTypedNameReferences]
```

The constructor must load XAML:

```csharp
RobustXamlLoader.Load(this);
```

Example:

```csharp
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Maths;

namespace Content.Client.NightCity.UI;

[GenerateTypedNameReferences]
public sealed partial class NightCityTerminalWindow : DefaultWindow
{
    private float _fade;
    private float _targetFade = 1f;

    public NightCityTerminalWindow()
    {
        RobustXamlLoader.Load(this);

        // Connect UI events after XAML has been loaded.
        ConnectButton.OnPressed += OnConnectPressed;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Smoothly fade the window in.
        _fade = MathHelper.Lerp(_fade, _targetFade, args.DeltaSeconds * 8f);
        ModulateSelfOverride = Color.White.WithAlpha(_fade);
    }

    private void OnConnectPressed(BaseButton.ButtonEventArgs args)
    {
        // Handle the connect action here.
    }
}
```

All C# comments must be written in English.

## 10. Animation Rules

Do not animate in XAML.

Do not use WPF animation systems.

Allowed animation approach:

- override `FrameUpdate(FrameEventArgs args)`;
- use `MathHelper.Lerp`;
- modify `ModulateSelfOverride`, `MinSize`, alpha, offsets, or internal animation state;
- keep animation deterministic and lightweight.

Example:

```csharp
protected override void FrameUpdate(FrameEventArgs args)
{
    base.FrameUpdate(args);

    // Smoothly interpolate the warning pulse.
    _warningPulse = MathHelper.Lerp(_warningPulse, _targetWarningPulse, args.DeltaSeconds * 10f);
}
```

For complex dynamic elements, use custom drawing:

```csharp
protected override void Draw(DrawingHandleScreen handle)
{
    base.Draw(handle);

    // Draw custom cyberpunk interface elements here.
}
```

Use custom drawing for:

- radar sweeps;
- pulse lines;
- data graphs;
- netrunner grids;
- scanner arcs;
- waveform displays;
- targeting overlays.

## 11. `.sw` Style Rules

Styles must be defined in `.sw` files.

Use `.sw` for:

- text colors;
- background style boxes;
- padding;
- margins when supported;
- button states;
- panel states;
- font choices;
- texture-backed UI elements.

For final UI, prefer `StyleBoxTexture` over plain flat color panels.

Use `StyleBoxFlat` only for temporary placeholders or very simple panels.

When using `StyleBoxTexture`, also define the required texture in the documentation or code comments.

Example texture requirement:

```text
Texture: /Textures/Interface/NightCity/panel_terminal_32x32.png
Type: nine-patch panel frame
Size: 32x32
Center: transparent dark graphite
Border: thin cyan neon edge
Corners: damaged metal with small magenta pixels
Purpose: terminal panel background
```

## 12. Required Texture Guidelines

Cyberpunk visual quality should come from textures and shaders, not fake WPF properties.

Recommended texture types:

```text
panel_terminal_32x32.png
panel_inset_32x32.png
button_normal_32x32.png
button_hover_32x32.png
button_pressed_32x32.png
button_danger_32x32.png
input_box_32x32.png
scanline_overlay.png
divider_neon_horizontal.png
warning_stripes_32x32.png
```

Nine-patch textures should be small, readable, and designed to stretch cleanly.

Preferred sizes:

```text
16x16
24x24
32x32
64x64
```

## 13. Visual Direction

The UI must feel like Night City / Cyberpunk RED 2045.

Use:

- dark graphite backgrounds;
- dirty metal panels;
- cyan neon accents;
- magenta secondary accents;
- red danger states;
- amber warning states;
- green success states;
- scanlines;
- subtle glitches;
- small technical labels;
- corporate warning blocks;
- readable terminal typography.

Avoid:

- clean Star Trek style;
- bright white panels;
- excessive neon everywhere;
- fantasy ornaments;
- default Nano UI look;
- unreadable low-contrast text.

The UI should look expensive, but still be functional.

## 14. Recommended File Structure

Recommended project structure:

```text
Content.Client/Stylesheets/StyleNightCity.cs
Resources/Stylesheets/nightcity.sw
Resources/Textures/Interface/NightCity/
Content.Client/NightCity/UI/
```

Example C# style constants:

```csharp
namespace Content.Client.Stylesheets;

public static class StyleNightCity
{
    public const string Window = "NightCityWindow";
    public const string Panel = "NightCityPanel";
    public const string PanelDark = "NightCityPanelDark";
    public const string TerminalPanel = "NightCityTerminalPanel";
    public const string TerminalHeader = "NightCityTerminalHeader";
    public const string TerminalBody = "NightCityTerminalBody";
    public const string TerminalFooter = "NightCityTerminalFooter";
    public const string Button = "NightCityButton";
    public const string ButtonDanger = "NightCityButtonDanger";
    public const string ButtonGhost = "NightCityButtonGhost";
    public const string Input = "NightCityInput";
    public const string Divider = "NightCityDivider";
    public const string GlowText = "NightCityGlowText";
    public const string MutedText = "NightCityMutedText";
    public const string StatusGood = "NightCityStatusGood";
    public const string StatusWarning = "NightCityStatusWarning";
    public const string StatusDanger = "NightCityStatusDanger";
}
```

`StyleNightCity.cs` should contain class name constants only.

Actual visual style belongs in `.sw`.

## 15. Required AI Output Format

When asked to create a new UI, the AI must provide three connected parts.

### 1. `[Name].xaml`

Clean layout only.

Must include:

- valid Robust UI controls;
- semantic hierarchy;
- `StyleClasses`;
- useful `Name` values for interactive elements.

### 2. `[Name].sw`

Style definitions.

Must include:

- Night City style classes;
- colors;
- style boxes;
- text styling;
- button states where needed.

### 3. `[Name].xaml.cs`

Code-behind.

Must include:

- `partial` class;
- `RobustXamlLoader.Load(this)`;
- event hookups;
- animation through `FrameUpdate` if the UI is animated;
- English comments only.

### 4. `Required textures`

If texture-backed styling is used, list all needed textures.

For every texture, describe:

- file path;
- size;
- purpose;
- how it should look;
- whether it is nine-patch/stretchable.

## 16. Pre-Generation Checklist

Before generating UI code, verify:

```text
[ ] No StyleNano usage.
[ ] No StyleNano imports.
[ ] No WPF/UWP controls.
[ ] No StackPanel.
[ ] No Grid instead of GridContainer.
[ ] No Border.
[ ] No Storyboard.
[ ] No DoubleAnimation.
[ ] XAML is only layout and hierarchy.
[ ] Visual style is in `.sw`.
[ ] Complex visuals use textures, shaders, or Draw.
[ ] Animations are in C#.
[ ] Code-behind calls RobustXamlLoader.Load(this).
[ ] GenerateTypedNameReferences is used when XAML names are referenced.
[ ] C# comments are in English.
[ ] Style classes are NightCity-prefixed.
[ ] UI visually fits Night City / Cyberpunk RED 2045.
```

If any item fails, fix the code before answering.

## 17. Anti-Hallucination Rule

If unsure whether a Robust UI control, property, style key, or XAML attribute exists, do not present it as fact.

Use a safe baseline control instead:

```text
Control
PanelContainer
BoxContainer
GridContainer
Label
RichTextLabel
Button
LineEdit
TextureRect
ProgressBar
ScrollContainer
```

If a more advanced control is needed, explicitly mark it as something to verify in the codebase before use.

## 18. Good Minimal Example

### NightCityExample.xaml

```xml
<DefaultWindow xmlns="https://spacestation14.io"
               Title="NIGHT CITY TERMINAL"
               MinSize="420 300">
    <PanelContainer StyleClasses="NightCityTerminalPanel">
        <BoxContainer Orientation="Vertical" HorizontalExpand="True" VerticalExpand="True">
            <Label Text="NCPD ACCESS NODE" StyleClasses="NightCityGlowText" />

            <PanelContainer StyleClasses="NightCityPanelInset" VerticalExpand="True">
                <RichTextLabel Name="OutputLog" Text="Waiting for uplink..." />
            </PanelContainer>

            <BoxContainer Orientation="Horizontal" HorizontalExpand="True">
                <Button Name="ConnectButton" Text="CONNECT" StyleClasses="NightCityButton" />
                <Button Name="PurgeButton" Text="PURGE" StyleClasses="NightCityButtonDanger" />
            </BoxContainer>
        </BoxContainer>
    </PanelContainer>
</DefaultWindow>
```

### NightCityExample.xaml.cs

```csharp
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Maths;

namespace Content.Client.NightCity.UI;

[GenerateTypedNameReferences]
public sealed partial class NightCityExample : DefaultWindow
{
    private float _fade;
    private float _targetFade = 1f;

    public NightCityExample()
    {
        RobustXamlLoader.Load(this);

        // Connect button events after the XAML tree has been loaded.
        ConnectButton.OnPressed += OnConnectPressed;
        PurgeButton.OnPressed += OnPurgePressed;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Smooth fade-in for the terminal window.
        _fade = MathHelper.Lerp(_fade, _targetFade, args.DeltaSeconds * 8f);
        ModulateSelfOverride = Color.White.WithAlpha(_fade);
    }

    private void OnConnectPressed(BaseButton.ButtonEventArgs args)
    {
        // Start terminal connection logic here.
        OutputLog.SetMessage("Uplink established.");
    }

    private void OnPurgePressed(BaseButton.ButtonEventArgs args)
    {
        // Start purge logic here.
        OutputLog.SetMessage("Purge command accepted.");
    }
}
```

## 19. Final Rule

When there is a conflict between old SS14 UI examples and this document, this document wins for all new Night City UI.

Do not make new UI look like Nano UI.

Do not use `StyleNano`.

Use `StyleNightCity`.

## Night City 2045 Visual Style Bible

- UI не должен быть чистым sci-fi.
- Это не гладкий корпоративный интерфейс 2077.
- Это 2045: послевоенный, грязный, ремонтированный, частично сломанный high-tech.
- Панели выглядят как дешёвый металл, пластик, повреждённое стекло, старые терминалы.
- Неон используется как акцент, а не как заливка всего интерфейса.
- Корпоративные UI могут быть чище, но всё равно не стерильные.
- NCPD UI должен быть жёсткий, утилитарный, с синим/красным/янтарным.
- Trauma Team UI — медицинский, бело-зелёный/бирюзовый, но с тревожными красными статусами.
- Netrunner UI — сетки, пульсы, линии, шум, глитчи, пакеты данных.
- Gang UI — грубее, ярче, больше поломанных рамок и хаотичных элементов.