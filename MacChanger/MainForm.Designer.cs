namespace MacChanger
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components.Dispose();   // components 는 InitializeComponent 의 첫 줄에서 만들어지므로 null 이 아니다
            base.Dispose(disposing);
        }

        // 이 파일은 손으로 관리한다 (Visual Studio 디자이너용 아님): 컨트롤의 위치·크기는 아래 표(layout)에 네 값씩(x, y, 너비, 높이; 너비 0 = AutoSize) 적고
        // 한 루프가 적용한다. 표의 순서 = Controls 추가 순서 = 탭 순서 (Controls.Add 가 TabIndex 를 추가 순서대로 매긴다). 네 표(all, layout, 글자 text, 툴팁 tips)는 같은 순서.
        // 자동 변경 설정 줄(y=324)과 자동 정지 제한 줄(y=417)에는 두 방식의 컨트롤이 같은 자리에 놓이고, 스위치에 따라 한쪽만 보인다 (MainForm.ApplyMode).
        // 처음 Enabled 상태(시작 버튼·설정 칸·스위치·커서)는 생성자가 UpdateControlStates 로 한 번에 맞춘다.
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
            modeSwitch = new ModeSwitch("IP 할당 시", "일정 시간마다");
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
            chkAutoLimit = new System.Windows.Forms.CheckBox();
            lblLimitTime = new System.Windows.Forms.Label();
            limitSwitch = new ModeSwitch("시간", "횟수");
            lblLimitCount = new System.Windows.Forms.Label();
            nudLimitTime = new System.Windows.Forms.NumericUpDown();
            lblLimitTimeSuffix = new System.Windows.Forms.Label();
            nudLimitCount = new System.Windows.Forms.NumericUpDown();
            lblLimitCountSuffix = new System.Windows.Forms.Label();
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
            adapterTimer = new System.Windows.Forms.Timer(components);
            limitTimer = new System.Windows.Forms.Timer(components);
            worker = new System.ComponentModel.BackgroundWorker();
            notifyIcon = new System.Windows.Forms.NotifyIcon(components);
            SuspendLayout();
            System.Drawing.Font mono = new System.Drawing.Font("Consolas", 10F);
            System.Drawing.Font bold = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            //
            // 글자·동작
            //
            cboAdapters.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboAdapters.SelectedIndexChanged += new System.EventHandler(cboAdapters_SelectedIndexChanged);
            cboAdapters.DropDownClosed += new System.EventHandler(cboAdapters_DropDownClosed);
            btnRefresh.Click += new System.EventHandler(btnRefresh_Click);
            foreach (System.Windows.Forms.TextBox box in new System.Windows.Forms.TextBox[] { txtPermanentMac, txtCurrentMac, txtCurrentIp, txtMask, txtGateway, txtDns })
            {
                box.BackColor = System.Drawing.SystemColors.Window;
                box.Font = mono;
                box.ReadOnly = true;
                box.TabStop = false;
            }
            txtNewMac.Font = mono;
            txtNewMac.MaxLength = 17;
            btnRandom.Click += new System.EventHandler(btnRandom_Click);
            btnApply.Font = bold;
            btnApply.Click += new System.EventHandler(btnApply_Click);
            btnRenewIp.Click += new System.EventHandler(btnIp_Click);
            btnRestore.Click += new System.EventHandler(btnRestore_Click);
            btnClearLog.Click += new System.EventHandler(btnClearLog_Click);
            chkAuto.CheckedChanged += new System.EventHandler(chkAuto_CheckedChanged);
            System.EventHandler labelClick = new System.EventHandler(lblMode_Click);   // 스위치 양쪽 글자는 처리기 하나를 함께 쓴다
            lblModeIp.Click += labelClick;
            modeSwitch.CheckedChanged += new System.EventHandler(modeSwitch_CheckedChanged);
            lblModeTimer.Click += labelClick;
            btnAutoToggle.Click += new System.EventHandler(btnAutoToggle_Click);
            SetNumber(nudAutoDelay, 604800, 0, 10);   // 숫자 칸: 최대, 최소, 처음 값 (최소 0 은 원래 기본값이라 다시 넣어도 바뀌는 것이 없다)
            SetNumber(nudWaitTimeout, 60, 10, 30);
            SetNumber(nudInterval, 604800, 10, 30);
            SetNumber(nudLimitTime, 31536000, 60, 3600);
            SetNumber(nudLimitCount, 10000000, 1, 100);
            chkAutoStopIp.CheckedChanged += new System.EventHandler(chkAutoStopIp_CheckedChanged);
            txtAutoStopIp.Font = mono;
            txtAutoStopIp.MaxLength = 15;
            chkAutoLimit.CheckedChanged += new System.EventHandler(chkAutoLimit_CheckedChanged);
            labelClick = new System.EventHandler(lblLimit_Click);   // 제한 스위치의 양쪽 글자도 마찬가지
            lblLimitTime.Click += labelClick;
            limitSwitch.CheckedChanged += new System.EventHandler(limitSwitch_CheckedChanged);
            lblLimitCount.Click += labelClick;
            btnReleaseIp.Click += new System.EventHandler(btnIp_Click);
            chkLogBox.CheckedChanged += new System.EventHandler(chkLogBox_CheckedChanged);
            chkIpLog.CheckedChanged += new System.EventHandler(chkIpLog_CheckedChanged);
            btnBackground.Click += new System.EventHandler(btnBackground_Click);
            lblStatus.AutoEllipsis = true;
            lblStatus.Font = bold;
            System.Windows.Forms.TextBox logBox = txtIpLog;
            logBox.BackColor = System.Drawing.SystemColors.Window;
            logBox.Font = new System.Drawing.Font("Consolas", 9F);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            logBox.TabStop = false;
            logBox.WordWrap = false;
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
                lblDelayPrefix, nudAutoDelay, lblAutoSuffix, lblIntervalPrefix, nudInterval, lblIntervalSuffix,
                lblWait, lblWaitPrefix, nudWaitTimeout, lblWaitSuffix,
                lblAutoStop, chkAutoStopIp, txtAutoStopIp,
                chkAutoLimit, lblLimitTime, limitSwitch, lblLimitCount, nudLimitTime, lblLimitTimeSuffix, nudLimitCount, lblLimitCountSuffix,
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
                12, 297, 0, 0,   118, 295, 0, 0,   186, 297, 0, 0,   254, 294, 44, 20,   304, 297, 0, 0,   404, 292, 144, 25,
                118, 328, 0, 0,   210, 324, 72, 23,   288, 328, 0, 0,   118, 328, 0, 0,   210, 324, 72, 23,   288, 328, 0, 0,
                12, 359, 0, 0,   118, 359, 0, 0,   210, 355, 60, 23,   276, 359, 0, 0,
                12, 390, 0, 0,   118, 388, 0, 0,   292, 386, 130, 23,
                118, 419, 0, 0,   176, 421, 0, 0,   218, 418, 44, 20,   268, 421, 0, 0,   304, 417, 80, 23,   390, 421, 0, 0,   304, 417, 80, 23,   390, 421, 0, 0,
                12, 451, 0, 0,   118, 449, 0, 0,   404, 446, 144, 25,
                12, 481, 0, 0,   118, 479, 0, 0,   180, 479, 0, 0,   297, 479, 0, 0,   426, 479, 0, 0,
                118, 510, 0, 0,   258, 510, 0, 0,   440, 506, 108, 25,
                12, 539, 536, 20,   12, 566, 262, 167,   286, 566, 262, 167
            };
            // 글자: all 의 한 줄 = 아래 한 줄, 칸은 '|' 로 나눈다 (빈 칸 = 글자를 넣지 않는 컨트롤)
            string[] text = (
                "네트워크 어댑터:||새로 고침|" +
                "원래(공장) MAC:||현재 MAC:||현재 IP:||서브넷 마스크:||기본 게이트웨이:||DNS 서버:||" +
                "새 MAC:||랜덤 MAC 주소|DNS 초기화|" +
                "변경 적용|IP 갱신|원상복구|로그 비우기|" +
                "자동 변경:|사용|IP 할당 시||일정 시간마다|시작|" +
                "IP 할당 후||초 뒤 새 MAC 적용|IP와 관계없이||초마다 새 MAC 적용|" +
                "할당 대기:|IP 할당 대기가||초를 넘으면 새 MAC 적용|" +
                "자동 정지:|지정 IP/대역 할당 시 정지||" +
                "제한|시간||횟수||초 뒤 정지||번 바꾸면 정지|" +
                "IP 반환:|DHCP 서버에 IP 반환|즉시 반환|" +
                "로그:|시간|서브넷 마스크|기본 게이트웨이|MAC|" +
                "로그 보기|할당 IP 로그 저장|백그라운드 실행|" +
                "상태: 준비||"
            ).Split('|');
            // 툴팁: all 과 같은 순서로 한 컨트롤에 한 칸 (빈 칸 = 툴팁 없음, "^" = 바로 앞 컨트롤과 같은 툴팁 — 스위치와 양쪽 글자).
            // 일반 사용자 기준 — 무엇이 일어나는지 + 먼저 알아야 할 것 (줄당 60자 안팎, 최대 2줄; 자동 정지 IP 칸만 예시 때문에 3줄).
            string[] tips = (
                "|" +   // lblAdapter
                "MAC을 바꿀 어댑터를 고릅니다. [유선]·[무선] 등은 종류, 뒤는 현재 MAC입니다.|" +   // cboAdapters
                "어댑터 목록과 MAC·IP 정보를 다시 읽습니다. 연결은 끊기지 않습니다.\n목록은 어댑터를 꽂거나 뺄 때 자동으로 갱신되므로 평소에는 누를 일이 없습니다.|" +   // btnRefresh
                "|" +   // lblPermanent
                "어댑터에 처음부터 새겨진 원래 MAC입니다. 원상복구를 누르면 이 값으로 돌아갑니다.|" +   // txtPermanentMac
                "|" +   // lblCurrent
                "어댑터가 지금 실제로 쓰고 있는 MAC입니다.|" +   // txtCurrentMac
                "|" +   // lblIp
                "어댑터가 지금 받은 IP 주소입니다. 바뀌면 바로 갱신됩니다.\nIP가 없을 때는 그 이유를 대신 보여 줍니다.|" +   // txtCurrentIp
                "|" +   // lblMask
                "같은 네트워크로 묶이는 IP 범위를 정하는 값입니다.|" +   // txtMask
                "|" +   // lblGateway
                "인터넷으로 나가는 출입구 장비(보통 공유기)의 IP입니다.|" +   // txtGateway
                "|" +   // lblDns
                "사이트 이름을 숫자 IP로 바꿔 주는 서버입니다.|" +   // txtDns
                "|" +   // lblNew
                "변경 적용으로 바꿀 MAC — 0~9, A~F 12자리. -, :, . 기호는 있어도 없어도 됩니다.\n예: 02-1A-2B-3C-4D-5E|" +   // txtNewMac
                "어댑터에 맞는 랜덤 MAC을 만들어 새 MAC 칸에 채웁니다. 적용은 변경 적용을 눌러야 합니다.\n무선 어댑터는 무선 드라이버가 받아 주는 형식으로 만듭니다.|" +   // btnRandom
                "변경 적용·IP 갱신·원상복구·자동 변경 때 직접 입력해 둔 DNS 서버도 함께 지워\nDNS도 IP처럼 자동으로 받게 합니다. IP를 자동으로 받는 어댑터에만 해당합니다.|" +   // chkResetDns
                "새 MAC을 확인 창 없이 바로 적용합니다. 어댑터가 재시작되어 인터넷이 몇 초 끊깁니다.\n끝나면 실제로 바뀌었는지 확인해 상태 줄에 알려 줍니다.|" +   // btnApply
                "MAC은 그대로 두고 어댑터를 재시작해 IP를 새로 받습니다. 재시작 중 인터넷이 몇 초 끊깁니다.\n즉시 반환 뒤 다시 연결할 때도 이 버튼을 누릅니다.|" +   // btnRenewIp
                "원래 MAC으로 되돌립니다. 예/아니요 확인 창을 거친 뒤 어댑터가 재시작되어 인터넷이 몇 초 끊깁니다.|" +   // btnRestore
                "로그 박스의 내용만 지웁니다. 파일에 저장된 로그는 그대로 남습니다.|" +   // btnClearLog
                "|" +   // lblAuto
                "자동 변경 기능을 쓸지 정합니다. 체크해야 시작 버튼과 스위치, 설정 칸이 열립니다.\n실제 동작은 시작을 눌러야 하며, 새 MAC은 매번 랜덤으로 만듭니다.|" +   // chkAuto
                "자동 변경을 언제 할지 고릅니다. 왼쪽 IP 할당 시: IP를 받을 때마다 정한 초 뒤에 바꿉니다.\n오른쪽 일정 시간마다: IP와 관계없이 정한 초마다 바꿉니다. 양쪽 글자를 눌러도 바뀝니다.|" +   // lblModeIp
                "^|" +   // modeSwitch
                "^|" +   // lblModeTimer
                "자동 변경을 시작/정지합니다. 실행 중에는 정지·로그 비우기·백그라운드 실행만 쓸 수 있습니다.\n남은 초나 IP 대기 초가 버튼에 보이며, 변경 실패·재부팅 필요·지정 IP·정한 시간/횟수에 스스로 멈춥니다.|" +   // btnAutoToggle
                "|" +   // lblDelayPrefix
                "IP를 받고 이만큼 기다렸다가 새 MAC을 적용합니다.\n0이면 받는 즉시 적용하며, 최대 7일까지 정할 수 있습니다.|" +   // nudAutoDelay
                "|" +   // lblAutoSuffix
                "|" +   // lblIntervalPrefix
                "IP가 있든 없든 이 간격마다 새 MAC을 적용합니다. 10초~7일, 1시간은 3600, 하루는 86400.\n너무 짧으면 IP를 받기 전에 또 바뀝니다.|" +   // nudInterval
                "|" +   // lblIntervalSuffix
                "|" +   // lblWait
                "|" +   // lblWaitPrefix
                "연결은 됐는데 이만큼 기다려도 IP를 못 받으면 새 MAC을 적용합니다.\nIP를 받을 때까지 같은 간격으로 되풀이합니다. 10~60초|" +   // nudWaitTimeout
                "|" +   // lblWaitSuffix
                "|" +   // lblAutoStop
                "오른쪽 칸의 IP나 대역에 속하는 IP를 받는 순간 자동 변경이 멈춥니다.\n시작할 때와 매번 바꾸기 직전에도 확인합니다.|" +   // chkAutoStopIp
                "자동 변경을 멈출 IP나 대역. 앞부분만 적으면 그 숫자로 시작하는 IP 전부가 해당합니다.\n예: 123 → 123.*.*.*   123.123 → 123.123.*.*\n123.123.123 → 123.123.123.*   숫자 네 개를 다 적으면 그 주소 하나만 해당합니다.|" +   // txtAutoStopIp
                "체크하면 정한 시간이 지나거나 정한 횟수만큼 바꾼 뒤 자동 변경이 스스로 멈춥니다.\n시간과 횟수 가운데 하나를 오른쪽 스위치로 고릅니다.|" +   // chkAutoLimit
                "자동 변경을 언제 멈출지 고릅니다. 왼쪽 시간: 시작하고 정한 초가 지나면 멈춥니다.\n오른쪽 횟수: 새 MAC을 정한 횟수만큼 적용하면 멈춥니다. 양쪽 글자를 눌러도 바뀝니다.|" +   // lblLimitTime
                "^|" +   // limitSwitch
                "^|" +   // lblLimitCount
                "시작하고 이 시간(초)이 지나면 자동 변경을 멈춥니다. 1분(60)~1년(31536000).\n1시간은 3600, 하루는 86400입니다. 바꾸는 중이면 그 변경을 마친 뒤 멈춥니다.|" +   // nudLimitTime
                "|" +   // lblLimitTimeSuffix
                "새 MAC을 이만큼 적용하면 자동 변경을 멈춥니다. 1~10000000번.\n실패한 변경은 세지 않으며, 변경이 실패하면 그 자리에서 멈춥니다.|" +   // nudLimitCount
                "|" +   // lblLimitCountSuffix
                "|" +   // lblRelease
                "변경 적용·IP 갱신·원상복구·자동 변경 때마다 쓰던 IP를 먼저 DHCP 서버(공유기 등)에 돌려줍니다.\n서버가 다른 IP를 줄 수도, 같은 IP를 다시 줄 수도 있습니다.|" +   // chkReleaseIp
                "누르는 즉시 IP를 반환해 인터넷 연결이 끊어집니다.\n다시 연결하려면 IP 갱신 버튼을 누르면 됩니다.|" +   // btnReleaseIp
                "|" +   // lblLogOptions
                "로그 한 줄마다 기록한 날짜와 시각도 함께 적습니다.|" +   // chkLogTime
                "로그 한 줄마다 그때의 서브넷 마스크도 함께 적습니다.|" +   // chkLogMask
                "로그 한 줄마다 그때의 기본 게이트웨이도 함께 적습니다.|" +   // chkLogGateway
                "로그 한 줄마다 그때 쓰던 MAC도 함께 적습니다.|" +   // chkLogMac
                "체크한 뒤부터 새로 받는 IP를 로그 박스에 한 줄씩 보여 줍니다. 끄면 박스가 흐려지고 기록하지 않습니다.\n파일로도 남기려면 할당 IP 로그 저장을 켜세요. 로그 비우기는 꺼져 있어도 쓸 수 있습니다.|" +   // chkLogBox
                "체크한 뒤부터 새로 받는 IP를 프로그램 폴더의 " + Core.IpMonitor.LogFileName + " 파일에도 한 줄씩 저장합니다.\n로그 보기가 꺼져 있어도 저장되며, 파일은 프로그램을 닫아도 남습니다.|" +   // chkIpLog
                "창을 숨기고 시계 옆 트레이 아이콘으로 계속 실행합니다. 자동 변경·그래프·로그는 그대로 돕니다.\n아이콘을 한 번 누르면 창이 다시 열리고, 마우스 오른쪽 버튼을 누르면 열기/종료 메뉴가 나옵니다.|" +   // btnBackground
                "|" +   // lblStatus
                "|" +   // txtIpLog
                "통신 속도 그래프입니다. 빨강은 받는(수신) 속도, 초록은 보내는(송신) 속도입니다.\n아래는 누적량과 현재 속도입니다. 누적량은 변경 적용·IP 갱신·원상복구 뒤 0부터 다시 셉니다."   // trafficGraph
            ).Split('|');
            string tip = null;
            for (int i = 0; i < all.Length; i++)
            {
                System.Windows.Forms.Control c = all[i];
                int j = i * 4;
                if (text[i].Length > 0) c.Text = text[i];
                c.Location = new System.Drawing.Point(layout[j], layout[j + 1]);
                if (layout[j + 2] > 0) c.Size = new System.Drawing.Size(layout[j + 2], layout[j + 3]);
                else c.AutoSize = true;
                Controls.Add(c);
                if (tips[i] != "^") tip = tips[i];
                if (tip.Length > 0) toolTip.SetToolTip(c, tip);
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
            adapterTimer.Interval = 500;
            adapterTimer.Tick += new System.EventHandler(adapterTimer_Tick);
            limitTimer.Tick += new System.EventHandler(limitTimer_Tick);
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

        /// <summary>숫자 칸의 범위와 처음 값을 정하고 오른쪽 정렬한다. BeginInit/EndInit 를 쓰지 않으므로 Maximum → Minimum → Value 순서를 지켜야 한다 (Value 는 그때의 범위 안이어야 함)</summary>
        private static void SetNumber(System.Windows.Forms.NumericUpDown nud, int maximum, int minimum, int value)
        {
            nud.Maximum = new decimal(maximum);
            nud.Minimum = new decimal(minimum);
            nud.Value = new decimal(value);
            nud.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
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
        private System.Windows.Forms.CheckBox chkAutoLimit;
        private System.Windows.Forms.Label lblLimitTime;
        private ModeSwitch limitSwitch;
        private System.Windows.Forms.Label lblLimitCount;
        private System.Windows.Forms.NumericUpDown nudLimitTime;
        private System.Windows.Forms.Label lblLimitTimeSuffix;
        private System.Windows.Forms.NumericUpDown nudLimitCount;
        private System.Windows.Forms.Label lblLimitCountSuffix;
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
        private System.Windows.Forms.Timer adapterTimer;
        private System.Windows.Forms.Timer limitTimer;
        private System.ComponentModel.BackgroundWorker worker;
        private System.Windows.Forms.NotifyIcon notifyIcon;
    }
}
