using Content.Shared._NC.CitiNet;
using Robust.Client.UserInterface;

namespace Content.Client._NC.CitiNet.UI;

/// <summary>
/// Interface for UI fragments that need to handle raw BUI messages from the CitiNet browser.
/// </summary>
public interface ICitiNetSiteFragment
{
    void ReceiveMessage(BoundUserInterfaceMessage message);
}
