namespace LidarPC;

/// <summary>Entry point of the PC GUI.</summary>
internal static class Program
{
    /// <summary>
    /// Starts the WinForms message loop with <see cref="MainForm"/>.
    /// [STAThread] is required by WinForms (clipboard, dialogs and COM-based controls).
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // Generated from the Application* properties in LidarPC.csproj (DPI mode, visual styles, default font).
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
