namespace Content.Shared._NC.Netrunning.Components;

[RegisterComponent]
public sealed partial class DefensiveDaemonComponent : Component
{
    [DataField("shard")]
    public EntityUid? Shard;
}
