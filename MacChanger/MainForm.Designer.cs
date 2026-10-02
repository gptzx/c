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
            this.lblNew = new System.Windows.Forms.Label();
            this.txtNewMac = new System.Windows.Forms.TextBox();
            this.btnRandom = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.btnRestore = new System.Windows.Forms.Button();
            this.chkIpLog = new System.Windows.Forms.CheckBox();
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
            // lblNew
            // 
            this.lblNew.AutoSize = true;
            this.lblNew.Location = new System.Drawing.Point(12, 146);
            this.lblNew.Name = "lblNew";
            this.lblNew.Size = new System.Drawing.Size(55, 15);
            this.lblNew.TabIndex = 9;
            this.lblNew.Text = "새 MAC:";
            // 
            // txtNewMac
            // 
            this.txtNewMac.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtNewMac.Location = new System.Drawing.Point(118, 142);
            this.txtNewMac.MaxLength = 17;
            this.txtNewMac.Name = "txtNewMac";
            this.txtNewMac.Size = new System.Drawing.Size(190, 23);
            this.txtNewMac.TabIndex = 10;
            // 
            // btnRandom
            // 
            this.btnRandom.Location = new System.Drawing.Point(314, 141);
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
            this.btnApply.Location = new System.Drawing.Point(118, 178);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(110, 30);
            this.btnApply.TabIndex = 12;
            this.btnApply.Text = "변경 적용";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            // 
            // btnRestore
            // 
            this.btnRestore.Location = new System.Drawing.Point(236, 178);
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
            this.chkIpLog.Location = new System.Drawing.Point(362, 184);
            this.chkIpLog.Name = "chkIpLog";
            this.chkIpLog.Size = new System.Drawing.Size(129, 19);
            this.chkIpLog.TabIndex = 14;
            this.chkIpLog.Text = "할당 IP 로그 저장";
            this.chkIpLog.UseVisualStyleBackColor = true;
            this.chkIpLog.CheckedChanged += new System.EventHandler(this.chkIpLog_CheckedChanged);
            // 
            // lblLogOptions
            // 
            this.lblLogOptions.AutoSize = true;
            this.lblLogOptions.Location = new System.Drawing.Point(12, 218);
            this.lblLogOptions.Name = "lblLogOptions";
            this.lblLogOptions.Size = new System.Drawing.Size(62, 15);
            this.lblLogOptions.TabIndex = 15;
            this.lblLogOptions.Text = "로그 항목:";
            // 
            // chkLogTime
            // 
            this.chkLogTime.AutoSize = true;
            this.chkLogTime.Location = new System.Drawing.Point(118, 216);
            this.chkLogTime.Name = "chkLogTime";
            this.chkLogTime.Size = new System.Drawing.Size(50, 19);
            this.chkLogTime.TabIndex = 16;
            this.chkLogTime.Text = "시간";
            this.chkLogTime.UseVisualStyleBackColor = true;
            // 
            // chkLogMac
            // 
            this.chkLogMac.AutoSize = true;
            this.chkLogMac.Location = new System.Drawing.Point(188, 216);
            this.chkLogMac.Name = "chkLogMac";
            this.chkLogMac.Size = new System.Drawing.Size(55, 19);
            this.chkLogMac.TabIndex = 17;
            this.chkLogMac.Text = "MAC";
            this.chkLogMac.UseVisualStyleBackColor = true;
            // 
            // chkShowLog
            // 
            this.chkShowLog.AutoSize = true;
            this.chkShowLog.Location = new System.Drawing.Point(362, 216);
            this.chkShowLog.Name = "chkShowLog";
            this.chkShowLog.Size = new System.Drawing.Size(107, 19);
            this.chkShowLog.TabIndex = 18;
            this.chkShowLog.Text = "로그 상자 표시";
            this.chkShowLog.UseVisualStyleBackColor = true;
            this.chkShowLog.CheckedChanged += new System.EventHandler(this.chkShowLog_CheckedChanged);
            // 
            // btnClearLog
            // 
            this.btnClearLog.Location = new System.Drawing.Point(474, 212);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(74, 25);
            this.btnClearLog.TabIndex = 19;
            this.btnClearLog.Text = "로그 지우기";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatus.Location = new System.Drawing.Point(12, 246);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(536, 20);
            this.lblStatus.TabIndex = 20;
            this.lblStatus.Text = "상태: 준비";
            // 
            // txtIpLog
            // 
            this.txtIpLog.BackColor = System.Drawing.SystemColors.Window;
            this.txtIpLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtIpLog.Location = new System.Drawing.Point(12, 272);
            this.txtIpLog.Multiline = true;
            this.txtIpLog.Name = "txtIpLog";
            this.txtIpLog.ReadOnly = true;
            this.txtIpLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtIpLog.Size = new System.Drawing.Size(536, 110);
            this.txtIpLog.TabIndex = 21;
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
            this.ClientSize = new System.Drawing.Size(560, 394);
            this.Controls.Add(this.txtIpLog);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnClearLog);
            this.Controls.Add(this.chkShowLog);
            this.Controls.Add(this.chkLogMac);
            this.Controls.Add(this.chkLogTime);
            this.Controls.Add(this.lblLogOptions);
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
        private System.Windows.Forms.Label lblNew;
        private System.Windows.Forms.TextBox txtNewMac;
        private System.Windows.Forms.Button btnRandom;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.CheckBox chkIpLog;
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
