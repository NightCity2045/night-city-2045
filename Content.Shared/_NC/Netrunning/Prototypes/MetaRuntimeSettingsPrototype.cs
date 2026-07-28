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

    [DataField("neuralLoadBaseCapacity")]
    public float NeuralLoadBaseCapacity { get; private set; } = 20f;

    [DataField("neuralLoadWillMultiplier")]
    public float NeuralLoadWillMultiplier { get; private set; } = 6f;

    [DataField("neuralLoadIntelligenceMultiplier")]
    public float NeuralLoadIntelligenceMultiplier { get; private set; } = 4f;

    [DataField("neuralLoadConcentrationMultiplier")]
    public float NeuralLoadConcentrationMultiplier { get; private set; } = 2f;

    [DataField("neuralLoadPerOperation")]
    public float NeuralLoadPerOperation { get; private set; } = 0.01f;

    [DataField("neuralLoadPerSystemCall")]
    public float NeuralLoadPerSystemCall { get; private set; } = 0.5f;

    [DataField("neuralLoadRecoveryPerSecond")]
    public float NeuralLoadRecoveryPerSecond { get; private set; } = 8f;

    [DataField("neuralLoadHotSimRecoveryPerSecond")]
    public float NeuralLoadHotSimRecoveryPerSecond { get; private set; } = 1f;

    [DataField("neuralLoadRecoveryDelaySeconds")]
    public float NeuralLoadRecoveryDelaySeconds { get; private set; } = 3f;

    [DataField("neuralLoadWarningRatio")]
    public float NeuralLoadWarningRatio { get; private set; } = 0.6f;

    [DataField("neuralLoadCriticalRatio")]
    public float NeuralLoadCriticalRatio { get; private set; } = 0.8f;

    [DataField("neuralLoadCriticalTracePenalty")]
    public int NeuralLoadCriticalTracePenalty { get; private set; } = 10;

    [DataField("neuralLoadCriticalYieldMultiplier")]
    public float NeuralLoadCriticalYieldMultiplier { get; private set; } = 1.5f;

    [DataField("neuralLoadOverloadDamage")]
    public float NeuralLoadOverloadDamage { get; private set; } = 80f;

    [DataField("neuralLoadOverloadStunSeconds")]
    public float NeuralLoadOverloadStunSeconds { get; private set; } = 8f;

    [DataField("neuralLoadOverloadRecoveryLockSeconds")]
    public float NeuralLoadOverloadRecoveryLockSeconds { get; private set; } = 15f;

    [DataField("hotSimGasPerNeuralCapacity")]
    public float HotSimGasPerNeuralCapacity { get; private set; } = 10f;

    [DataField("yieldPressureBaselineMilliseconds")]
    public int YieldPressureBaselineMilliseconds { get; private set; } = 3000;

    [DataField("yieldPressureMinimumMilliseconds")]
    public int YieldPressureMinimumMilliseconds { get; private set; } = 100;

    [DataField("yieldPressureMaximumMultiplier")]
    public float YieldPressureMaximumMultiplier { get; private set; } = 30f;

    [DataField("serverRuntimeLoadPerOperation")]
    public float ServerRuntimeLoadPerOperation { get; private set; } = 0.01f;

    [DataField("serverRuntimeLoadPerSystemCall")]
    public float ServerRuntimeLoadPerSystemCall { get; private set; } = 0.5f;

    [DataField("serverRuntimeLoadRecoveryPerSecond")]
    public float ServerRuntimeLoadRecoveryPerSecond { get; private set; } = 5f;

    [DataField("serverRuntimeOverloadYieldMultiplier")]
    public float ServerRuntimeOverloadYieldMultiplier { get; private set; } = 2f;
}
