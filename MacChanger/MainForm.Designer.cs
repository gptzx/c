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
            this.chkIpLog = new System.Windows.Forms.CheckBox();
            this.lblAuto = new System.Windows.Forms.Label();
            this.chkAuto = new System.Windows.Forms.CheckBox();
            this.nudAutoDelay = new System.Windows.Forms.NumericUpDown();
            this.lblAutoSuffix = new System.Windows.Forms.Label();
            this.lblLogOptions = new System.Windows.Forms.Label();
            this.chkLogTime = new System.Windows.Forms.CheckBox();
            this.chkLogMac = new System.Windows.Forms.CheckBox();
            this.chkShowLog = new System.Windows.Forms.CheckBox();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.txtIpLog = new System.Windows.Forms.TextBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.ipTimer = new System.Windows.Forms.Timer(this.components);
            this.worker = new System.ComponentModel.BackgroundWorker();
            ((System.ComponentModel.ISupportInitialize)(this.nudAutoDelay)).BeginInit();
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
            this.lblPermanent.Location = new System.Drawing.Point(12, 50);
            this.lblPermanent.Name = "lblPermanent";
            this.lblPermanent.Size = new System.Drawing.Size(99, 15);
            this.lblPermanent.TabIndex = 3;
            this.lblPermanent.Text = "원래(공장) MAC:";
            // 
            // txtPermanentMac
            // 
            this.txtPermanentMac.BackColor = System.Drawing.SystemColors.Window;
            this.txtPermanentMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtPermanentMac.Location = new System.Drawing.Point(118, 46);
            this.txtPermanentMac.Name = "txtPermanentMac";
            this.txtPermanentMac.ReadOnly = true;
            this.txtPermanentMac.Size = new System.Drawing.Size(190, 23);
            this.txtPermanentMac.TabIndex = 4;
            this.txtPermanentMac.TabStop = false;
            // 
            // lblCurrent
            // 
            this.lblCurrent.AutoSize = true;
            this.lblCurrent.Location = new System.Drawing.Point(12, 82);
            this.lblCurrent.Name = "lblCurrent";
            this.lblCurrent.Size = new System.Drawing.Size(64, 15);
            this.lblCurrent.TabIndex = 5;
            this.lblCurrent.Text = "현재 MAC:";
            // 
            // txtCurrentMac
            // 
            this.txtCurrentMac.BackColor = System.Drawing.SystemColors.Window;
            this.txtCurrentMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtCurrentMac.Location = new System.Drawing.Point(118, 78);
            this.txtCurrentMac.Name = "txtCurrentMac";
            this.txtCurrentMac.ReadOnly = true;
            this.txtCurrentMac.Size = new System.Drawing.Size(190, 23);
            this.txtCurrentMac.TabIndex = 6;
            this.txtCurrentMac.TabStop = false;
            // 
            // lblIp
            // 
            this.lblIp.AutoSize = true;
            this.lblIp.Location = new System.Drawing.Point(12, 114);
            this.lblIp.Name = "lblIp";
            this.lblIp.Size = new System.Drawing.Size(50, 15);
            this.lblIp.TabIndex = 7;
            this.lblIp.Text = "현재 IP:";
            // 
            // txtCurrentIp
            // 
            this.txtCurrentIp.BackColor = System.Drawing.SystemColors.Window;
            this.txtCurrentIp.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtCurrentIp.Location = new System.Drawing.Point(118, 110);
            this.txtCurrentIp.Name = "txtCurrentIp";
            this.txtCurrentIp.ReadOnly = true;
            this.txtCurrentIp.Size = new System.Drawing.Size(430, 23);
            this.txtCurrentIp.TabIndex = 8;
            this.txtCurrentIp.TabStop = false;
            // 
            // lblMask
            // 
            this.lblMask.AutoSize = true;
            this.lblMask.Location = new System.Drawing.Point(12, 146);
            this.lblMask.Name = "lblMask";
            this.lblMask.Size = new System.Drawing.Size(84, 15);
            this.lblMask.TabIndex = 26;
            this.lblMask.Text = "서브넷 마스크:";
            // 
            // txtMask
            // 
            this.txtMask.BackColor = System.Drawing.SystemColors.Window;
            this.txtMask.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtMask.Location = new System.Drawing.Point(118, 142);
            this.txtMask.Name = "txtMask";
            this.txtMask.ReadOnly = true;
            this.txtMask.Size = new System.Drawing.Size(430, 23);
            this.txtMask.TabIndex = 27;
            this.txtMask.TabStop = false;
            // 
            // lblGateway
            // 
            this.lblGateway.AutoSize = true;
            this.lblGateway.Location = new System.Drawing.Point(12, 178);
            this.lblGateway.Name = "lblGateway";
            this.lblGateway.Size = new System.Drawing.Size(96, 15);
            this.lblGateway.TabIndex = 28;
            this.lblGateway.Text = "기본 게이트웨이:";
            // 
            // txtGateway
            // 
            this.txtGateway.BackColor = System.Drawing.SystemColors.Window;
            this.txtGateway.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtGateway.Location = new System.Drawing.Point(118, 174);
            this.txtGateway.Name = "txtGateway";
            this.txtGateway.ReadOnly = true;
            this.txtGateway.Size = new System.Drawing.Size(430, 23);
            this.txtGateway.TabIndex = 29;
            this.txtGateway.TabStop = false;
            // 
            // lblDns
            // 
            this.lblDns.AutoSize = true;
            this.lblDns.Location = new System.Drawing.Point(12, 210);
            this.lblDns.Name = "lblDns";
            this.lblDns.Size = new System.Drawing.Size(62, 15);
            this.lblDns.TabIndex = 30;
            this.lblDns.Text = "DNS 서버:";
            // 
            // txtDns
            // 
            this.txtDns.BackColor = System.Drawing.SystemColors.Window;
            this.txtDns.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtDns.Location = new System.Drawing.Point(118, 206);
            this.txtDns.Name = "txtDns";
            this.txtDns.ReadOnly = true;
            this.txtDns.Size = new System.Drawing.Size(430, 23);
            this.txtDns.TabIndex = 31;
            this.txtDns.TabStop = false;
            // 
            // lblNew
            // 
            this.lblNew.AutoSize = true;
            this.lblNew.Location = new System.Drawing.Point(12, 242);
            this.lblNew.Name = "lblNew";
            this.lblNew.Size = new System.Drawing.Size(55, 15);
            this.lblNew.TabIndex = 9;
            this.lblNew.Text = "새 MAC:";
            // 
            // txtNewMac
            // 
            this.txtNewMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtNewMac.Location = new System.Drawing.Point(118, 238);
            this.txtNewMac.MaxLength = 17;
            this.txtNewMac.Name = "txtNewMac";
            this.txtNewMac.Size = new System.Drawing.Size(190, 23);
            this.txtNewMac.TabIndex = 10;
            // 
            // btnRandom
            // 
            this.btnRandom.Location = new System.Drawing.Point(314, 237);
            this.btnRandom.Name = "btnRandom";
            this.btnRandom.Size = new System.Drawing.Size(90, 25);
            this.btnRandom.TabIndex = 11;
            this.btnRandom.Text = "랜덤 생성";
            this.btnRandom.UseVisualStyleBackColor = true;
            this.btnRandom.Click += new System.EventHandler(this.btnRandom_Click);
            // 
            // btnApply
            // 
            this.btnApply.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.btnApply.Location = new System.Drawing.Point(118, 274);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(110, 30);
            this.btnApply.TabIndex = 12;
            this.btnApply.Text = "변경 적용";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            // 
            // btnRestore
            // 
            this.btnRestore.Location = new System.Drawing.Point(236, 274);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Size = new System.Drawing.Size(110, 30);
            this.btnRestore.TabIndex = 13;
            this.btnRestore.Text = "원상복구";
            this.btnRestore.UseVisualStyleBackColor = true;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            // 
            // chkIpLog
            // 
            this.chkIpLog.AutoSize = true;
            this.chkIpLog.Location = new System.Drawing.Point(380, 346);
            this.chkIpLog.Name = "chkIpLog";
            this.chkIpLog.Size = new System.Drawing.Size(129, 19);
            this.chkIpLog.TabIndex = 23;
            this.chkIpLog.Text = "할당 IP 로그 저장";
            this.chkIpLog.UseVisualStyleBackColor = true;
            this.chkIpLog.CheckedChanged += new System.EventHandler(this.chkIpLog_CheckedChanged);
            // 
            // lblAuto
            // 
            this.lblAuto.AutoSize = true;
            this.lblAuto.Location = new System.Drawing.Point(12, 316);
            this.lblAuto.Name = "lblAuto";
            this.lblAuto.Size = new System.Drawing.Size(62, 15);
            this.lblAuto.TabIndex = 15;
            this.lblAuto.Text = "자동 변경:";
            // 
            // chkAuto
            // 
            this.chkAuto.AutoSize = true;
            this.chkAuto.Location = new System.Drawing.Point(118, 314);
            this.chkAuto.Name = "chkAuto";
            this.chkAuto.Size = new System.Drawing.Size(84, 19);
            this.chkAuto.TabIndex = 16;
            this.chkAuto.Text = "IP 할당 후";
            this.chkAuto.UseVisualStyleBackColor = true;
            this.chkAuto.CheckedChanged += new System.EventHandler(this.chkAuto_CheckedChanged);
            // 
            // nudAutoDelay
            // 
            this.nudAutoDelay.Location = new System.Drawing.Point(210, 312);
            this.nudAutoDelay.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.nudAutoDelay.Name = "nudAutoDelay";
            this.nudAutoDelay.Size = new System.Drawing.Size(60, 23);
            this.nudAutoDelay.TabIndex = 17;
            this.nudAutoDelay.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.nudAutoDelay.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // lblAutoSuffix
            // 
            this.lblAutoSuffix.AutoSize = true;
            this.lblAutoSuffix.Location = new System.Drawing.Point(276, 316);
            this.lblAutoSuffix.Name = "lblAutoSuffix";
            this.lblAutoSuffix.Size = new System.Drawing.Size(110, 15);
            this.lblAutoSuffix.TabIndex = 33;
            this.lblAutoSuffix.Text = "초 뒤 새 MAC 적용";
            // 
            // btnAutoToggle
            // 
            this.btnAutoToggle.Enabled = false;
            this.btnAutoToggle.Location = new System.Drawing.Point(458, 311);
            this.btnAutoToggle.Name = "btnAutoToggle";
            this.btnAutoToggle.Size = new System.Drawing.Size(90, 25);
            this.btnAutoToggle.TabIndex = 18;
            this.btnAutoToggle.Text = "시작";
            this.btnAutoToggle.UseVisualStyleBackColor = true;
            this.btnAutoToggle.Click += new System.EventHandler(this.btnAutoToggle_Click);
            // 
            // lblLogOptions
            // 
            this.lblLogOptions.AutoSize = true;
            this.lblLogOptions.Location = new System.Drawing.Point(12, 348);
            this.lblLogOptions.Name = "lblLogOptions";
            this.lblLogOptions.Size = new System.Drawing.Size(38, 15);
            this.lblLogOptions.TabIndex = 19;
            this.lblLogOptions.Text = "로그:";
            // 
            // chkLogTime
            // 
            this.chkLogTime.AutoSize = true;
            this.chkLogTime.Location = new System.Drawing.Point(118, 346);
            this.chkLogTime.Name = "chkLogTime";
            this.chkLogTime.Size = new System.Drawing.Size(50, 19);
            this.chkLogTime.TabIndex = 20;
            this.chkLogTime.Text = "시간";
            this.chkLogTime.UseVisualStyleBackColor = true;
            // 
            // chkLogMac
            // 
            this.chkLogMac.AutoSize = true;
            this.chkLogMac.Location = new System.Drawing.Point(188, 346);
            this.chkLogMac.Name = "chkLogMac";
            this.chkLogMac.Size = new System.Drawing.Size(55, 19);
            this.chkLogMac.TabIndex = 21;
            this.chkLogMac.Text = "MAC";
            this.chkLogMac.UseVisualStyleBackColor = true;
            // 
            // chkShowLog
            // 
            this.chkShowLog.AutoSize = true;
            this.chkShowLog.Location = new System.Drawing.Point(258, 346);
            this.chkShowLog.Name = "chkShowLog";
            this.chkShowLog.Size = new System.Drawing.Size(107, 19);
            this.chkShowLog.TabIndex = 22;
            this.chkShowLog.Text = "로그 상자 표시";
            this.chkShowLog.UseVisualStyleBackColor = true;
            this.chkShowLog.CheckedChanged += new System.EventHandler(this.chkShowLog_CheckedChanged);
            // 
            // btnClearLog
            // 
            this.btnClearLog.Location = new System.Drawing.Point(458, 276);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(90, 25);
            this.btnClearLog.TabIndex = 14;
            this.btnClearLog.Text = "로그 지우기";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatus.Location = new System.Drawing.Point(12, 376);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(536, 20);
            this.lblStatus.TabIndex = 24;
            this.lblStatus.Text = "상태: 준비";
            // 
            // txtIpLog
            // 
            this.txtIpLog.BackColor = System.Drawing.SystemColors.Window;
            this.txtIpLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtIpLog.Location = new System.Drawing.Point(12, 402);
            this.txtIpLog.Multiline = true;
            this.txtIpLog.Name = "txtIpLog";
            this.txtIpLog.ReadOnly = true;
            this.txtIpLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtIpLog.Size = new System.Drawing.Size(536, 110);
            this.txtIpLog.TabIndex = 25;
            this.txtIpLog.TabStop = false;
            this.txtIpLog.WordWrap = false;
            // 
            // ipTimer
            // 
            this.ipTimer.Interval = 2000;
            this.ipTimer.Tick += new System.EventHandler(this.ipTimer_Tick);
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
            this.ClientSize = new System.Drawing.Size(560, 524);
            this.Controls.Add(this.txtIpLog);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnClearLog);
            this.Controls.Add(this.chkShowLog);
            this.Controls.Add(this.chkLogMac);
            this.Controls.Add(this.chkLogTime);
            this.Controls.Add(this.lblLogOptions);
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
            this.Controls.Add(this.chkIpLog);
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
        private System.Windows.Forms.CheckBox chkIpLog;
        private System.Windows.Forms.Label lblAuto;
        private System.Windows.Forms.CheckBox chkAuto;
        private System.Windows.Forms.NumericUpDown nudAutoDelay;
        private System.Windows.Forms.Label lblAutoSuffix;
        private System.Windows.Forms.Label lblLogOptions;
        private System.Windows.Forms.CheckBox chkLogTime;
        private System.Windows.Forms.CheckBox chkLogMac;
        private System.Windows.Forms.CheckBox chkShowLog;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.TextBox txtIpLog;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Timer ipTimer;
        private System.ComponentModel.BackgroundWorker worker;
    }
}
