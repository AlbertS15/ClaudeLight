using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lumi;

/// Lumi, the glowing ghost, on the same grid as the Mac version: B body, E eyes and mouth.
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
    private static readonly Brush HatBrush = new SolidColorBrush(Color.FromRgb(92, 51, 133));
    private static readonly Brush BandBrush = new SolidColorBrush(Pumpkin);

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

    public static readonly Color Teal = Color.FromRgb(93, 202, 165);

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

    public static DrawingImage Image(int step, Brush body, Brush? eyes)
    {
        var bodyGeometry = new GeometryGroup();
        var eyeGeometry = new GeometryGroup();
        foreach (var (x, y, isEye) in Pixels(step))
            (isEye ? eyeGeometry : bodyGeometry).Children.Add(new RectangleGeometry(new Rect(x, y, 1.02, 1.02)));
        var group = new DrawingGroup();
        // A transparent frame keeps both poses the same size, so the image never jumps.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Width, Height))));
        group.Children.Add(new GeometryDrawing(body, null, bodyGeometry));
        if (eyes != null) group.Children.Add(new GeometryDrawing(eyes, null, eyeGeometry));
        var hat = new GeometryGroup();
        var band = new GeometryGroup();
        foreach (var (x, y, isBand) in HatPixels(step))
            (isBand ? band : hat).Children.Add(new RectangleGeometry(new Rect(x, y, 1.02, 1.02)));
        group.Children.Add(new GeometryDrawing(HatBrush, null, hat));
        group.Children.Add(new GeometryDrawing(BandBrush, null, band));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// The tray icon: the teal ghost with dark eyes on a transparent square.
    public static System.Drawing.Icon TrayIcon()
    {
        const int size = 32;
        using var bitmap = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.Transparent);
            var u = size / (float)Width;
            var top = (size - u * 12) / 2;
            using var body = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(93, 202, 165));
            using var eye = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            foreach (var (x, y, isEye) in Pixels(withHat: false))
                g.FillRectangle(isEye ? eye : body, x * u, top + y * u, u + 0.3f, u + 0.3f);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}

/// Lumi as a WPF element; it walks while IsWalking is true.
public sealed class MascotView : System.Windows.Controls.Image
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Brush _body;
    private readonly Brush? _eyes;
    private int _step;

    public MascotView(Brush? body = null, Brush? eyes = null)
    {
        _body = body ?? new SolidColorBrush(Mascot.Teal);
        _eyes = eyes ?? Brushes.Black;
        Stretch = Stretch.Uniform;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Source = Mascot.Image(0, _body, _eyes);
        _timer.Tick += (_, _) =>
        {
            _step = 1 - _step;
            Source = Mascot.Image(_step, _body, _eyes);
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
                Source = Mascot.Image(0, _body, _eyes);
            }
        }
    }
}
