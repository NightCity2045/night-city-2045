using Content.Client.UserInterface.Fragments;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Robust.Client.UserInterface;

namespace Content.Client._NC.CitiNet.UI;

public sealed partial class NetHomeSiteUIFragment : UIFragment
{
    private NetHomeSiteUI? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new NetHomeSiteUI();
        _fragment.OnNavigate += (url) => userInterface.SendMessage(new NetBrowserNavigateMessage(url));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is NetBrowserUiState browserState)
            _fragment?.UpdateState(browserState);
    }
}
