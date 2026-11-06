namespace LidarPC
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            pollTimer = new System.Windows.Forms.Timer(components);
            flowConnection = new FlowLayoutPanel();
            lblHost = new Label();
            txtHost = new TextBox();
            lblPort = new Label();
            numPort = new NumericUpDown();
            lblUser = new Label();
            txtUser = new TextBox();
            lblPassword = new Label();
            txtPassword = new TextBox();
            btnConnect = new Button();
            lblConnStatus = new Label();
            pnlBanner = new Panel();
            lblZone = new Label();
            lblBannerDetails = new Label();
            btnResetEstop = new Button();
            tabControls = new TabControl();
            tabLeds = new TabPage();
            lblLedState = new Label();
            btnApplyLeds = new Button();
            numLedTimeout = new NumericUpDown();
            lblLedTimeout = new Label();
            chkRed = new CheckBox();
            chkYellow = new CheckBox();
            chkGreen = new CheckBox();
            rbLedTest = new RadioButton();
            rbLedAuto = new RadioButton();
            tabEStop = new TabPage();
            grpSim = new GroupBox();
            chkSimLidar = new CheckBox();
            chkSimEStop = new CheckBox();
            lblEStopState = new Label();
            btnReadEStop = new Button();
            tabLcd = new TabPage();
            lblLcdPreview = new Label();
            lblLcdNow = new Label();
            btnSendLcd = new Button();
            numLcdSeconds = new NumericUpDown();
            lblLcdSeconds = new Label();
            txtLine2 = new TextBox();
            lblLine2 = new Label();
            txtLine1 = new TextBox();
            lblLine1 = new Label();
            tabConfig = new TabPage();
            grpPassword = new GroupBox();
            btnChangePassword = new Button();
            txtConfirmPassword = new TextBox();
            lblConfirmPassword = new Label();
            txtNewPassword = new TextBox();
            lblNewPassword = new Label();
            txtNewUser = new TextBox();
            lblNewUser = new Label();
            txtCurrentPassword = new TextBox();
            lblCurrentPassword = new Label();
            btnSaveConfig = new Button();
            btnLoadConfig = new Button();
            txtLcdAddress = new TextBox();
            lblLcdAddress = new Label();
            numMinPoints = new NumericUpDown();
            lblMinPoints = new Label();
            numClusterGap = new NumericUpDown();
            lblClusterGap = new Label();
            numAngleOffset = new NumericUpDown();
            lblAngleOffset = new Label();
            numFrontAngle = new NumericUpDown();
            lblFrontAngle = new Label();
            cmbSector = new ComboBox();
            lblSector = new Label();
            numHysteresis = new NumericUpDown();
            lblHysteresis = new Label();
            numCaution = new NumericUpDown();
            lblCaution = new Label();
            numStop = new NumericUpDown();
            lblStop = new Label();
            tabSystem = new TabPage();
            txtSystem = new TextBox();
            btnShutdown = new Button();
            btnRefreshSystem = new Button();
            objectList = new LidarPC.Controls.ObjectListControl();
            plot = new LidarPC.Controls.LidarPlotControl();
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel();
            lblPollRate = new ToolStripStatusLabel();
            flowConnection.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numPort).BeginInit();
            pnlBanner.SuspendLayout();
            tabControls.SuspendLayout();
            tabLeds.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numLedTimeout).BeginInit();
            tabEStop.SuspendLayout();
            grpSim.SuspendLayout();
            tabLcd.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numLcdSeconds).BeginInit();
            tabConfig.SuspendLayout();
            grpPassword.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numMinPoints).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numClusterGap).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numAngleOffset).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numFrontAngle).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numHysteresis).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numCaution).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numStop).BeginInit();
            tabSystem.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            //
            // pollTimer
            //
            pollTimer.Interval = 100;
            pollTimer.Tick += pollTimer_Tick;
            //
            // flowConnection
            //
            flowConnection.Controls.Add(lblHost);
            flowConnection.Controls.Add(txtHost);
            flowConnection.Controls.Add(lblPort);
            flowConnection.Controls.Add(numPort);
            flowConnection.Controls.Add(lblUser);
            flowConnection.Controls.Add(txtUser);
            flowConnection.Controls.Add(lblPassword);
            flowConnection.Controls.Add(txtPassword);
            flowConnection.Controls.Add(btnConnect);
            flowConnection.Controls.Add(lblConnStatus);
            flowConnection.Dock = DockStyle.Top;
            flowConnection.Location = new Point(0, 0);
            flowConnection.Name = "flowConnection";
            flowConnection.Padding = new Padding(6, 4, 6, 0);
            flowConnection.Size = new Size(1280, 40);
            flowConnection.TabIndex = 0;
            flowConnection.WrapContents = false;
            //
            // lblHost
            //
            lblHost.AutoSize = true;
            lblHost.Margin = new Padding(3, 9, 3, 0);
            lblHost.Name = "lblHost";
            lblHost.TabIndex = 0;
            lblHost.Text = "Pi host / IP:";
            //
            // txtHost
            //
            txtHost.Margin = new Padding(3, 5, 10, 3);
            txtHost.Name = "txtHost";
            txtHost.Size = new Size(150, 23);
            txtHost.TabIndex = 1;
            txtHost.Text = "192.168.1.100";
            //
            // lblPort
            //
            lblPort.AutoSize = true;
            lblPort.Margin = new Padding(3, 9, 3, 0);
            lblPort.Name = "lblPort";
            lblPort.TabIndex = 2;
            lblPort.Text = "Port:";
            //
            // numPort
            //
            numPort.Margin = new Padding(3, 5, 10, 3);
            numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            numPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numPort.Name = "numPort";
            numPort.Size = new Size(70, 23);
            numPort.TabIndex = 3;
            numPort.Value = new decimal(new int[] { 5000, 0, 0, 0 });
            //
            // lblUser
            //
            lblUser.AutoSize = true;
            lblUser.Margin = new Padding(3, 9, 3, 0);
            lblUser.Name = "lblUser";
            lblUser.TabIndex = 4;
            lblUser.Text = "User:";
            //
            // txtUser
            //
            txtUser.Margin = new Padding(3, 5, 10, 3);
            txtUser.Name = "txtUser";
            txtUser.Size = new Size(90, 23);
            txtUser.TabIndex = 5;
            txtUser.Text = "admin";
            //
            // lblPassword
            //
            lblPassword.AutoSize = true;
            lblPassword.Margin = new Padding(3, 9, 3, 0);
            lblPassword.Name = "lblPassword";
            lblPassword.TabIndex = 6;
            lblPassword.Text = "Password:";
            //
            // txtPassword
            //
            txtPassword.Margin = new Padding(3, 5, 10, 3);
            txtPassword.Name = "txtPassword";
            txtPassword.Size = new Size(120, 23);
            txtPassword.TabIndex = 7;
            txtPassword.UseSystemPasswordChar = true;
            txtPassword.KeyDown += txtPassword_KeyDown;
            //
            // btnConnect
            //
            btnConnect.Margin = new Padding(3, 4, 10, 3);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(100, 27);
            btnConnect.TabIndex = 8;
            btnConnect.Text = "Connect";
            btnConnect.UseVisualStyleBackColor = true;
            btnConnect.Click += btnConnect_Click;
            //
            // lblConnStatus
            //
            lblConnStatus.AutoSize = true;
            lblConnStatus.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            lblConnStatus.ForeColor = Color.Gray;
            lblConnStatus.Margin = new Padding(3, 8, 3, 0);
            lblConnStatus.Name = "lblConnStatus";
            lblConnStatus.TabIndex = 9;
            lblConnStatus.Text = "● Not connected";
            //
            // pnlBanner
            //
            pnlBanner.BackColor = Color.DimGray;
            pnlBanner.Controls.Add(lblZone);
            pnlBanner.Controls.Add(lblBannerDetails);
            pnlBanner.Controls.Add(btnResetEstop);
            pnlBanner.Dock = DockStyle.Top;
            pnlBanner.Location = new Point(0, 40);
            pnlBanner.Name = "pnlBanner";
            pnlBanner.Padding = new Padding(10, 4, 10, 4);
            pnlBanner.Size = new Size(1280, 84);
            pnlBanner.TabIndex = 1;
            //
            // lblZone
            //
            lblZone.Dock = DockStyle.Fill;
            lblZone.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
            lblZone.ForeColor = Color.White;
            lblZone.Name = "lblZone";
            lblZone.TabIndex = 0;
            lblZone.Text = "NOT CONNECTED";
            lblZone.TextAlign = ContentAlignment.MiddleLeft;
            //
            // lblBannerDetails
            //
            lblBannerDetails.Dock = DockStyle.Bottom;
            lblBannerDetails.Font = new Font("Segoe UI", 10F);
            lblBannerDetails.ForeColor = Color.White;
            lblBannerDetails.Name = "lblBannerDetails";
            lblBannerDetails.Size = new Size(1080, 24);
            lblBannerDetails.TabIndex = 1;
            lblBannerDetails.Text = "Enter the Pi's address and password, then press Connect.";
            lblBannerDetails.TextAlign = ContentAlignment.MiddleLeft;
            //
            // btnResetEstop
            //
            btnResetEstop.BackColor = Color.WhiteSmoke;
            btnResetEstop.Dock = DockStyle.Right;
            btnResetEstop.Enabled = false;
            btnResetEstop.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            btnResetEstop.Name = "btnResetEstop";
            btnResetEstop.Size = new Size(180, 76);
            btnResetEstop.TabIndex = 2;
            btnResetEstop.Text = "Reset E-Stop";
            btnResetEstop.UseVisualStyleBackColor = false;
            btnResetEstop.Click += btnResetEstop_Click;
            //
            // tabControls
            //
            tabControls.Controls.Add(tabLeds);
            tabControls.Controls.Add(tabEStop);
            tabControls.Controls.Add(tabLcd);
            tabControls.Controls.Add(tabConfig);
            tabControls.Controls.Add(tabSystem);
            tabControls.Dock = DockStyle.Bottom;
            tabControls.Enabled = false;
            tabControls.Location = new Point(0, 584);
            tabControls.Name = "tabControls";
            tabControls.SelectedIndex = 0;
            tabControls.Size = new Size(1280, 234);
            tabControls.TabIndex = 2;
            //
            // tabLeds
            //
            tabLeds.Controls.Add(lblLedState);
            tabLeds.Controls.Add(btnApplyLeds);
            tabLeds.Controls.Add(numLedTimeout);
            tabLeds.Controls.Add(lblLedTimeout);
            tabLeds.Controls.Add(chkRed);
            tabLeds.Controls.Add(chkYellow);
            tabLeds.Controls.Add(chkGreen);
            tabLeds.Controls.Add(rbLedTest);
            tabLeds.Controls.Add(rbLedAuto);
            tabLeds.Name = "tabLeds";
            tabLeds.Padding = new Padding(3);
            tabLeds.TabIndex = 0;
            tabLeds.Text = "LEDs (output)";
            tabLeds.UseVisualStyleBackColor = true;
            //
            // lblLedState
            //
            lblLedState.AutoSize = true;
            lblLedState.Font = new Font("Consolas", 10F);
            lblLedState.Location = new Point(340, 18);
            lblLedState.Name = "lblLedState";
            lblLedState.TabIndex = 8;
            lblLedState.Text = "LEDs on the Pi: -";
            //
            // btnApplyLeds
            //
            btnApplyLeds.Location = new Point(16, 140);
            btnApplyLeds.Name = "btnApplyLeds";
            btnApplyLeds.Size = new Size(140, 30);
            btnApplyLeds.TabIndex = 7;
            btnApplyLeds.Text = "Apply to Pi LEDs";
            btnApplyLeds.UseVisualStyleBackColor = true;
            btnApplyLeds.Click += btnApplyLeds_Click;
            //
            // numLedTimeout
            //
            numLedTimeout.Location = new Point(205, 103);
            numLedTimeout.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            numLedTimeout.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numLedTimeout.Name = "numLedTimeout";
            numLedTimeout.Size = new Size(70, 23);
            numLedTimeout.TabIndex = 6;
            numLedTimeout.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // lblLedTimeout
            //
            lblLedTimeout.AutoSize = true;
            lblLedTimeout.Location = new Point(40, 106);
            lblLedTimeout.Name = "lblLedTimeout";
            lblLedTimeout.TabIndex = 5;
            lblLedTimeout.Text = "Back to auto after (s):";
            //
            // chkRed
            //
            chkRed.AutoSize = true;
            chkRed.ForeColor = Color.Firebrick;
            chkRed.Location = new Point(210, 74);
            chkRed.Name = "chkRed";
            chkRed.TabIndex = 4;
            chkRed.Text = "Red";
            chkRed.UseVisualStyleBackColor = true;
            //
            // chkYellow
            //
            chkYellow.AutoSize = true;
            chkYellow.ForeColor = Color.DarkGoldenrod;
            chkYellow.Location = new Point(120, 74);
            chkYellow.Name = "chkYellow";
            chkYellow.TabIndex = 3;
            chkYellow.Text = "Yellow";
            chkYellow.UseVisualStyleBackColor = true;
            //
            // chkGreen
            //
            chkGreen.AutoSize = true;
            chkGreen.ForeColor = Color.ForestGreen;
            chkGreen.Location = new Point(40, 74);
            chkGreen.Name = "chkGreen";
            chkGreen.TabIndex = 2;
            chkGreen.Text = "Green";
            chkGreen.UseVisualStyleBackColor = true;
            //
            // rbLedTest
            //
            rbLedTest.AutoSize = true;
            rbLedTest.Location = new Point(16, 46);
            rbLedTest.Name = "rbLedTest";
            rbLedTest.TabIndex = 1;
            rbLedTest.Text = "Test mode: set the LEDs by hand (motion enable is not affected)";
            rbLedTest.UseVisualStyleBackColor = true;
            //
            // rbLedAuto
            //
            rbLedAuto.AutoSize = true;
            rbLedAuto.Checked = true;
            rbLedAuto.Location = new Point(16, 18);
            rbLedAuto.Name = "rbLedAuto";
            rbLedAuto.TabIndex = 0;
            rbLedAuto.TabStop = true;
            rbLedAuto.Text = "Auto: LEDs follow the safety zone";
            rbLedAuto.UseVisualStyleBackColor = true;
            //
            // tabEStop
            //
            tabEStop.Controls.Add(grpSim);
            tabEStop.Controls.Add(lblEStopState);
            tabEStop.Controls.Add(btnReadEStop);
            tabEStop.Name = "tabEStop";
            tabEStop.Padding = new Padding(3);
            tabEStop.TabIndex = 1;
            tabEStop.Text = "E-Stop (input)";
            tabEStop.UseVisualStyleBackColor = true;
            //
            // grpSim
            //
            grpSim.Controls.Add(chkSimLidar);
            grpSim.Controls.Add(chkSimEStop);
            grpSim.Location = new Point(620, 10);
            grpSim.Name = "grpSim";
            grpSim.Size = new Size(340, 96);
            grpSim.TabIndex = 2;
            grpSim.TabStop = false;
            grpSim.Text = "Simulator only (Pi app started with --simulate)";
            grpSim.Visible = false;
            //
            // chkSimLidar
            //
            chkSimLidar.AutoSize = true;
            chkSimLidar.Location = new Point(14, 58);
            chkSimLidar.Name = "chkSimLidar";
            chkSimLidar.TabIndex = 1;
            chkSimLidar.Text = "Simulated LiDAR unplugged";
            chkSimLidar.UseVisualStyleBackColor = true;
            chkSimLidar.CheckedChanged += chkSim_CheckedChanged;
            //
            // chkSimEStop
            //
            chkSimEStop.AutoSize = true;
            chkSimEStop.Location = new Point(14, 28);
            chkSimEStop.Name = "chkSimEStop";
            chkSimEStop.TabIndex = 0;
            chkSimEStop.Text = "Simulated E-stop button pressed";
            chkSimEStop.UseVisualStyleBackColor = true;
            chkSimEStop.CheckedChanged += chkSim_CheckedChanged;
            //
            // lblEStopState
            //
            lblEStopState.AutoSize = true;
            lblEStopState.Font = new Font("Consolas", 10F);
            lblEStopState.Location = new Point(16, 58);
            lblEStopState.Name = "lblEStopState";
            lblEStopState.TabIndex = 1;
            lblEStopState.Text = "Press \"Read E-stop input\" to query the button.";
            //
            // btnReadEStop
            //
            btnReadEStop.Location = new Point(16, 16);
            btnReadEStop.Name = "btnReadEStop";
            btnReadEStop.Size = new Size(170, 30);
            btnReadEStop.TabIndex = 0;
            btnReadEStop.Text = "Read E-stop input";
            btnReadEStop.UseVisualStyleBackColor = true;
            btnReadEStop.Click += btnReadEStop_Click;
            //
            // tabLcd
            //
            tabLcd.Controls.Add(lblLcdPreview);
            tabLcd.Controls.Add(lblLcdNow);
            tabLcd.Controls.Add(btnSendLcd);
            tabLcd.Controls.Add(numLcdSeconds);
            tabLcd.Controls.Add(lblLcdSeconds);
            tabLcd.Controls.Add(txtLine2);
            tabLcd.Controls.Add(lblLine2);
            tabLcd.Controls.Add(txtLine1);
            tabLcd.Controls.Add(lblLine1);
            tabLcd.Name = "tabLcd";
            tabLcd.Padding = new Padding(3);
            tabLcd.TabIndex = 2;
            tabLcd.Text = "LCD";
            tabLcd.UseVisualStyleBackColor = true;
            //
            // lblLcdPreview
            //
            lblLcdPreview.BackColor = Color.FromArgb(120, 170, 60);
            lblLcdPreview.BorderStyle = BorderStyle.FixedSingle;
            lblLcdPreview.Font = new Font("Consolas", 15F, FontStyle.Bold);
            lblLcdPreview.ForeColor = Color.FromArgb(20, 40, 10);
            lblLcdPreview.Location = new Point(340, 42);
            lblLcdPreview.Name = "lblLcdPreview";
            lblLcdPreview.Padding = new Padding(6, 4, 0, 0);
            lblLcdPreview.Size = new Size(210, 64);
            lblLcdPreview.TabIndex = 8;
            lblLcdPreview.Text = "                \r\n                ";
            //
            // lblLcdNow
            //
            lblLcdNow.AutoSize = true;
            lblLcdNow.Location = new Point(340, 20);
            lblLcdNow.Name = "lblLcdNow";
            lblLcdNow.TabIndex = 7;
            lblLcdNow.Text = "What the Pi's LCD shows now:";
            //
            // btnSendLcd
            //
            btnSendLcd.Location = new Point(16, 120);
            btnSendLcd.Name = "btnSendLcd";
            btnSendLcd.Size = new Size(140, 30);
            btnSendLcd.TabIndex = 6;
            btnSendLcd.Text = "Send to LCD";
            btnSendLcd.UseVisualStyleBackColor = true;
            btnSendLcd.Click += btnSendLcd_Click;
            //
            // numLcdSeconds
            //
            numLcdSeconds.Location = new Point(110, 82);
            numLcdSeconds.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            numLcdSeconds.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numLcdSeconds.Name = "numLcdSeconds";
            numLcdSeconds.Size = new Size(70, 23);
            numLcdSeconds.TabIndex = 5;
            numLcdSeconds.Value = new decimal(new int[] { 10, 0, 0, 0 });
            //
            // lblLcdSeconds
            //
            lblLcdSeconds.AutoSize = true;
            lblLcdSeconds.Location = new Point(16, 85);
            lblLcdSeconds.Name = "lblLcdSeconds";
            lblLcdSeconds.TabIndex = 4;
            lblLcdSeconds.Text = "Show for (s):";
            //
            // txtLine2
            //
            txtLine2.Font = new Font("Consolas", 10F);
            txtLine2.Location = new Point(80, 48);
            txtLine2.MaxLength = 16;
            txtLine2.Name = "txtLine2";
            txtLine2.Size = new Size(200, 23);
            txtLine2.TabIndex = 3;
            txtLine2.Text = "from the PC";
            //
            // lblLine2
            //
            lblLine2.AutoSize = true;
            lblLine2.Location = new Point(16, 51);
            lblLine2.Name = "lblLine2";
            lblLine2.TabIndex = 2;
            lblLine2.Text = "Line 2:";
            //
            // txtLine1
            //
            txtLine1.Font = new Font("Consolas", 10F);
            txtLine1.Location = new Point(80, 16);
            txtLine1.MaxLength = 16;
            txtLine1.Name = "txtLine1";
            txtLine1.Size = new Size(200, 23);
            txtLine1.TabIndex = 1;
            txtLine1.Text = "Hello Lab 5";
            //
            // lblLine1
            //
            lblLine1.AutoSize = true;
            lblLine1.Location = new Point(16, 19);
            lblLine1.Name = "lblLine1";
            lblLine1.TabIndex = 0;
            lblLine1.Text = "Line 1:";
            //
            // tabConfig
            //
            tabConfig.Controls.Add(grpPassword);
            tabConfig.Controls.Add(btnSaveConfig);
            tabConfig.Controls.Add(btnLoadConfig);
            tabConfig.Controls.Add(txtLcdAddress);
            tabConfig.Controls.Add(lblLcdAddress);
            tabConfig.Controls.Add(numMinPoints);
            tabConfig.Controls.Add(lblMinPoints);
            tabConfig.Controls.Add(numClusterGap);
            tabConfig.Controls.Add(lblClusterGap);
            tabConfig.Controls.Add(numAngleOffset);
            tabConfig.Controls.Add(lblAngleOffset);
            tabConfig.Controls.Add(numFrontAngle);
            tabConfig.Controls.Add(lblFrontAngle);
            tabConfig.Controls.Add(cmbSector);
            tabConfig.Controls.Add(lblSector);
            tabConfig.Controls.Add(numHysteresis);
            tabConfig.Controls.Add(lblHysteresis);
            tabConfig.Controls.Add(numCaution);
            tabConfig.Controls.Add(lblCaution);
            tabConfig.Controls.Add(numStop);
            tabConfig.Controls.Add(lblStop);
            tabConfig.Name = "tabConfig";
            tabConfig.Padding = new Padding(3);
            tabConfig.TabIndex = 3;
            tabConfig.Text = "Configuration (saved on Pi)";
            tabConfig.UseVisualStyleBackColor = true;
            //
            // grpPassword
            //
            grpPassword.Controls.Add(btnChangePassword);
            grpPassword.Controls.Add(txtConfirmPassword);
            grpPassword.Controls.Add(lblConfirmPassword);
            grpPassword.Controls.Add(txtNewPassword);
            grpPassword.Controls.Add(lblNewPassword);
            grpPassword.Controls.Add(txtNewUser);
            grpPassword.Controls.Add(lblNewUser);
            grpPassword.Controls.Add(txtCurrentPassword);
            grpPassword.Controls.Add(lblCurrentPassword);
            grpPassword.Location = new Point(600, 4);
            grpPassword.Name = "grpPassword";
            grpPassword.Size = new Size(470, 172);
            grpPassword.TabIndex = 20;
            grpPassword.TabStop = false;
            grpPassword.Text = "Change REST API login (saved as a salted hash on the Pi)";
            //
            // btnChangePassword
            //
            btnChangePassword.Location = new Point(320, 132);
            btnChangePassword.Name = "btnChangePassword";
            btnChangePassword.Size = new Size(130, 28);
            btnChangePassword.TabIndex = 8;
            btnChangePassword.Text = "Change login";
            btnChangePassword.UseVisualStyleBackColor = true;
            btnChangePassword.Click += btnChangePassword_Click;
            //
            // txtConfirmPassword
            //
            txtConfirmPassword.Location = new Point(150, 105);
            txtConfirmPassword.Name = "txtConfirmPassword";
            txtConfirmPassword.Size = new Size(160, 23);
            txtConfirmPassword.TabIndex = 7;
            txtConfirmPassword.UseSystemPasswordChar = true;
            //
            // lblConfirmPassword
            //
            lblConfirmPassword.AutoSize = true;
            lblConfirmPassword.Location = new Point(12, 108);
            lblConfirmPassword.Name = "lblConfirmPassword";
            lblConfirmPassword.TabIndex = 6;
            lblConfirmPassword.Text = "Confirm new:";
            //
            // txtNewPassword
            //
            txtNewPassword.Location = new Point(150, 77);
            txtNewPassword.Name = "txtNewPassword";
            txtNewPassword.Size = new Size(160, 23);
            txtNewPassword.TabIndex = 5;
            txtNewPassword.UseSystemPasswordChar = true;
            //
            // lblNewPassword
            //
            lblNewPassword.AutoSize = true;
            lblNewPassword.Location = new Point(12, 80);
            lblNewPassword.Name = "lblNewPassword";
            lblNewPassword.TabIndex = 4;
            lblNewPassword.Text = "New password:";
            //
            // txtNewUser
            //
            txtNewUser.Location = new Point(150, 49);
            txtNewUser.Name = "txtNewUser";
            txtNewUser.PlaceholderText = "(keep current)";
            txtNewUser.Size = new Size(160, 23);
            txtNewUser.TabIndex = 3;
            //
            // lblNewUser
            //
            lblNewUser.AutoSize = true;
            lblNewUser.Location = new Point(12, 52);
            lblNewUser.Name = "lblNewUser";
            lblNewUser.TabIndex = 2;
            lblNewUser.Text = "New user (optional):";
            //
            // txtCurrentPassword
            //
            txtCurrentPassword.Location = new Point(150, 21);
            txtCurrentPassword.Name = "txtCurrentPassword";
            txtCurrentPassword.Size = new Size(160, 23);
            txtCurrentPassword.TabIndex = 1;
            txtCurrentPassword.UseSystemPasswordChar = true;
            //
            // lblCurrentPassword
            //
            lblCurrentPassword.AutoSize = true;
            lblCurrentPassword.Location = new Point(12, 24);
            lblCurrentPassword.Name = "lblCurrentPassword";
            lblCurrentPassword.TabIndex = 0;
            lblCurrentPassword.Text = "Current password:";
            //
            // btnSaveConfig
            //
            btnSaveConfig.Location = new Point(440, 132);
            btnSaveConfig.Name = "btnSaveConfig";
            btnSaveConfig.Size = new Size(120, 30);
            btnSaveConfig.TabIndex = 19;
            btnSaveConfig.Text = "Save to Pi";
            btnSaveConfig.UseVisualStyleBackColor = true;
            btnSaveConfig.Click += btnSaveConfig_Click;
            //
            // btnLoadConfig
            //
            btnLoadConfig.Location = new Point(310, 132);
            btnLoadConfig.Name = "btnLoadConfig";
            btnLoadConfig.Size = new Size(120, 30);
            btnLoadConfig.TabIndex = 18;
            btnLoadConfig.Text = "Load from Pi";
            btnLoadConfig.UseVisualStyleBackColor = true;
            btnLoadConfig.Click += btnLoadConfig_Click;
            //
            // txtLcdAddress
            //
            txtLcdAddress.Location = new Point(480, 97);
            txtLcdAddress.Name = "txtLcdAddress";
            txtLcdAddress.Size = new Size(80, 23);
            txtLcdAddress.TabIndex = 17;
            txtLcdAddress.Text = "0x27";
            //
            // lblLcdAddress
            //
            lblLcdAddress.AutoSize = true;
            lblLcdAddress.Location = new Point(310, 100);
            lblLcdAddress.Name = "lblLcdAddress";
            lblLcdAddress.TabIndex = 16;
            lblLcdAddress.Text = "LCD I2C address (hex):";
            //
            // numMinPoints
            //
            numMinPoints.Location = new Point(480, 69);
            numMinPoints.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            numMinPoints.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numMinPoints.Name = "numMinPoints";
            numMinPoints.Size = new Size(80, 23);
            numMinPoints.TabIndex = 15;
            numMinPoints.Value = new decimal(new int[] { 3, 0, 0, 0 });
            //
            // lblMinPoints
            //
            lblMinPoints.AutoSize = true;
            lblMinPoints.Location = new Point(310, 72);
            lblMinPoints.Name = "lblMinPoints";
            lblMinPoints.TabIndex = 14;
            lblMinPoints.Text = "Min points per object:";
            //
            // numClusterGap
            //
            numClusterGap.DecimalPlaces = 2;
            numClusterGap.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numClusterGap.Location = new Point(480, 41);
            numClusterGap.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            numClusterGap.Minimum = new decimal(new int[] { 2, 0, 0, 131072 });
            numClusterGap.Name = "numClusterGap";
            numClusterGap.Size = new Size(80, 23);
            numClusterGap.TabIndex = 13;
            numClusterGap.Value = new decimal(new int[] { 15, 0, 0, 131072 });
            //
            // lblClusterGap
            //
            lblClusterGap.AutoSize = true;
            lblClusterGap.Location = new Point(310, 44);
            lblClusterGap.Name = "lblClusterGap";
            lblClusterGap.TabIndex = 12;
            lblClusterGap.Text = "Object split gap (m):";
            //
            // numAngleOffset
            //
            numAngleOffset.DecimalPlaces = 1;
            numAngleOffset.Location = new Point(480, 13);
            numAngleOffset.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            numAngleOffset.Minimum = new decimal(new int[] { 180, 0, 0, int.MinValue });
            numAngleOffset.Name = "numAngleOffset";
            numAngleOffset.Size = new Size(80, 23);
            numAngleOffset.TabIndex = 11;
            //
            // lblAngleOffset
            //
            lblAngleOffset.AutoSize = true;
            lblAngleOffset.Location = new Point(310, 16);
            lblAngleOffset.Name = "lblAngleOffset";
            lblAngleOffset.TabIndex = 10;
            lblAngleOffset.Text = "LiDAR angle offset (°):";
            //
            // numFrontAngle
            //
            numFrontAngle.Location = new Point(190, 125);
            numFrontAngle.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            numFrontAngle.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
            numFrontAngle.Name = "numFrontAngle";
            numFrontAngle.Size = new Size(80, 23);
            numFrontAngle.TabIndex = 9;
            numFrontAngle.Value = new decimal(new int[] { 35, 0, 0, 0 });
            //
            // lblFrontAngle
            //
            lblFrontAngle.AutoSize = true;
            lblFrontAngle.Location = new Point(16, 128);
            lblFrontAngle.Name = "lblFrontAngle";
            lblFrontAngle.TabIndex = 8;
            lblFrontAngle.Text = "Front half-angle (± °):";
            //
            // cmbSector
            //
            cmbSector.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSector.FormattingEnabled = true;
            cmbSector.Items.AddRange(new object[] { "Full", "Front" });
            cmbSector.Location = new Point(190, 97);
            cmbSector.Name = "cmbSector";
            cmbSector.Size = new Size(80, 23);
            cmbSector.TabIndex = 7;
            //
            // lblSector
            //
            lblSector.AutoSize = true;
            lblSector.Location = new Point(16, 100);
            lblSector.Name = "lblSector";
            lblSector.TabIndex = 6;
            lblSector.Text = "Monitoring sector:";
            //
            // numHysteresis
            //
            numHysteresis.DecimalPlaces = 2;
            numHysteresis.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numHysteresis.Location = new Point(190, 69);
            numHysteresis.Maximum = new decimal(new int[] { 3, 0, 0, 65536 });
            numHysteresis.Name = "numHysteresis";
            numHysteresis.Size = new Size(80, 23);
            numHysteresis.TabIndex = 5;
            numHysteresis.Value = new decimal(new int[] { 5, 0, 0, 131072 });
            //
            // lblHysteresis
            //
            lblHysteresis.AutoSize = true;
            lblHysteresis.Location = new Point(16, 72);
            lblHysteresis.Name = "lblHysteresis";
            lblHysteresis.TabIndex = 4;
            lblHysteresis.Text = "Hysteresis (m):";
            //
            // numCaution
            //
            numCaution.DecimalPlaces = 2;
            numCaution.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numCaution.Location = new Point(190, 41);
            numCaution.Maximum = new decimal(new int[] { 8, 0, 0, 0 });
            numCaution.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            numCaution.Name = "numCaution";
            numCaution.Size = new Size(80, 23);
            numCaution.TabIndex = 3;
            numCaution.Value = new decimal(new int[] { 8, 0, 0, 65536 });
            //
            // lblCaution
            //
            lblCaution.AutoSize = true;
            lblCaution.Location = new Point(16, 44);
            lblCaution.Name = "lblCaution";
            lblCaution.TabIndex = 2;
            lblCaution.Text = "Caution distance (m):";
            //
            // numStop
            //
            numStop.DecimalPlaces = 2;
            numStop.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numStop.Location = new Point(190, 13);
            numStop.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            numStop.Minimum = new decimal(new int[] { 15, 0, 0, 131072 });
            numStop.Name = "numStop";
            numStop.Size = new Size(80, 23);
            numStop.TabIndex = 1;
            numStop.Value = new decimal(new int[] { 35, 0, 0, 131072 });
            //
            // lblStop
            //
            lblStop.AutoSize = true;
            lblStop.Location = new Point(16, 16);
            lblStop.Name = "lblStop";
            lblStop.TabIndex = 0;
            lblStop.Text = "Stop distance (m):";
            //
            // tabSystem
            //
            tabSystem.Controls.Add(txtSystem);
            tabSystem.Controls.Add(btnShutdown);
            tabSystem.Controls.Add(btnRefreshSystem);
            tabSystem.Name = "tabSystem";
            tabSystem.Padding = new Padding(3);
            tabSystem.TabIndex = 4;
            tabSystem.Text = "System info / Shutdown";
            tabSystem.UseVisualStyleBackColor = true;
            //
            // txtSystem
            //
            txtSystem.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtSystem.Font = new Font("Consolas", 9F);
            txtSystem.Location = new Point(190, 8);
            txtSystem.Multiline = true;
            txtSystem.Name = "txtSystem";
            txtSystem.ReadOnly = true;
            txtSystem.ScrollBars = ScrollBars.Vertical;
            txtSystem.Size = new Size(1070, 188);
            txtSystem.TabIndex = 2;
            //
            // btnShutdown
            //
            btnShutdown.BackColor = Color.Firebrick;
            btnShutdown.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnShutdown.ForeColor = Color.White;
            btnShutdown.Location = new Point(16, 60);
            btnShutdown.Name = "btnShutdown";
            btnShutdown.Size = new Size(160, 34);
            btnShutdown.TabIndex = 1;
            btnShutdown.Text = "Shut down Pi...";
            btnShutdown.UseVisualStyleBackColor = false;
            btnShutdown.Click += btnShutdown_Click;
            //
            // btnRefreshSystem
            //
            btnRefreshSystem.Location = new Point(16, 16);
            btnRefreshSystem.Name = "btnRefreshSystem";
            btnRefreshSystem.Size = new Size(160, 30);
            btnRefreshSystem.TabIndex = 0;
            btnRefreshSystem.Text = "Get system info";
            btnRefreshSystem.UseVisualStyleBackColor = true;
            btnRefreshSystem.Click += btnRefreshSystem_Click;
            //
            // objectList
            //
            objectList.Dock = DockStyle.Right;
            objectList.Location = new Point(920, 124);
            objectList.Name = "objectList";
            objectList.Size = new Size(360, 460);
            objectList.TabIndex = 3;
            objectList.SelectedObjectChanged += objectList_SelectedObjectChanged;
            //
            // plot
            //
            plot.Dock = DockStyle.Fill;
            plot.Location = new Point(0, 124);
            plot.Name = "plot";
            plot.Size = new Size(920, 460);
            plot.TabIndex = 4;
            plot.ObjectClicked += plot_ObjectClicked;
            //
            // statusStrip
            //
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, lblPollRate });
            statusStrip.Location = new Point(0, 818);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(1280, 22);
            statusStrip.TabIndex = 5;
            //
            // lblStatus
            //
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(1150, 17);
            lblStatus.Spring = true;
            lblStatus.Text = "Ready.";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            //
            // lblPollRate
            //
            lblPollRate.Name = "lblPollRate";
            lblPollRate.Size = new Size(115, 17);
            lblPollRate.Text = "poll: -";
            //
            // MainForm
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1280, 840);
            Controls.Add(plot);
            Controls.Add(objectList);
            Controls.Add(tabControls);
            Controls.Add(pnlBanner);
            Controls.Add(flowConnection);
            Controls.Add(statusStrip);
            MinimumSize = new Size(1000, 700);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "LiDAR AGV Safety Monitor";
            FormClosing += MainForm_FormClosing;
            Load += MainForm_Load;
            flowConnection.ResumeLayout(false);
            flowConnection.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numPort).EndInit();
            pnlBanner.ResumeLayout(false);
            tabControls.ResumeLayout(false);
            tabLeds.ResumeLayout(false);
            tabLeds.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numLedTimeout).EndInit();
            tabEStop.ResumeLayout(false);
            tabEStop.PerformLayout();
            grpSim.ResumeLayout(false);
            grpSim.PerformLayout();
            tabLcd.ResumeLayout(false);
            tabLcd.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numLcdSeconds).EndInit();
            tabConfig.ResumeLayout(false);
            tabConfig.PerformLayout();
            grpPassword.ResumeLayout(false);
            grpPassword.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numMinPoints).EndInit();
            ((System.ComponentModel.ISupportInitialize)numClusterGap).EndInit();
            ((System.ComponentModel.ISupportInitialize)numAngleOffset).EndInit();
            ((System.ComponentModel.ISupportInitialize)numFrontAngle).EndInit();
            ((System.ComponentModel.ISupportInitialize)numHysteresis).EndInit();
            ((System.ComponentModel.ISupportInitialize)numCaution).EndInit();
            ((System.ComponentModel.ISupportInitialize)numStop).EndInit();
            tabSystem.ResumeLayout(false);
            tabSystem.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Timer pollTimer;
        private FlowLayoutPanel flowConnection;
        private Label lblHost;
        private TextBox txtHost;
        private Label lblPort;
        private NumericUpDown numPort;
        private Label lblUser;
        private TextBox txtUser;
        private Label lblPassword;
        private TextBox txtPassword;
        private Button btnConnect;
        private Label lblConnStatus;
        private Panel pnlBanner;
        private Label lblZone;
        private Label lblBannerDetails;
        private Button btnResetEstop;
        private TabControl tabControls;
        private TabPage tabLeds;
        private Label lblLedState;
        private Button btnApplyLeds;
        private NumericUpDown numLedTimeout;
        private Label lblLedTimeout;
        private CheckBox chkRed;
        private CheckBox chkYellow;
        private CheckBox chkGreen;
        private RadioButton rbLedTest;
        private RadioButton rbLedAuto;
        private TabPage tabEStop;
        private GroupBox grpSim;
        private CheckBox chkSimLidar;
        private CheckBox chkSimEStop;
        private Label lblEStopState;
        private Button btnReadEStop;
        private TabPage tabLcd;
        private Label lblLcdPreview;
        private Label lblLcdNow;
        private Button btnSendLcd;
        private NumericUpDown numLcdSeconds;
        private Label lblLcdSeconds;
        private TextBox txtLine2;
        private Label lblLine2;
        private TextBox txtLine1;
        private Label lblLine1;
        private TabPage tabConfig;
        private GroupBox grpPassword;
        private Button btnChangePassword;
        private TextBox txtConfirmPassword;
        private Label lblConfirmPassword;
        private TextBox txtNewPassword;
        private Label lblNewPassword;
        private TextBox txtNewUser;
        private Label lblNewUser;
        private TextBox txtCurrentPassword;
        private Label lblCurrentPassword;
        private Button btnSaveConfig;
        private Button btnLoadConfig;
        private TextBox txtLcdAddress;
        private Label lblLcdAddress;
        private NumericUpDown numMinPoints;
        private Label lblMinPoints;
        private NumericUpDown numClusterGap;
        private Label lblClusterGap;
        private NumericUpDown numAngleOffset;
        private Label lblAngleOffset;
        private NumericUpDown numFrontAngle;
        private Label lblFrontAngle;
        private ComboBox cmbSector;
        private Label lblSector;
        private NumericUpDown numHysteresis;
        private Label lblHysteresis;
        private NumericUpDown numCaution;
        private Label lblCaution;
        private NumericUpDown numStop;
        private Label lblStop;
        private TabPage tabSystem;
        private TextBox txtSystem;
        private Button btnShutdown;
        private Button btnRefreshSystem;
        private LidarPC.Controls.ObjectListControl objectList;
        private LidarPC.Controls.LidarPlotControl plot;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
        private ToolStripStatusLabel lblPollRate;
    }
}
