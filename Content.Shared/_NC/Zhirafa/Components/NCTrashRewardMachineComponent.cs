using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Zhirafa.Components;

/// <summary>
/// Configures a powered machine that destroys accepted trash and rewards its user with physical currency.
/// </summary>
[RegisterComponent]
public sealed partial class NCTrashRewardMachineComponent : Component
{
    /// <summary>
    /// Items accepted as recyclable trash.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist TrashWhitelist = new();

    /// <summary>
    /// Storage items whose direct contents may be processed while the storage itself is preserved.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist TrashContainerWhitelist = new();

    /// <summary>
    /// Stack currency paid to the user.
    /// </summary>
    [DataField]
    public ProtoId<StackPrototype> Currency = "Credit";

    /// <summary>
    /// Currency units paid for each destroyed entity.
    /// </summary>
    [DataField]
    public int RewardPerItem = 5;

    /// <summary>
    /// Sound played after at least one item is processed.
    /// </summary>
    [DataField]
    public SoundSpecifier ProcessSound = new SoundPathSpecifier("/Audio/Effects/saw.ogg");
}
