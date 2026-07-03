using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Administration;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._NC.ZLevels.Mapping.Commands;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed class CELoadZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IResourceManager _resourceMgr = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly CEZLevelsSystem _zLevel = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override string Command => "znetwork-load";
    public override string Description => "Load all zNetwork maps from a saved folder";
    public override string Help => "znetwork-load <folder name>";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var savesPath = new ResPath("/ZNetworkSaves");
            if (!_resourceMgr.UserData.IsDir(savesPath))
                return CompletionResult.Empty;

            var options = _resourceMgr.UserData.DirectoryEntries(savesPath)
                .Select(d => new CompletionOption(d))
                .ToList();

            return CompletionResult.FromHintOptions(options, "Save folder name");
        }
        return CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        var saveName = args[0];
        var folder = new ResPath("/ZNetworkSaves") / saveName;

        if (!_resourceMgr.UserData.IsDir(folder))
        {
            shell.WriteError($"Directory {folder} does not exist in UserData.");
            return;
        }

        var entries = _resourceMgr.UserData.DirectoryEntries(folder).ToList();
        if (entries.Count == 0)
        {
            shell.WriteError($"No files found in {folder}.");
            return;
        }

        // Regex to match the filename pattern: {name}{depth}.yml
        var regex = new Regex($@"^{Regex.Escape(saveName)}(?<depth>-?\d+)\.yml$");
        var mapsToLoad = new Dictionary<ResPath, int>();

        foreach (var entry in entries)
        {
            var match = regex.Match(entry);
            if (!match.Success)
                continue;

            if (int.TryParse(match.Groups["depth"].Value, out var depth))
            {
                mapsToLoad.Add(folder / entry, depth);
            }
        }

        if (mapsToLoad.Count == 0)
        {
            shell.WriteError($"No valid z-network map files found in {folder} matching the pattern {saveName}{{depth}}.yml");
            return;
        }

        // Create the network entity
        var network = _zLevel.CreateZNetwork();
        _meta.SetEntityName(network, $"z-Network: {saveName}");

        var loadedMaps = new Dictionary<EntityUid, int>();
        var opts = new DeserializationOptions { StoreYamlUids = true };

        foreach (var (path, depth) in mapsToLoad.OrderBy(kv => kv.Value))
        {
            shell.WriteLine($"Loading map for depth {depth} from {path}...");
            
            if (!_mapLoader.TryLoadMap(path, out var mapEnt, out _, opts))
            {
                shell.WriteError($"Failed to load map from {path}!");
                continue;
            }

            if (!_entities.TryGetComponent<MapComponent>(mapEnt.Value, out var mapComp))
            {
                shell.WriteError($"Loaded entity {mapEnt.Value} doesn't have MapComponent.");
                _entities.QueueDeleteEntity(mapEnt.Value);
                continue;
            }

            loadedMaps.Add(mapEnt.Value, depth);
            _meta.SetEntityName(mapEnt.Value, $"{saveName} [{depth}]");
        }

        if (loadedMaps.Count > 0)
        {
            if (_zLevel.TryAddMapsIntoZNetwork(network, loadedMaps))
            {
                shell.WriteLine($"Successfully loaded z-network '{saveName}' with {loadedMaps.Count} maps.");
                shell.WriteLine($"ZNetwork Entity: {_entities.GetNetEntity(network)}");
                shell.WriteLine("Use 'znetwork-initialize' to initialize the maps if needed.");
            }
            else
            {
                shell.WriteError("Failed to add loaded maps to the z-network component.");
            }
        }
        else
        {
            shell.WriteError("No maps were successfully loaded.");
            _entities.QueueDeleteEntity(network);
        }
    }
}
