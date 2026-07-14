using Content.Shared.NPC.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Factions;

/// <summary>
/// Maps a Night City job department to the NPC faction applied to players in that department.
/// </summary>
[Prototype("ncDepartmentFaction")]
public sealed partial class NCDepartmentFactionPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<DepartmentPrototype> Department;

    [DataField(required: true)]
    public ProtoId<NpcFactionPrototype> Faction;
}
