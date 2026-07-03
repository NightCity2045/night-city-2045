using Content.Shared._NC.Crafting.ArmorWorkbench.Components;
using Content.Shared._NC.Crafting.ArmorWorkbench.Events;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._NC.Crafting.ArmorWorkbench.UI;

public sealed class ArmorWorkbenchBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private ArmorWorkbenchWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new ArmorWorkbenchWindow();

        if (State != null)
            UpdateState(State);

        _window.OnClose += Close;
        _window.OnMaterialSelected += OnMaterialSelected;
        _window.OnEjectRequested += OnEjectRequested;
        _window.OnStartCraft += OnStartCraft;
        _window.OpenCentered();
    }

    private void OnMaterialSelected(ArmorWorkbenchLayerSlot layer, NetEntity material)
    {
        SendMessage(new ArmorWorkbenchSelectMaterialMessage(layer, material));
    }

    private void OnStartCraft()
    {
        SendMessage(new ArmorWorkbenchStartCraftMessage());
    }

    private void OnEjectRequested(ArmorWorkbenchEjectTarget target)
    {
        SendMessage(new ArmorWorkbenchEjectRequestMessage(target));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || !_window.IsOpen || state is not ArmorWorkbenchBoundUserInterfaceState workbenchState)
            return;

        _window.UpdateState(workbenchState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Close();
    }
}
