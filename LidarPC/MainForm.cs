using System.Diagnostics;
using System.Globalization;
using System.Text;
using LidarPC.Api;
using LidarPC.Controls;

namespace LidarPC;

/// <summary>
/// Main window: login bar, status banner, live LiDAR plot, object sidebar, and one tab per
/// course requirement (LED output, E-stop input, LCD, nonvolatile config, system info and
/// shutdown). The layout lives in MainForm.Designer.cs and can be edited in the Designer;
/// this file only holds behaviour.
/// </summary>
/// <remarks>
/// <para><b>Live updates without freezing the UI:</b> a WinForms timer ticks every 100 ms
/// and starts an async GET /api/scan. While a request is in flight the next ticks are
/// skipped, so a slow or dead Pi never piles up requests. Because the await resumes on
/// the UI thread, the result can be drawn directly with no Invoke calls.</para>
/// <para><b>Object selection across scans:</b> object IDs are renumbered by the Pi every
/// revolution (closest = 1). To keep a clicked object highlighted, the form remembers its
/// position and picks the nearest object (within 0.4 m) in each new scan.</para>
/// </remarks>
public partial class MainForm : Form
{
    private const double SelectionTrackingRadiusM = 0.4;

    private LidarPiClient? _client;
    private CancellationTokenSource _pollCancel = new();
    private bool _pollInFlight;                // skip timer ticks while a request is running
    private int _consecutiveFailures;
    private bool _simulate;                    // the connected Pi app runs with --simulate
    private bool _suppressSimEvents;           // set while code (not the user) changes the sim check boxes
    private bool _shutdownRequested;
    private bool _defaultPasswordWarned;
    private ScanResponse? _lastScan;
    private int? _selectedId;
    private (double X, double Y)? _selectedPosition;
    private readonly Stopwatch _rateWatch = new();
    private int _pollsInWindow;
    private ConnectionSettings _settings = new();

    /// <summary>Creates the form (layout only; nothing connects until the user presses Connect).</summary>
    public MainForm()
    {
        InitializeComponent();
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Fills the login bar from the saved settings.</summary>
    private void MainForm_Load(object? sender, EventArgs e)
    {
        if (DesignMode) return;
        _settings = ConnectionSettings.Load();
        txtHost.Text = _settings.Host;
        numPort.Value = Math.Clamp(_settings.Port, (int)numPort.Minimum, (int)numPort.Maximum);
        txtUser.Text = _settings.Username;
        cmbSector.SelectedIndex = 0;
        ActiveControl = txtPassword;
    }

    /// <summary>Stops polling and releases the HTTP client when the window closes.</summary>
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        pollTimer.Stop();
        _pollCancel.Cancel();
        DisposeClient();
    }

    // ------------------------------------------------------------------ connection

    /// <summary>Connect / Disconnect toggle.</summary>
    private async void btnConnect_Click(object? sender, EventArgs e)
    {
        if (_client is not null) Disconnect("Disconnected.");
        else await ConnectAsync();
    }

