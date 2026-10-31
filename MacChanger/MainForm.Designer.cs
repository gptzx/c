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

        // 이 파일은 손으로 관리한다 (Visual Studio 디자이너용 아님): 컨트롤의 위치·크기는 아래 표(Layout)에 네 값씩(x, y, 너비, 높이; 너비 0 = AutoSize) 적고
        // 한 루프가 적용한다. 표의 순서 = 탭 순서 = Controls 추가 순서 (TabIndex 는 모두 0 이므로 추가 순서가 탭 순서가 된다).
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            lblAdapter = new System.Windows.Forms.Label();
            cboAdapters = new System.Windows.Forms.ComboBox();
            btnRefresh = new System.Windows.Forms.Button();
            lblPermanent = new System.Windows.Forms.Label();
            txtPermanentMac = new System.Windows.Forms.TextBox();
            lblCurrent = new System.Windows.Forms.Label();
            txtCurrentMac = new System.Windows.Forms.TextBox();
            lblIp = new System.Windows.Forms.Label();
            txtCurrentIp = new System.Windows.Forms.TextBox();
            lblMask = new System.Windows.Forms.Label();
            txtMask = new System.Windows.Forms.TextBox();
            lblGateway = new System.Windows.Forms.Label();
            txtGateway = new System.Windows.Forms.TextBox();
            lblDns = new System.Windows.Forms.Label();
            txtDns = new System.Windows.Forms.TextBox();
            lblNew = new System.Windows.Forms.Label();
            txtNewMac = new System.Windows.Forms.TextBox();
            btnRandom = new System.Windows.Forms.Button();
            chkResetDns = new System.Windows.Forms.CheckBox();
            btnApply = new System.Windows.Forms.Button();
            btnRenewIp = new System.Windows.Forms.Button();
            btnRestore = new System.Windows.Forms.Button();
            btnClearLog = new System.Windows.Forms.Button();
            lblAuto = new System.Windows.Forms.Label();
            chkAuto = new System.Windows.Forms.CheckBox();
            lblModeIp = new System.Windows.Forms.Label();
            modeSwitch = new ModeSwitch();
            lblModeTimer = new System.Windows.Forms.Label();
            btnAutoToggle = new System.Windows.Forms.Button();
            lblDelayPrefix = new System.Windows.Forms.Label();
            nudAutoDelay = new System.Windows.Forms.NumericUpDown();
            lblAutoSuffix = new System.Windows.Forms.Label();
            lblWait = new System.Windows.Forms.Label();
            lblWaitPrefix = new System.Windows.Forms.Label();
            nudWaitTimeout = new System.Windows.Forms.NumericUpDown();
            lblWaitSuffix = new System.Windows.Forms.Label();
            lblIntervalPrefix = new System.Windows.Forms.Label();
            nudInterval = new System.Windows.Forms.NumericUpDown();
            lblIntervalSuffix = new System.Windows.Forms.Label();
            lblAutoStop = new System.Windows.Forms.Label();
            chkAutoStopIp = new System.Windows.Forms.CheckBox();
            txtAutoStopIp = new System.Windows.Forms.TextBox();
            lblRelease = new System.Windows.Forms.Label();
            chkReleaseIp = new System.Windows.Forms.CheckBox();
            btnReleaseIp = new System.Windows.Forms.Button();
            lblLogOptions = new System.Windows.Forms.Label();
            chkLogTime = new System.Windows.Forms.CheckBox();
            chkLogMask = new System.Windows.Forms.CheckBox();
            chkLogGateway = new System.Windows.Forms.CheckBox();
            chkLogMac = new System.Windows.Forms.CheckBox();
            chkLogBox = new System.Windows.Forms.CheckBox();
            chkIpLog = new System.Windows.Forms.CheckBox();
            btnBackground = new System.Windows.Forms.Button();
            lblStatus = new System.Windows.Forms.Label();
            txtIpLog = new System.Windows.Forms.TextBox();
            trafficGraph = new TrafficGraph();
            toolTip = new System.Windows.Forms.ToolTip(components);
            ipTimer = new System.Windows.Forms.Timer(components);
            graphTimer = new System.Windows.Forms.Timer(components);
            secondTimer = new System.Windows.Forms.Timer(components);
            worker = new System.ComponentModel.BackgroundWorker();
            notifyIcon = new System.Windows.Forms.NotifyIcon(components);
            SuspendLayout();
            System.Drawing.Font mono = new System.Drawing.Font("Consolas", 10F);
            System.Drawing.Font bold = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            //
            // 글자·동작
            //
            lblAdapter.Text = "네트워크 어댑터:";
            cboAdapters.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboAdapters.FormattingEnabled = true;
            cboAdapters.SelectedIndexChanged += new System.EventHandler(cboAdapters_SelectedIndexChanged);
            btnRefresh.Text = "새로 고침";
            btnRefresh.Click += new System.EventHandler(btnRefresh_Click);
            lblPermanent.Text = "원래(공장) MAC:";
            lblCurrent.Text = "현재 MAC:";
            lblIp.Text = "현재 IP:";
            lblMask.Text = "서브넷 마스크:";
            lblGateway.Text = "기본 게이트웨이:";
            lblDns.Text = "DNS 서버:";
            foreach (System.Windows.Forms.TextBox box in new System.Windows.Forms.TextBox[] { txtPermanentMac, txtCurrentMac, txtCurrentIp, txtMask, txtGateway, txtDns })
            {
                box.BackColor = System.Drawing.SystemColors.Window;
                box.Font = mono;
                box.ReadOnly = true;
                box.TabStop = false;
            }
            lblNew.Text = "새 MAC:";
            txtNewMac.Font = mono;
            txtNewMac.MaxLength = 17;
            btnRandom.Text = "랜덤 MAC 주소";
            btnRandom.Click += new System.EventHandler(btnRandom_Click);
            chkResetDns.Text = "DNS 초기화";
            btnApply.Font = bold;
            btnApply.Text = "변경 적용";
            btnApply.Click += new System.EventHandler(btnApply_Click);
            btnRenewIp.Text = "IP 갱신";
            btnRenewIp.Click += new System.EventHandler(btnRenewIp_Click);
            btnRestore.Text = "원상복구";
            btnRestore.Click += new System.EventHandler(btnRestore_Click);
            btnClearLog.Text = "로그 비우기";
            btnClearLog.Click += new System.EventHandler(btnClearLog_Click);
            lblAuto.Text = "자동 변경:";
            chkAuto.Text = "사용";
            chkAuto.CheckedChanged += new System.EventHandler(chkAuto_CheckedChanged);
            lblModeIp.Text = "IP 할당 시";
            lblModeIp.Click += new System.EventHandler(lblModeIp_Click);
            modeSwitch.CheckedChanged += new System.EventHandler(modeSwitch_CheckedChanged);
            lblModeTimer.Text = "일정 시간마다";
            lblModeTimer.Click += new System.EventHandler(lblModeTimer_Click);
            btnAutoToggle.Enabled = false;
            btnAutoToggle.Text = "시작";
            btnAutoToggle.Click += new System.EventHandler(btnAutoToggle_Click);
            lblDelayPrefix.Text = "IP 할당 후";
            nudAutoDelay.Maximum = 604800M;
            nudAutoDelay.Value = 10M;
            lblAutoSuffix.Text = "초 뒤 새 MAC 적용";
            lblWait.Text = "할당 대기:";
            lblWaitPrefix.Text = "IP 할당 대기가";
            nudWaitTimeout.Maximum = 60M;
            nudWaitTimeout.Minimum = 10M;
            nudWaitTimeout.Value = 30M;
            lblWaitSuffix.Text = "초를 넘으면 새 MAC 적용";
            lblIntervalPrefix.Text = "IP와 관계없이";
            nudInterval.Maximum = 604800M;
            nudInterval.Minimum = 10M;
            nudInterval.Value = 60M;
            lblIntervalSuffix.Text = "초마다 새 MAC 적용";
            foreach (System.Windows.Forms.NumericUpDown nud in new System.Windows.Forms.NumericUpDown[] { nudAutoDelay, nudWaitTimeout, nudInterval })
            {
                nud.Enabled = false;
                nud.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            }
            lblAutoStop.Text = "자동 정지:";
            chkAutoStopIp.Text = "지정 IP/대역 할당 시 정지";
            chkAutoStopIp.CheckedChanged += new System.EventHandler(chkAutoStopIp_CheckedChanged);
            txtAutoStopIp.Enabled = false;
            txtAutoStopIp.Font = mono;
            txtAutoStopIp.MaxLength = 15;
            lblRelease.Text = "IP 반환:";
            chkReleaseIp.Text = "DHCP 서버에 IP 반환";
            btnReleaseIp.Text = "즉시 반환";
            btnReleaseIp.Click += new System.EventHandler(btnReleaseIp_Click);
            lblLogOptions.Text = "로그:";
            chkLogTime.Text = "시간";
            chkLogMask.Text = "서브넷 마스크";
            chkLogGateway.Text = "기본 게이트웨이";
            chkLogMac.Text = "MAC";
            chkLogBox.Text = "로그 상자 사용";
            chkLogBox.CheckedChanged += new System.EventHandler(chkLogBox_CheckedChanged);
            chkIpLog.Text = "할당 IP 로그 저장";
            chkIpLog.CheckedChanged += new System.EventHandler(chkIpLog_CheckedChanged);
            btnBackground.Text = "백그라운드 실행";
            btnBackground.Click += new System.EventHandler(btnBackground_Click);
            lblStatus.AutoEllipsis = true;
            lblStatus.Font = bold;
            lblStatus.Text = "상태: 준비";
            txtIpLog.BackColor = System.Drawing.SystemColors.Window;
            txtIpLog.Font = new System.Drawing.Font("Consolas", 9F);
            txtIpLog.Multiline = true;
            txtIpLog.ReadOnly = true;
            txtIpLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            txtIpLog.TabStop = false;
            txtIpLog.WordWrap = false;
            //
            // 배치 (탭 순서대로; x, y, 너비, 높이 — 너비 0 이면 AutoSize)
            //
            System.Windows.Forms.Control[] all =
            {
                lblAdapter, cboAdapters, btnRefresh,
                lblPermanent, txtPermanentMac, lblCurrent, txtCurrentMac, lblIp, txtCurrentIp, lblMask, txtMask, lblGateway, txtGateway, lblDns, txtDns,
                lblNew, txtNewMac, btnRandom, chkResetDns,
                btnApply, btnRenewIp, btnRestore, btnClearLog,
                lblAuto, chkAuto, lblModeIp, modeSwitch, lblModeTimer, btnAutoToggle,
                lblDelayPrefix, nudAutoDelay, lblAutoSuffix,
                lblWait, lblWaitPrefix, nudWaitTimeout, lblWaitSuffix,
                lblIntervalPrefix, nudInterval, lblIntervalSuffix,
                lblAutoStop, chkAutoStopIp, txtAutoStopIp,
                lblRelease, chkReleaseIp, btnReleaseIp,
                lblLogOptions, chkLogTime, chkLogMask, chkLogGateway, chkLogMac,
                chkLogBox, chkIpLog, btnBackground,
                lblStatus, txtIpLog, trafficGraph
            };
            short[] layout =
            {
                12, 16, 0, 0,   118, 12, 350, 23,   474, 11, 74, 25,
                12, 48, 0, 0,   118, 44, 190, 23,   12, 78, 0, 0,   118, 74, 190, 23,   12, 108, 0, 0,   118, 104, 430, 23,   12, 138, 0, 0,   118, 134, 430, 23,   12, 168, 0, 0,   118, 164, 430, 23,   12, 198, 0, 0,   118, 194, 430, 23,
                12, 228, 0, 0,   118, 224, 190, 23,   314, 223, 110, 25,   436, 226, 0, 0,
                118, 256, 110, 30,   236, 256, 100, 30,   342, 256, 110, 30,   458, 258, 90, 25,
                12, 297, 0, 0,   118, 295, 0, 0,   172, 297, 0, 0,   240, 294, 44, 20,   290, 297, 0, 0,   404, 292, 144, 25,
                118, 328, 0, 0,   210, 324, 72, 23,   288, 328, 0, 0,
                12, 359, 0, 0,   118, 359, 0, 0,   210, 355, 60, 23,   276, 359, 0, 0,
                118, 390, 0, 0,   210, 386, 72, 23,   288, 390, 0, 0,
                12, 421, 0, 0,   118, 419, 0, 0,   292, 417, 130, 23,
                12, 451, 0, 0,   118, 449, 0, 0,   404, 446, 144, 25,
                12, 481, 0, 0,   118, 479, 0, 0,   180, 479, 0, 0,   297, 479, 0, 0,   426, 479, 0, 0,
                118, 510, 0, 0,   258, 510, 0, 0,   440, 506, 108, 25,
                12, 539, 536, 20,   12, 566, 262, 167,   286, 566, 262, 167
            };
            for (int i = 0; i < all.Length; i++)
            {
                System.Windows.Forms.Control c = all[i];
                int j = i * 4;
                c.Location = new System.Drawing.Point(layout[j], layout[j + 1]);
                if (layout[j + 2] > 0) c.Size = new System.Drawing.Size(layout[j + 2], layout[j + 3]);
                else c.AutoSize = true;
                Controls.Add(c);
            }
            //
            // 구성 요소
            //
            notifyIcon.Text = "MAC 주소 변경 유틸리티";
            notifyIcon.MouseUp += new System.Windows.Forms.MouseEventHandler(notifyIcon_MouseUp);
            ipTimer.Tick += new System.EventHandler(ipTimer_Tick);
            graphTimer.Interval = 1000;
            graphTimer.Tick += new System.EventHandler(graphTimer_Tick);
            secondTimer.Interval = 1000;
            secondTimer.Tick += new System.EventHandler(secondTimer_Tick);
            worker.WorkerReportsProgress = true;
            worker.DoWork += new System.ComponentModel.DoWorkEventHandler(worker_DoWork);
            worker.ProgressChanged += new System.ComponentModel.ProgressChangedEventHandler(worker_ProgressChanged);
            worker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);
            //
            // MainForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(560, 745);
            Font = new System.Drawing.Font("맑은 고딕", 9F);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "MAC 주소 변경 유틸리티";
            FormClosing += new System.Windows.Forms.FormClosingEventHandler(MainForm_FormClosing);
            Shown += new System.EventHandler(MainForm_Shown);
            ResumeLayout(false);
            PerformLayout();
        }

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
        private System.Windows.Forms.Label lblNew;
        private System.Windows.Forms.TextBox txtNewMac;
        private System.Windows.Forms.Button btnRandom;
        private System.Windows.Forms.CheckBox chkResetDns;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnRenewIp;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.Label lblAuto;
        private System.Windows.Forms.CheckBox chkAuto;
        private System.Windows.Forms.Label lblModeIp;
        private ModeSwitch modeSwitch;
        private System.Windows.Forms.Label lblModeTimer;
        private System.Windows.Forms.Button btnAutoToggle;
        private System.Windows.Forms.Label lblDelayPrefix;
        private System.Windows.Forms.NumericUpDown nudAutoDelay;
        private System.Windows.Forms.Label lblAutoSuffix;
        private System.Windows.Forms.Label lblWait;
        private System.Windows.Forms.Label lblWaitPrefix;
        private System.Windows.Forms.NumericUpDown nudWaitTimeout;
        private System.Windows.Forms.Label lblWaitSuffix;
        private System.Windows.Forms.Label lblIntervalPrefix;
        private System.Windows.Forms.NumericUpDown nudInterval;
        private System.Windows.Forms.Label lblIntervalSuffix;
        private System.Windows.Forms.Label lblAutoStop;
        private System.Windows.Forms.CheckBox chkAutoStopIp;
        private System.Windows.Forms.TextBox txtAutoStopIp;
        private System.Windows.Forms.Label lblRelease;
        private System.Windows.Forms.CheckBox chkReleaseIp;
        private System.Windows.Forms.Button btnReleaseIp;
        private System.Windows.Forms.Label lblLogOptions;
        private System.Windows.Forms.CheckBox chkLogTime;
        private System.Windows.Forms.CheckBox chkLogMask;
        private System.Windows.Forms.CheckBox chkLogGateway;
        private System.Windows.Forms.CheckBox chkLogMac;
        private System.Windows.Forms.CheckBox chkLogBox;
        private System.Windows.Forms.CheckBox chkIpLog;
        private System.Windows.Forms.Button btnBackground;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.TextBox txtIpLog;
        private TrafficGraph trafficGraph;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Timer ipTimer;
        private System.Windows.Forms.Timer graphTimer;
        private System.Windows.Forms.Timer secondTimer;
        private System.ComponentModel.BackgroundWorker worker;
        private System.Windows.Forms.NotifyIcon notifyIcon;
    }
}
