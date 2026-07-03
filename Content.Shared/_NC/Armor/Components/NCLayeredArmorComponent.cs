using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Armor.Components;

/// <summary>
/// Stores the runtime state for the GDD two-layer armor model.
/// All logic lives in the NC armor systems.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCLayeredArmorComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public NCLayeredArmorLayer SoftLayer = new();

    [DataField(required: true), AutoNetworkedField]
    public NCLayeredArmorLayer HardLayer = new();

    [DataField, AutoNetworkedField]
    public float EqualPenetrationDamageMultiplier = 0.5f;
}

/// <summary>
/// A single physical armor layer with its own coverage and durability pool.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class NCLayeredArmorLayer
{
    [DataField]
    public int ArmorClass;

    [DataField]
    public float MaxDurability = 100f;

    [DataField]
    public float CurrentDurability = 100f;

    [DataField]
    public List<TargetBodyPart> Coverage = new();
}
