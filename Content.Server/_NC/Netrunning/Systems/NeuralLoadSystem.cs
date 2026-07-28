using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared._NC.Netrunning.Prototypes;
using Content.Shared._NC.Stats;
using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Events;
using Content.Shared._NC.Stats.Systems;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
/// Owns the human neural resource used by HotSim META execution.
/// </summary>
public sealed class NeuralLoadSystem : EntitySystem
{
    private const string ConcentrationSkillId = "Concentration";
    private const float RecoveryInterval = 1f;

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedNCStatsSystem _stats = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    private MetaRuntimeSettingsPrototype _settings = default!;
    private float _recoveryTimer;

    public override void Initialize()
    {
        base.Initialize();

        _settings = _prototypes.Index<MetaRuntimeSettingsPrototype>(MetaRuntimeSettingsPrototype.DefaultId);
        SubscribeLocalEvent<NCStatsComponent, NCProfileStatsAppliedEvent>(OnProfileStatsApplied);
    }

    private void OnProfileStatsApplied(
        EntityUid uid,
        NCStatsComponent component,
        ref NCProfileStatsAppliedEvent args)
    {
        RecalculateCapacity(uid, component);
    }

    private void RecalculateCapacity(EntityUid uid, NCStatsComponent stats)
    {
        if (!TryComp<NCSkillsComponent>(uid, out var skills))
            return;

        _stats.TryGetStatValue(stats, NCStatIds.Will, out var will);
        _stats.TryGetStatValue(stats, NCStatIds.Intelligence, out var intelligence);
        _stats.TryGetSkillValue(skills, ConcentrationSkillId, out var concentration);

        var neural = EnsureComp<NeuralLoadComponent>(uid);
        var oldMax = Math.Max(1f, neural.MaxLoad);
        var ratio = Math.Clamp(neural.CurrentLoad / oldMax, 0f, 1f);
        neural.MaxLoad = Math.Max(1f,
            _settings.NeuralLoadBaseCapacity +
            will * _settings.NeuralLoadWillMultiplier +
            intelligence * _settings.NeuralLoadIntelligenceMultiplier +
            concentration * _settings.NeuralLoadConcentrationMultiplier);
        neural.CurrentLoad = neural.MaxLoad * ratio;
        Dirty(uid, neural);
    }

    public int GetEffectiveGasLimit(EntityUid user, int deckGasLimit)
    {
        if (!TryResolveHotSimOperator(user, out _, out var neural))
            return deckGasLimit;

        var bonus = (int) MathF.Floor(neural.MaxLoad * Math.Max(0f, _settings.HotSimGasPerNeuralCapacity));
        return Math.Max(1, deckGasLimit + bonus);
    }

    public float GetYieldPressure(int yieldMilliseconds)
    {
        if (yieldMilliseconds <= 0)
            return 1f;

        var minimum = Math.Max(1, _settings.YieldPressureMinimumMilliseconds);
        var delay = Math.Max(minimum, yieldMilliseconds);
        var pressure = _settings.YieldPressureBaselineMilliseconds / (float) delay;
        return Math.Clamp(pressure, 1f, Math.Max(1f, _settings.YieldPressureMaximumMultiplier));
    }

    public int GetAdjustedYieldMilliseconds(EntityUid user, int yieldMilliseconds)
    {
        if (yieldMilliseconds <= 0 ||
            !TryResolveHotSimOperator(user, out _, out var neural) ||
            neural.MaxLoad <= 0f ||
            neural.CurrentLoad / neural.MaxLoad < _settings.NeuralLoadCriticalRatio)
        {
            return yieldMilliseconds;
        }

        return (int) MathF.Ceiling(yieldMilliseconds *
            Math.Max(1f, _settings.NeuralLoadCriticalYieldMultiplier));
    }

