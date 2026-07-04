using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Armor.Components;

/// <summary>
/// Physical armor layer from the NC ballistic armor GDD.
/// Attach this to clothing, plates, and subdermal implants.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PhysicalArmorComponent : Component
{
    /// <summary>
    /// Stopping Power. Penetration must be greater than SP to pass through.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StoppingPower;

    [DataField]
    [AutoNetworkedField]
    public float MaxDurability = 100f;

    [DataField]
    [AutoNetworkedField]
    public float CurrentDurability = 100f;

    [DataField]
    [AutoNetworkedField]
    public List<TargetBodyPart> Coverage = new();

    [DataField]
    [AutoNetworkedField]
    public PhysicalArmorMaterialType MaterialType = PhysicalArmorMaterialType.Generic;

    /// <summary>
    /// How much stopped ballistic or blunt energy reaches the body as Blunt damage.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float BluntDamageMultiplier = 0.4f;

    /// <summary>
    /// Multiplier applied to impact damage before subtracting durability.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float DurabilityDamageMultiplier = 1f;

    /// <summary>
    /// If true, this layer is treated as the final chrome layer under flesh.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool Subdermal;
}

[Serializable, NetSerializable]
public enum PhysicalArmorMaterialType : byte
{
    Generic,
    Kevlar,
    Ceramic,
    Steel,
    Subdermal,
}
