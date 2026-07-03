namespace Content.Server._White.GameTicking.Aspects.Components;

[RegisterComponent]
public sealed partial class ImmersiveAspectComponent : Component
{
    [DataField]
    public float EyeModifier = 1f;
}
