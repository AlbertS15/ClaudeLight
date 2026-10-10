using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Lumi;

/// Lumi, the glowing ghost, on the same grid as the Mac and Windows versions: B body, E eyes and mouth.
public static class Mascot
{
    public const int Width = 14;
    public static int Height => IsHalloween ? 12 + Hat.Length : 12;

    /// From October 24 to November 1 Lumi wears a witch's hat and the accent turns pumpkin orange.
    /// LUMI_HALLOWEEN=1 forces it on (and =0 off), to try it any day.
    public static readonly bool IsHalloween = Environment.GetEnvironmentVariable("LUMI_HALLOWEEN") is { } forced
        ? forced == "1"
        : (DateTime.Now.Month == 10 && DateTime.Now.Day >= 24) || (DateTime.Now.Month == 11 && DateTime.Now.Day == 1);

    /// The hat: H crown and brim, O the orange band.
    private static readonly string[] Hat =
    {
        "........HH....",
        ".......HHH....",
        "......HHHH....",
        ".....OOOOOO...",
        "..HHHHHHHHHH..",
    };

    public static readonly Color Pumpkin = Color.FromRgb(255, 138, 40);
    public static readonly Color HatColor = Color.FromRgb(92, 51, 133);
    public static readonly Color Teal = Color.FromRgb(93, 202, 165);

    private static readonly string[] Rows =
    {
        "....BBBBBB....",
        "..BBBBBBBBBB..",
        ".BBBBBBBBBBBB.",
        ".BBBBBBBBBBBB.",
        "BBBEEBBBBEEBBB",
        "BBBEEBBBBEEBBB",
        "BBBBBBBBBBBBBB",
        "BBBBBBEEBBBBBB",
        "BBBBBBBBBBBBBB",
        "BBBBBBBBBBBBBB",
    };

    private static readonly string[] Hems = { "BB..BBBBBB..BB", "..BBBB..BBBB.." };

    /// Lit pixels as (column, row, isEye); `step` 1 is the floating pose, risen a pixel with the hem rippled.
    public static IEnumerable<(int X, int Y, bool IsEye)> Pixels(int step = 0, bool withHat = true)
    {
        var lift = step == 1 ? 0 : 1;
        var top = withHat && IsHalloween ? Hat.Length : 0;
        var lines = new List<string>(Rows) { Hems[step] };
        for (var r = 0; r < lines.Count; r++)
            for (var c = 0; c < lines[r].Length; c++)
                if (lines[r][c] != '.') yield return (c, r + lift + top, lines[r][c] == 'E');
    }

    /// The hat's pixels as (column, row, isBand); none outside the Halloween week. It bobs with the ghost.
    public static IEnumerable<(int X, int Y, bool IsBand)> HatPixels(int step = 0)
    {
        if (!IsHalloween) yield break;
        var lift = step == 1 ? 0 : 1;
        for (var r = 0; r < Hat.Length; r++)
            for (var c = 0; c < Hat[r].Length; c++)
                if (Hat[r][c] != '.') yield return (c, r + lift, Hat[r][c] == 'O');
    }

    /// The window and tray icon: the teal ghost with dark eyes on a transparent square.
    public static WriteableBitmap Icon(int size = 64)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var u = size / Width;
        var top = (size - u * 12) / 2;
        var left = (size - u * Width) / 2;
        unsafe
        {
            var pixels = (uint*)buffer.Address;
            foreach (var (x, y, isEye) in Pixels(withHat: false))
            {
                var color = isEye ? 0xFF000000u : 0xFF000000u | ((uint)Teal.R << 16) | ((uint)Teal.G << 8) | Teal.B;
                for (var dy = 0; dy < u; dy++)
                    for (var dx = 0; dx < u; dx++)
                        pixels[(top + y * u + dy) * (buffer.RowBytes / 4) + left + x * u + dx] = color;
            }
        }
        return bitmap;
    }
}

/// Lumi as an Avalonia control, drawn pixel by pixel; it walks while IsWalking is true.
public sealed class MascotView : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly IBrush _body;
    private readonly IBrush _eyes;
    private int _step;

    public MascotView(IBrush? body = null, IBrush? eyes = null)
    {
        _body = body ?? new SolidColorBrush(Mascot.Teal);
        _eyes = eyes ?? Brushes.Black;
        _timer.Tick += (_, _) =>
        {
            _step = 1 - _step;
            InvalidateVisual();
        };
    }

    public bool IsWalking
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled) return;
            if (value) _timer.Start();
            else
            {
                _timer.Stop();
                _step = 0;
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var u = Math.Min(Bounds.Width / Mascot.Width, Bounds.Height / Mascot.Height);
        var left = (Bounds.Width - u * Mascot.Width) / 2;
        var top = (Bounds.Height - u * Mascot.Height) / 2;
        foreach (var (x, y, isEye) in Mascot.Pixels(_step))
            context.FillRectangle(isEye ? _eyes : _body, new Rect(left + x * u, top + y * u, u + 0.3, u + 0.3));
        var hat = new SolidColorBrush(Mascot.HatColor);
        var band = new SolidColorBrush(Mascot.Pumpkin);
        foreach (var (x, y, isBand) in Mascot.HatPixels(_step))
            context.FillRectangle(isBand ? band : hat, new Rect(left + x * u, top + y * u, u + 0.3, u + 0.3));
    }
}