    /// <summary>Enter in the password box = Connect.</summary>
    private async void txtPassword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || _client is not null) return;
        e.SuppressKeyPress = true;
        await ConnectAsync();
    }

    /// <summary>
    /// Creates the client and checks the login with GET /api/system before starting the
    /// 10 Hz polling, so a wrong password is reported once instead of ten times per second.
    /// </summary>
    private async Task ConnectAsync()
    {
        Uri baseAddress;
        try
        {
            baseAddress = LidarPiClient.BuildBaseAddress(txtHost.Text, (int)numPort.Value);
        }
        catch (FormatException ex)
        {
            ShowStatus(ex.Message, isError: true);
            return;
        }

        btnConnect.Enabled = false;
        SetConnectionLabel($"● Connecting to {baseAddress.Authority}...", Color.DarkOrange);
        var client = new LidarPiClient(baseAddress, txtUser.Text.Trim(), txtPassword.Text);
        try
        {
            SystemResponse system = await client.GetSystemAsync();
            _client = client;
            _simulate = system.Simulate;
            _consecutiveFailures = 0;
            _shutdownRequested = false;
            _defaultPasswordWarned = false;
            _settings.Host = txtHost.Text.Trim();
            _settings.Port = (int)numPort.Value;
            _settings.Username = txtUser.Text.Trim();
            _settings.Save();

            ShowSystem(system);
            grpSim.Visible = _simulate;
            tabControls.Enabled = true;
            btnConnect.Text = "Disconnect";
            SetConnectionLabel($"● Connected to {system.System.Hostname}{(_simulate ? " (SIMULATOR)" : "")}", Color.ForestGreen);
            ShowStatus($"Connected to {baseAddress.Authority}.");
            _rateWatch.Restart();
            _pollsInWindow = 0;
            pollTimer.Start();
            await LoadConfigAsync();
        }
        catch (ApiException ex)
        {
            client.Dispose();
            SetConnectionLabel("● Not connected", Color.Firebrick);
            ShowStatus($"Connect failed: {ex.Message}", isError: true);
            string hint = ex.Kind == ApiErrorKind.Unauthorized
                ? "Check the user name and password (default admin / changeme)."
                : "Check the IP address, that the Pi app is running (systemctl status lidar-agv), and the firewall.";
            MessageBox.Show(this, $"{ex.Message}\n\n{hint}", "Cannot connect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            btnConnect.Enabled = true;
        }
    }

    /// <summary>Stops polling and resets the window to the "not connected" look.</summary>
    private void Disconnect(string message)
    {
        pollTimer.Stop();
        _pollCancel.Cancel();
        _pollCancel = new CancellationTokenSource();
        DisposeClient();
        _lastScan = null;
        plot.Scan = null;
        objectList.Clear();
        tabControls.Enabled = false;
        btnResetEstop.Enabled = false;
        btnConnect.Text = "Connect";
        SetConnectionLabel("● Not connected", Color.Gray);
        SetBanner("NOT CONNECTED", "Enter the Pi's address and password, then press Connect.", Color.DimGray, Color.White);
        lblPollRate.Text = "poll: -";
        ShowStatus(message);
    }

    /// <summary>Disposes the HTTP client (also called from Dispose / FormClosing).</summary>
    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }

    // ------------------------------------------------------------------ live polling

    /// <summary>Timer tick (100 ms): fetch the newest scan unless the previous request is still running.</summary>
    private async void pollTimer_Tick(object? sender, EventArgs e)
    {
        if (_client is null || _pollInFlight) return;
        _pollInFlight = true;
        LidarPiClient client = _client;
        try
        {
            ScanResponse scan = await client.GetScanAsync(_pollCancel.Token);
            if (client != _client) return;   // disconnected while the request was running
            OnScanReceived(scan);
        }
        catch (ApiException ex)
        {
            if (client == _client) OnPollFailed(ex);
        }
        catch (OperationCanceledException)
        {
            // Disconnect pressed: nothing to report.
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    /// <summary>Updates plot, sidebar, banner and the small status read-outs from one scan.</summary>
    private void OnScanReceived(ScanResponse scan)
    {
        if (_consecutiveFailures > 0) ShowStatus("Connection restored.");
        _consecutiveFailures = 0;
        _lastScan = scan;

        TrackSelection(scan.Objects);
        plot.Scan = scan;
        plot.SelectedObjectId = _selectedId;
        objectList.SetObjects(scan.Objects, _selectedId);
        UpdateBanner(scan.Status);
        UpdateSmallReadouts(scan.Status);
        UpdatePollRate();

        if (scan.Status.DefaultPassword && !_defaultPasswordWarned)
        {
            _defaultPasswordWarned = true;
            ShowStatus("Warning: the Pi still uses the default password. Change it on the Configuration tab.", isError: true);
        }
    }

    /// <summary>A poll failed: show the problem, stop on a wrong password, otherwise keep retrying.</summary>
    private void OnPollFailed(ApiException ex)
    {
        _consecutiveFailures++;
        if (ex.Kind == ApiErrorKind.Unauthorized)
        {
            Disconnect("The Pi rejected the password (was it changed?). Log in again.");
            return;
        }
        if (_shutdownRequested)
        {
            SetBanner("PI SHUT DOWN", "The Pi is powering off. Wait ~20 s before removing power.", Color.DimGray, Color.White);
            SetConnectionLabel("● Pi shut down", Color.Gray);
            pollTimer.Stop();
            return;
        }
        SetConnectionLabel($"● Connection lost ({_consecutiveFailures})", Color.Firebrick);
        // Fail-safe display: if we cannot see the Pi we must not keep showing an old "CLEAR".
        SetBanner("NO DATA FROM PI", ex.Message, Color.DimGray, Color.White);
        btnResetEstop.Enabled = false;
        lblPollRate.Text = "poll: failing";
        if (_consecutiveFailures == 1) ShowStatus($"Lost connection: {ex.Message} (retrying)", isError: true);
    }

    /// <summary>Banner colour and text from the safety state (E-stop &gt; fault &gt; zone).</summary>
    private void UpdateBanner(StatusResponse s)
    {
        string where = s.ClosestM is double d && s.BearingDeg is double b
            ? string.Create(CultureInfo.InvariantCulture, $"   {d:0.00} m @ {b:000}°")
            : "";
        string title;
        Color back;
        if (s.ShuttingDown) { title = "PI SHUTTING DOWN"; back = Color.DimGray; }
        else if (s.EStopLatched) { title = "E-STOP" + where; back = ZoneColors.Strong(SafetyZone.Stop); }
        else if (s.LidarFault) { title = "LIDAR FAULT"; back = ZoneColors.Strong(SafetyZone.Stop); }
        else
        {
            title = s.Zone switch
            {
                SafetyZone.Clear => "CLEAR",
                SafetyZone.Caution => "CAUTION - CLOSE",
                _ => "STOP - TOO CLOSE",
            } + where;
            back = ZoneColors.Strong(s.Zone);
        }

        var details = new StringBuilder();
        details.Append("Motion: ").Append(s.MotionEnabled ? "ENABLED" : "DISABLED");
        details.Append("   |   E-stop: ").Append(s.EStopLatched ? $"LATCHED ({s.EStopReason})" : s.EStopPressed ? "PRESSED" : "OK");
        details.Append("   |   LiDAR: ").Append(s.LidarFault ? $"FAULT ({s.FaultMessage})" : "OK");
        details.Append("   |   LEDs: ").Append(s.Leds.Mode);
        if (s.HardwareFaults.Count > 0) details.Append("   |   HW: ").Append(string.Join("; ", s.HardwareFaults));
        if (s.ShutdownError is not null) details.Append("   |   Shutdown failed: ").Append(s.ShutdownError);

        // Black text on yellow is far more readable than white.
        Color fore = back == ZoneColors.Strong(SafetyZone.Caution) ? Color.Black : Color.White;
        SetBanner(title, details.ToString(), back, fore);
        btnResetEstop.Enabled = s.EStopLatched && !s.ShuttingDown;
    }

    /// <summary>LED and LCD read-outs on their tabs, so the effect of a command is visible immediately.</summary>
    private void UpdateSmallReadouts(StatusResponse s)
    {
        LedState l = s.Leds;
        lblLedState.Text = $"LEDs on the Pi ({l.Mode}{(l.Mode == LedMode.Test ? $", {l.TestSecondsRemaining} s left" : "")}):\n"
                           + $"  green  {(l.Green ? "ON " : "off")}\n  yellow {(l.Yellow ? "ON " : "off")}\n  red    {(l.Red ? "ON " : "off")}";
        lblLcdPreview.Text = s.LcdLine1.PadRight(16) + "\r\n" + s.LcdLine2.PadRight(16);
    }

    /// <summary>Shows how many scans per second actually arrive (target ~10).</summary>
    private void UpdatePollRate()
    {
        _pollsInWindow++;
        if (_rateWatch.Elapsed.TotalSeconds < 1) return;
        double rate = _pollsInWindow / _rateWatch.Elapsed.TotalSeconds;
        lblPollRate.Text = string.Create(CultureInfo.InvariantCulture, $"poll: {rate:0.0} Hz");
        SetConnectionLabel($"● Connected{(_simulate ? " (SIMULATOR)" : "")} - {rate:0.0} Hz", Color.ForestGreen);
        _pollsInWindow = 0;
        _rateWatch.Restart();
    }

    // ------------------------------------------------------------------ object selection

    /// <summary>Follows the selected object into the new scan by position (IDs change every scan).</summary>
    private void TrackSelection(IReadOnlyList<DetectedObject> objects)
    {
        if (_selectedPosition is not (double x, double y))
        {
            _selectedId = null;
            return;
        }
        DetectedObject? best = null;
        double bestDistance = SelectionTrackingRadiusM;
        foreach (DetectedObject o in objects)
        {
            double d = Math.Sqrt((o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y));
            if (d < bestDistance) { bestDistance = d; best = o; }
        }
        _selectedId = best?.Id;
        _selectedPosition = best is null ? null : (best.X, best.Y);
    }

    /// <summary>Remembers a selection made in the list or the plot.</summary>
    private void Select(DetectedObject? selected)
    {
        _selectedId = selected?.Id;
        _selectedPosition = selected is null ? null : (selected.X, selected.Y);
        plot.SelectedObjectId = _selectedId;
        if (_lastScan is not null) objectList.SetObjects(_lastScan.Objects, _selectedId);
    }

    /// <summary>Row selected in the sidebar: highlight it on the plot.</summary>
    private void objectList_SelectedObjectChanged(object? sender, ObjectSelectedEventArgs e) => Select(e.Selected);

    /// <summary>Object clicked on the plot: select it in the sidebar too.</summary>
    private void plot_ObjectClicked(object? sender, ObjectSelectedEventArgs e) => Select(e.Selected);

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// Runs one REST command with uniform error handling: success goes to the status bar,
    /// a refusal from the Pi (400/409) is shown in a message box because the operator has
    /// to act on it (e.g. "release the E-stop first").
    /// </summary>
    /// <returns>The result, or default if the command failed.</returns>
    private async Task<T?> RunCommandAsync<T>(string description, Func<LidarPiClient, Task<T>> command, Control? button = null)
    {
        if (_client is null)
        {
            ShowStatus("Not connected.", isError: true);
            return default;
        }
        if (button is not null) button.Enabled = false;
        try
        {
            T result = await command(_client);
            ShowStatus($"{description}: OK" + (result is ApiMessage m ? $" - {m.Message}" : ""));
            return result;
        }
        catch (ApiException ex)
        {
            ShowStatus($"{description} failed: {ex.Message}", isError: true);
            if (ex.Kind is ApiErrorKind.Rejected or ApiErrorKind.ServerError)
                MessageBox.Show(this, ex.Message, description, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return default;
        }
        finally
        {
            if (button is not null) button.Enabled = true;
        }
    }

    /// <summary>POST /api/estop/reset. The Pi refuses it (409) while the button is still pressed.</summary>
    private async void btnResetEstop_Click(object? sender, EventArgs e) =>
        await RunCommandAsync("Reset E-stop", c => c.ResetEStopAsync(), btnResetEstop);

    /// <summary>POST /api/leds: auto mode, or test mode with the ticked LEDs. (Output requirement.)</summary>
    private async void btnApplyLeds_Click(object? sender, EventArgs e)
    {
        LedState? state = rbLedAuto.Checked
            ? await RunCommandAsync("LEDs auto", c => c.SetLedsAutoAsync(), btnApplyLeds)
            : await RunCommandAsync("LED test", c => c.SetLedsTestAsync(chkGreen.Checked, chkYellow.Checked, chkRed.Checked, (int)numLedTimeout.Value), btnApplyLeds);
        if (state is not null) ShowStatus($"LEDs now {state.Mode}: green={state.Green} yellow={state.Yellow} red={state.Red}");
    }

    /// <summary>GET /api/estop: shows the raw and latched state of the button. (Input requirement.)</summary>
    private async void btnReadEStop_Click(object? sender, EventArgs e)
    {
        EStopState? s = await RunCommandAsync("Read E-stop", c => c.GetEStopAsync(), btnReadEStop);
        if (s is null) return;
        string lastTrip = s.LastTripUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";
        lblEStopState.Text =
            $"Button (debounced): {(s.Pressed ? "PRESSED / wire broken" : "released")}\n" +
            $"Raw pin level:      {s.RawLevel} ({(s.RawPressed ? "contact open = STOP" : "contact closed = OK")})\n" +
            $"Latched:            {(s.Latched ? "YES - " + s.Reason : "no")}\n" +
            $"Last trip:          {lastTrip}   (trips since start: {s.TripCount})\n" +
            $"Input fault:        {s.InputFault ?? "none"}\n" +
            $"Read at:            {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>Simulator check boxes: POST /api/sim to press the E-stop or unplug the LiDAR.</summary>
    private async void chkSim_CheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressSimEvents || !_simulate) return;
        Dictionary<string, bool>? result = await RunCommandAsync("Simulation", c => c.SetSimulationAsync(chkSimEStop.Checked, chkSimLidar.Checked));
        if (result is null)
        {
            // Put the boxes back to the state the Pi really has (if the call failed, nothing changed there).
            _suppressSimEvents = true;
            if (sender == chkSimEStop) chkSimEStop.Checked = !chkSimEStop.Checked;
            if (sender == chkSimLidar) chkSimLidar.Checked = !chkSimLidar.Checked;
            _suppressSimEvents = false;
        }
    }

    /// <summary>POST /api/lcd: custom message for N seconds. (LCD requirement.)</summary>
    private async void btnSendLcd_Click(object? sender, EventArgs e) =>
        await RunCommandAsync("LCD message", c => c.SetLcdAsync(txtLine1.Text, txtLine2.Text, (int)numLcdSeconds.Value), btnSendLcd);

    /// <summary>Reloads the configuration tab from the Pi.</summary>
    private async void btnLoadConfig_Click(object? sender, EventArgs e) => await LoadConfigAsync();

    /// <summary>GET /api/config into the configuration tab.</summary>
    private async Task LoadConfigAsync()
    {
        ConfigResponse? cfg = await RunCommandAsync("Load configuration", c => c.GetConfigAsync(), btnLoadConfig);
        if (cfg is not null) ShowConfig(cfg);
    }

    /// <summary>PUT /api/config: saves the settings on the Pi; they survive a power cycle. (Nonvolatile requirement.)</summary>
    private async void btnSaveConfig_Click(object? sender, EventArgs e)
    {
        if (numCaution.Value <= numStop.Value)
        {
            MessageBox.Show(this, "The caution distance must be larger than the stop distance.", "Configuration",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!LidarPiClient.TryParseI2cAddress(txtLcdAddress.Text, out int address))
        {
            MessageBox.Show(this, "Enter the LCD address in hex, e.g. 0x27 or 0x3F (see i2cdetect -y 1 on the Pi).", "Configuration",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var update = new ConfigUpdate(
            (double)numStop.Value, (double)numCaution.Value, (double)numHysteresis.Value,
            cmbSector.SelectedItem as string ?? "Full", (double)numFrontAngle.Value, (double)numAngleOffset.Value,
            (double)numClusterGap.Value, (int)numMinPoints.Value, address);
        ConfigResponse? saved = await RunCommandAsync("Save configuration", c => c.UpdateConfigAsync(update), btnSaveConfig);
        if (saved is not null)
        {
            ShowConfig(saved);
            ShowStatus($"Configuration saved on the Pi ({saved.ConfigPath}). It will survive a reboot.");
        }
    }

    /// <summary>PUT /api/config/password, then reconnects with the new credentials.</summary>
    private async void btnChangePassword_Click(object? sender, EventArgs e)
    {
        if (txtNewPassword.Text != txtConfirmPassword.Text)
        {
            MessageBox.Show(this, "The new password and the confirmation do not match.", "Change login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string newUser = txtNewUser.Text.Trim();
        string newPassword = txtNewPassword.Text;
        ApiMessage? ok = await RunCommandAsync("Change login",
            c => c.ChangePasswordAsync(txtCurrentPassword.Text, newPassword, newUser.Length > 0 ? newUser : null), btnChangePassword);
        if (ok is null) return;

        // The old credentials stop working immediately, so log in again with the new ones.
        txtCurrentPassword.Clear();
        txtNewPassword.Clear();
        txtConfirmPassword.Clear();
        txtNewUser.Clear();
        if (newUser.Length > 0) txtUser.Text = newUser;
        txtPassword.Text = newPassword;
        Disconnect("Login changed. Reconnecting with the new password...");
        await ConnectAsync();
    }

    /// <summary>GET /api/system and display it. (System information requirement.)</summary>
    private async void btnRefreshSystem_Click(object? sender, EventArgs e)
    {
        SystemResponse? s = await RunCommandAsync("System info", c => c.GetSystemAsync(), btnRefreshSystem);
        if (s is not null) ShowSystem(s);
    }

    /// <summary>POST /api/shutdown after an "Are you sure?" confirmation. (Shutdown requirement.)</summary>
    private async void btnShutdown_Click(object? sender, EventArgs e)
    {
        DialogResult answer = MessageBox.Show(this,
            "Are you sure you want to shut down the Raspberry Pi?\n\n" +
            "The LEDs turn off, the LCD shows \"Shutting down...\", the LiDAR motor stops and the Pi powers off. " +
            "It can only be started again by cycling its power.",
            "Shut down the Pi?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        ApiMessage? ok = await RunCommandAsync("Shutdown", c => c.ShutdownAsync(), btnShutdown);
        if (ok is not null)
        {
            _shutdownRequested = true;
            MessageBox.Show(this, ok.Message, "Shutdown", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ------------------------------------------------------------------ display helpers

    /// <summary>Copies a config response into the configuration controls.</summary>
    private void ShowConfig(ConfigResponse c)
    {
        SetNumeric(numStop, c.StopDistanceM);
        SetNumeric(numCaution, c.CautionDistanceM);
        SetNumeric(numHysteresis, c.HysteresisM);
        cmbSector.SelectedItem = string.Equals(c.SectorMode, "Front", StringComparison.OrdinalIgnoreCase) ? "Front" : "Full";
        SetNumeric(numFrontAngle, c.FrontHalfAngleDeg);
        SetNumeric(numAngleOffset, c.AngleOffsetDeg);
        SetNumeric(numClusterGap, c.ClusterGapM);
        SetNumeric(numMinPoints, c.MinClusterPoints);
        txtLcdAddress.Text = LidarPiClient.FormatI2cAddress(c.LcdI2cAddress);
        grpPassword.Text = $"Change REST API login (user: {c.Username}{(c.IsDefaultPassword ? ", DEFAULT PASSWORD" : "")})";
    }

    /// <summary>Sets a NumericUpDown without throwing if the Pi sends a value outside the control's range.</summary>
    private static void SetNumeric(NumericUpDown control, double value) =>
        control.Value = Math.Clamp((decimal)value, control.Minimum, control.Maximum);

    /// <summary>Formats GET /api/system for the read-only text box.</summary>
    private void ShowSystem(SystemResponse r)
    {
        SystemInfo s = r.System;
        LidarSystemInfo l = r.Lidar;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Host:           {s.Hostname}{(r.Simulate ? "   (SIMULATOR)" : "")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS version:     {s.OsVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Kernel:         {s.Kernel}   .NET: {s.DotnetVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RAM available:  {s.MemAvailableMb:0} MB of {s.MemTotalMb:0} MB");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Uptime:         {FormatDuration(s.SystemUptimeSeconds)} (service: {FormatDuration(s.ServiceUptimeSeconds)})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"IP address:     {string.Join(", ", s.IpAddresses)}");
        if (s.CpuTemperatureC is double t) sb.AppendLine(CultureInfo.InvariantCulture, $"CPU temp:       {t:0.0} °C");
        sb.AppendLine(CultureInfo.InvariantCulture, $"LiDAR:          {l.Model ?? "unknown"} on {l.Device}, firmware {l.Firmware ?? "-"}, hardware {l.Hardware?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"LiDAR status:   {(l.Connected ? "connected" : "DISCONNECTED")}, health {l.Health ?? "-"} (code {l.HealthCode?.ToString(CultureInfo.InvariantCulture) ?? "-"}), {l.ScanRateHz:0.0} Hz, reconnects {l.ErrorCount}{(l.LastError is null ? "" : ", last error: " + l.LastError)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"LCD:            {(r.Lcd.Connected ? "OK" : "NOT FOUND")} at {r.Lcd.Address}{(r.Lcd.Fault is null ? "" : " - " + r.Lcd.Fault)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"GPIO:           {r.Gpio}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Config file:    {r.ConfigPath}");
        sb.Append(CultureInfo.InvariantCulture, $"Read at:        {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        txtSystem.Text = sb.ToString();
    }

    /// <summary>Seconds as "2d 03:14:15".</summary>
    private static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.Days > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{t.Days}d {t:hh\\:mm\\:ss}")
            : t.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Sets the banner texts and colours.</summary>
    private void SetBanner(string title, string details, Color back, Color fore)
    {
        if (lblZone.Text != title) lblZone.Text = title;
        if (lblBannerDetails.Text != details) lblBannerDetails.Text = details;
        pnlBanner.BackColor = back;
        lblZone.ForeColor = fore;
        lblBannerDetails.ForeColor = fore;
    }

    /// <summary>Sets the coloured connection indicator next to the Connect button.</summary>
    private void SetConnectionLabel(string text, Color color)
    {
        lblConnStatus.Text = text;
        lblConnStatus.ForeColor = color;
    }

    /// <summary>Writes a message to the status bar (red for errors).</summary>
    private void ShowStatus(string message, bool isError = false)
    {
        lblStatus.Text = $"{DateTime.Now:HH:mm:ss}  {message}";
        lblStatus.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
    }
}
