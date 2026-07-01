using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Crafting.ArmorWorkbench;

/// <summary>
/// Raised when the armor assembly do-after completes or is cancelled.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ArmorWorkbenchDoAfterEvent : SimpleDoAfterEvent
{
}
