using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MacChanger.Core;
using MacChanger.Native;

namespace MacChanger
{
    public partial class MainForm : Form
    {
        private enum OperationKind
        {
            Apply,
            Restore,
            RenewIp
        }

        private sealed class OperationArgs
        {
            public OperationKind Kind;
            public NetworkAdapterInfo Adapter;
            public string NewMac;
            /// <summary>Tcpip 값 정리 때 NameServer 에 값이 있어도 지울지 (DNS 초기화 옵션, UI 스레드에서 작업 시작 시 읽어 둔다)</summary>
            public bool ResetDns;
            /// <summary>작업을 시작한 이유 (예: "IP 할당 대기 30초 초과"). 시작·완료 상태 앞에 붙인다. 직접 누른 작업은 null.</summary>
            public string Reason;
        }

        private bool busy;
        /// <summary>드롭다운 항목 텍스트를 갱신(Items[i] = item)하거나 목록을 다시 채울 때 SelectedIndexChanged를 막는다.</summary>
        private bool suppressSelectionChanged;
        /// <summary>마지막으로 정보를 읽은 어댑터 GUID (실제로 다른 어댑터를 선택했을 때만 IP 로그 기준을 초기화)</summary>
        private string lastInfoGuid;
        /// <summary>선택한 어댑터에 대해 마지막으로 로그에 남긴 "실제 할당 IP"(169.254 제외, 정렬). 어댑터가 내려가거나 재시작되면 초기화한다.</summary>
        private string lastLoggedIp;
        /// <summary>lastLoggedIp 와 함께 읽은 서브넷 마스크·기본 게이트웨이 (로그 상자를 켜거나 파일 저장을 켤 때 같은 줄을 다시 만들기 위해)</summary>
        private string lastLoggedMask, lastLoggedGateway;
        /// <summary>IP 구성 변화 알림 (Windows 가 알려 줄 때만 다시 읽는다; 못 쓰는 OS 에서는 ipTimer 가 1초 폴링으로 대신)</summary>
        private IpChangeNotifier ipNotifier;
        /// <summary>알림이 몰려와도 UI 스레드에는 한 번만 넘기기 위한 플래그 (0: 대기 중 없음, 1: 넘겨 둠)</summary>
        private int refreshQueued;
        /// <summary>선택한 어댑터의 송수신 카운터 (1초마다 읽어 그래프에 넣는다)</summary>
        private readonly TrafficMonitor traffic = new TrafficMonitor();
        /// <summary>직전 RefreshIp 에서 발생한 로그 저장 오류 (없으면 null)</summary>
        private string ipLogError;
        /// <summary>로그 상자에 마지막으로 넣은 "실제 할당 IP" — 상자를 다시 켤 때 같은 줄을 반복하지 않기 위한 기준</summary>
        private string lastBoxIp;
        /// <summary>자동 변경이 "시작" 상태인지 (체크박스는 기능 사용 여부, 버튼이 실제 시작/정지)</summary>
        private bool autoRunning;
        /// <summary>자동 변경 예약 여부와 예약 시각(Environment.TickCount 기준 — 시스템 시계 변경에 영향받지 않음)</summary>
        private bool autoPending;
        private int autoDueTick;
        /// <summary>"IP 할당 대기 중"(링크는 올라왔지만 실제 IP 없음) 상태가 시작된 시각 — 할당 대기 초과 시 새 MAC 적용에 사용</summary>
        private bool waiting;
        private int waitingSinceTick;
        /// <summary>진행 중인 작업의 이유(OperationArgs.Reason) — 완료 상태에도 붙인다</summary>
        private string operationReason;
        /// <summary>지정 IP/대역 할당으로 자동 변경을 정지했을 때의 안내문 — 작업 완료 상태에 그대로 붙인다 (어느 IP가 맞았는지 남기기 위해)</summary>
        private string autoStopMessage;

        /// <summary>알림을 쓸 때의 안전망 폴링 주기 / 알림 직후 한 번 더 읽는 간격(게이트웨이·DNS 는 주소보다 조금 늦게 기록된다) / 알림을 못 쓰는 OS 에서의 폴링 주기 (ms)</summary>
        private const int IpSafetyPollMs = 3000, IpSettleMs = 1000, IpFallbackPollMs = 1000;