    public void ApplyExecutionLoad(
        EntityUid deckUid,
        CyberdeckComponent deck,
        EntityUid user,
        MetaExecutionResult result)
    {
        if (!TryResolveHotSimOperator(user, out var body, out var neural))
            return;

        var pressure = GetYieldPressure(result.YieldMilliseconds);
        var amount = (result.OperationsThisSlice * Math.Max(0f, _settings.NeuralLoadPerOperation) +
                      result.SystemCallsThisSlice * Math.Max(0f, _settings.NeuralLoadPerSystemCall)) * pressure;
        if (amount <= 0f)
            return;

        neural.CurrentLoad = Math.Min(neural.MaxLoad, neural.CurrentLoad + amount);
        neural.RecoveryBlockedUntil = _timing.CurTime.TotalSeconds +
                                      Math.Max(0f, _settings.NeuralLoadRecoveryDelaySeconds);

        var ratio = neural.MaxLoad > 0f ? neural.CurrentLoad / neural.MaxLoad : 1f;
        if (ratio >= _settings.NeuralLoadWarningRatio && !neural.WarningIssued)
        {
            neural.WarningIssued = true;
            _popup.PopupEntity(Loc.GetString("netrunning-neural-load-warning"), body, body,
                PopupType.MediumCaution);
        }

        if (ratio >= _settings.NeuralLoadCriticalRatio && !neural.CriticalIssued)
        {
            neural.CriticalIssued = true;
            deck.TraceLevel = Math.Clamp(deck.TraceLevel + _settings.NeuralLoadCriticalTracePenalty, 0, 100);
            Dirty(deckUid, deck);
            _popup.PopupEntity(Loc.GetString("netrunning-neural-load-critical"), body, body,
                PopupType.LargeCaution);
        }

        if (ratio >= 1f && !neural.Overloaded)
            Overload(body, neural);

        Dirty(body, neural);
    }

    public bool TryGetUiState(
        EntityUid user,
        out float currentLoad,
        out float maxLoad,
        out bool hotSim)
    {
        hotSim = TryResolveHotSimOperator(user, out _, out var neural);
        if (hotSim)
        {
            currentLoad = neural.CurrentLoad;
            maxLoad = neural.MaxLoad;
            return true;
        }

        if (TryComp<NeuralLoadComponent>(user, out neural))
        {
            currentLoad = neural.CurrentLoad;
            maxLoad = neural.MaxLoad;
            return true;
        }

        currentLoad = 0f;
        maxLoad = 0f;
        return false;
    }

    private bool TryResolveHotSimOperator(
        EntityUid user,
        out EntityUid body,
        out NeuralLoadComponent neural)
    {
        body = EntityUid.Invalid;
        neural = default!;
        if (!TryComp<NetAvatarComponent>(user, out var avatar) ||
            avatar.PhysicalBody is not { } physicalBody ||
            Deleted(physicalBody) ||
            !TryComp<NeuralLoadComponent>(physicalBody, out var neuralComponent))
        {
            return false;
        }

        body = physicalBody;
        neural = neuralComponent;
        return true;
    }

    private void Overload(EntityUid body, NeuralLoadComponent neural)
    {
        neural.Overloaded = true;
        neural.RecoveryBlockedUntil = _timing.CurTime.TotalSeconds +
                                      Math.Max(0f, _settings.NeuralLoadOverloadRecoveryLockSeconds);

        var damage = new DamageSpecifier();
        damage.DamageDict["Heat"] = _settings.NeuralLoadOverloadDamage;
        _damageable.TryChangeDamage(body, damage, ignoreResistances: false, interruptsDoAfters: true);
        _stun.TryParalyze(body, TimeSpan.FromSeconds(Math.Max(0f, _settings.NeuralLoadOverloadStunSeconds)), true);
        _popup.PopupEntity(Loc.GetString("netrunning-neural-load-overload"), body, body, PopupType.LargeCaution);

        var overload = new NeuralLoadOverloadEvent();
        RaiseLocalEvent(body, ref overload);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _recoveryTimer += frameTime;
        if (_recoveryTimer < RecoveryInterval)
            return;

        var elapsed = _recoveryTimer;
        _recoveryTimer = 0f;
        var now = _timing.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<NeuralLoadComponent>();
        while (query.MoveNext(out var uid, out var neural))
        {
            if (neural.CurrentLoad <= 0f || now < neural.RecoveryBlockedUntil)
                continue;

            var rate = HasComp<ImmersedBodyComponent>(uid)
                ? _settings.NeuralLoadHotSimRecoveryPerSecond
                : _settings.NeuralLoadRecoveryPerSecond;
            neural.CurrentLoad = Math.Max(0f, neural.CurrentLoad - Math.Max(0f, rate) * elapsed);

            var ratio = neural.MaxLoad > 0f ? neural.CurrentLoad / neural.MaxLoad : 0f;
            if (ratio < _settings.NeuralLoadCriticalRatio)
            {
                neural.CriticalIssued = false;
                neural.Overloaded = false;
            }

            if (ratio < _settings.NeuralLoadWarningRatio)
                neural.WarningIssued = false;

            Dirty(uid, neural);
        }
    }
}

[ByRefEvent]
public readonly struct NeuralLoadOverloadEvent
{
}
