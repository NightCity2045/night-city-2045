using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetFileStoreComponent : Component
{
    /// <summary>
    /// Files exposed by this networked entity for META GET_FILES/DOWNLOAD/UPLOAD.
    /// </summary>
    [DataField("files"), ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public List<string> Files = new();
}
