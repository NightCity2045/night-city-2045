using Content.Shared.NPC;

namespace Content.Server._NC.NPC.Components;

[RegisterComponent]
public sealed partial class NCTacticalMovementComponent : Component
{
    [DataField]
    public NCTacticalMovementType Type = NCTacticalMovementType.None;

    [DataField]
    public EntityUid? Target;

    [DataField]
    public float IdealDistance = 10f;
}

public enum NCTacticalMovementType : byte
{
    None,
    Flee,
    Circle
}
