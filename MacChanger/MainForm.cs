using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
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
        /// <summary>그래프 위치: 로그 상자와 반반일 때 / 로그 상자를 숨겨 아래쪽 전체 폭을 쓸 때 (DPI/글꼴 배율이 적용된 뒤의 실제 픽셀)</summary>
        private Rectangle graphHalfBounds, graphFullBounds;
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

        public MainForm()
        {
            InitializeComponent();
            ipTimer.Interval = (int)nudIpInterval.Value;                              // IP 확인 주기의 기본값은 입력 칸 한 곳에서만 정한다
            graphHalfBounds = trafficGraph.Bounds;                                    // AutoScaleMode.Font 배율이 적용된 뒤의 값
            graphFullBounds = Rectangle.Union(txtIpLog.Bounds, trafficGraph.Bounds);   // 로그 상자를 숨기면 그 자리까지 그래프가 차지
            ApplyLogBoxVisibility();                                                  // 기본값: 로그 상자 숨김
            try
            {
                // 실행 파일에 내장된 아이콘(그룹 아이콘 ID 32512)을 EXE 리소스에서 직접 읽어 창/작업표시줄 아이콘으로 쓴다.
                // (Icon.ExtractAssociatedIcon 은 UNC 경로에서 예외를 던지므로 폴백으로만 사용)
                IntPtr handle = NativeMethods.LoadIcon(NativeMethods.GetModuleHandle(null), new IntPtr(32512));
                Icon = handle != IntPtr.Zero ? Icon.FromHandle(handle) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception) { }
            toolTip.SetToolTip(txtNewMac, "12자리 16진수. 구분자(-, :, .)는 있어도 되고 없어도 됩니다. 예: 02-1A-2B-3C-4D-5E");
            toolTip.SetToolTip(btnRandom, "무선 어댑터: 두 번째 자리 2/6/A/E, 그 외: 두 번째 자리 짝수, 나머지 11자리 0~F 무작위");
            toolTip.SetToolTip(btnApply, "확인 창 없이 바로 어댑터를 비활성화하고 NetworkAddress를 기록한 뒤(EnableDHCP = 1이면 Tcpip 값 자동 정리) 다시 활성화합니다.");
            toolTip.SetToolTip(btnRenewIp, "MAC은 그대로 두고(NetworkAddress 유지) 어댑터를 비활성화 → Tcpip 값 자동 정리(EnableDHCP = 1이면) → 활성화하여 IP만 새로 받습니다.");
            toolTip.SetToolTip(btnRestore, "확인 창 없이 바로 NetworkAddress 값을 삭제하고(EnableDHCP = 1이면 Tcpip 값 자동 정리) 어댑터를 재시작하여 공장 MAC으로 되돌립니다.");
            toolTip.SetToolTip(chkIpLog, "켜 두면 새 IP가 할당될 때마다 로그 상자와 같은 줄을 실행 파일 옆 " + IpMonitor.LogFileName + " 에도 추가합니다. 169.254.x.x 자동 사설 주소는 기록하지 않습니다.");
            toolTip.SetToolTip(chkLogTime, "로그 줄 맨 앞에 시각(yyyy-MM-dd HH:mm:ss)을 넣습니다.");
            toolTip.SetToolTip(chkLogMask, "로그 줄의 IP 뒤에 서브넷 마스크를 넣습니다.");
            toolTip.SetToolTip(chkLogGateway, "로그 줄에 기본 게이트웨이를 넣습니다 (없으면 \"(없음)\").");
            toolTip.SetToolTip(chkLogMac, "로그 줄 끝에 그때 사용 중인 MAC을 넣습니다.");
            toolTip.SetToolTip(chkShowLog, "할당된 IP 주소 로그 상자를 보이거나 숨깁니다 (숨기면 그래프가 아래쪽 전체 폭을 씁니다). 숨겨진 동안에는 상자에 기록하지 않고, 켤 때 현재 IP가 마지막 줄과 다르면 한 줄 추가합니다.");
            toolTip.SetToolTip(trafficGraph, "선택한 어댑터의 송수신 속도 그래프 (빨강: 수신, 초록: 송신, 1초마다 갱신, 가로 2픽셀 = 1초). MAC을 변경하면 그래프는 유지되고 누적 데이터 양만 0부터 다시 셉니다.");
            toolTip.SetToolTip(btnClearLog, "로그 상자의 내용을 지웁니다 (파일에는 영향 없음).");
            toolTip.SetToolTip(chkAuto, "자동 변경 기능을 사용합니다. 체크한 뒤 '시작'을 누르면 선택한 어댑터에 IP가 할당될 때마다 지정한 초 뒤에 랜덤 MAC을 적용하고, 다시 IP를 받으면 반복합니다.");
            toolTip.SetToolTip(nudAutoDelay, "IP 할당을 감지한 뒤 MAC 변경까지 기다리는 시간(초, 0~604800 = 최대 7일; 0이면 즉시). 자동 변경이 실행 중일 때는 바꿀 수 없습니다 — 정지한 뒤 바꾸세요.");
            toolTip.SetToolTip(nudIpInterval, "선택한 어댑터의 IP 구성을 다시 읽는 간격(밀리초, 기본 1000). 위/아래 버튼은 100ms 단위로 100~2000, 직접 입력하면 1~2000ms 어떤 값이든 됩니다. 현재 IP 표시, 할당 IP 로그, 자동 변경의 IP 감지가 모두 이 주기로 돌아가며 바꾸면 바로 적용됩니다. 자동 변경이 실행 중일 때는 바꿀 수 없습니다 — 정지한 뒤 바꾸세요.");
            toolTip.SetToolTip(btnAutoToggle, "자동 변경을 시작하거나 정지합니다. 시작할 때 이미 IP가 있으면 바로 세기 시작하며, 변경이 실패하거나 재부팅이 필요하면 스스로 정지합니다. 실행 중에는 정지와 로그 지우기 외의 버튼·체크박스·어댑터 선택·입력 칸이 잠깁니다.");
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
            BeginInvoke(new MethodInvoker(delegate { LoadAdapters(null); ipTimer.Start(); secondTimer.Start(); }));
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing)
            {
                MessageBox.Show(this, "작업이 진행 중입니다. 완료될 때까지 기다려 주세요.", Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
            }
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
                SetStatus("준비", "랜덤 MAC 생성됨 (" + (wireless ? "무선: 두 번째 자리 2/6/A/E" : "유선/기타: 두 번째 자리 짝수") + "): " + txtNewMac.Text);
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
                SetStatus("실패", "새 MAC 형식이 올바르지 않습니다. 12자리 16진수를 입력하세요 (예: 02-1A-2B-3C-4D-5E).");
                txtNewMac.Focus();
                return;
            }
            if (!MacAddressUtil.IsUnicast(mac))
            {
                SetStatus("실패", "두 번째 자리가 홀수(멀티캐스트 주소)입니다. 두 번째 자리를 짝수로 바꾸세요.");
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

        private void chkShowLog_CheckedChanged(object sender, EventArgs e)
        {
            ApplyLogBoxVisibility();
        }

        private void chkAuto_CheckedChanged(object sender, EventArgs e)
        {
            btnAutoToggle.Enabled = chkAuto.Checked;
            if (!chkAuto.Checked && autoRunning) StopAuto();
        }

        private void btnAutoToggle_Click(object sender, EventArgs e)
        {
            if (autoRunning) StopAuto();
            else StartAuto();
        }

        private void StartAuto()
        {
            autoRunning = true;
            UpdateControlStates();   // 실행 중에는 정지·로그 지우기 외의 조작을 잠근다
            btnAutoToggle.Text = "정지 (IP 대기)";
            if (!busy) SetStatus("준비", "자동 변경 시작: IP 할당 후 " + nudAutoDelay.Value + "초 뒤 새 MAC을 적용합니다.");
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
            autoPending = false;
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
            StartOperation(args);
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

        /// <summary>로그 상자 표시 여부에 맞춰 상자와 지우기 버튼, 그래프 폭을 맞춘다 (상자를 숨기면 그래프가 아래쪽 전체 폭을 쓴다).</summary>
        private void ApplyLogBoxVisibility()
        {
            bool show = chkShowLog.Checked;
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (show && adapter != null && lastLoggedIp != null && lastLoggedIp != lastBoxIp)   // 켜는 순간 현재 IP (마지막 줄과 같으면 생략)
                AppendToBox(lastLoggedIp, BuildLastLogLine(adapter));
            txtIpLog.Visible = show;
            btnClearLog.Enabled = show;
            trafficGraph.Bounds = show ? graphHalfBounds : graphFullBounds;
        }

        /// <summary>IP 확인 주기(기본 1초)마다: IP 구성 표시와 할당 IP 로그 (새 IP가 보이면 자동 변경 예약).</summary>
        private void ipTimer_Tick(object sender, EventArgs e)
        {
            RefreshIp();
            if (ipLogError != null) SetStatus("실패", ipLogError);
        }

        private void nudIpInterval_ValueChanged(object sender, EventArgs e)
        {
            ipTimer.Interval = (int)nudIpInterval.Value;   // 바로 적용 (동작 중이면 새 주기로 다시 시작)
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
                    SetStatus("실패", "현재 MAC을 읽지 못했습니다 (어댑터가 비활성화되었거나 드라이버가 조회를 거부).");
                else if (permanent == null)
                    SetStatus("준비", "공장 MAC을 읽지 못했습니다 (드라이버가 OID 조회를 지원하지 않음). 현재 MAC은 읽었습니다.");
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

            // 로그 기준은 169.254.x.x 를 뺀 "실제 할당" 주소 목록 — 전환 중 자동 사설 주소가 붙었다 떨어져도 중복 기록되지 않는다.
            string assigned, assignedMasks;
            IpMonitor.FilterAssigned(ip, info != null ? info.Masks : null, out assigned, out assignedMasks);
            string text;
            if (ip == null) text = "(어댑터 비활성 상태)";
            else if (ip.Length == 0) text = up ? "(IP 없음 — 할당 대기 중)" : "(연결 안 됨 — 링크 없음)";
            else if (!up) text = ip + "  (링크 없음)";
            else if (assigned.Length == 0) text = ip + "  (DHCP 응답 없음 — 자동 사설 주소)";
            else text = ip;
            SetIfChanged(txtCurrentIp, text);
            SetIfChanged(txtMask, ip == null || ip.Length == 0 ? string.Empty : info.Masks);
            SetIfChanged(txtGateway, ip == null ? string.Empty : info.Gateways.Length > 0 ? info.Gateways : "(없음)");
            SetIfChanged(txtDns, ip == null ? string.Empty : info.Dns.Length > 0 ? info.Dns : "(없음)");

            if (ip == null || ip.Length == 0 || !up)
            {
                lastLoggedIp = null;   // 어댑터가 내려갔거나 링크가 끊긴 경우만 기준 초기화 (다시 같은 IP를 받아도 새 할당으로 기록)
                CancelAuto();          // IP가 사라지면 예약도 취소 (다시 받으면 새로 센다)
                return;
            }
            if (assigned.Length == 0)
            {
                CancelAuto();   // 임대가 빠져 169.254 만 남은 동안은 변경하지 않는다 (기준 IP 는 유지 — 같은 IP가 돌아오면 중복 기록하지 않음)
                return;
            }
            // 작업 중(busy)에는 기록하지 않는다: 어댑터가 올라온 직후의 IP는 작업 완료 후 새 MAC과 함께 기록된다.
            if (busy || assigned == lastLoggedIp) return;

            lastLoggedIp = assigned;
            lastLoggedMask = assignedMasks;
            lastLoggedGateway = info.Gateways;
            string line = BuildLastLogLine(adapter);   // 상자와 파일에 같은 줄
            if (chkShowLog.Checked) AppendToBox(assigned, line);   // 숨겨진 동안에는 상자에 기록하지 않음
            ipLogError = AppendToFile(line);
            ScheduleAuto();   // 새로 할당된 IP → 자동 변경 예약
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
            SetBusy(true);
            SetStatus("진행", OperationLabel(args.Kind) + " 시작...");
            worker.RunWorkerAsync(args);
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            OperationArgs args = (OperationArgs)e.Argument;
            BackgroundWorker w = (BackgroundWorker)sender;
            Action<string> log = delegate(string message) { w.ReportProgress(0, message); };
            switch (args.Kind)
            {
                case OperationKind.Apply: e.Result = MacChangeService.Apply(args.Adapter, args.NewMac, log); break;
                case OperationKind.Restore: e.Result = MacChangeService.Restore(args.Adapter, log); break;
                default: e.Result = MacChangeService.RenewIp(args.Adapter, log); break;
            }
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
            string finalMessage = e.Error != null ? e.Error.Message : result != null ? result.Message : "결과를 받지 못했습니다.";

            // 실패했거나 재부팅이 필요하면 자동 변경을 멈춘다 (같은 실패를 반복하거나 대화 상자가 겹치지 않도록)
            if (autoRunning && (result == null || !result.Success || result.RebootRequired))
            {
                StopAuto();
                finalMessage += " 자동 변경을 정지했습니다.";
            }

            // 어댑터가 실제로 중지되었다면 이후 받는 IP는 같은 값이라도 새 할당이므로 새 MAC과 함께 기록한다.
            if (result != null && result.AdapterRestarted) lastLoggedIp = null;
            // MAC 변경/복구가 끝나면 그래프는 그대로 두고 누적 송수신량만 0부터 다시 센다 (TMAC 과 같은 동작).
            if (result != null && result.Success)
            {
                traffic.ResetTotals();
                trafficGraph.ResetTotals();   // 다음 샘플을 기다리지 않고 바로 0으로 표시
            }

            // 변경/복구 후 현재 MAC/IP를 다시 읽어 UI를 갱신한 뒤, 작업 결과 상태를 최종적으로 표시한다.
            RefreshSelectedAdapterInfo();
            SetStatus(finalState, finalMessage + (ipLogError != null ? " / " + ipLogError : ""));

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
        /// 자동 변경 실행 중에는 정지(btnAutoToggle)와 로그 지우기(로그 상자 표시 여부를 따름) 외의 버튼·체크박스·어댑터 선택·입력 칸을 모두 잠근다.
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
            chkShowLog.Enabled = !autoRunning;
            nudAutoDelay.Enabled = !autoRunning;
            nudIpInterval.Enabled = !autoRunning;
            UseWaitCursor = busy;
        }

        /// <param name="state">준비 / 진행 / 완료 / 실패</param>
        /// <param name="message">상태 라벨에 덧붙일 한국어 메시지 (전체 내용은 툴팁으로도 볼 수 있다)</param>
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
            toolTip.SetToolTip(lblStatus, text);
        }
    }
}
