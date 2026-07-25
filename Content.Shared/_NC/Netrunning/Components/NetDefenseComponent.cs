using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Persistent defensive object hosted by a local NET server.
///     ICE is static, demons are active, but both reserve server load.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetDefenseComponent : Component
{
    [DataField("server"), AutoNetworkedField]
    public EntityUid? Server;

    [DataField("ownerDeck"), AutoNetworkedField]
    public EntityUid? OwnerDeck;

    [DataField("reservedLoad"), AutoNetworkedField]
    public int ReservedLoad = 5;

    [DataField("kind"), AutoNetworkedField]
    public NetDefenseKind Kind = NetDefenseKind.Ice;
}

[Serializable, NetSerializable]
public enum NetDefenseKind : byte
{
    Ice,
    BlackIce,
    Demon,
    Wall,
    Trap
}
