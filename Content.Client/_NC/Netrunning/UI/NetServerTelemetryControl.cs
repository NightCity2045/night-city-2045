using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._NC.Netrunning.UI;

public sealed class NetServerTelemetryControl : Control
{
    private float _loadRatioTarget;
    private float _moduleRatioTarget;
    private float _deviceRatioTarget;

    private float _loadRatioCurrent;
    private float _moduleRatioCurrent;
    private float _deviceRatioCurrent;

    private float _pulse;
    private float _sweep;

    public bool HasDaemon;

    public void SetTelemetry(float loadRatio, float moduleRatio, float deviceRatio, bool hasDaemon)
    {
        _loadRatioTarget = Math.Clamp(loadRatio, 0f, 1f);
        _moduleRatioTarget = Math.Clamp(moduleRatio, 0f, 1f);
        _deviceRatioTarget = Math.Clamp(deviceRatio, 0f, 1f);
        HasDaemon = hasDaemon;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _pulse += args.DeltaSeconds * 1.8f;
        _sweep += args.DeltaSeconds * 42f;
        _loadRatioCurrent = MathHelper.Lerp(_loadRatioCurrent, _loadRatioTarget, 6f * args.DeltaSeconds);
        _moduleRatioCurrent = MathHelper.Lerp(_moduleRatioCurrent, _moduleRatioTarget, 6f * args.DeltaSeconds);
        _deviceRatioCurrent = MathHelper.Lerp(_deviceRatioCurrent, _deviceRatioTarget, 6f * args.DeltaSeconds);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var rect = PixelSizeBox;
        handle.DrawRect(rect, Color.FromHex("#071219"));

        var lineColor = Color.FromHex("#123847");
        for (var x = rect.Left; x < rect.Right; x += 12)
        {
            handle.DrawLine(new Vector2(x, rect.Top), new Vector2(x, rect.Bottom), lineColor.WithAlpha(0.35f));
        }

        for (var y = rect.Top; y < rect.Bottom; y += 10)
        {
            handle.DrawLine(new Vector2(rect.Left, y), new Vector2(rect.Right, y), lineColor.WithAlpha(0.25f));
        }

        var sweepX = rect.Left + (_sweep % Math.Max(1, rect.Width));
        handle.DrawRect(new UIBox2i((int) sweepX - 8, rect.Top, (int) sweepX + 8, rect.Bottom), Color.FromHex("#0d6f86").WithAlpha(0.08f));

        DrawBar(handle, rect.Left + 12, rect.Top + 14, rect.Width - 24, 12, _loadRatioCurrent, Color.FromHex("#00d5ff"));
        DrawBar(handle, rect.Left + 12, rect.Top + 38, rect.Width - 24, 10, _moduleRatioCurrent, Color.FromHex("#ffb347"));
        DrawBar(handle, rect.Left + 12, rect.Top + 58, rect.Width - 24, 8, _deviceRatioCurrent, Color.FromHex("#7dff8a"));

        var daemonColor = HasDaemon ? Color.FromHex("#ff4d6d") : Color.FromHex("#38464d");
        var pulseAlpha = HasDaemon ? 0.55f + 0.35f * MathF.Sin(_pulse) : 0.25f;
        handle.DrawCircle(new Vector2(rect.Right - 18, rect.Top + 18), 6f, daemonColor.WithAlpha(pulseAlpha));
        handle.DrawCircle(new Vector2(rect.Right - 18, rect.Top + 18), 9f, daemonColor.WithAlpha(pulseAlpha * 0.4f), false);
    }

    private static void DrawBar(DrawingHandleScreen handle, int x, int y, int width, int height, float fill, Color color)
    {
        var frame = new UIBox2i(x, y, x + width, y + height);
        handle.DrawRect(frame, Color.FromHex("#0e2028"));
        handle.DrawRect(frame, color.WithAlpha(0.6f), false);

        var filledWidth = Math.Max(0, (int) ((width - 2) * fill));
        if (filledWidth <= 0)
            return;

        var filled = new UIBox2i(x + 1, y + 1, x + 1 + filledWidth, y + height - 1);
        handle.DrawRect(filled, color.WithAlpha(0.9f));
    }
}
