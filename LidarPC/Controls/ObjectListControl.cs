using System.ComponentModel;
using System.Globalization;
using LidarPC.Api;

namespace LidarPC.Controls;

/// <summary>
/// Sidebar listing every detected object (ID, distance, bearing, width, points), closest
/// first, with each row coloured by its zone. Selecting a row raises
/// <see cref="SelectedObjectChanged"/> so the plot can highlight that object.
/// </summary>
/// <remarks>
/// <para>WHY a UserControl that CONTAINS a ListView, instead of deriving from ListView: the
/// Designer serialises a ListView's Columns into the form's InitializeComponent. Columns
/// created in our constructor would then be saved by the Designer as well and appear twice
/// at runtime. Child controls of a UserControl are never serialised by the parent form.</para>
/// <para>Rows are updated in place 10 times per second (no Clear/re-add) inside
/// BeginUpdate/EndUpdate, and the ListView is double-buffered, so the list does not flicker
/// and the user's selection survives the refresh.</para>
/// </remarks>
public class ObjectListControl : UserControl
{
    private readonly ListView _list;
    private readonly Label _header;
    private bool _updating;   // suppresses SelectedObjectChanged while rows are refreshed by code
    private IReadOnlyList<DetectedObject> _objects = [];

    /// <summary>Creates the header label and the five-column list.</summary>
    public ObjectListControl()
    {
        _header = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Detected objects (closest first)",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
        };
        _list = new DoubleBufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true,
        };
        _list.Columns.Add("ID", 40, HorizontalAlignment.Right);
        _list.Columns.Add("Dist (m)", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Bearing (°)", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Width (m)", 75, HorizontalAlignment.Right);
        _list.Columns.Add("Pts", 45, HorizontalAlignment.Right);
        _list.SelectedIndexChanged += OnListSelectionChanged;

        Controls.Add(_list);
        Controls.Add(_header);   // added last so it docks first (top), above the list
        Size = new Size(330, 300);
    }

    /// <summary>Raised when the user selects a different object (or clears the selection).</summary>
    [Category("Action")]
    [Description("Raised when the user selects an object in the list.")]
    public event EventHandler<ObjectSelectedEventArgs>? SelectedObjectChanged;

    /// <summary>ID of the selected object, or null.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? SelectedObjectId { get; private set; }

    /// <summary>Replaces the list contents with the latest objects and re-selects <paramref name="selectId"/>.</summary>
    /// <param name="objects">Objects, already sorted closest first by the Pi.</param>
    /// <param name="selectId">Object to keep selected (tracked by the form across scans), or null.</param>
    public void SetObjects(IReadOnlyList<DetectedObject> objects, int? selectId)
    {
        _objects = objects;
        _updating = true;
        _list.BeginUpdate();
        try
        {
            while (_list.Items.Count > objects.Count) _list.Items.RemoveAt(_list.Items.Count - 1);
            for (int i = 0; i < objects.Count; i++)
            {
                DetectedObject o = objects[i];
                string[] cells =
                [
                    o.Id.ToString(CultureInfo.InvariantCulture),
                    o.DistanceM.ToString("0.00", CultureInfo.InvariantCulture),
                    o.BearingDeg.ToString("0", CultureInfo.InvariantCulture),
                    o.WidthM.ToString("0.00", CultureInfo.InvariantCulture),
                    o.PointCount.ToString(CultureInfo.InvariantCulture),
                ];
                ListViewItem item;
                if (i < _list.Items.Count)
                {
                    item = _list.Items[i];
                    for (int c = 0; c < cells.Length; c++) item.SubItems[c].Text = cells[c];
                }
                else
                {
                    item = new ListViewItem(cells);
                    _list.Items.Add(item);
                }
                // Objects outside the monitored sector are greyed: they do not affect the zone.
                item.BackColor = o.InSector ? ZoneColors.Pale(o.Zone) : Color.WhiteSmoke;
                item.ForeColor = o.InSector ? Color.Black : Color.Gray;
                item.Selected = o.Id == selectId;
            }
            SelectedObjectId = objects.Any(o => o.Id == selectId) ? selectId : null;
            _header.Text = $"Detected objects (closest first): {objects.Count}";
        }
        finally
        {
            _list.EndUpdate();
            _updating = false;
        }
    }

    /// <summary>Clears the list (e.g. after disconnecting).</summary>
    public void Clear() => SetObjects([], null);

    /// <summary>Forwards a user selection as <see cref="SelectedObjectChanged"/>.</summary>
    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        DetectedObject? selected = _list.SelectedIndices.Count == 1 && _list.SelectedIndices[0] < _objects.Count
            ? _objects[_list.SelectedIndices[0]]
            : null;
        SelectedObjectId = selected?.Id;
        SelectedObjectChanged?.Invoke(this, new ObjectSelectedEventArgs(selected));
    }

    /// <summary>
    /// ListView with double buffering switched on. WHY: ListView.DoubleBuffered is protected,
    /// so a tiny subclass is the standard way to stop the 10 Hz refresh from flickering.
    /// </summary>
    private sealed class DoubleBufferedListView : ListView
    {
        /// <summary>Enables double buffering.</summary>
        public DoubleBufferedListView() => DoubleBuffered = true;
    }
}
