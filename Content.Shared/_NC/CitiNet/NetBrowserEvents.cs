namespace Content.Shared._NC.CitiNet;

/// <summary>
/// Raised when a CitiNet browser navigates to a new URL or is opened.
/// </summary>
public sealed class NetBrowserUrlChangedEvent : EntityEventArgs
{
    public EntityUid Browser { get; }
    public string NewUrl { get; }
    public EntityUid? Actor { get; }

    public NetBrowserUrlChangedEvent(EntityUid browser, string newUrl, EntityUid? actor = null)
    {
        Browser = browser;
        NewUrl = newUrl;
        Actor = actor;
    }
}
