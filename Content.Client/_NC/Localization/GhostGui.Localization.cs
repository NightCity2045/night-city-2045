using Content.Client.Localization;

namespace Content.Client.UserInterface.Systems.Ghost.Widgets;

public sealed partial class GhostGui : ILocalizedControl
{
    public void Relocalize()
    {
        ReturnToBodyButton.Text = Loc.GetString("ghost-gui-return-to-body-button");
        GhostWarpButton.Text = Loc.GetString("ghost-gui-ghost-warp-button");

        // The role count is assigned dynamically and therefore must be refreshed explicitly after a culture change.
        GhostRolesButton.Text = Loc.GetString("ghost-gui-ghost-roles-button", ("count", _prevNumberRoles));

        GhostBarButton.Text = Loc.GetString("ghost-target-window-ghostbar");
        ReturnToRound.Text = Loc.GetString("ghost-gui-return-to-round-button");
    }
}
