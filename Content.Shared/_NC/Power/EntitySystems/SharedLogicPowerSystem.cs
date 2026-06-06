using Content.Shared._NC.Power.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._NC.Power.EntitySystems;

public abstract class SharedLogicPowerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WireSpoolComponent, ComponentGetState>(OnSpoolGetState);
        SubscribeLocalEvent<WireSpoolComponent, ComponentHandleState>(OnSpoolHandleState);

        SubscribeLocalEvent<LogicPowerProviderComponent, ComponentGetState>(OnProviderGetState);
        SubscribeLocalEvent<LogicPowerProviderComponent, ComponentHandleState>(OnProviderHandleState);

        SubscribeLocalEvent<LogicPowerReceiverComponent, ComponentGetState>(OnReceiverGetState);
        SubscribeLocalEvent<LogicPowerReceiverComponent, ComponentHandleState>(OnReceiverHandleState);
    }

    private void OnSpoolGetState(EntityUid uid, WireSpoolComponent component, ref ComponentGetState args)
    {
        args.State = new WireSpoolComponentState { ActiveProvider = GetNetEntity(component.ActiveProvider) };
    }

    private void OnSpoolHandleState(EntityUid uid, WireSpoolComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not WireSpoolComponentState state)
            return;
        component.ActiveProvider = GetEntity(state.ActiveProvider);
    }

    private void OnProviderGetState(EntityUid uid, LogicPowerProviderComponent component, ref ComponentGetState args)
    {
        args.State = new LogicPowerProviderComponentState { Receivers = GetNetEntityList(component.Receivers) };
    }

    private void OnProviderHandleState(EntityUid uid, LogicPowerProviderComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not LogicPowerProviderComponentState state)
            return;
        component.Receivers = GetEntityList(state.Receivers);
    }

    private void OnReceiverGetState(EntityUid uid, LogicPowerReceiverComponent component, ref ComponentGetState args)
    {
        args.State = new LogicPowerReceiverComponentState
        {
            Provider = GetNetEntity(component.Provider),
            Powered = component.Powered,
            PowerLoad = component.PowerLoad
        };
    }

    private void OnReceiverHandleState(EntityUid uid, LogicPowerReceiverComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not LogicPowerReceiverComponentState state)
            return;
        component.Provider = GetEntity(state.Provider);
        component.Powered = state.Powered;
        component.PowerLoad = state.PowerLoad;
    }
}
