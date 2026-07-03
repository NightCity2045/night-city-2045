using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class DataShardComponent : Component
{
    [DataField("sourceCode")]
    public string? SourceCode;

    [ViewVariables]
    public MetaBytecode? Bytecode;

    [DataField("requiredRam")]
    public int RequiredRam;

    [DataField("programKind")]
    public MetaProgramKind ProgramKind = MetaProgramKind.Standard;
}
