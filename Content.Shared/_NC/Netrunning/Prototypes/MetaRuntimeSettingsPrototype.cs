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

    [DataField("minimumProgramHealth")]
    public int MinimumProgramHealth { get; private set; } = 25;

    [DataField("maximumProgramHealth")]
    public int MaximumProgramHealth { get; private set; } = 500;

    [DataField("programHealthPerServerLoad")]
    public int ProgramHealthPerServerLoad { get; private set; } = 25;

    [DataField("programHealthPerGas")]
    public int ProgramHealthPerGas { get; private set; } = 25;

    [DataField("maximumIceDamage")]
    public int MaximumIceDamage { get; private set; } = 250;

    [DataField("iceDamagePerGas")]
    public int IceDamagePerGas { get; private set; } = 10;

    [DataField("maximumNeuralDamage")]
    public int MaximumNeuralDamage { get; private set; } = 100;

    [DataField("neuralDamagePerGas")]
    public int NeuralDamagePerGas { get; private set; } = 5;
}
