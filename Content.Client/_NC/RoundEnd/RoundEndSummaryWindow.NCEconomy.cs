using Content.Shared._NC.Bank;
using Content.Shared._NC.Bank.Manifest;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.RoundEnd;

public sealed partial class RoundEndSummaryWindow
{
    /// <summary>
    /// Replaces the old player-role manifest with gross bank movement leaderboards.
    /// </summary>
    private BoxContainer MakeNCEconomyPlayersTab(NCRoundEconomyStats stats)
    {
        var tab = CreateEconomyTab("round-end-economy-players-tab");
        var content = GetEconomyContent(tab);

        AddPlayerLeaderboard(
            content,
            "round-end-economy-top-earned",
            stats.TopEarned,
            earned: true);

        content.AddChild(new BoxContainer { MinHeight = 12 });

        AddPlayerLeaderboard(
            content,
            "round-end-economy-top-lost",
            stats.TopLost,
            earned: false);

        return tab;
    }

    /// <summary>
    /// Displays gross deposits and withdrawals for the four tracked Night City organizations.
    /// </summary>
    private BoxContainer MakeNCEconomyFactionsTab(NCRoundEconomyStats stats)
    {
        var tab = CreateEconomyTab("round-end-economy-factions-tab");
        var content = GetEconomyContent(tab);

        foreach (var faction in stats.Factions)
        {
            var factionName = Loc.GetString(GetFactionLocale(faction.Account));
            var net = faction.Earned - faction.Lost;
            var name = new Label
            {
                Text = factionName,
                Margin = new Thickness(0, 8, 0, 2),
            };
            name.AddStyleClass("LabelBig");
            content.AddChild(name);

            content.AddChild(new Label
            {
                Text = net switch
                {
                    > 0 => Loc.GetString("round-end-economy-faction-net-positive", ("amount", net)),
                    < 0 => Loc.GetString("round-end-economy-faction-net-negative", ("amount", -net)),
                    _ => Loc.GetString("round-end-economy-faction-net-zero"),
                },
                FontColorOverride = net switch
                {
                    > 0 => Color.LightGreen,
                    < 0 => Color.LightCoral,
                    _ => Color.White,
                },
            });
        }

        return tab;
    }

    private static BoxContainer CreateEconomyTab(string titleLocale)
    {
        var tab = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Name = Loc.GetString(titleLocale),
        };

        tab.AddChild(new ScrollContainer
        {
            Name = "EconomyScroll",
            VerticalExpand = true,
            Margin = new Thickness(10),
        });

        return tab;
    }

    private static BoxContainer GetEconomyContent(BoxContainer tab)
    {
        var scroll = (ScrollContainer) tab.GetChild(0);
        var content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        scroll.AddChild(content);
        return content;
    }

    private static void AddPlayerLeaderboard(
        BoxContainer content,
        string headingLocale,
        IReadOnlyList<NCRoundPlayerEconomyEntry> entries,
        bool earned)
    {
        var heading = new Label
        {
            Text = Loc.GetString(headingLocale),
            Margin = new Thickness(0, 4, 0, 6),
        };
        heading.AddStyleClass("LabelBig");
        content.AddChild(heading);

        if (entries.Count == 0)
        {
            content.AddChild(new Label { Text = Loc.GetString("round-end-economy-no-transactions") });
            return;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var amount = earned ? entry.Earned : entry.Lost;
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 2),
            };

            row.AddChild(new Label
            {
                Text = Loc.GetString(
                    "round-end-economy-player-row",
                    ("rank", index + 1),
                    ("character", entry.CharacterName),
                    ("ooc", entry.OocName)),
                HorizontalExpand = true,
            });
            row.AddChild(new Label
            {
                Text = Loc.GetString(
                    earned ? "round-end-economy-earned-amount" : "round-end-economy-lost-amount",
                    ("amount", amount)),
                FontColorOverride = earned ? Color.LightGreen : Color.LightCoral,
            });
            content.AddChild(row);
        }
    }

    private static string GetFactionLocale(SectorBankAccount account)
    {
        return account switch
        {
            SectorBankAccount.Biotechnica => "round-end-economy-faction-biotechnica",
            SectorBankAccount.TraumaTeam => "round-end-economy-faction-trauma-team",
            SectorBankAccount.Militech => "round-end-economy-faction-militech",
            SectorBankAccount.Ncpd => "round-end-economy-faction-ncpd",
            _ => "round-end-economy-faction-unknown",
        };
    }
}
