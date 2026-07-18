using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Verbs;

namespace Content.Shared._NC.Clothing;

/// <summary>
/// Controls the two independent jacket toggles and keeps item, in-hand, and equipped visuals synchronized.
/// </summary>
public sealed class NCJacketStateSystem : EntitySystem
{
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCJacketStateComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<NCJacketStateComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<NCJacketStateComponent, GetVerbsEvent<AlternativeVerb>>(OnGetSleevesVerbs);
        SubscribeLocalEvent<NCJacketClosureComponent, GetVerbsEvent<AlternativeVerb>>(OnGetClosureVerb);
    }

    private void OnComponentInit(EntityUid uid, NCJacketStateComponent component, ComponentInit args)
    {
        ApplyVisualState(uid, component);
    }

    private void OnHandleState(EntityUid uid, NCJacketStateComponent component, ref AfterAutoHandleStateEvent args)
    {
        // Reapply derived prefixes after the authoritative component state reaches the client.
        ApplyVisualState(uid, component);
    }

    private void OnGetSleevesVerbs(
        EntityUid uid,
        NCJacketStateComponent component,
        GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (component.CanRollSleeves)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(component.SleevesRolled
                    ? component.LowerSleevesVerbText
                    : component.RollSleevesVerbText),
                Act = () => ToggleSleeves(uid),
                Category = VerbCategory.Interaction,
                Priority = 1,
            });
        }
    }

    private void OnGetClosureVerb(
        EntityUid uid,
        NCJacketClosureComponent component,
        GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null ||
            !TryComp<NCJacketStateComponent>(uid, out var state))
        {
            return;
        }

        // The presence of the marker is the capability check; no inherited boolean can suppress this verb.
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(state.IsOpen ? state.CloseVerbText : state.OpenVerbText),
            Act = () => ToggleClosure(uid),
            Category = VerbCategory.Interaction,
            Priority = 2,
        });
    }

    private void ToggleClosure(EntityUid uid)
    {
        if (!HasComp<NCJacketClosureComponent>(uid) ||
            !TryComp<NCJacketStateComponent>(uid, out var component))
        {
            return;
        }

        component.IsOpen = !component.IsOpen;
        Dirty(uid, component);
        ApplyVisualState(uid, component);
    }

    private void ToggleSleeves(EntityUid uid)
    {
        if (!TryComp<NCJacketStateComponent>(uid, out var component) || !component.CanRollSleeves)
            return;

        component.SleevesRolled = !component.SleevesRolled;
        Dirty(uid, component);
        ApplyVisualState(uid, component);
    }

    private void ApplyVisualState(EntityUid uid, NCJacketStateComponent component)
    {
        var state = GetVisualState(component);
        var prefix = state switch
        {
            NCJacketVisualState.OpenSleevesDown => component.OpenPrefix,
            NCJacketVisualState.ClosedSleevesRolled => component.RolledPrefix,
            NCJacketVisualState.OpenSleevesRolled => component.OpenRolledPrefix,
            _ => null,
        };

        // One computed state drives all three representations, preventing mixed open/rolled visuals.
        _appearance.SetData(uid, NCJacketVisuals.State, state);
        _clothing.SetEquippedPrefix(uid, prefix);
        _item.SetHeldPrefix(uid, prefix);
    }

    private static NCJacketVisualState GetVisualState(NCJacketStateComponent component)
    {
        if (component.IsOpen)
        {
            return component.SleevesRolled
                ? NCJacketVisualState.OpenSleevesRolled
                : NCJacketVisualState.OpenSleevesDown;
        }

        return component.SleevesRolled
            ? NCJacketVisualState.ClosedSleevesRolled
            : NCJacketVisualState.ClosedSleevesDown;
    }
}
