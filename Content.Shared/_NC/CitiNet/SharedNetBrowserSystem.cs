using Content.Shared._NC.CitiNet.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.CitiNet;

public abstract class SharedNetBrowserSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public virtual void NavigateTo(EntityUid uid, NetBrowserComponent component, string url)
    {
        component.CurrentUrl = url;
        Dirty(uid, component);
    }
}
