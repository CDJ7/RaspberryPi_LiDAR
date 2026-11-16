using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using LidarPC.Api;

namespace LidarPC.Controls;

/// <summary>Event data for <see cref="LidarPlotControl.ObjectClicked"/> and <see cref="ObjectListControl.SelectedObjectChanged"/>.</summary>
public sealed class ObjectSelectedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public ObjectSelectedEventArgs(DetectedObject? selected) => Selected = selected;

    /// <summary>The selected object, or null when the selection was cleared.</summary>
    public DetectedObject? Selected { get; }
}

/// <summary>
/// Real-time top-down view of the LiDAR scan, drawn with GDI+ (no charting library).
/// Robot at the centre, forward (0 degrees) pointing UP, left on the LEFT of the screen.
/// </summary>
/// <remarks>
/// <para>Draws range rings with labels, the forward marker, the caution (yellow) and stop
/// (red) circles, the front sector when it is active, the points coloured by zone, the
/// closest point highlighted, and the selected object outlined. The mouse wheel zooms.</para>
/// <para><b>Designer-safe:</b> parameterless constructor, no timers or network, and a
/// placeholder drawing in design mode. Data properties are hidden from the Designer
/// (<see cref="DesignerSerializationVisibility.Hidden"/>) so it never tries to save a scan
/// into InitializeComponent.</para>
/// <para><b>Double-buffered:</b> the whole frame is drawn into an off-screen buffer and
/// copied in one go, so the 10 Hz redraw does not flicker.</para>
/// </remarks>
public class LidarPlotControl : Control
{
    private const double MinViewRangeM = 0.5, MaxViewRangeM = 12.0;

    private ScanResponse? _scan;
    private int? _selectedObjectId;
    private double _viewRangeM = 3.0;

