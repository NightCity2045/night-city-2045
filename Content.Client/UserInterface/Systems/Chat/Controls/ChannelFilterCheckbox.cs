using Content.Shared.Chat;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

public sealed class ChannelFilterCheckbox : CheckBox
{
    public readonly ChatChannel Channel;
    private int? _unread;

    public bool IsHidden => Parent == null;

    public ChannelFilterCheckbox(ChatChannel channel)
    {
        Channel = channel;
        RefreshLocalization();
    }

    private void UpdateText(int? unread)
    {
        var name = Loc.GetString($"hud-chatbox-channel-{Channel}");

        if (unread > 0)
            // todo: proper fluent stuff here.
            name += " (" + (unread > 9 ? "9+" : unread) + ")";

        Text = name;
    }

    public void UpdateUnreadCount(int? unread)
    {
        _unread = unread;
        UpdateText(unread);
    }

    public void RefreshLocalization()
    {
        UpdateText(_unread);
    }
}