        public MainForm()
        {
            InitializeComponent();
            ApplyLogBoxEnabled();   // 기본값: 로그 상자 꺼짐(흐리게 표시, 기록 안 함)
            try
            {
                // 실행 파일에 내장된 아이콘(그룹 아이콘 ID 32512)을 EXE 리소스에서 직접 읽어 창/작업표시줄 아이콘으로 쓴다.
                // (Icon.ExtractAssociatedIcon 은 UNC 경로에서 예외를 던지므로 폴백으로만 사용)
                IntPtr handle = NativeMethods.LoadIcon(NativeMethods.GetModuleHandle(null), new IntPtr(32512));
                Icon = handle != IntPtr.Zero ? Icon.FromHandle(handle) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception) { }
            notifyIcon.Icon = Icon;   // 백그라운드 실행 중 트레이에 같은 아이콘을 보여 준다
            // 툴팁: 컨트롤마다 핵심 동작 한 줄 (+ 필요한 예시 한 줄). 세부 규칙은 README
            toolTip.SetToolTip(cboAdapters, "MAC을 바꿀 네트워크 어댑터");
            toolTip.SetToolTip(btnRefresh, "어댑터 목록과 현재 정보를 다시 읽습니다.");
            toolTip.SetToolTip(txtNewMac, "적용할 MAC 주소 — 16진수 12자리 (-, :, . 구분자는 있어도 없어도 됨)\n예: 02-1A-2B-3C-4D-5E");
            toolTip.SetToolTip(btnRandom, "어댑터 종류(유선/무선)에 맞는 랜덤 MAC을 새 MAC 칸에 채웁니다.");
            toolTip.SetToolTip(chkResetDns, "변경 적용·원상복구·IP 갱신 때 직접 지정한 DNS도 함께 지웁니다. (DHCP 어댑터만)");
            toolTip.SetToolTip(btnApply, "새 MAC을 바로 적용합니다. (어댑터 재시작, 확인 창 없음)");
            toolTip.SetToolTip(btnRenewIp, "MAC은 그대로 두고 IP만 새로 받습니다. (어댑터 재시작, 확인 창 없음)");
            toolTip.SetToolTip(btnRestore, "원래(공장) MAC으로 되돌립니다. (어댑터 재시작, 확인 창 없음)");
            toolTip.SetToolTip(btnClearLog, "로그 상자를 비웁니다. (로그 파일은 그대로)");
            toolTip.SetToolTip(chkAuto, "자동 변경 사용 — '시작'을 누르면 IP를 받을 때마다 지정한 초 뒤에 랜덤 MAC을 적용합니다.");
            toolTip.SetToolTip(nudAutoDelay, "IP를 받은 뒤 MAC을 바꾸기까지 기다리는 초 (0 = 즉시, 최대 7일)");
            toolTip.SetToolTip(btnAutoToggle, "자동 변경 시작 / 정지\n변경 실패·재부팅 필요·지정 IP 할당 시 스스로 멈춥니다.");
            toolTip.SetToolTip(nudWaitTimeout, "자동 변경 중 IP를 못 받은 채 새 MAC을 적용하기까지 기다리는 초 (10~60)");
            toolTip.SetToolTip(chkAutoStopIp, "체크하면 오른쪽 칸의 IP(또는 그 대역의 IP)를 받는 순간 자동 변경을 멈춥니다.");
            toolTip.SetToolTip(txtAutoStopIp, "멈출 IP 또는 대역\n123 → 123.*.*.*   123.123 → 123.123.*.*   123.123.123 → 123.123.123.*");
            toolTip.SetToolTip(chkLogTime, "로그에 시각을 함께 기록합니다.");
            toolTip.SetToolTip(chkLogMask, "로그에 서브넷 마스크를 함께 기록합니다.");
            toolTip.SetToolTip(chkLogGateway, "로그에 기본 게이트웨이를 함께 기록합니다.");
            toolTip.SetToolTip(chkLogMac, "로그에 그 시점의 MAC을 함께 기록합니다.");
            toolTip.SetToolTip(chkLogBox, "할당 IP 로그 상자를 켜거나 끕니다.\n끄면 흐리게 보이고 기록하지 않습니다. (로그 비우기는 언제나 가능)");
            toolTip.SetToolTip(chkIpLog, "새 IP를 받을 때마다 실행 파일 옆 " + IpMonitor.LogFileName + " 에도 기록합니다.");
            toolTip.SetToolTip(btnBackground, "창을 숨기고 트레이 아이콘으로 계속 실행합니다.\n트레이 아이콘을 두 번 클릭하면 창이 다시 열립니다.");
            toolTip.SetToolTip(trafficGraph, "송수신 속도 그래프 (빨강: 수신, 초록: 송신)");
        }

        private NetworkAdapterInfo SelectedAdapter
        {
            get { return cboAdapters.SelectedItem as NetworkAdapterInfo; }
        }

        // ------------------------------------------------------------------
        // 폼 이벤트
        // ------------------------------------------------------------------
        private void MainForm_Load(object sender, EventArgs e)
        {
            SetStatus("진행", "어댑터 목록을 읽는 중...");
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // 창이 먼저 그려진 뒤에 (WMI 조회가 몇 초 걸릴 수 있으므로) 목록을 읽는다.
            BeginInvoke(new MethodInvoker(delegate { LoadAdapters(null); StartIpMonitoring(); secondTimer.Start(); }));
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing)
            {
                RestoreFromTray();
                MessageBox.Show(this, "작업이 진행 중입니다. 완료될 때까지 기다려 주세요.", Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
            }
        }

        /// <summary>창이 실제로 닫힌 뒤(종료가 취소될 수 있는 FormClosing 이 아니라) 알림을 해제한다 — UI 스레드, 콜백 밖.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (ipNotifier != null)
            {
                ipNotifier.Dispose();
                ipNotifier = null;
            }
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------
        // IP 변화 감지: Windows 알림(즉시) + 안전망 폴링
        // ------------------------------------------------------------------
        /// <summary>
        /// 알림이 등록되면 변화가 있을 때만 즉시 다시 읽고(1초 뒤 한 번 더 — 늦게 기록되는 게이트웨이·DNS 반영), ipTimer 는 놓친 변화를 메우는 안전망(3초)으로 돈다.
        /// 알림을 못 쓰는 OS 면 1초 폴링.
        /// </summary>
        private void StartIpMonitoring()
        {
            ipNotifier = new IpChangeNotifier();
            if (ipNotifier.Available)
            {
                ipNotifier.Changed += ipNotifier_Changed;
                ipTimer.Interval = IpSafetyPollMs;
            }
            else
            {
                string why = ipNotifier.Error;
                ipNotifier.Dispose();
                ipNotifier = null;
                ipTimer.Interval = IpFallbackPollMs;
                if (!busy) SetStatus("준비", "IP 변경 알림을 쓸 수 없어 1초마다 확인합니다. (" + why + ")");
            }
            ipTimer.Start();
        }

