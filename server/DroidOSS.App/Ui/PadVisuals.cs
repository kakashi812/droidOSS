using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DroidOSS.Core;

namespace DroidOSS.App.Ui;

/// <summary>
/// The Xbox 360 ring of light: four quarters, with the player's own lit, so a
/// card says which controller it is the same way the controller would.
/// </summary>
internal sealed class PlayerRing : FrameworkElement
{
    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(
        nameof(Slot), typeof(int), typeof(PlayerRing),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LitProperty = DependencyProperty.Register(
        nameof(Lit), typeof(bool), typeof(PlayerRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Slot { get => (int)GetValue(SlotProperty); set => SetValue(SlotProperty, value); }

    /// <summary>On when a phone holds this slot; otherwise all four quarters stay dim.</summary>
    public bool Lit { get => (bool)GetValue(LitProperty); set => SetValue(LitProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - 2;
        var dim = new Pen(Res("Border"), 3);
        var lit = new Pen(Res("Good"), 3.5);

        // Quarters clockwise from top-left, as on the controller: 1 TL, 2 TR, 4 BR, 3 BL.
        int[] quarterForSlot = [0, 1, 3, 2];
        for (var q = 0; q < 4; q++)
        {
            var start = 180 + q * 90 + 12;
            var end = start + 66;
            var isLit = Lit && quarterForSlot[Slot] == q;
            dc.DrawGeometry(null, isLit ? lit : dim, Arc(centre, radius, start, end));
        }
    }

    private static Geometry Arc(Point centre, double radius, double fromDeg, double toDeg)
    {
        Point At(double deg) => new(
            centre.X + radius * Math.Cos(deg * Math.PI / 180),
            centre.Y + radius * Math.Sin(deg * Math.PI / 180));

        var figure = new PathFigure { StartPoint = At(fromDeg) };
        figure.Segments.Add(new ArcSegment(At(toDeg), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>
/// A live drawing of one pad: sticks, triggers, D-pad, face and shoulder
/// buttons, lit as the phone presses them. Developer mode only — it answers
/// "is this phone actually sending what I think it is?" at a glance.
/// </summary>
internal sealed class InputPreview : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PadState), typeof(InputPreview),
        new FrameworkPropertyMetadata(default(PadState), FrameworkPropertyMetadataOptions.AffectsRender));

    public PadState State { get => (PadState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 320 : available.Width, 132);

    protected override void OnRender(DrawingContext dc)
    {
        var s = State;
        var w = ActualWidth;
        if (w <= 0) return;

        var line = new Pen(Res("Border"), 1.5);
        var off = Res("SurfaceAlt");
        var on = Res("Accent");
        var text = Res("TextMuted");

        // Layout scales with the card's width; the height is fixed.
        var u = Math.Min(w / 320, 1.4);
        double X(double x) => (w - 320 * u) / 2 + x * u;

        // ── triggers and shoulders along the top ───────────────────────
        TriggerBar(dc, new Rect(X(14), 6, 56 * u, 10), s.LeftTrigger, "LT", line, off, on, text);
        TriggerBar(dc, new Rect(X(250), 6, 56 * u, 10), s.RightTrigger, "RT", line, off, on, text);
        Pill(dc, new Rect(X(80), 4, 40 * u, 14), s.IsPressed(GamepadButtons.LeftShoulder), "LB", line, off, on, text);
        Pill(dc, new Rect(X(200), 4, 40 * u, 14), s.IsPressed(GamepadButtons.RightShoulder), "RB", line, off, on, text);

        // ── sticks ─────────────────────────────────────────────────────
        Stick(dc, new Point(X(52), 70), 30 * u, s.ThumbLX, s.ThumbLY, s.IsPressed(GamepadButtons.LeftThumb), line, off, on);
        Stick(dc, new Point(X(200), 100), 24 * u, s.ThumbRX, s.ThumbRY, s.IsPressed(GamepadButtons.RightThumb), line, off, on);

        // ── D-pad ──────────────────────────────────────────────────────
        var d = new Point(X(118), 100);
        var arm = 9 * u;
        DpadArm(dc, new Rect(d.X - arm / 2, d.Y - arm * 1.6, arm, arm), s.IsPressed(GamepadButtons.DPadUp), line, off, on);
        DpadArm(dc, new Rect(d.X - arm / 2, d.Y + arm * 0.6, arm, arm), s.IsPressed(GamepadButtons.DPadDown), line, off, on);
        DpadArm(dc, new Rect(d.X - arm * 1.6, d.Y - arm / 2, arm, arm), s.IsPressed(GamepadButtons.DPadLeft), line, off, on);
        DpadArm(dc, new Rect(d.X + arm * 0.6, d.Y - arm / 2, arm, arm), s.IsPressed(GamepadButtons.DPadRight), line, off, on);

        // ── back, guide, start ─────────────────────────────────────────
        Pill(dc, new Rect(X(132), 40, 22 * u, 12), s.IsPressed(GamepadButtons.Back), "", line, off, on, text);
        Pill(dc, new Rect(X(166), 40, 22 * u, 12), s.IsPressed(GamepadButtons.Start), "", line, off, on, text);

        // ── face buttons, in their colours when pressed ────────────────
        var f = new Point(X(268), 66);
        var gap = 17 * u;
        Face(dc, new Point(f.X, f.Y - gap), "Y", s.IsPressed(GamepadButtons.Y), Res("PadY"), line, off, text, u);
        Face(dc, new Point(f.X - gap, f.Y), "X", s.IsPressed(GamepadButtons.X), Res("PadX"), line, off, text, u);
        Face(dc, new Point(f.X + gap, f.Y), "B", s.IsPressed(GamepadButtons.B), Res("PadB"), line, off, text, u);
        Face(dc, new Point(f.X, f.Y + gap), "A", s.IsPressed(GamepadButtons.A), Res("PadA"), line, off, text, u);
    }

    private void TriggerBar(DrawingContext dc, Rect r, byte value, string label, Pen line, Brush off, Brush on, Brush text)
    {
        dc.DrawRoundedRectangle(off, line, r, 4, 4);
        if (value > 0)
        {
            var fill = new Rect(r.X, r.Y, r.Width * value / 255.0, r.Height);
            dc.DrawRoundedRectangle(on, null, fill, 4, 4);
        }
        Label(dc, label, new Point(r.X + r.Width / 2, r.Bottom + 7), text, 9);
    }

    private void Pill(DrawingContext dc, Rect r, bool pressed, string label, Pen line, Brush off, Brush on, Brush text)
    {
        dc.DrawRoundedRectangle(pressed ? on : off, line, r, r.Height / 2, r.Height / 2);
        if (label.Length > 0)
            Label(dc, label, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), pressed ? Res("AccentText") : text, 9);
    }

    private static void Stick(DrawingContext dc, Point c, double radius, short x, short y, bool clicked, Pen line, Brush off, Brush on)
    {
        dc.DrawEllipse(off, clicked ? new Pen(on, 2.5) : line, c, radius, radius);
        // Positive Y is up on the wire; screens count down.
        var dot = new Point(c.X + x / 32768.0 * radius * 0.75, c.Y - y / 32768.0 * radius * 0.75);
        dc.DrawEllipse(on, null, dot, radius * 0.32, radius * 0.32);
    }

    private static void DpadArm(DrawingContext dc, Rect r, bool pressed, Pen line, Brush off, Brush on) =>
        dc.DrawRoundedRectangle(pressed ? on : off, line, r, 2, 2);

    private void Face(DrawingContext dc, Point c, string label, bool pressed, Brush colour, Pen line, Brush off, Brush text, double u)
    {
        var r = 8 * u;
        dc.DrawEllipse(pressed ? colour : off, pressed ? null : line, c, r, r);
        Label(dc, label, c, pressed ? Brushes.White : text, 9 * u);
    }

    private void Label(DrawingContext dc, string s, Point centre, Brush brush, double size)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(centre.X - ft.Width / 2, centre.Y - ft.Height / 2));
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
