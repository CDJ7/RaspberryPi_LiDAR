using LidarPC.Api;

namespace LidarPC.Controls;

/// <summary>
/// The colours used for the zones everywhere in the GUI (plot, object list, banner), kept
/// in one place so green/yellow/red always mean the same thing as the LEDs on the Pi.
/// </summary>
public static class ZoneColors
{
    /// <summary>Strong colour for points, circles and the banner background.</summary>
    public static Color Strong(SafetyZone zone) => zone switch
    {
        SafetyZone.Clear => Color.FromArgb(46, 160, 67),
        SafetyZone.Caution => Color.FromArgb(230, 180, 0),
        _ => Color.FromArgb(215, 38, 38),
    };

    /// <summary>Pale colour for list rows, so black text stays readable.</summary>
    public static Color Pale(SafetyZone zone) => zone switch
    {
        SafetyZone.Clear => Color.FromArgb(214, 240, 218),
        SafetyZone.Caution => Color.FromArgb(255, 241, 194),
        _ => Color.FromArgb(255, 210, 210),
    };

    /// <summary>Zone for a distance using the Pi's thresholds (no hysteresis; used to colour individual points).</summary>
    public static SafetyZone ZoneFor(double distanceM, double stopM, double cautionM) =>
        distanceM < stopM ? SafetyZone.Stop : distanceM < cautionM ? SafetyZone.Caution : SafetyZone.Clear;
}
