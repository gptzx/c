namespace MacChanger
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lblAdapter = new System.Windows.Forms.Label();
            this.cboAdapters = new System.Windows.Forms.ComboBox();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.lblPermanent = new System.Windows.Forms.Label();
            this.txtPermanentMac = new System.Windows.Forms.TextBox();
            this.lblCurrent = new System.Windows.Forms.Label();
            this.txtCurrentMac = new System.Windows.Forms.TextBox();
            this.lblIp = new System.Windows.Forms.Label();
            this.txtCurrentIp = new System.Windows.Forms.TextBox();
            this.lblMask = new System.Windows.Forms.Label();
            this.txtMask = new System.Windows.Forms.TextBox();
            this.lblGateway = new System.Windows.Forms.Label();
            this.txtGateway = new System.Windows.Forms.TextBox();
            this.lblDns = new System.Windows.Forms.Label();
            this.txtDns = new System.Windows.Forms.TextBox();
            this.btnAutoToggle = new System.Windows.Forms.Button();
            this.lblNew = new System.Windows.Forms.Label();
            this.txtNewMac = new System.Windows.Forms.TextBox();
            this.btnRandom = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.btnRestore = new System.Windows.Forms.Button();
            this.btnRenewIp = new System.Windows.Forms.Button();
            this.chkIpLog = new System.Windows.Forms.CheckBox();
            this.lblAuto = new System.Windows.Forms.Label();
            this.chkAuto = new System.Windows.Forms.CheckBox();
            this.nudAutoDelay = new System.Windows.Forms.NumericUpDown();
            this.lblAutoSuffix = new System.Windows.Forms.Label();
            this.lblAutoStop = new System.Windows.Forms.Label();
            this.chkAutoStopIp = new System.Windows.Forms.CheckBox();
            this.txtAutoStopIp = new System.Windows.Forms.TextBox();
            this.chkResetDns = new System.Windows.Forms.CheckBox();
            this.lblWait = new System.Windows.Forms.Label();
            this.lblWaitPrefix = new System.Windows.Forms.Label();
            this.nudWaitTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblWaitSuffix = new System.Windows.Forms.Label();
            this.lblLogOptions = new System.Windows.Forms.Label();
            this.chkLogTime = new System.Windows.Forms.CheckBox();
            this.chkLogMask = new System.Windows.Forms.CheckBox();
            this.chkLogGateway = new System.Windows.Forms.CheckBox();
            this.chkLogMac = new System.Windows.Forms.CheckBox();
            this.chkLogBox = new System.Windows.Forms.CheckBox();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.txtIpLog = new System.Windows.Forms.TextBox();
            this.trafficGraph = new MacChanger.TrafficGraph();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.ipTimer = new System.Windows.Forms.Timer(this.components);
            this.secondTimer = new System.Windows.Forms.Timer(this.components);
            this.worker = new System.ComponentModel.BackgroundWorker();
            this.btnBackground = new System.Windows.Forms.Button();
            this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.trayMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.trayOpenItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayExitItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudAutoDelay)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudWaitTimeout)).BeginInit();
            this.SuspendLayout();
            // 
            // lblAdapter
            // 
            this.lblAdapter.AutoSize = true;
            this.lblAdapter.Location = new System.Drawing.Point(12, 16);
            this.lblAdapter.Name = "lblAdapter";
            this.lblAdapter.Size = new System.Drawing.Size(97, 15);
            this.lblAdapter.TabIndex = 0;
            this.lblAdapter.Text = "네트워크 어댑터:";
            // 
            // cboAdapters
            // 
            this.cboAdapters.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboAdapters.FormattingEnabled = true;
            this.cboAdapters.Location = new System.Drawing.Point(118, 12);
            this.cboAdapters.Name = "cboAdapters";
            this.cboAdapters.Size = new System.Drawing.Size(350, 23);
            this.cboAdapters.TabIndex = 1;
            this.cboAdapters.SelectedIndexChanged += new System.EventHandler(this.cboAdapters_SelectedIndexChanged);
            // 
            // btnRefresh
            // 
            this.btnRefresh.Location = new System.Drawing.Point(474, 11);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(74, 25);
            this.btnRefresh.TabIndex = 2;
            this.btnRefresh.Text = "새로 고침";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            // 
            // lblPermanent
            // 
            this.lblPermanent.AutoSize = true;
            this.lblPermanent.Location = new System.Drawing.Point(12, 48);
            this.lblPermanent.Name = "lblPermanent";
            this.lblPermanent.Size = new System.Drawing.Size(99, 15);
            this.lblPermanent.TabIndex = 3;
            this.lblPermanent.Text = "원래(공장) MAC:";
            // 
            // txtPermanentMac
            // 
            this.txtPermanentMac.BackColor = System.Drawing.SystemColors.Window;
            this.txtPermanentMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtPermanentMac.Location = new System.Drawing.Point(118, 44);
            this.txtPermanentMac.Name = "txtPermanentMac";
            this.txtPermanentMac.ReadOnly = true;
            this.txtPermanentMac.Size = new System.Drawing.Size(190, 23);
            this.txtPermanentMac.TabIndex = 4;
            this.txtPermanentMac.TabStop = false;
            // 
            // lblCurrent
            // 
            this.lblCurrent.AutoSize = true;
            this.lblCurrent.Location = new System.Drawing.Point(12, 78);
            this.lblCurrent.Name = "lblCurrent";
            this.lblCurrent.Size = new System.Drawing.Size(64, 15);
            this.lblCurrent.TabIndex = 5;
            this.lblCurrent.Text = "현재 MAC:";
            // 
            // txtCurrentMac
            // 
            this.txtCurrentMac.BackColor = System.Drawing.SystemColors.Window;
            this.txtCurrentMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtCurrentMac.Location = new System.Drawing.Point(118, 74);
            this.txtCurrentMac.Name = "txtCurrentMac";
            this.txtCurrentMac.ReadOnly = true;
            this.txtCurrentMac.Size = new System.Drawing.Size(190, 23);
            this.txtCurrentMac.TabIndex = 6;
            this.txtCurrentMac.TabStop = false;
            // 
            // lblIp
            // 
            this.lblIp.AutoSize = true;
            this.lblIp.Location = new System.Drawing.Point(12, 108);
            this.lblIp.Name = "lblIp";
            this.lblIp.Size = new System.Drawing.Size(50, 15);
            this.lblIp.TabIndex = 7;
            this.lblIp.Text = "현재 IP:";
            // 
            // txtCurrentIp
            // 
            this.txtCurrentIp.BackColor = System.Drawing.SystemColors.Window;
            this.txtCurrentIp.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtCurrentIp.Location = new System.Drawing.Point(118, 104);
            this.txtCurrentIp.Name = "txtCurrentIp";
            this.txtCurrentIp.ReadOnly = true;
            this.txtCurrentIp.Size = new System.Drawing.Size(430, 23);
            this.txtCurrentIp.TabIndex = 8;
            this.txtCurrentIp.TabStop = false;
            // 
            // lblMask
            // 
            this.lblMask.AutoSize = true;
            this.lblMask.Location = new System.Drawing.Point(12, 138);
            this.lblMask.Name = "lblMask";
            this.lblMask.Size = new System.Drawing.Size(84, 15);
            this.lblMask.TabIndex = 9;
            this.lblMask.Text = "서브넷 마스크:";
            // 
            // txtMask
            // 
            this.txtMask.BackColor = System.Drawing.SystemColors.Window;
            this.txtMask.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtMask.Location = new System.Drawing.Point(118, 134);
            this.txtMask.Name = "txtMask";
            this.txtMask.ReadOnly = true;
            this.txtMask.Size = new System.Drawing.Size(430, 23);
            this.txtMask.TabIndex = 10;
            this.txtMask.TabStop = false;
            // 
            // lblGateway
            // 
            this.lblGateway.AutoSize = true;
            this.lblGateway.Location = new System.Drawing.Point(12, 168);
            this.lblGateway.Name = "lblGateway";
            this.lblGateway.Size = new System.Drawing.Size(96, 15);
            this.lblGateway.TabIndex = 11;
            this.lblGateway.Text = "기본 게이트웨이:";
            // 
            // txtGateway
            // 
            this.txtGateway.BackColor = System.Drawing.SystemColors.Window;
            this.txtGateway.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtGateway.Location = new System.Drawing.Point(118, 164);
            this.txtGateway.Name = "txtGateway";
            this.txtGateway.ReadOnly = true;
            this.txtGateway.Size = new System.Drawing.Size(430, 23);
            this.txtGateway.TabIndex = 12;
            this.txtGateway.TabStop = false;
            // 
            // lblDns
            // 
            this.lblDns.AutoSize = true;
            this.lblDns.Location = new System.Drawing.Point(12, 198);
            this.lblDns.Name = "lblDns";
            this.lblDns.Size = new System.Drawing.Size(62, 15);
            this.lblDns.TabIndex = 13;
            this.lblDns.Text = "DNS 서버:";
            // 
            // txtDns
            // 
            this.txtDns.BackColor = System.Drawing.SystemColors.Window;
            this.txtDns.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtDns.Location = new System.Drawing.Point(118, 194);
            this.txtDns.Name = "txtDns";
            this.txtDns.ReadOnly = true;
            this.txtDns.Size = new System.Drawing.Size(430, 23);
            this.txtDns.TabIndex = 14;
            this.txtDns.TabStop = false;
            // 
            // lblNew
            // 
            this.lblNew.AutoSize = true;
            this.lblNew.Location = new System.Drawing.Point(12, 228);
            this.lblNew.Name = "lblNew";
            this.lblNew.Size = new System.Drawing.Size(55, 15);
            this.lblNew.TabIndex = 15;
            this.lblNew.Text = "새 MAC:";
            // 
            // txtNewMac
            // 
            this.txtNewMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtNewMac.Location = new System.Drawing.Point(118, 224);
            this.txtNewMac.MaxLength = 17;
            this.txtNewMac.Name = "txtNewMac";
            this.txtNewMac.Size = new System.Drawing.Size(190, 23);
            this.txtNewMac.TabIndex = 16;
            // 
            // btnRandom
            // 
            this.btnRandom.Location = new System.Drawing.Point(314, 223);
            this.btnRandom.Name = "btnRandom";
            this.btnRandom.Size = new System.Drawing.Size(110, 25);
            this.btnRandom.TabIndex = 17;
            this.btnRandom.Text = "랜덤 MAC 주소";
            this.btnRandom.UseVisualStyleBackColor = true;
            this.btnRandom.Click += new System.EventHandler(this.btnRandom_Click);
            // 
            // btnApply
            // 
            this.btnApply.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.btnApply.Location = new System.Drawing.Point(118, 256);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(110, 30);
            this.btnApply.TabIndex = 19;
            this.btnApply.Text = "변경 적용";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            // 
            // btnRestore
            // 
            this.btnRestore.Location = new System.Drawing.Point(342, 256);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Size = new System.Drawing.Size(110, 30);
            this.btnRestore.TabIndex = 21;
            this.btnRestore.Text = "원상복구";
            this.btnRestore.UseVisualStyleBackColor = true;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            // 
            // btnRenewIp
            // 
            this.btnRenewIp.Location = new System.Drawing.Point(236, 256);
            this.btnRenewIp.Name = "btnRenewIp";
            this.btnRenewIp.Size = new System.Drawing.Size(100, 30);
            this.btnRenewIp.TabIndex = 20;
            this.btnRenewIp.Text = "IP 갱신";
            this.btnRenewIp.UseVisualStyleBackColor = true;
            this.btnRenewIp.Click += new System.EventHandler(this.btnRenewIp_Click);
            // 
            // chkIpLog
            // 
            this.chkIpLog.AutoSize = true;
            this.chkIpLog.Location = new System.Drawing.Point(258, 418);
            this.chkIpLog.Name = "chkIpLog";
            this.chkIpLog.Size = new System.Drawing.Size(129, 19);
            this.chkIpLog.TabIndex = 43;
            this.chkIpLog.Text = "할당 IP 로그 저장";
            this.chkIpLog.UseVisualStyleBackColor = true;
            this.chkIpLog.CheckedChanged += new System.EventHandler(this.chkIpLog_CheckedChanged);
            // 
            // btnBackground
            // 
            this.btnBackground.Location = new System.Drawing.Point(440, 414);
            this.btnBackground.Name = "btnBackground";
            this.btnBackground.Size = new System.Drawing.Size(108, 25);
            this.btnBackground.TabIndex = 44;
            this.btnBackground.Text = "백그라운드 실행";
            this.btnBackground.UseVisualStyleBackColor = true;
            this.btnBackground.Click += new System.EventHandler(this.btnBackground_Click);
            // 
            // notifyIcon
            // 
            this.notifyIcon.ContextMenuStrip = this.trayMenu;
            this.notifyIcon.Text = "MAC 주소 변경 유틸리티";
            this.notifyIcon.DoubleClick += new System.EventHandler(this.trayOpen_Click);
            // 
            // trayMenu
            // 
            this.trayMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.trayOpenItem,
            this.trayExitItem});
            this.trayMenu.Name = "trayMenu";
            this.trayMenu.Size = new System.Drawing.Size(100, 48);
            // 
            // trayOpenItem
            // 
            this.trayOpenItem.Name = "trayOpenItem";
            this.trayOpenItem.Size = new System.Drawing.Size(99, 22);
            this.trayOpenItem.Text = "열기";
            this.trayOpenItem.Click += new System.EventHandler(this.trayOpen_Click);
            // 
            // trayExitItem
            // 
            this.trayExitItem.Name = "trayExitItem";
            this.trayExitItem.Size = new System.Drawing.Size(99, 22);
            this.trayExitItem.Text = "종료";
            this.trayExitItem.Click += new System.EventHandler(this.trayExit_Click);
            // 
            // lblAuto
            // 
            this.lblAuto.AutoSize = true;
            this.lblAuto.Location = new System.Drawing.Point(12, 297);
            this.lblAuto.Name = "lblAuto";
            this.lblAuto.Size = new System.Drawing.Size(62, 15);
            this.lblAuto.TabIndex = 23;
            this.lblAuto.Text = "자동 변경:";
            // 
            // chkAuto
            // 
            this.chkAuto.AutoSize = true;
            this.chkAuto.Location = new System.Drawing.Point(118, 295);
            this.chkAuto.Name = "chkAuto";
            this.chkAuto.Size = new System.Drawing.Size(84, 19);
            this.chkAuto.TabIndex = 24;
            this.chkAuto.Text = "IP 할당 후";
            this.chkAuto.UseVisualStyleBackColor = true;
            this.chkAuto.CheckedChanged += new System.EventHandler(this.chkAuto_CheckedChanged);
            // 
            // nudAutoDelay
            // 
            this.nudAutoDelay.Enabled = false;
            this.nudAutoDelay.Location = new System.Drawing.Point(210, 293);
            this.nudAutoDelay.Maximum = new decimal(new int[] { 604800, 0, 0, 0 });
            this.nudAutoDelay.Name = "nudAutoDelay";
            this.nudAutoDelay.Size = new System.Drawing.Size(72, 23);
            this.nudAutoDelay.TabIndex = 25;
            this.nudAutoDelay.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.nudAutoDelay.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // lblAutoSuffix
            // 
            this.lblAutoSuffix.AutoSize = true;
            this.lblAutoSuffix.Location = new System.Drawing.Point(288, 297);
            this.lblAutoSuffix.Name = "lblAutoSuffix";
            this.lblAutoSuffix.Size = new System.Drawing.Size(110, 15);
            this.lblAutoSuffix.TabIndex = 26;
            this.lblAutoSuffix.Text = "초 뒤 새 MAC 적용";
            // 
            // btnAutoToggle
            // 
            this.btnAutoToggle.Enabled = false;
            this.btnAutoToggle.Location = new System.Drawing.Point(404, 292);
            this.btnAutoToggle.Name = "btnAutoToggle";
            this.btnAutoToggle.Size = new System.Drawing.Size(144, 25);
            this.btnAutoToggle.TabIndex = 27;
            this.btnAutoToggle.Text = "시작";
            this.btnAutoToggle.UseVisualStyleBackColor = true;
            this.btnAutoToggle.Click += new System.EventHandler(this.btnAutoToggle_Click);
            // 
            // lblAutoStop
            // 
            this.lblAutoStop.AutoSize = true;
            this.lblAutoStop.Location = new System.Drawing.Point(12, 359);
            this.lblAutoStop.Name = "lblAutoStop";
            this.lblAutoStop.Size = new System.Drawing.Size(62, 15);
            this.lblAutoStop.TabIndex = 32;
            this.lblAutoStop.Text = "자동 정지:";
            // 
            // chkResetDns
            // 
            this.chkResetDns.AutoSize = true;
            this.chkResetDns.Location = new System.Drawing.Point(436, 226);
            this.chkResetDns.Name = "chkResetDns";
            this.chkResetDns.Size = new System.Drawing.Size(82, 19);
            this.chkResetDns.TabIndex = 18;
            this.chkResetDns.Text = "DNS 초기화";
            this.chkResetDns.UseVisualStyleBackColor = true;
            // 
            // lblWait
            // 
            this.lblWait.AutoSize = true;
            this.lblWait.Location = new System.Drawing.Point(12, 328);
            this.lblWait.Name = "lblWait";
            this.lblWait.Size = new System.Drawing.Size(62, 15);
            this.lblWait.TabIndex = 28;
            this.lblWait.Text = "할당 대기:";
            // 
            // lblWaitPrefix
            // 
            this.lblWaitPrefix.AutoSize = true;
            this.lblWaitPrefix.Location = new System.Drawing.Point(118, 328);
            this.lblWaitPrefix.Name = "lblWaitPrefix";
            this.lblWaitPrefix.Size = new System.Drawing.Size(86, 15);
            this.lblWaitPrefix.TabIndex = 29;
            this.lblWaitPrefix.Text = "IP 할당 대기가";
            // 
            // nudWaitTimeout
            // 
            this.nudWaitTimeout.Enabled = false;
            this.nudWaitTimeout.Location = new System.Drawing.Point(210, 324);
            this.nudWaitTimeout.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.nudWaitTimeout.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudWaitTimeout.Name = "nudWaitTimeout";
            this.nudWaitTimeout.Size = new System.Drawing.Size(60, 23);
            this.nudWaitTimeout.TabIndex = 30;
            this.nudWaitTimeout.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.nudWaitTimeout.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // lblWaitSuffix
            // 
            this.lblWaitSuffix.AutoSize = true;
            this.lblWaitSuffix.Location = new System.Drawing.Point(276, 328);
            this.lblWaitSuffix.Name = "lblWaitSuffix";
            this.lblWaitSuffix.Size = new System.Drawing.Size(150, 15);
            this.lblWaitSuffix.TabIndex = 31;
            this.lblWaitSuffix.Text = "초를 넘으면 새 MAC 적용";
            // 
            // chkAutoStopIp
            // 
            this.chkAutoStopIp.AutoSize = true;
            this.chkAutoStopIp.Location = new System.Drawing.Point(118, 357);
            this.chkAutoStopIp.Name = "chkAutoStopIp";
            this.chkAutoStopIp.Size = new System.Drawing.Size(166, 19);
            this.chkAutoStopIp.TabIndex = 35;
            this.chkAutoStopIp.Text = "지정 IP/대역 할당 시 정지";
            this.chkAutoStopIp.UseVisualStyleBackColor = true;
            this.chkAutoStopIp.CheckedChanged += new System.EventHandler(this.chkAutoStopIp_CheckedChanged);
            // 
            // txtAutoStopIp
            // 
            this.txtAutoStopIp.Enabled = false;
            this.txtAutoStopIp.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtAutoStopIp.Location = new System.Drawing.Point(292, 355);
            this.txtAutoStopIp.MaxLength = 15;
            this.txtAutoStopIp.Name = "txtAutoStopIp";
            this.txtAutoStopIp.Size = new System.Drawing.Size(130, 23);
            this.txtAutoStopIp.TabIndex = 36;
            // 
            // lblLogOptions
            // 
            this.lblLogOptions.AutoSize = true;
            this.lblLogOptions.Location = new System.Drawing.Point(12, 389);
            this.lblLogOptions.Name = "lblLogOptions";
            this.lblLogOptions.Size = new System.Drawing.Size(38, 15);
            this.lblLogOptions.TabIndex = 37;
            this.lblLogOptions.Text = "로그:";
            // 
            // chkLogTime
            // 
            this.chkLogTime.AutoSize = true;
            this.chkLogTime.Location = new System.Drawing.Point(118, 387);
            this.chkLogTime.Name = "chkLogTime";
            this.chkLogTime.Size = new System.Drawing.Size(50, 19);
            this.chkLogTime.TabIndex = 38;
            this.chkLogTime.Text = "시간";
            this.chkLogTime.UseVisualStyleBackColor = true;
            // 
            // chkLogMask
            // 
            this.chkLogMask.AutoSize = true;
            this.chkLogMask.Location = new System.Drawing.Point(180, 387);
            this.chkLogMask.Name = "chkLogMask";
            this.chkLogMask.Size = new System.Drawing.Size(105, 19);
            this.chkLogMask.TabIndex = 39;
            this.chkLogMask.Text = "서브넷 마스크";
            this.chkLogMask.UseVisualStyleBackColor = true;
            // 
            // chkLogGateway
            // 
            this.chkLogGateway.AutoSize = true;
            this.chkLogGateway.Location = new System.Drawing.Point(297, 387);
            this.chkLogGateway.Name = "chkLogGateway";
            this.chkLogGateway.Size = new System.Drawing.Size(117, 19);
            this.chkLogGateway.TabIndex = 40;
            this.chkLogGateway.Text = "기본 게이트웨이";
            this.chkLogGateway.UseVisualStyleBackColor = true;
            // 
            // chkLogMac
            // 
            this.chkLogMac.AutoSize = true;
            this.chkLogMac.Location = new System.Drawing.Point(426, 387);
            this.chkLogMac.Name = "chkLogMac";
            this.chkLogMac.Size = new System.Drawing.Size(55, 19);
            this.chkLogMac.TabIndex = 41;
            this.chkLogMac.Text = "MAC";
            this.chkLogMac.UseVisualStyleBackColor = true;
            // 
            // chkLogBox
            // 
            this.chkLogBox.AutoSize = true;
            this.chkLogBox.Location = new System.Drawing.Point(118, 418);
            this.chkLogBox.Name = "chkLogBox";
            this.chkLogBox.Size = new System.Drawing.Size(107, 19);
            this.chkLogBox.TabIndex = 42;
            this.chkLogBox.Text = "로그 상자 사용";
            this.chkLogBox.UseVisualStyleBackColor = true;
            this.chkLogBox.CheckedChanged += new System.EventHandler(this.chkLogBox_CheckedChanged);
            // 
            // btnClearLog
            // 
            this.btnClearLog.Location = new System.Drawing.Point(458, 258);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(90, 25);
            this.btnClearLog.TabIndex = 22;
            this.btnClearLog.Text = "로그 비우기";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatus.Location = new System.Drawing.Point(12, 447);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(536, 20);
            this.lblStatus.TabIndex = 45;
            this.lblStatus.Text = "상태: 준비";
            // 
            // txtIpLog
            // 
            this.txtIpLog.BackColor = System.Drawing.SystemColors.Window;
            this.txtIpLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtIpLog.Location = new System.Drawing.Point(12, 474);
            this.txtIpLog.Multiline = true;
            this.txtIpLog.Name = "txtIpLog";
            this.txtIpLog.ReadOnly = true;
            this.txtIpLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtIpLog.Size = new System.Drawing.Size(262, 148);
            this.txtIpLog.TabIndex = 46;
            this.txtIpLog.TabStop = false;
            this.txtIpLog.WordWrap = false;
            // 
            // trafficGraph
            // 
            this.trafficGraph.Location = new System.Drawing.Point(286, 474);
            this.trafficGraph.Name = "trafficGraph";
            this.trafficGraph.Size = new System.Drawing.Size(262, 148);
            this.trafficGraph.TabIndex = 47;
            this.trafficGraph.TabStop = false;
            // 
            // ipTimer
            // 
            this.ipTimer.Interval = 1000;
            this.ipTimer.Tick += new System.EventHandler(this.ipTimer_Tick);
            // 
            // secondTimer
            // 
            this.secondTimer.Interval = 1000;
            this.secondTimer.Tick += new System.EventHandler(this.secondTimer_Tick);
            // 
            // worker
            // 
            this.worker.WorkerReportsProgress = true;
            this.worker.DoWork += new System.ComponentModel.DoWorkEventHandler(this.worker_DoWork);
            this.worker.ProgressChanged += new System.ComponentModel.ProgressChangedEventHandler(this.worker_ProgressChanged);
            this.worker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.worker_RunWorkerCompleted);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(560, 634);
            this.Controls.Add(this.trafficGraph);
            this.Controls.Add(this.txtIpLog);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnClearLog);
            this.Controls.Add(this.chkLogBox);
            this.Controls.Add(this.chkLogMac);
            this.Controls.Add(this.chkLogGateway);
            this.Controls.Add(this.chkLogMask);
            this.Controls.Add(this.chkLogTime);
            this.Controls.Add(this.lblLogOptions);
            this.Controls.Add(this.chkResetDns);
            this.Controls.Add(this.lblWaitSuffix);
            this.Controls.Add(this.nudWaitTimeout);
            this.Controls.Add(this.lblWaitPrefix);
            this.Controls.Add(this.lblWait);
            this.Controls.Add(this.txtAutoStopIp);
            this.Controls.Add(this.chkAutoStopIp);
            this.Controls.Add(this.lblAutoStop);
            this.Controls.Add(this.lblAutoSuffix);
            this.Controls.Add(this.nudAutoDelay);
            this.Controls.Add(this.chkAuto);
            this.Controls.Add(this.lblAuto);
            this.Controls.Add(this.btnAutoToggle);
            this.Controls.Add(this.txtDns);
            this.Controls.Add(this.lblDns);
            this.Controls.Add(this.txtGateway);
            this.Controls.Add(this.lblGateway);
            this.Controls.Add(this.txtMask);
            this.Controls.Add(this.lblMask);
            this.Controls.Add(this.btnBackground);
            this.Controls.Add(this.chkIpLog);
            this.Controls.Add(this.btnRenewIp);
            this.Controls.Add(this.btnRestore);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.btnRandom);
            this.Controls.Add(this.txtNewMac);
            this.Controls.Add(this.lblNew);
            this.Controls.Add(this.txtCurrentIp);
            this.Controls.Add(this.lblIp);
            this.Controls.Add(this.txtCurrentMac);
            this.Controls.Add(this.lblCurrent);
            this.Controls.Add(this.txtPermanentMac);
            this.Controls.Add(this.lblPermanent);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.cboAdapters);
            this.Controls.Add(this.lblAdapter);
            this.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "MAC 주소 변경 유틸리티";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            ((System.ComponentModel.ISupportInitialize)(this.nudAutoDelay)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudWaitTimeout)).EndInit();
            this.trayMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblAdapter;
        private System.Windows.Forms.ComboBox cboAdapters;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Label lblPermanent;
        private System.Windows.Forms.TextBox txtPermanentMac;
        private System.Windows.Forms.Label lblCurrent;
        private System.Windows.Forms.TextBox txtCurrentMac;
        private System.Windows.Forms.Label lblIp;
        private System.Windows.Forms.TextBox txtCurrentIp;
        private System.Windows.Forms.Label lblMask;
        private System.Windows.Forms.TextBox txtMask;
        private System.Windows.Forms.Label lblGateway;
        private System.Windows.Forms.TextBox txtGateway;
        private System.Windows.Forms.Label lblDns;
        private System.Windows.Forms.TextBox txtDns;
        private System.Windows.Forms.Button btnAutoToggle;
        private System.Windows.Forms.Label lblNew;
        private System.Windows.Forms.TextBox txtNewMac;
        private System.Windows.Forms.Button btnRandom;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnRenewIp;
        private System.Windows.Forms.CheckBox chkIpLog;
        private System.Windows.Forms.Button btnBackground;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.ContextMenuStrip trayMenu;
        private System.Windows.Forms.ToolStripMenuItem trayOpenItem;
        private System.Windows.Forms.ToolStripMenuItem trayExitItem;
        private System.Windows.Forms.Label lblAuto;
        private System.Windows.Forms.CheckBox chkAuto;
        private System.Windows.Forms.NumericUpDown nudAutoDelay;
        private System.Windows.Forms.Label lblAutoSuffix;
        private System.Windows.Forms.Label lblAutoStop;
        private System.Windows.Forms.CheckBox chkAutoStopIp;
        private System.Windows.Forms.TextBox txtAutoStopIp;
        private System.Windows.Forms.CheckBox chkResetDns;
        private System.Windows.Forms.Label lblWait;
        private System.Windows.Forms.Label lblWaitPrefix;
        private System.Windows.Forms.NumericUpDown nudWaitTimeout;
        private System.Windows.Forms.Label lblWaitSuffix;
        private System.Windows.Forms.Label lblLogOptions;
        private System.Windows.Forms.CheckBox chkLogTime;
        private System.Windows.Forms.CheckBox chkLogMask;
        private System.Windows.Forms.CheckBox chkLogGateway;
        private System.Windows.Forms.CheckBox chkLogMac;
        private System.Windows.Forms.CheckBox chkLogBox;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.TextBox txtIpLog;
        private TrafficGraph trafficGraph;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Timer ipTimer;
        private System.Windows.Forms.Timer secondTimer;
        private System.ComponentModel.BackgroundWorker worker;
    }
}
