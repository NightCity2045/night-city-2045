using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.CitiNet.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedNetBrowserSystem))]
public sealed partial class NetBrowserComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("currentUrl")]
    public string CurrentUrl = "nightcity.gov";

    /// <summary>
    /// URLs that have been unlocked (e.g., via Data Chips or secret codes).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("unlockedUrls")]
    public HashSet<string> UnlockedUrls = new();
}

[Serializable, NetSerializable]
public sealed class NetBrowserUiState : BoundUserInterfaceState
{
    public readonly string CurrentUrl;
    public readonly List<string> AvailableSiteIds;

    public NetBrowserUiState(string currentUrl, List<string> availableSiteIds)
    {
        CurrentUrl = currentUrl;
        AvailableSiteIds = availableSiteIds;
    }
}

[Serializable, NetSerializable]
public enum NetBrowserUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class NetBrowserNavigateMessage : BoundUserInterfaceMessage
{
    public readonly string Url;

    public NetBrowserNavigateMessage(string url)
    {
        Url = url;
    }
}
