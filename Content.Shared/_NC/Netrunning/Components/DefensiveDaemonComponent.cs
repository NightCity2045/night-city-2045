namespace Content.Shared._NC.Netrunning.Components;

[RegisterComponent]
public sealed partial class DefensiveDaemonComponent : Component
{
    public const string DefaultSlotId = "meta_defense_0";

    /// <summary>
    /// Fixed defensive program slots owned by this exact electronic device.
    /// </summary>
    [DataField("slots")]
    public List<string> Slots = new() { DefaultSlotId };

    /// <summary>
    /// Local-network server that owns this physical device slot.
    /// </summary>
    [ViewVariables]
    public EntityUid? Server;

    [ViewVariables]
    public readonly List<EntityUid> Shards = new();
}
