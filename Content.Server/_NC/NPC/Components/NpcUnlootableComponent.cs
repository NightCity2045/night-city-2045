using Robust.Shared.GameObjects;

namespace Content.Server._NC.NPC.Components;

/// <summary>
/// This component prevents the NPC's items from dropping on death or being stripped by players
/// by automatically adding the UnremoveableComponent to all inserted items.
/// </summary>
[RegisterComponent]
public sealed partial class NpcUnlootableComponent : Component
{
}
