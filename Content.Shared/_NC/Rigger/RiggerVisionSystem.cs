using Content.Shared._NC.Rigger.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Threading;

namespace Content.Shared._NC.Rigger;

/// <summary>
/// Computes visible tiles for a rigger eye using only its linked drones as vision seeds.
/// </summary>
public sealed class RiggerVisionSystem : EntitySystem
{
    [Dependency] private readonly IParallelManager _parallel = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;

    private readonly HashSet<Entity<OccluderComponent>> _occluders = new();
    private readonly HashSet<Vector2i> _viewportTiles = new();
    private readonly HashSet<Vector2i> _opaque = new();
    private readonly HashSet<Vector2i> _singleTiles = new();
    private readonly List<Entity<RiggerDroneComponent>> _seeds = new();

    private EntityQuery<OccluderComponent> _occluderQuery;
    private ViewJob _job;

    private bool _fastPath;

    public override void Initialize()
    {
        _occluderQuery = GetEntityQuery<OccluderComponent>();
        _job = new ViewJob
        {
            EntManager = EntityManager,
            Maps = _maps,
            System = this,
        };
    }

    public void GetView(EntityUid viewer, Entity<BroadphaseComponent, MapGridComponent> grid, Box2Rotated worldBounds, HashSet<Vector2i> visibleTiles, float expansionSize = 8.5f)
    {
        if (!TryComp<RiggerConsoleUserComponent>(viewer, out var user))
            return;

        _viewportTiles.Clear();
        _opaque.Clear();
        _seeds.Clear();

        var invMatrix = _xforms.GetInvWorldMatrix(grid);
        var localAabb = invMatrix.TransformBox(worldBounds);
        var enlargedLocalAabb = invMatrix.TransformBox(worldBounds.Enlarged(expansionSize));
        _fastPath = false;

        foreach (var droneUid in user.LinkedDrones)
        {
            if (!TryComp<RiggerDroneComponent>(droneUid, out var drone) ||
                !drone.Enabled ||
                Transform(droneUid).GridUid != grid.Owner)
            {
                continue;
            }

            _seeds.Add((droneUid, drone));
        }

        if (_seeds.Count == 0)
            return;

        var tileEnumerator = _maps.GetLocalTilesEnumerator(grid, grid, localAabb, ignoreEmpty: false);
        while (tileEnumerator.MoveNext(out var tileRef))
        {
            if (IsOccluded(grid, tileRef.GridIndices))
                _opaque.Add(tileRef.GridIndices);

            _viewportTiles.Add(tileRef.GridIndices);
        }

        tileEnumerator = _maps.GetLocalTilesEnumerator(grid, grid, enlargedLocalAabb, ignoreEmpty: false);
        while (tileEnumerator.MoveNext(out var tileRef))
        {
            if (_viewportTiles.Contains(tileRef.GridIndices))
                continue;

            if (IsOccluded(grid, tileRef.GridIndices))
                _opaque.Add(tileRef.GridIndices);
        }

        EnsureJobCapacity();
        _job.Grid = (grid.Owner, grid.Comp2);
        _job.VisibleTiles = visibleTiles;
        _job.Data.Clear();
        _job.Data.AddRange(_seeds);
        _parallel.ProcessNow(_job, _job.Data.Count);
    }

    public bool IsAccessible(EntityUid viewer, Entity<BroadphaseComponent, MapGridComponent> grid, Vector2i tile, float expansionSize = 8.5f)
    {
        if (!TryComp<RiggerConsoleUserComponent>(viewer, out var user))
            return false;

        _viewportTiles.Clear();
        _opaque.Clear();
        _seeds.Clear();
        _singleTiles.Clear();
        _viewportTiles.Add(tile);
        _fastPath = true;

        var localBounds = _lookup.GetLocalBounds(tile, grid.Comp2.TileSize);
        var expandedBounds = localBounds.Enlarged(expansionSize);

        foreach (var droneUid in user.LinkedDrones)
        {
            if (!TryComp<RiggerDroneComponent>(droneUid, out var drone) ||
                !drone.Enabled ||
                Transform(droneUid).GridUid != grid.Owner ||
                !_lookup.GetWorldAABB(droneUid).Intersects(expandedBounds))
            {
                continue;
            }

            _seeds.Add((droneUid, drone));
        }

        if (_seeds.Count == 0)
            return false;

        EnsureJobCapacity();
        _job.Grid = (grid.Owner, grid.Comp2);
        _job.VisibleTiles = _singleTiles;
        _job.Data.Clear();
        _job.Data.AddRange(_seeds);
        _parallel.ProcessNow(_job, _job.Data.Count);

        return _singleTiles.Contains(tile);
    }

    private void EnsureJobCapacity()
    {
        for (var i = _job.Vis1.Count; i < _seeds.Count; i++)
        {
            _job.Vis1.Add(new Dictionary<Vector2i, int>());
            _job.Vis2.Add(new Dictionary<Vector2i, int>());
            _job.SeedTiles.Add(new HashSet<Vector2i>());
            _job.BoundaryTiles.Add(new HashSet<Vector2i>());
        }
    }

    private bool IsOccluded(Entity<BroadphaseComponent, MapGridComponent> grid, Vector2i tile)
    {
        var tileBounds = _lookup.GetLocalBounds(tile, grid.Comp2.TileSize).Enlarged(-0.05f);
        _occluders.Clear();
        _lookup.GetLocalEntitiesIntersecting((grid.Owner, grid.Comp1), tileBounds, _occluders, query: _occluderQuery, flags: LookupFlags.Static | LookupFlags.Approximate);

        foreach (var occluder in _occluders)
        {
            if (occluder.Comp.Enabled)
                return true;
        }

        return false;
    }