    /// <summary>Creates the control with double buffering and a dark background.</summary>
    public LidarPlotControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(22, 24, 28);
        ForeColor = Color.Gainsboro;
    }

    /// <summary>Raised when the user clicks on (or near) an object in the plot.</summary>
    [Category("Action")]
    [Description("Raised when an object is clicked in the plot.")]
    public event EventHandler<ObjectSelectedEventArgs>? ObjectClicked;

    /// <summary>The scan to draw. Setting it repaints the control.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ScanResponse? Scan
    {
        get => _scan;
        set
        {
            _scan = value;
            Invalidate();
        }
    }

    /// <summary>ID of the object to outline, or null.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? SelectedObjectId
    {
        get => _selectedObjectId;
        set
        {
            _selectedObjectId = value;
            Invalidate();
        }
    }

    /// <summary>Distance from the centre to the nearest edge, in metres (the zoom level).</summary>
    [Category("LiDAR")]
    [Description("Metres shown from the robot to the nearest edge of the plot (zoom).")]
    [DefaultValue(3.0)]
    public double ViewRangeM
    {
        get => _viewRangeM;
        set
        {
            _viewRangeM = Math.Clamp(value, MinViewRangeM, MaxViewRangeM);
            Invalidate();
        }
    }

    /// <summary>Text drawn when there is no scan yet.</summary>
    [Category("LiDAR")]
    [Description("Text shown while no scan has been received.")]
    [DefaultValue("Not connected")]
    public string PlaceholderText { get; set; } = "Not connected";

    /// <summary>Mouse wheel: zoom in/out by ~15 % per notch around the robot.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ViewRangeM *= e.Delta > 0 ? 1 / 1.15 : 1.15;
    }

    /// <summary>Takes focus when the mouse enters, because WinForms only sends wheel events to the focused control.</summary>
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!DesignMode) Focus();
    }

    /// <summary>Click: selects the object whose outline is closest to the click (within ~15 px), or clears the selection.</summary>
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_scan is null) return;
        var (cx, cy, scale) = Geometry();
        DetectedObject? best = null;
        double bestDist = 15;
        foreach (DetectedObject o in _scan.Objects)
        {
            foreach (float[] p in o.Outline)
            {
                PointF s = ToScreen(p[0], p[1], cx, cy, scale);
                double d = Math.Sqrt(Math.Pow(s.X - e.X, 2) + Math.Pow(s.Y - e.Y, 2));
                if (d < bestDist) { bestDist = d; best = o; }
            }
        }
        SelectedObjectId = best?.Id;
        ObjectClicked?.Invoke(this, new ObjectSelectedEventArgs(best));
    }

    /// <summary>Draws one frame (see class remarks for the layers, in drawing order).</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var (cx, cy, scale) = Geometry();

        double stop = _scan?.StopDistanceM ?? 0.35;
        double caution = _scan?.CautionDistanceM ?? 0.80;

        DrawRings(g, cx, cy, scale);
        DrawSector(g, cx, cy);
        DrawThresholdCircle(g, cx, cy, scale, caution, ZoneColors.Strong(SafetyZone.Caution), "caution");
        DrawThresholdCircle(g, cx, cy, scale, stop, ZoneColors.Strong(SafetyZone.Stop), "stop");
        DrawRobot(g, cx, cy, scale);

        if (DesignMode || _scan is null)
        {
            string text = DesignMode ? "LidarPlotControl (live LiDAR view)" : PlaceholderText;
            TextRenderer.DrawText(g, text, Font, new Rectangle(0, 0, Width, Height), Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
            return;
        }

        DrawPoints(g, cx, cy, scale, stop, caution);
        DrawSelectedObject(g, cx, cy, scale);
        DrawClosest(g, cx, cy, scale);
        DrawLegend(g);
    }

    /// <summary>Centre of the plot and pixels per metre for the current size and zoom.</summary>
    private (float Cx, float Cy, float Scale) Geometry()
    {
        float cx = Width / 2f, cy = Height / 2f;
        float scale = (float)(Math.Max(10, Math.Min(Width, Height) / 2f - 12) / _viewRangeM);
        return (cx, cy, scale);
    }

    /// <summary>Robot frame (x forward, y left) to screen: forward is up, left is left.</summary>
    private static PointF ToScreen(double x, double y, float cx, float cy, float scale) =>
        new((float)(cx - y * scale), (float)(cy - x * scale));

    /// <summary>Range rings with labels, plus the forward and left axis markers.</summary>
    private void DrawRings(Graphics g, float cx, float cy, float scale)
    {
        // Ring spacing: 0.5 m normally, doubled when zoomed out so there are never more than ~8 rings.
        double spacing = 0.5;
        while (_viewRangeM / spacing > 8) spacing *= 2;

        using var ringPen = new Pen(Color.FromArgb(60, 66, 74)) { DashStyle = DashStyle.Dot };
        using var axisPen = new Pen(Color.FromArgb(70, 78, 88));
        using var labelBrush = new SolidBrush(Color.FromArgb(130, 140, 150));
        double maxR = Math.Sqrt(Width * Width + Height * Height) / 2 / scale;   // rings out to the corners
        for (double r = spacing; r <= maxR; r += spacing)
        {
            float pr = (float)(r * scale);
            g.DrawEllipse(ringPen, cx - pr, cy - pr, 2 * pr, 2 * pr);
            g.DrawString(r.ToString("0.0#", CultureInfo.InvariantCulture) + " m", Font, labelBrush, cx + pr * 0.707f + 2, cy - pr * 0.707f - 14);
        }

        g.DrawLine(axisPen, cx, 0, cx, Height);
        g.DrawLine(axisPen, 0, cy, Width, cy);

        // Forward marker: a triangle at the top edge.
        using var fwdBrush = new SolidBrush(Color.DeepSkyBlue);
        g.FillPolygon(fwdBrush, new[] { new PointF(cx, 4), new PointF(cx - 7, 18), new PointF(cx + 7, 18) });
        g.DrawString("0° FWD", Font, fwdBrush, cx + 10, 4);
        g.DrawString("90° L", Font, labelBrush, 4, cy + 2);
        g.DrawString("270° R", Font, labelBrush, Width - 48, cy + 2);
        g.DrawString("180°", Font, labelBrush, cx + 4, Height - 18);
    }

    /// <summary>Shades the front sector when only it is monitored.</summary>
    private void DrawSector(Graphics g, float cx, float cy)
    {
        if (_scan is null || !string.Equals(_scan.SectorMode, "Front", StringComparison.OrdinalIgnoreCase)) return;
        float r = (float)(Math.Max(Width, Height) * 1.5);
        float half = (float)_scan.FrontHalfAngleDeg;
        using var brush = new SolidBrush(Color.FromArgb(28, 0, 191, 255));
        // GDI+ angles: 0 = +X (screen right), clockwise. Robot forward is screen up = -90.
        g.FillPie(brush, cx - r, cy - r, 2 * r, 2 * r, -90 - half, 2 * half);
    }

    /// <summary>A threshold circle (stop or caution distance) with a small label.</summary>
    private void DrawThresholdCircle(Graphics g, float cx, float cy, float scale, double radiusM, Color color, string label)
    {
        float r = (float)(radiusM * scale);
        using var pen = new Pen(color, 2f) { DashStyle = DashStyle.Dash };
        g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
        using var brush = new SolidBrush(color);
        g.DrawString($"{label} {radiusM.ToString("0.00", CultureInfo.InvariantCulture)} m", Font, brush, cx - r * 0.707f - 70, cy + r * 0.707f);
    }

    /// <summary>The robot outline (about 30 cm square) at the centre, with a nose showing forward.</summary>
    private static void DrawRobot(Graphics g, float cx, float cy, float scale)
    {
        float s = Math.Max(8, 0.15f * scale);
        using var pen = new Pen(Color.DeepSkyBlue, 2f);
        g.DrawRectangle(pen, cx - s, cy - s, 2 * s, 2 * s);
        g.DrawLine(pen, cx, cy, cx, cy - s - 6);
    }

    /// <summary>Scan points, each coloured by the zone its distance falls in.</summary>
    private void DrawPoints(Graphics g, float cx, float cy, float scale, double stop, double caution)
    {
        using var clear = new SolidBrush(ZoneColors.Strong(SafetyZone.Clear));
        using var cautionBrush = new SolidBrush(ZoneColors.Strong(SafetyZone.Caution));
        using var stopBrush = new SolidBrush(ZoneColors.Strong(SafetyZone.Stop));
        foreach (float[] p in _scan!.Points)
        {
            double r = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
            Brush b = ZoneColors.ZoneFor(r, stop, caution) switch
            {
                SafetyZone.Stop => stopBrush,
                SafetyZone.Caution => cautionBrush,
                _ => clear,
            };
            PointF s = ToScreen(p[0], p[1], cx, cy, scale);
            g.FillRectangle(b, s.X - 1.5f, s.Y - 1.5f, 3f, 3f);
        }
    }

    /// <summary>Outline and label of the object selected in the sidebar or by clicking.</summary>
    private void DrawSelectedObject(Graphics g, float cx, float cy, float scale)
    {
        DetectedObject? o = _scan!.Objects.FirstOrDefault(x => x.Id == _selectedObjectId);
        if (o is null || o.Outline.Count == 0) return;
        PointF[] pts = o.Outline.Select(p => ToScreen(p[0], p[1], cx, cy, scale)).ToArray();
        using var pen = new Pen(Color.Cyan, 3f) { LineJoin = LineJoin.Round };
        if (pts.Length > 1) g.DrawLines(pen, pts);
        else g.DrawEllipse(pen, pts[0].X - 5, pts[0].Y - 5, 10, 10);
        PointF c = ToScreen(o.X, o.Y, cx, cy, scale);
        using var brush = new SolidBrush(Color.Cyan);
        g.DrawString($"#{o.Id}  {o.DistanceM:0.00} m", Font, brush, c.X + 8, c.Y + 6);
    }

    /// <summary>Highlights the closest obstacle in the monitoring sector with a ring and a line from the robot.</summary>
    private void DrawClosest(Graphics g, float cx, float cy, float scale)
    {
        StatusResponse s = _scan!.Status;
        if (s.ClosestM is not double d || s.BearingDeg is not double b) return;
        double rad = b * Math.PI / 180.0;
        PointF p = ToScreen(d * Math.Cos(rad), d * Math.Sin(rad), cx, cy, scale);
        Color color = ZoneColors.Strong(s.Zone);
        using var pen = new Pen(color, 2.5f);
        using var thin = new Pen(Color.FromArgb(150, color), 1f) { DashStyle = DashStyle.Dash };
        g.DrawLine(thin, cx, cy, p.X, p.Y);
        g.DrawEllipse(pen, p.X - 7, p.Y - 7, 14, 14);
        using var brush = new SolidBrush(Color.White);
        g.DrawString($"{d:0.00} m @ {b:000}°", Font, brush, p.X + 9, p.Y - 18);
    }

    /// <summary>Top-left info: zone, scan rate, point/object counts and zoom hint.</summary>
    private void DrawLegend(Graphics g)
    {
        ScanResponse s = _scan!;
        string text = $"{s.Points.Count} points   {s.Objects.Count} objects   {s.ScanRateHz:0.0} Hz\n"
                      + $"sector: {s.SectorMode}   view ±{_viewRangeM:0.0} m (mouse wheel = zoom)";
        TextRenderer.DrawText(g, text, Font, new Point(6, 24), Color.Silver);
    }
}