        /// <summary>시스템 스레드에서 온 알림 — UI 스레드에 한 번만 넘긴다 (몰려오는 알림은 한 번의 갱신으로 합친다).</summary>
        private void ipNotifier_Changed(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new MethodInvoker(RefreshIpFromNotification));
                else Interlocked.Exchange(ref refreshQueued, 0);
            }
            catch (Exception)   // 창이 닫히는 중
            {
                Interlocked.Exchange(ref refreshQueued, 0);
            }
        }

        private void RefreshIpFromNotification()
        {
            Interlocked.Exchange(ref refreshQueued, 0);
            if (IsDisposed) return;
            RefreshIp();
            if (ipLogError != null) SetStatus("실패", ipLogError);
            ipTimer.Stop();                 // 1초 뒤 한 번 더 읽고(settle), 그 다음부터 안전망 주기로 돌아간다
            ipTimer.Interval = IpSettleMs;
            ipTimer.Start();
        }

        // ------------------------------------------------------------------
        // 백그라운드 실행 (트레이)
        // ------------------------------------------------------------------
        private void btnBackground_Click(object sender, EventArgs e)
        {
            notifyIcon.Visible = true;
            Hide();   // 타이머·자동 변경·그래프는 창이 숨겨져도 그대로 돈다
            notifyIcon.ShowBalloonTip(3000, Program.AppTitle, "백그라운드에서 계속 실행 중입니다. 트레이 아이콘을 두 번 클릭하면 창이 다시 열립니다.", ToolTipIcon.Info);
        }

        private void trayOpen_Click(object sender, EventArgs e)
        {
            RestoreFromTray();
        }

        private void trayExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>트레이로 숨겨져 있으면 창을 다시 보이고 앞으로 가져온다 (대화 상자를 띄우기 전에도 호출).</summary>
        private void RestoreFromTray()
        {
            if (Visible) return;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            notifyIcon.Visible = false;
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo current = SelectedAdapter;
            LoadAdapters(current != null ? current.InterfaceGuid : null);
        }

        private void cboAdapters_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressSelectionChanged) return;
            RefreshSelectedAdapterInfo();
        }

        private void btnRandom_Click(object sender, EventArgs e)
        {
            try
            {
                NetworkAdapterInfo adapter = SelectedAdapter;
                bool wireless = adapter != null && adapter.Kind == AdapterKind.Wireless;
                txtNewMac.Text = MacAddressUtil.Format(MacAddressUtil.GenerateRandom(wireless));
                SetStatus("준비", "랜덤 MAC 생성 (" + (wireless ? "무선" : "유선") + "용): " + txtNewMac.Text);
            }
            catch (Exception ex)
            {
                SetStatus("실패", "랜덤 생성 오류: " + ex.Message);
            }
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
            {
                SetStatus("실패", "어댑터를 선택하세요.");
                return;
            }
            string mac = MacAddressUtil.Normalize(txtNewMac.Text);
            if (mac == null)
            {
                SetStatus("실패", "새 MAC은 16진수 12자리여야 합니다. (예: 02-1A-2B-3C-4D-5E)");
                txtNewMac.Focus();
                return;
            }
            if (!MacAddressUtil.IsUnicast(mac))
            {
                SetStatus("실패", "두 번째 자리는 짝수여야 합니다. (홀수는 멀티캐스트 주소)");
                txtNewMac.Focus();
                return;
            }
            if (MacAddressUtil.IsAllZero(mac) || MacAddressUtil.IsAllFF(mac))
            {
                SetStatus("실패", "00-00-00-00-00-00과 FF-FF-FF-FF-FF-FF는 사용할 수 없습니다.");
                txtNewMac.Focus();
                return;
            }
            string currentNormalized = MacAddressUtil.Normalize(txtCurrentMac.Text);
            if (currentNormalized != null && string.Equals(currentNormalized, mac, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("준비", "새 MAC이 현재 MAC과 같습니다. 변경할 내용이 없습니다.");
                return;
            }

            // 확인 대화 상자 없이 바로 적용한다.
            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.Apply;
            args.Adapter = adapter;
            args.NewMac = mac;
            StartOperation(args);
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
            {
                SetStatus("실패", "어댑터를 선택하세요.");
                return;
            }
            // 이미 공장 MAC이고 레지스트리에 NetworkAddress 값도 없으면 복구할 것이 없다 (변경 적용의 "같은 MAC" 안내와 같은 방식).
            string currentNormalized = MacAddressUtil.Normalize(txtCurrentMac.Text);
            string permanentNormalized = MacAddressUtil.Normalize(txtPermanentMac.Text);
            if (currentNormalized != null && permanentNormalized != null
                && string.Equals(currentNormalized, permanentNormalized, StringComparison.OrdinalIgnoreCase) && !HasNetworkAddressOverride(adapter))
            {
                SetStatus("준비", "현재 MAC이 이미 원래(공장) MAC과 같습니다. 복구할 내용이 없습니다.");
                return;
            }
            // 확인 대화 상자 없이 바로 복구한다 (자동 변경 실행 중에는 이 버튼이 잠겨 있다).
            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.Restore;
            args.Adapter = adapter;
            StartOperation(args);
        }

        /// <summary>레지스트리에 NetworkAddress 값이 남아 있는지. 읽지 못하면(키 없음·권한) 남아 있다고 보고 복구를 진행하게 한다.</summary>
        private static bool HasNetworkAddressOverride(NetworkAdapterInfo adapter)
        {
            try
            {
                return MacRegistry.HasNetworkAddress(adapter.InterfaceGuid);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private void btnRenewIp_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
            {
                SetStatus("실패", "어댑터를 선택하세요.");
                return;
            }
            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.RenewIp;
            args.Adapter = adapter;
            StartOperation(args);
        }

        private void chkIpLog_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkIpLog.Checked)
            {
                SetStatus("준비", "할당 IP 로그 파일 저장을 껐습니다.");
                return;
            }
            // 켜는 순간 현재 할당된 IP를 기준 줄로 파일에만 한 번 기록한다 (상자는 표시 중일 때만 RefreshIp 가 채운다).
            NetworkAdapterInfo adapter = SelectedAdapter;
            string error = adapter != null && lastLoggedIp != null ? AppendToFile(BuildLastLogLine(adapter)) : null;
            if (error != null) SetStatus("실패", error);
            else SetStatus("준비", "할당 IP 로그를 실행 파일 옆 " + IpMonitor.LogFileName + " 에 저장합니다.");
        }

        private void chkLogBox_CheckedChanged(object sender, EventArgs e)
        {
            ApplyLogBoxEnabled();
        }

        private void chkAuto_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkAuto.Checked && autoRunning) StopAuto();
            UpdateControlStates();   // 시작 버튼, 지연 초, 할당 대기 초는 체크해야 열린다
        }

        private void chkAutoStopIp_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkAutoStopIp.Checked) txtAutoStopIp.Text = string.Empty;   // 체크를 풀면 입력한 IP/대역은 지운다
            UpdateControlStates();   // 지정 IP 칸은 체크해야 열린다
        }

        private void btnAutoToggle_Click(object sender, EventArgs e)
        {
            if (autoRunning) StopAuto();
            else StartAuto();
        }

        private void StartAuto()
        {
            if (chkAutoStopIp.Checked && ParseStopIp() == null)
            {
                SetStatus("실패", "자동 정지 IP/대역 형식 오류 — 예: 123, 123.123, 123.123.123, 123.123.123.123");
                txtAutoStopIp.Focus();
                return;
            }
            waiting = false;   // 할당 대기 시간은 시작 시점부터 센다
            autoStopMessage = null;
            autoRunning = true;
            UpdateControlStates();   // 실행 중에는 정지·로그 비우기·백그라운드 실행 외의 조작을 잠근다
            btnAutoToggle.Text = "정지 (IP 대기)";
            if (!busy) SetStatus("준비", "자동 변경 시작 — IP 할당 " + nudAutoDelay.Value + "초 뒤, 또는 할당 대기 " + nudWaitTimeout.Value + "초 초과 시 새 MAC 적용");
            RefreshIp();   // 지금 상태를 바로 읽어 둔다 — IP 없이 링크만 올라와 있으면 할당 대기 시계가 이 순간부터 센다 (알림만 기다리면 최대 3초 늦는다)
            if (AutoStopIfTargetAssigned(lastLoggedIp)) return;   // 지정 IP가 이미 할당되어 있으면 바꾸지 않고 바로 정지
            ScheduleAuto();   // 이미 IP가 할당되어 있으면 지금부터 센다
            RunAutoIfDue();   // 바로 남은 시간을 보여주고, 0초면 즉시 시작
        }

        private void StopAuto()
        {
            autoRunning = false;
            CancelAuto();
            UpdateControlStates();
            btnAutoToggle.Text = "시작";
            if (!busy) SetStatus("준비", "자동 변경 정지");
        }

        /// <summary>자동 변경 중이고 어댑터에 IP가 할당되어 있으면 지연 시간 뒤로 변경을 예약하고 남은 시간을 바로 버튼에 보여준다 (0초면 다음 1초 확인 때 바로).</summary>
        private void ScheduleAuto()
        {
            if (!autoRunning || busy || lastLoggedIp == null) return;
            int delay = (int)nudAutoDelay.Value;
            autoPending = true;
            autoDueTick = unchecked(Environment.TickCount + delay * 1000);
            if (delay > 0)
            {
                // 1초 타이머의 위상을 예약 시각에 맞춘다: 다음 틱이 정확히 1초 뒤에 오므로 숫자마다 1초씩 표시되고 변경은 정확히 N초 뒤에 시작된다
                secondTimer.Stop();
                secondTimer.Start();
                btnAutoToggle.Text = "정지 (" + FormatRemaining(delay) + ")";
            }
            else btnAutoToggle.Text = "정지";
        }

        private void CancelAuto()
        {
            autoPending = false;
            if (autoRunning) btnAutoToggle.Text = "정지 (IP 대기)";
        }

        /// <summary>예약 시각이 지났으면 어댑터 종류에 맞는 랜덤 MAC을 만들어 바로 적용한다. 아직이면 남은 시간을 정지 버튼에 보여준다.</summary>
        private void RunAutoIfDue()
        {
            if (!autoPending || !autoRunning || busy) return;
            int remainingMs = unchecked(autoDueTick - Environment.TickCount);   // TickCount 가 한 바퀴 돌아도 차이는 올바르다
            if (remainingMs > 0)
            {
                btnAutoToggle.Text = "정지 (" + FormatRemaining((remainingMs + 999) / 1000) + ")";
                return;
            }
            if (AutoStopIfTargetAssigned(lastLoggedIp)) return;   // 변경 직전 마지막 확인: 지정 IP/대역이 할당되어 있으면 바꾸지 않는다
            autoPending = false;
            StartAutoChange(null);
        }

        private void StartWaitTimeoutChange()
        {
            StartAutoChange("IP 할당 대기 " + nudWaitTimeout.Value + "초 초과");
        }

        /// <summary>어댑터 종류에 맞는 랜덤 MAC을 만들어 바로 적용한다 (예약 시각 도달, 할당 대기 초과 두 경우 공통). reason 은 상태 표시에 붙일 이유(없으면 null).</summary>
        private void StartAutoChange(string reason)
        {
            btnAutoToggle.Text = "정지";
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null) return;
            string mac;
            try
            {
                mac = MacAddressUtil.GenerateRandom(adapter.Kind == AdapterKind.Wireless);
            }
            catch (Exception ex)
            {
                StopAuto();
                SetStatus("실패", "자동 변경 중단 — 랜덤 생성 오류: " + ex.Message);
                return;
            }
            txtNewMac.Text = MacAddressUtil.Format(mac);
            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.Apply;
            args.Adapter = adapter;
            args.NewMac = mac;
            args.Reason = reason;
            StartOperation(args);
        }

        /// <summary>
        /// 할당 대기 상태를 추적한다. 대기 중이면 시작 시각을 기억하고(자동 변경 중이면 버튼에 경과 초 표시),
        /// 자동 변경 중에 설정한 할당 대기 초를 넘겼으면 true 를 돌려준다 (호출자가 새 MAC 적용). 작업 중에는 세지 않는다.
        /// </summary>
        private bool TrackWaiting(bool isWaiting, int nowTick)
        {
            if (!isWaiting || busy)
            {
                waiting = false;
                return false;
            }
            if (!waiting)
            {
                waiting = true;
                waitingSinceTick = nowTick;
            }
            if (!autoRunning) return false;
            int elapsedMs = unchecked(nowTick - waitingSinceTick);
            btnAutoToggle.Text = "정지 (IP 대기 " + elapsedMs / 1000 + "초)";
            if (elapsedMs < (int)nudWaitTimeout.Value * 1000) return false;
            waiting = false;   // 변경 뒤 어댑터가 다시 올라오면 새로 센다
            return true;
        }

        /// <summary>
        /// 자동 정지 입력값을 10진수 IPv4 앞부분(옥텟 1~4개, 각 0~255)으로 읽어 표준 표기(앞자리 0 제거)로 돌려준다. 비었거나 형식이 틀리면 null.
        /// "123" 은 123.0.0.0/8, "123.123" 은 /16, "123.123.123" 은 /24, 네 자리 전부면 그 주소 하나. IPAddress.TryParse 는 앞자리 0을 8진수로 읽으므로 쓰지 않는다.
        /// </summary>
        private string ParseStopIp()
        {
            string text = txtAutoStopIp.Text.Trim();
            if (text.Length == 0) return null;
            string[] parts = text.Split('.');
            if (parts.Length > 4) return null;
            string[] octets = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part.Length > 3) return null;
                int value = 0;
                foreach (char c in part)
                {
                    if (c < '0' || c > '9') return null;
                    value = value * 10 + (c - '0');
                }
                if (value > 255) return null;
                octets[i] = value.ToString();
            }
            return string.Join(".", octets);
        }

        /// <summary>쉼표로 구분된 IP 목록에서 prefix(옥텟 1~4개)로 시작하는 첫 IP. 없으면 null. 옥텟 단위로 비교하므로 "123" 은 1123.x 와 맞지 않는다.</summary>
        private static string FirstIpInPrefix(string ipList, string prefix)
        {
            string[] want = prefix.Split('.');
            foreach (string item in ipList.Split(','))
            {
                string ip = item.Trim();
                string[] have = ip.Split('.');
                if (have.Length < want.Length) continue;
                bool match = true;
                for (int i = 0; i < want.Length && match; i++)
                    if (have[i] != want[i]) match = false;
                if (match) return ip;
            }
            return null;
        }

        /// <summary>자동 변경 중 "자동 정지: 지정 IP/대역 할당 시 정지"의 IP/대역에 속하는 IP가 할당 목록에 있으면 자동 변경을 정지한다. 정지했으면 true.</summary>
        private bool AutoStopIfTargetAssigned(string assignedIps)
        {
            if (busy || !autoRunning || !chkAutoStopIp.Checked || string.IsNullOrEmpty(assignedIps)) return false;
            string prefix = ParseStopIp();
            string matched = prefix == null ? null : FirstIpInPrefix(assignedIps, prefix);
            if (matched == null) return false;
            StopAuto();
            autoStopMessage = StopTargetLabel(prefix) + " IP " + matched + " 가 할당되어 자동 변경을 정지했습니다.";
            SetStatus("완료", autoStopMessage);
            return true;
        }

        /// <summary>정지 조건 표시: 네 자리면 "지정한", 짧으면 "지정한 대역 123.123.* 의"</summary>
        private static string StopTargetLabel(string prefix)
        {
            return prefix.Split('.').Length == 4 ? "지정한" : "지정한 대역 " + prefix + ".* 의";
        }

        /// <summary>남은 시간 표시: 1시간 미만 "N초", 하루 미만 "H:MM:SS", 그 이상 "D일 H:MM:SS"</summary>
        private static string FormatRemaining(int seconds)
        {
            if (seconds < 3600) return seconds + "초";
            int days = seconds / 86400;
            string hms = seconds % 86400 / 3600 + ":" + (seconds % 3600 / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            return days > 0 ? days + "일 " + hms : hms;
        }

        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtIpLog.Clear();
            lastBoxIp = null;
        }

        /// <summary>마지막으로 기록한 할당 IP·마스크·게이트웨이와 지금 쓰는 MAC으로, 현재 로그 옵션에 맞는 한 줄을 만든다.</summary>
        private string BuildLastLogLine(NetworkAdapterInfo adapter)
        {
            return IpMonitor.BuildLogLine(lastLoggedIp, lastLoggedMask, lastLoggedGateway, adapter.CurrentMac,
                                          chkLogTime.Checked, chkLogMask.Checked, chkLogGateway.Checked, chkLogMac.Checked);
        }

        /// <summary>상자에 한 줄 추가하고 마지막 줄의 할당 IP를 기억한다.</summary>
        private void AppendToBox(string assignedIps, string line)
        {
            txtIpLog.AppendText(line + Environment.NewLine);
            lastBoxIp = assignedIps;
        }

        /// <summary>저장 옵션이 켜져 있으면 파일에 한 줄 추가한다. 실패하면 옵션을 끄고 오류 메시지를 돌려준다 (성공/꺼짐이면 null).</summary>
        private string AppendToFile(string line)
        {
            if (!chkIpLog.Checked) return null;
            try
            {
                IpMonitor.AppendLine(Application.ExecutablePath, line);
                return null;
            }
            catch (Exception ex)
            {
                chkIpLog.Checked = false;   // CheckedChanged 가 먼저 '껐습니다' 상태를 쓰고, 호출자가 오류를 그 뒤에 표시한다
                return "IP 로그 저장 실패: " + ex.Message;
            }
        }

        /// <summary>로그 상자 사용 여부에 맞춘다: 상자는 늘 보이되 꺼져 있으면 흐리게(비활성) 표시하고 기록하지 않는다. 로그 비우기는 언제나 된다.</summary>
        private void ApplyLogBoxEnabled()
        {
            bool on = chkLogBox.Checked;
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (on && adapter != null && lastLoggedIp != null && lastLoggedIp != lastBoxIp)   // 켜는 순간 현재 IP (마지막 줄과 같으면 생략)
                AppendToBox(lastLoggedIp, BuildLastLogLine(adapter));
            txtIpLog.Enabled = on;
        }

        /// <summary>안전망 폴링(알림 직후에는 settle 재조회): 알림을 놓쳤을 때를 대비해 IP 구성을 다시 읽는다 (알림을 못 쓰는 OS 에서는 이것이 유일한 갱신 경로).</summary>
        private void ipTimer_Tick(object sender, EventArgs e)
        {
            RefreshIp();
            if (ipLogError != null) SetStatus("실패", ipLogError);
            if (ipNotifier != null && ipTimer.Interval != IpSafetyPollMs)
            {
                ipTimer.Stop();
                ipTimer.Interval = IpSafetyPollMs;
                ipTimer.Start();
            }
        }

        /// <summary>
        /// 1초마다: 선택한 어댑터의 송수신 카운터를 읽어 그래프에 한 샘플을 넣고(어댑터가 내려가 있으면 속도 0),
        /// 자동 변경 카운트다운을 1초 단위로 갱신해 예약 시각이 되면 변경을 시작한다.
        /// </summary>
        private void secondTimer_Tick(object sender, EventArgs e)
        {
            if (SelectedAdapter != null)
            {
                try
                {
                    traffic.Sample();
                }
                catch (Exception)
                {
                    // 조회 실패는 Sample 이 false 로 돌려주므로 여기는 P/Invoke 자체가 실패하는 경우(iphlpapi 에 GetIfEntry2 가 없는 OS 등)뿐 — 이번 초는 속도 0으로 둔다
                }
                trafficGraph.AddSample(traffic.ReceivedSpeed, traffic.SentSpeed, traffic.ReceivedTotal, traffic.SentTotal);
            }
            RunAutoIfDue();
            // 할당 대기 시계는 IP 를 다시 읽지 않고도 1초마다 센다 (대기 상태는 마지막 RefreshIp 가 정한다)
            if (waiting && autoRunning && !busy && TrackWaiting(true, Environment.TickCount)) StartWaitTimeoutChange();
        }

        // ------------------------------------------------------------------
        // 어댑터 목록 / 정보 갱신
        // ------------------------------------------------------------------
        private void LoadAdapters(string guidToSelect)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                SetStatus("진행", "어댑터 목록을 읽는 중...");
                lblStatus.Update();
                List<NetworkAdapterInfo> adapters;
                string enumError = null;
                try
                {
                    adapters = AdapterEnumerator.Enumerate();
                }
                catch (Exception ex)
                {
                    adapters = new List<NetworkAdapterInfo>();
                    enumError = "어댑터 열거 오류: " + ex.Message;
                }

                suppressSelectionChanged = true;
                try
                {
                    cboAdapters.BeginUpdate();
                    cboAdapters.Items.Clear();
                    int selectIndex = -1;
                    foreach (NetworkAdapterInfo a in adapters)
                    {
                        int idx = cboAdapters.Items.Add(a);
                        if (guidToSelect != null && string.Equals(a.InterfaceGuid, guidToSelect, StringComparison.OrdinalIgnoreCase))
                            selectIndex = idx;
                    }
                    cboAdapters.EndUpdate();
                    if (cboAdapters.Items.Count > 0)
                        cboAdapters.SelectedIndex = selectIndex >= 0 ? selectIndex : 0;
                }
                finally
                {
                    suppressSelectionChanged = false;
                }

                if (cboAdapters.Items.Count == 0)
                {
                    txtPermanentMac.Text = txtCurrentMac.Text = string.Empty;
                    txtCurrentIp.Text = txtMask.Text = txtGateway.Text = txtDns.Text = string.Empty;
                    traffic.Attach(null);
                    trafficGraph.Clear();
                    SetStatus("실패", enumError ?? "네트워크 어댑터를 찾지 못했습니다.");
                    return;
                }
                RefreshSelectedAdapterInfo();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void RefreshSelectedAdapterInfo()
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                SetStatus("진행", "어댑터 정보를 읽는 중...");
                lblStatus.Update();
                if (!string.Equals(adapter.InterfaceGuid, lastInfoGuid, StringComparison.OrdinalIgnoreCase))
                {
                    lastInfoGuid = adapter.InterfaceGuid;
                    lastLoggedIp = null;   // 다른 어댑터를 골랐을 때만 기준 초기화 (단순 새로 고침은 중복 기록하지 않음)
                    CancelAuto();          // 이전 어댑터의 예약이 새 어댑터에 적용되지 않도록
                }
                if (!string.Equals(adapter.InterfaceGuid, traffic.InterfaceGuid, StringComparison.OrdinalIgnoreCase))
                {
                    traffic.Attach(adapter.InterfaceGuid);   // 그래프와 누적량은 어댑터별 — 다른 어댑터를 고르면 새로 시작
                    trafficGraph.Clear();
                }

                string permanent = MacChangeService.ReadPermanentMac(adapter);
                txtPermanentMac.Text = permanent != null ? MacAddressUtil.Format(permanent) : "(조회 실패)";

                string current = MacChangeService.ReadCurrentMac(adapter);
                txtCurrentMac.Text = current != null ? MacAddressUtil.Format(current) : "(조회 실패)";
                ShowCurrentMac(adapter, current);

                if (current == null)
                    SetStatus("실패", "현재 MAC을 읽지 못했습니다. (어댑터 비활성 또는 드라이버 거부)");
                else if (permanent == null)
                    SetStatus("준비", "공장 MAC은 읽지 못했습니다(드라이버 미지원). 현재 MAC만 표시합니다.");
                else
                    SetStatus("준비", "어댑터 정보를 읽었습니다.");

                RefreshIp();
                if (ipLogError != null) SetStatus("실패", ipLogError);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private static void SetIfChanged(TextBox box, string text)
        {
            if (box.Text != text) box.Text = text;
        }

        /// <summary>현재 MAC을 모델과 드롭다운 항목 텍스트에 반영한다 (ComboBox는 Add 시점의 문자열을 캐시하므로 항목을 다시 넣어야 한다).</summary>
        private void ShowCurrentMac(NetworkAdapterInfo adapter, string current)
        {
            if (current == null || string.Equals(adapter.CurrentMac, current, StringComparison.OrdinalIgnoreCase)) return;
            adapter.CurrentMac = current;
            txtCurrentMac.Text = MacAddressUtil.Format(current);
            int i = cboAdapters.SelectedIndex;
            if (i < 0) return;
            suppressSelectionChanged = true;
            try { cboAdapters.Items[i] = adapter; }
            finally { suppressSelectionChanged = false; }
        }

        /// <summary>선택한 어댑터의 현재 IPv4를 표시하고, 로그 옵션이 켜져 있으면 새로 할당된 IP를 파일에 기록한다. 오류는 ipLogError 에 남긴다.</summary>
        private void RefreshIp()
        {
            ipLogError = null;
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
            {
                txtCurrentIp.Text = txtMask.Text = txtGateway.Text = txtDns.Text = string.Empty;
                return;
            }
            IpMonitor.IpInfo info;
            try
            {
                info = IpMonitor.ReadIpInfo(adapter.InterfaceGuid);
            }
            catch (Exception)
            {
                txtCurrentIp.Text = "(IP 조회 실패)";
                txtMask.Text = txtGateway.Text = txtDns.Text = string.Empty;
                return;
            }
            string ip = info != null ? info.Addresses : null;
            bool up = info != null && info.Up;
            // 어댑터가 올라와 있으면 지금 사용 중인 MAC도 함께 갱신한다 (느리게 올라온 어댑터, Wi-Fi 임의 주소 변경 등).
            if (info != null && info.Mac != null && info.Mac != "000000000000") ShowCurrentMac(adapter, info.Mac);

            // 표시·로그 기준은 169.254.x.x 를 뺀 "실제 할당" 주소 목록 — 전환 중 자동 사설 주소가 붙었다 떨어져도 보이거나 중복 기록되지 않는다.
            string assigned, assignedMasks;
            IpMonitor.FilterAssigned(ip, info != null ? info.Masks : null, out assigned, out assignedMasks);
            // 표시는 ipconfig 와 같은 기준: 링크가 없으면 주소를 보이지 않고, 실제 IP가 있으면 169.254 자동 사설 주소는 숨긴다.
            bool shown = ip != null && up && ip.Length > 0;
            SetIfChanged(txtCurrentIp, IpDisplayText(ip, up, assigned));
            SetIfChanged(txtMask, !shown ? string.Empty : assigned.Length > 0 ? assignedMasks : info.Masks);
            SetIfChanged(txtGateway, ip == null || !up ? string.Empty : info.Gateways.Length > 0 ? info.Gateways : "(없음)");
            SetIfChanged(txtDns, ip == null || !up ? string.Empty : info.Dns.Length > 0 ? info.Dns : "(없음)");

            if (ip == null || ip.Length == 0 || !up)
            {
                lastLoggedIp = null;   // 어댑터가 내려갔거나 링크가 끊긴 경우만 기준 초기화 (다시 같은 IP를 받아도 새 할당으로 기록)
                CancelAuto();          // IP가 사라지면 예약도 취소 (다시 받으면 새로 센다)
                // 링크는 올라왔는데 IP가 없으면 "할당 대기" — 설정한 초를 넘기면 새 MAC (어댑터가 꺼졌거나 링크가 없으면 세지 않는다)
                if (TrackWaiting(ip != null && up, Environment.TickCount)) StartWaitTimeoutChange();
                return;
            }
            if (assigned.Length == 0)
            {
                CancelAuto();   // 임대가 빠져 169.254 만 남은 동안은 변경하지 않는다 (기준 IP 는 유지 — 같은 IP가 돌아오면 중복 기록하지 않음)
                if (TrackWaiting(true, Environment.TickCount)) StartWaitTimeoutChange();   // 자동 사설 주소만 있는 것도 할당 대기
                return;
            }
            TrackWaiting(false, 0);   // 실제 IP를 받았으면 대기 종료
            // 지정 IP 자동 정지: 자동 변경 중 대상 IP가 할당되면 바로 멈춘다 (로그 중복 판정과 상관없이 매번 확인, IP 줄은 아래에서 평소처럼 기록)
            AutoStopIfTargetAssigned(assigned);
            // 작업 중(busy)에는 기록하지 않는다: 어댑터가 올라온 직후의 IP는 작업 완료 후 새 MAC과 함께 기록된다.
            if (busy || assigned == lastLoggedIp) return;

            lastLoggedIp = assigned;
            lastLoggedMask = assignedMasks;
            lastLoggedGateway = info.Gateways;
            string line = BuildLastLogLine(adapter);   // 상자와 파일에 같은 줄
            if (chkLogBox.Checked) AppendToBox(assigned, line);   // 꺼져 있는 동안에는 상자에 기록하지 않음
            ipLogError = AppendToFile(line);
            ScheduleAuto();   // 새로 할당된 IP → 자동 변경 예약
            if (autoPending && (int)nudAutoDelay.Value == 0) RunAutoIfDue();   // 0초면 다음 타이머 틱을 기다리지 않고 바로 바꾼다
        }

        /// <summary>
        /// 현재 IP 칸의 문장 (ipconfig 기준). ip: 어댑터의 IPv4 목록(어댑터가 IP 스택에 없으면 null), up: 링크 상태, assigned: 169.254 를 뺀 실제 할당 목록.
        /// 링크가 없으면 남아 있는 주소(대개 169.254 자동 사설 주소)를 보이지 않고, 실제 IP가 있으면 함께 붙어 있는 169.254 는 숨긴다.
        /// </summary>
        private static string IpDisplayText(string ip, bool up, string assigned)
        {
            if (ip == null) return "(어댑터 비활성 상태)";
            if (!up) return "(연결 안 됨 — 링크 없음)";
            if (ip.Length == 0) return "(IP 없음 — 할당 대기 중)";
            if (assigned.Length == 0) return ip + "  (DHCP 응답 없음 — 자동 사설 주소)";
            return assigned;
        }

        // ------------------------------------------------------------------
        // 백그라운드 작업
        // ------------------------------------------------------------------
        private void StartOperation(OperationArgs args)
        {
            if (worker.IsBusy) return;
            if (autoRunning)
            {
                autoPending = false;   // 작업이 끝나면 새 IP 할당 시점부터 다시 센다
                btnAutoToggle.Text = "정지";
            }
            args.ResetDns = chkResetDns.Checked;
            operationReason = args.Reason;
            SetBusy(true);
            SetStatus("진행", ReasonPrefix() + OperationLabel(args.Kind) + " 시작...");
            worker.RunWorkerAsync(args);
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            OperationArgs args = (OperationArgs)e.Argument;
            BackgroundWorker w = (BackgroundWorker)sender;
            Action<string> log = delegate(string message) { w.ReportProgress(0, message); };
            switch (args.Kind)
            {
                case OperationKind.Apply: e.Result = MacChangeService.Apply(args.Adapter, args.NewMac, args.ResetDns, log); break;
                case OperationKind.Restore: e.Result = MacChangeService.Restore(args.Adapter, args.ResetDns, log); break;
                default: e.Result = MacChangeService.RenewIp(args.Adapter, args.ResetDns, log); break;
            }
        }

        /// <summary>작업 이유가 있으면 "이유 — " 형태로 상태 문장 앞에 붙일 접두어</summary>
        private string ReasonPrefix()
        {
            return string.IsNullOrEmpty(operationReason) ? string.Empty : operationReason + " — ";
        }

        private static string OperationLabel(OperationKind kind)
        {
            switch (kind)
            {
                case OperationKind.Apply: return "MAC 변경";
                case OperationKind.Restore: return "원상복구";
                default: return "IP 갱신";
            }
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            string message = e.UserState as string;
            if (message != null) SetStatus("진행", message);
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);

            MacChangeResult result = e.Error == null ? e.Result as MacChangeResult : null;
            string finalState = result != null && result.Success ? "완료" : "실패";
            string finalMessage = ReasonPrefix() + (e.Error != null ? e.Error.Message : result != null ? result.Message : "결과를 받지 못했습니다.");

            // 실패했거나 재부팅이 필요하면 자동 변경을 멈춘다 (같은 실패를 반복하거나 대화 상자가 겹치지 않도록)
            if (autoRunning && (result == null || !result.Success || result.RebootRequired))
            {
                StopAuto();
                finalMessage += " 자동 변경을 정지했습니다.";
            }

            // 어댑터가 실제로 중지되었다면 이후 받는 IP는 같은 값이라도 새 할당이므로 새 MAC과 함께 기록한다.
            if (result != null && result.AdapterRestarted) lastLoggedIp = null;
            // 작업(MAC 변경·원상복구·IP 갱신)이 끝나면 그래프는 그대로 두고 누적 송수신량만 0부터 다시 센다 (TMAC 과 같은 동작).
            if (result != null && result.Success)
            {
                traffic.ResetTotals();
                trafficGraph.ResetTotals();   // 다음 샘플을 기다리지 않고 바로 0으로 표시
            }

            // 변경/복구 후 현재 MAC/IP를 다시 읽어 UI를 갱신한 뒤, 작업 결과 상태를 최종적으로 표시한다.
            bool autoBefore = autoRunning;   // 실패/재부팅으로 위에서 이미 정지했다면 false
            RefreshSelectedAdapterInfo();    // 새 IP가 지정 IP면 여기서 자동 변경이 정지된다
            if (autoBefore && !autoRunning) finalMessage += " " + (autoStopMessage ?? "자동 변경을 정지했습니다.");
            SetStatus(finalState, finalMessage + (ipLogError != null ? " / " + ipLogError : ""));

            if (finalState == "실패" || (result != null && result.RebootRequired)) RestoreFromTray();   // 숨긴 채로 대화 상자를 띄우지 않는다
            if (result != null && result.RebootRequired)
            {
                MessageBox.Show(this, result.Message + "\r\n\r\n지금 재부팅하거나, 장치 관리자에서 어댑터를 '사용 안 함' → '사용'으로 직접 재시작하세요.",
                    Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (finalState == "실패")
            {
                string text = finalMessage;
                if (result != null && !string.IsNullOrEmpty(result.Guidance)) text += "\r\n\r\n" + result.Guidance;
                MessageBox.Show(this, text, Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RunAutoIfDue();   // 지연 0초: IP가 이미 있으면 기다리지 않고 바로 다음 변경
        }

        // ------------------------------------------------------------------
        // UI 헬퍼
        // ------------------------------------------------------------------
        private void SetBusy(bool value)
        {
            busy = value;
            UpdateControlStates();
        }

        /// <summary>
        /// 작업 중(busy)과 자동 변경 실행 중(autoRunning)에 맞춰 컨트롤 사용 가능 여부를 한곳에서 맞춘다.
        /// 자동 변경 실행 중에는 정지(btnAutoToggle), 로그 비우기, 백그라운드 실행 외의 버튼·체크박스·어댑터 선택·입력 칸을 모두 잠근다.
        /// </summary>
        private void UpdateControlStates()
        {
            bool idle = !busy && !autoRunning;
            cboAdapters.Enabled = idle;
            btnRefresh.Enabled = idle;
            btnRandom.Enabled = idle;
            btnApply.Enabled = idle;
            btnRestore.Enabled = idle;
            btnRenewIp.Enabled = idle;
            chkIpLog.Enabled = idle;
            txtNewMac.Enabled = idle;   // 실행 중 입력해도 다음 자동 변경이 덮어쓰므로 함께 잠근다 (포커스가 정지 버튼으로 튀는 것도 막는다)
            chkAuto.Enabled = !autoRunning;
            chkLogTime.Enabled = !autoRunning;
            chkLogMask.Enabled = !autoRunning;
            chkLogGateway.Enabled = !autoRunning;
            chkLogMac.Enabled = !autoRunning;
            chkLogBox.Enabled = !autoRunning;
            btnAutoToggle.Enabled = chkAuto.Checked;                     // 실행 중에도 정지할 수 있어야 하므로 잠그지 않는다
            nudAutoDelay.Enabled = !autoRunning && chkAuto.Checked;        // 지연 초와 할당 대기 초는 자동 변경을 체크해야 열린다
            nudWaitTimeout.Enabled = !autoRunning && chkAuto.Checked;
            chkAutoStopIp.Enabled = !autoRunning;
            txtAutoStopIp.Enabled = !autoRunning && chkAutoStopIp.Checked;   // 체크해야 입력 칸이 열린다
            chkResetDns.Enabled = !autoRunning;
            UseWaitCursor = busy;
        }

        /// <param name="state">준비 / 진행 / 완료 / 실패</param>
        /// <param name="message">상태 라벨에 덧붙일 한국어 메시지 (라벨에 다 들어가지 않아 잘려 보일 때만 전체 문장을 툴팁으로 보여 준다)</param>
        private void SetStatus(string state, string message)
        {
            string text = "상태: " + state;
            if (!string.IsNullOrEmpty(message)) text += " — " + message.Replace("\r\n", " ");
            if (lblStatus.Text == text) return;
            lblStatus.Text = text;
            switch (state)
            {
                case "완료": lblStatus.ForeColor = Color.DarkGreen; break;
                case "실패": lblStatus.ForeColor = Color.Firebrick; break;
                case "진행": lblStatus.ForeColor = Color.DarkOrange; break;
                default: lblStatus.ForeColor = SystemColors.ControlText; break;
            }
            // 라벨에 다 들어가면 툴팁 없음, 잘려 보이면(…) 전체 문장을 툴팁으로
            bool clipped = TextRenderer.MeasureText(text, lblStatus.Font).Width > lblStatus.ClientSize.Width - 4;
            toolTip.SetToolTip(lblStatus, clipped ? text : null);
        }
    }
}