    private int GetMaxDelta(Vector2i tile, Vector2i center)
    {
        var delta = tile - center;
        return Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
    }

    private int GetSumDelta(Vector2i tile, Vector2i center)
    {
        var delta = tile - center;
        return Math.Abs(delta.X) + Math.Abs(delta.Y);
    }

    private bool CheckNeighborsVis(Dictionary<Vector2i, int> vis, Vector2i index, int d)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var neighbor = index + new Vector2i(x, y);
                if (vis.GetValueOrDefault(neighbor) == d)
                    return true;
            }
        }

        return false;
    }

    private bool IsCorner(HashSet<Vector2i> tiles, HashSet<Vector2i> blocked, Dictionary<Vector2i, int> vis1, Vector2i index, Vector2i delta)
    {
        var diagonalIndex = index + delta;
        if (!tiles.TryGetValue(diagonalIndex, out var diagonal))
            return false;

        var cardinal1 = new Vector2i(index.X, diagonal.Y);
        var cardinal2 = new Vector2i(diagonal.X, index.Y);

        return vis1.GetValueOrDefault(diagonal) != 0 &&
               vis1.GetValueOrDefault(cardinal1) != 0 &&
               vis1.GetValueOrDefault(cardinal2) != 0 &&
               blocked.Contains(cardinal1) &&
               blocked.Contains(cardinal2) &&
               !blocked.Contains(diagonal);
    }

    private struct ViewJob : IParallelRobustJob
    {
        public int BatchSize => 1;

        public ViewJob()
        {
        }

        public required IEntityManager EntManager;
        public required SharedMapSystem Maps;
        public required RiggerVisionSystem System;

        public Entity<MapGridComponent> Grid;
        public List<Entity<RiggerDroneComponent>> Data = new();
        public HashSet<Vector2i> VisibleTiles = new();

        public readonly List<Dictionary<Vector2i, int>> Vis1 = new();
        public readonly List<Dictionary<Vector2i, int>> Vis2 = new();
        public readonly List<HashSet<Vector2i>> SeedTiles = new();
        public readonly List<HashSet<Vector2i>> BoundaryTiles = new();

        public void Execute(int index)
        {
            var seed = Data[index];
            var seedXform = EntManager.GetComponent<TransformComponent>(seed);

            if (!seed.Comp.Occluded || System._fastPath)
            {
                var rangeTiles = Maps.GetLocalTilesIntersecting(Grid.Owner,
                    Grid.Comp,
                    new Circle(System._xforms.GetWorldPosition(seedXform), seed.Comp.VisionRange), ignoreEmpty: false);

                lock (VisibleTiles)
                {
                    foreach (var tile in rangeTiles)
                    {
                        VisibleTiles.Add(tile.GridIndices);
                    }
                }

                return;
            }

            var range = seed.Comp.VisionRange;
            var vis1 = Vis1[index];
            var vis2 = Vis2[index];
            var seedTiles = SeedTiles[index];
            var boundary = BoundaryTiles[index];

            vis1.Clear();
            vis2.Clear();
            seedTiles.Clear();
            boundary.Clear();

            var maxDepthMax = 0;
            var sumDepthMax = 0;
            var eyePos = Maps.GetTileRef(Grid.Owner, Grid, seedXform.Coordinates).GridIndices;

            for (var x = Math.Floor(eyePos.X - range); x <= eyePos.X + range; x++)
            {
                for (var y = Math.Floor(eyePos.Y - range); y <= eyePos.Y + range; y++)
                {
                    var tile = new Vector2i((int) x, (int) y);
                    var delta = tile - eyePos;
                    var xDelta = Math.Abs(delta.X);
                    var yDelta = Math.Abs(delta.Y);

                    var deltaSum = xDelta + yDelta;
                    maxDepthMax = Math.Max(maxDepthMax, Math.Max(xDelta, yDelta));
                    sumDepthMax = Math.Max(sumDepthMax, deltaSum);
                    seedTiles.Add(tile);
                }
            }

            for (var d = 0; d < maxDepthMax; d++)
            {
                foreach (var tile in seedTiles)
                {
                    if (System.GetMaxDelta(tile, eyePos) == d + 1 && System.CheckNeighborsVis(vis2, tile, d))
                        vis2[tile] = System._opaque.Contains(tile) ? -1 : d + 1;
                }
            }

            for (var d = 0; d < sumDepthMax; d++)
            {
                foreach (var tile in seedTiles)
                {
                    if (System.GetSumDelta(tile, eyePos) != d + 1 || !System.CheckNeighborsVis(vis1, tile, d))
                        continue;

                    if (System._opaque.Contains(tile))
                    {
                        vis1[tile] = -1;
                    }
                    else if (vis2.GetValueOrDefault(tile) != 0)
                    {
                        vis1[tile] = d + 1;
                    }
                }
            }

            vis1[eyePos] = 1;

            foreach (var tile in seedTiles)
            {
                vis2[tile] = vis1.GetValueOrDefault(tile, 0);
            }

            foreach (var tile in seedTiles)
            {
                if (!System._opaque.Contains(tile) || vis1.GetValueOrDefault(tile) != 0)
                    continue;

                if (System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.UpRight) ||
                    System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.UpLeft) ||
                    System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.DownLeft) ||
                    System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.DownRight))
                {
                    boundary.Add(tile);
                }
            }

            foreach (var tile in boundary)
            {
                vis1[tile] = -1;
            }

            foreach (var tile in seedTiles)
            {
                if (!System._viewportTiles.Contains(tile))
                    continue;

                if (vis1.GetValueOrDefault(tile, 0) == 0)
                    continue;

                lock (VisibleTiles)
                {
                    VisibleTiles.Add(tile);
                }
            }
        }
    }
}
