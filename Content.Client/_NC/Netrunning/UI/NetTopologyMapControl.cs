using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._NC.Netrunning.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._NC.Netrunning.UI;

/// <summary>
///     Lightweight topology canvas for local NET administration.
///     It does not depend on the actual digital grid being in client PVS.
/// </summary>
public sealed class NetTopologyMapControl : Control
{
    public event Action<Vector2i>? OnTilePressed;

    private readonly List<NetTopologyMapEntry> _entries = new();

    private Vector2i _minTile = new(-4, -4);
    private Vector2i _maxTile = new(4, 4);
    private Vector2i? _hoveredTile;
    private NetEntity? _selectedUid;
    private float _pulse;

    public NetTopologyMapControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MinSize = new Vector2(320f, 320f);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetTopology(Vector2i minTile, Vector2i maxTile, IReadOnlyList<NetTopologyMapEntry> entries, NetEntity? selectedUid)
    {
        _minTile = minTile;
        _maxTile = maxTile;
        _selectedUid = selectedUid;
        _entries.Clear();
        _entries.AddRange(entries);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _pulse += args.DeltaSeconds * 2.6f;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _hoveredTile = TryGetTileAt(args.RelativePosition, out var tile) ? tile : null;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (!TryGetTileAt(args.RelativePosition, out var tile))
            return;

        OnTilePressed?.Invoke(tile);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Color.FromHex("#04090d"));

        if (!TryGetLayout(out var origin, out var cellSize, out var width, out var height))
            return;

        DrawGrid(handle, origin, cellSize, width, height);

        if (_hoveredTile is { } hovered)
            DrawTileOverlay(handle, hovered, origin, cellSize, Color.FromHex("#2f9fd3").WithAlpha(0.16f), Color.FromHex("#53d9ff"));

        foreach (var entry in _entries)
        {
            DrawOccupiedTile(handle, entry, origin, cellSize);
        }
    }

    private bool TryGetTileAt(Vector2 localPosition, out Vector2i tile)
    {
        tile = default;

        if (!TryGetLayout(out var origin, out var cellSize, out var width, out var height))
            return false;

        var relative = localPosition - origin;
        if (relative.X < 0f || relative.Y < 0f)
            return false;

        var tileX = (int) (relative.X / cellSize);
        var tileY = (int) (relative.Y / cellSize);
        if (tileX < 0 || tileY < 0 || tileX >= width || tileY >= height)
            return false;

        tile = new Vector2i(_minTile.X + tileX, _maxTile.Y - tileY);
        return true;
    }

    private bool TryGetLayout(out Vector2 origin, out float cellSize, out int width, out int height)
    {
        origin = Vector2.Zero;
        cellSize = 0f;
        width = Math.Max(1, _maxTile.X - _minTile.X + 1);
        height = Math.Max(1, _maxTile.Y - _minTile.Y + 1);

        const float padding = 12f;
        var usableWidth = Math.Max(1f, Width - padding * 2f);
        var usableHeight = Math.Max(1f, Height - padding * 2f);
        cellSize = MathF.Floor(MathF.Min(usableWidth / width, usableHeight / height));
        if (cellSize < 10f)
            cellSize = MathF.Min(usableWidth / width, usableHeight / height);

        if (cellSize <= 0f)
            return false;

        var mapSize = new Vector2(width * cellSize, height * cellSize);
        origin = new Vector2((Width - mapSize.X) * 0.5f, (Height - mapSize.Y) * 0.5f);
        return true;
    }

    private void DrawGrid(DrawingHandleScreen handle, Vector2 origin, float cellSize, int width, int height)
    {
        var gridColor = Color.FromHex("#17303c");
        var axisColor = Color.FromHex("#2d6477");

        for (var x = 0; x <= width; x++)
        {
            var lineX = origin.X + x * cellSize;
            var color = _minTile.X + x == 0 ? axisColor : gridColor;
            handle.DrawLine(new Vector2(lineX, origin.Y), new Vector2(lineX, origin.Y + height * cellSize), color);
        }

        for (var y = 0; y <= height; y++)
        {
            var lineY = origin.Y + y * cellSize;
            var color = _maxTile.Y - y + 1 == 0 ? axisColor : gridColor;
            handle.DrawLine(new Vector2(origin.X, lineY), new Vector2(origin.X + width * cellSize, lineY), color);
        }
    }

    private void DrawOccupiedTile(DrawingHandleScreen handle, NetTopologyMapEntry entry, Vector2 origin, float cellSize)
    {
        if (!TryGetTileRect(entry.Tile, origin, cellSize, out var rect))
            return;

        var fillColor = GetEntryColor(entry.Class);
        var isSelected = _selectedUid is { } selected && entry.Uid == selected;
        var pulse = 0.68f + 0.22f * MathF.Sin(_pulse);

        var margin = MathF.Max(2f, cellSize * 0.18f);
        var inner = new UIBox2(rect.Left + margin, rect.Top + margin, rect.Right - margin, rect.Bottom - margin);
        handle.DrawRect(inner, fillColor.WithAlpha(0.82f));
        handle.DrawRect(rect, fillColor.WithAlpha(isSelected ? pulse : 0.55f), false);

        if (isSelected)
        {
            var glow = new UIBox2(rect.Left - 2f, rect.Top - 2f, rect.Right + 2f, rect.Bottom + 2f);
            handle.DrawRect(glow, Color.FromHex("#ffd27a").WithAlpha(pulse), false);
        }
    }

    private void DrawTileOverlay(DrawingHandleScreen handle, Vector2i tile, Vector2 origin, float cellSize, Color fill, Color border)
    {
        if (!TryGetTileRect(tile, origin, cellSize, out var rect))
            return;

        handle.DrawRect(rect, fill);
        handle.DrawRect(rect, border, false);
    }

    private bool TryGetTileRect(Vector2i tile, Vector2 origin, float cellSize, out UIBox2 rect)
    {
        rect = default;

        if (tile.X < _minTile.X || tile.X > _maxTile.X || tile.Y < _minTile.Y || tile.Y > _maxTile.Y)
            return false;

        var cellX = tile.X - _minTile.X;
        var cellY = _maxTile.Y - tile.Y;
        var left = origin.X + cellX * cellSize;
        var top = origin.Y + cellY * cellSize;
        rect = new UIBox2(left, top, left + cellSize, top + cellSize);
        return true;
    }

    private static Color GetEntryColor(string entryClass)
    {
        if (entryClass.Contains("ДВЕР", StringComparison.OrdinalIgnoreCase))
            return Color.FromHex("#ff9d57");

        if (entryClass.Contains("КАМЕР", StringComparison.OrdinalIgnoreCase))
            return Color.FromHex("#59d0ff");

        if (entryClass.Contains("СВЕТ", StringComparison.OrdinalIgnoreCase))
            return Color.FromHex("#ffe46b");

        if (entryClass.Contains("ЛЁД", StringComparison.OrdinalIgnoreCase) ||
            entryClass.Contains("ЛЕД", StringComparison.OrdinalIgnoreCase))
            return Color.FromHex("#ff5678");

        return Color.FromHex("#72f0a3");
    }
}
