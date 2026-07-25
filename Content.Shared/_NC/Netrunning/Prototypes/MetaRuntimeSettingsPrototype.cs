using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Netrunning.Prototypes;

/// <summary>
/// Server-wide limits for fair, preemptive META execution.
/// </summary>
[Prototype("metaRuntimeSettings")]
public sealed partial class MetaRuntimeSettingsPrototype : IPrototype
{
    public const string DefaultId = "default";

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("globalOperationsPerTick")]
    public int GlobalOperationsPerTick { get; private set; }

    [DataField("processOperationsPerTick")]
    public int ProcessOperationsPerTick { get; private set; }
}
