using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
            RenewIp,
            ReleaseIp
        }

        /// <summary>상태 줄의 상태 — 라벨에는 준비 / 진행 / 완료 / 실패 로 보인다 (SetStatus)</summary>
        private enum StatusKind
        {
            Ready,
            Progress,
            Done,
            Failed
        }

        private sealed class OperationArgs
        {
            public readonly OperationKind Kind;
            public readonly NetworkAdapterInfo Adapter;
            public string NewMac;
            /// <summary>Tcpip 값 정리 때 NameServer 에 값이 있어도 지울지 (DNS 초기화 옵션, UI 스레드에서 작업 시작 시 읽어 둔다)</summary>
            public bool ResetDns;
            /// <summary>작업 전에 DHCP 서버에 IP 를 반환할지 ("DHCP 서버에 IP 반환" 옵션, 작업 시작 시 읽어 둔다)</summary>
            public bool ReleaseIp;
            /// <summary>작업을 시작한 이유 (예: "IP 할당 대기 30초 초과"). 시작·완료 상태 앞에 붙인다. 직접 누른 작업은 null.</summary>
            public string Reason;
            /// <summary>자동 변경이 시작한 작업인지 (횟수 제한은 이것만 센다 — 시작 버튼을 누를 때 돌고 있던 작업은 직접 작업이든 이전 실행의 자동 변경이든 세지 않는다)</summary>
            public bool Auto;

            public OperationArgs(OperationKind kind, NetworkAdapterInfo adapter)
            {
                Kind = kind;
                Adapter = adapter;
            }
        }

        private bool busy;
        /// <summary>드롭다운 항목 텍스트를 갱신(Items[i] = item)하거나 목록을 다시 채울 때 SelectedIndexChanged를 막는다.</summary>
        private bool suppressSelectionChanged;
        /// <summary>마지막으로 정보를 읽은 어댑터 GUID (실제로 다른 어댑터를 선택했을 때만 IP 로그 기준을 초기화)</summary>
        private string lastInfoGuid;
        /// <summary>선택한 어댑터에서 마지막으로 본 "실제 할당 IP"(169.254 제외, 정렬) — 로그·예약의 기준. 로그 옵션이 꺼져 있어도 갱신한다
        /// (그래야 옵션을 켜는 순간 지금 IP 가 기록되지 않고 그 뒤에 새로 받는 IP 부터 기록된다). 어댑터가 내려가거나 재시작되거나 다른 어댑터를 고르면 초기화한다.</summary>
        private string lastLoggedIp;
        /// <summary>IP 구성 변화 알림 (Windows 가 알려 줄 때만 다시 읽는다; 못 쓰는 OS 에서는 ipTimer 가 1초 폴링으로 대신)</summary>
        private IpChangeNotifier ipNotifier;
        /// <summary>알림이 몰려와도 UI 스레드에는 한 번만 넘기기 위한 플래그 (0: 대기 중 없음, 1: 넘겨 둠)</summary>
        private int refreshQueued;
        /// <summary>어댑터 목록 자동 갱신: 인터페이스가 생기거나 없어졌다는 알림을 UI 스레드에 한 번만 넘기는 플래그 / 목록을 작업자 스레드에서 읽는 중인지 / 읽는 동안(또는 작업 중에) 또 바뀌었는지</summary>
        private int listQueued;
        private bool listQueryRunning, listDirty;
        /// <summary>새로 고침(LoadAdapters)마다 +1 / 작업자 스레드가 읽기 시작할 때의 값 — 다르면 그 결과는 새로 고침보다 오래된 것이라 버린다. 드롭다운이 열려 있어 미룬 갱신은 listDeferred.</summary>
        private int listEpoch, listJobEpoch;
        private bool listDeferred;
        /// <summary>선택한 어댑터의 송수신 카운터 (1초마다 읽어 그래프에 넣는다)</summary>
        private readonly TrafficMonitor traffic = new TrafficMonitor();
        /// <summary>자동 변경이 "시작" 상태인지 (체크박스는 기능 사용 여부, 버튼이 실제 시작/정지)</summary>
        private bool autoRunning;
        /// <summary>자동 변경 예약 여부와 예약 시각(Environment.TickCount 기준 — 시스템 시계 변경에 영향받지 않음)</summary>
        private bool autoPending;
        private int autoDueTick;
        /// <summary>실행 중인 자동 변경의 방식 (시작할 때 스위치에서 읽어 둔다 — 실행 중에는 스위치가 잠기지만, 한 번의 실행 안에서 방식이 바뀔 수 없음을 코드로도 보장)</summary>
        private bool intervalRun;
        /// <summary>"IP 할당 대기 중"(링크는 올라왔지만 실제 IP 없음) 상태가 시작된 시각 — 할당 대기 초과 시 새 MAC 적용에 사용</summary>
        private bool waiting;
        private int waitingSinceTick;
        /// <summary>진행 중인 작업의 이유(OperationArgs.Reason) — 완료 상태에도 붙인다 / 자동 변경이 시작한 작업인지</summary>
        private string operationReason;
        private bool operationIsAuto;
        /// <summary>자동 정지 제한 (시작할 때 읽어 둔다): 쓰는지, 횟수 기준인지, 시간 기준의 마감(Stopwatch 틱 — 시스템 시계 변경에 영향받지 않고 1년도 넘치지 않는다),
        /// 이번 실행에서 적용한 새 MAC 횟수, 작업 중에 시간이 다 돼 멈췄을 때 그 작업의 완료 메시지에 붙일 말</summary>
        private bool limitRun, limitByCount;
        private long limitDeadline;
        private int autoChanges;
        private string limitNote;
        /// <summary>트레이 아이콘의 오른쪽 클릭 메뉴 (처음 필요할 때 만든다)</summary>
        private TrayMenu trayMenu;

        /// <summary>알림을 쓸 때의 안전망 폴링 주기 / 알림 직후 한 번 더 읽는 간격(게이트웨이·DNS 는 주소보다 조금 늦게 기록된다) / 알림을 못 쓰는 OS 에서의 폴링 주기 (ms)</summary>
        /// <summary>ipTimer 주기: 알림을 쓸 때 놓친 변화를 메우는 안전망 3초, 알림 직후 한 번 더 읽는 settle 1초, 알림을 못 쓰는 OS 의 폴링 1초.
        /// 그래프를 돌리는 graphTimer, 카운트다운·할당 대기 숫자를 돌리는 secondTimer 와 분리해 둔다 — graphTimer 는 어디서도 재시작하지 않아야 그래프가 정확히 1초마다 갱신된다.</summary>
        private const int IpSafetyPollMs = 3000, IpSettleMs = 1000, IpFallbackPollMs = 1000;
        /// <summary>IP 조회가 작업자 스레드에서 진행 중인지 / 그동안 다시 요청됐는지</summary>
        private bool ipQueryRunning, ipQueryAgain;
        /// <summary>진행 중인 IP 조회의 어댑터 GUID 와 시작 때의 ipEpoch — RefreshIp 가 조회를 시작할 때만 쓰고 작업자 스레드가 읽는다.
        /// 조회는 한 번에 하나뿐이라 작업자가 읽는 동안 바뀌지 않는다: ipQueryRunning 은 작업자가 이 값을 다 읽고 BeginInvoke 로 넘긴 ApplyIpInfo 만 끈다 (다른 곳에서 끄면 작업자가 다음 조회의 값을 읽는다).</summary>
        private string ipQueryGuid;
        private int ipQueryEpoch;
        /// <summary>작업(어댑터 재시작)의 시작·완료마다 1씩 — 작업을 걸쳐 읽은 IP 결과는 버린다 (옛 IP 를 새 MAC 으로 기록하거나 변경을 또 걸지 않게)</summary>
        private int ipEpoch;

        public MainForm()
        {
            InitializeComponent();
            txtIpLog.Enabled = chkLogBox.Checked;   // 기본값: 로그 보기 꺼짐(흐리게 표시, 기록 안 함)
            try
            {
                // 실행 파일에 내장된 아이콘(그룹 아이콘 ID 32512)을 EXE 리소스에서 직접 읽어 창/작업표시줄 아이콘으로 쓴다.
                // (Icon.ExtractAssociatedIcon 은 UNC 경로에서 예외를 던지므로 폴백으로만 사용)
                IntPtr handle = NativeMethods.LoadIcon(NativeMethods.GetModuleHandle(null), new IntPtr(32512));
                Icon = handle != IntPtr.Zero ? Icon.FromHandle(handle) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception) { }
            notifyIcon.Icon = Icon;   // 백그라운드 실행 중 트레이에 같은 아이콘을 보여 준다
            ApplyMode();
            UpdateControlStates();   // 시작 버튼·설정 칸·스위치는 "사용"을 체크해야 열린다
        }

        /// <summary>자동 변경 방식: 토글 스위치가 오른쪽이면 IP 할당과 관계없이 일정 시간마다, 왼쪽이면 IP 할당 시</summary>
        private bool IntervalMode()
        {
            return modeSwitch.Checked;
        }

        private NetworkAdapterInfo SelectedAdapter()
        {
            return cboAdapters.SelectedItem as NetworkAdapterInfo;
        }

        // ------------------------------------------------------------------
        // 폼 이벤트
        // ------------------------------------------------------------------
        private void MainForm_Shown(object sender, EventArgs e)
        {
            // 창이 먼저 그려진 뒤에 (WMI 조회가 몇 초 걸릴 수 있으므로) 목록을 읽는다.
            BeginInvoke(new MethodInvoker(delegate { LoadAdapters(null); StartIpMonitoring(); graphTimer.Start(); secondTimer.Start(); }));
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
            if (trayMenu != null) trayMenu.Dispose();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------
        // IP 변화 감지: Windows 알림(즉시) + 안전망 폴링
        // ------------------------------------------------------------------
        /// <summary>
        /// 알림이 등록되면 변화가 있을 때만 즉시 다시 읽고(1초 뒤 한 번 더 — 늦게 기록되는 게이트웨이·DNS 반영), ipTimer 는 놓친 변화를 메우는 안전망(3초)으로 돈다.
        /// 알림을 못 쓰는 OS 면 1초 폴링. 조회 자체는 작업자 스레드에서 하므로 UI 스레드와 그래프·카운트다운 타이머의 틱은 밀리지 않는다.
        /// </summary>
        private void StartIpMonitoring()
        {
            IpChangeNotifier notifier = new IpChangeNotifier();
            if (notifier.Available)
            {
                ipNotifier = notifier;
                notifier.Changed += ipNotifier_Changed;
                notifier.InterfacesChanged += ipNotifier_InterfacesChanged;
                ipTimer.Interval = IpSafetyPollMs;
            }
            else
            {
                string why = notifier.Error;
                notifier.Dispose();   // ipNotifier 는 null 로 남는다
                ipTimer.Interval = IpFallbackPollMs;
                if (!busy) SetStatus(StatusKind.Ready, "IP 변경 알림을 쓸 수 없어 1초마다 확인합니다. (" + why + ")");
            }
            ipTimer.Start();
        }

        /// <summary>시스템 스레드에서 온 알림 — UI 스레드에 한 번만 넘긴다 (몰려오는 알림은 한 번의 갱신으로 합친다).</summary>
        private void ipNotifier_Changed(object sender, EventArgs e)
        {
            PostOnce(ref refreshQueued, false);   // RefreshIpFromNotification 을 넘긴다
        }

        /// <summary>시스템 스레드에서 온 인터페이스 추가/삭제 알림 — UI 스레드에 한 번만 넘긴다.</summary>
        private void ipNotifier_InterfacesChanged(object sender, EventArgs e)
        {
            PostOnce(ref listQueued, true);   // ArmAdapterRefresh 를 넘긴다
        }

        /// <summary>두 알림 공통: queued 가 0 일 때만 1 로 바꾸고 UI 스레드에 넘긴다 — list 면 ArmAdapterRefresh, 아니면 RefreshIpFromNotification (넘겨 받은 메서드가 queued 를 0 으로 되돌린다). 넘기지 못하면 바로 0 으로 되돌린다. 델리게이트는 실제로 넘길 때만 만든다.</summary>
        private void PostOnce(ref int queued, bool list)
        {
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(list ? new MethodInvoker(ArmAdapterRefresh) : new MethodInvoker(RefreshIpFromNotification));
                else Interlocked.Exchange(ref queued, 0);
            }
            catch (Exception)   // 창이 닫히는 중
            {
                Interlocked.Exchange(ref queued, 0);
            }
        }

        /// <summary>어댑터 목록 자동 갱신 예약: 알림이 몰려오면(어댑터 하나가 올라올 때도 여러 번) 마지막 알림 뒤 0.5초에 한 번만 읽는다.</summary>
        private void ArmAdapterRefresh()
        {
            Interlocked.Exchange(ref listQueued, 0);
            if (IsDisposed) return;
            Restart(adapterTimer);
        }

        /// <summary>타이머를 멈췄다 다시 켠다 — 다음 틱이 지금부터 정확히 한 주기 뒤에 온다.</summary>
        private static void Restart(System.Windows.Forms.Timer timer)
        {
            timer.Stop();
            timer.Start();
        }

        /// <summary>
        /// 어댑터 목록을 작업자 스레드에서 다시 읽는다 (WMI 열거는 몇 초 걸릴 수 있어 UI 스레드에서 하면 그래프·카운트다운이 밀린다).
        /// 작업 중(우리가 어댑터를 재시작하는 중)이거나 이미 읽는 중이면 표시만 해 두고, 끝난 뒤에 다시 읽는다. 결과는 ApplyAdapterList 가 바뀐 것이 있을 때만 반영한다.
        /// </summary>
        private void adapterTimer_Tick(object sender, EventArgs e)
        {
            adapterTimer.Stop();
            if (busy || listQueryRunning)
            {
                listDirty = true;
                return;
            }
            listQueryRunning = true;
            listJobEpoch = listEpoch;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<NetworkAdapterInfo> list = null;
                try { list = AdapterEnumerator.Enumerate(); }
                catch (Exception) { }
                try { BeginInvoke(new Action<List<NetworkAdapterInfo>>(ApplyAdapterList), list); }
                catch (Exception) { }   // 창이 이미 닫혔다
            });
        }

        /// <summary>
        /// 다시 읽은 어댑터 목록을 반영한다 (UI 스레드). 목록(GUID·설명·연결 이름·종류·가상 여부)이 같으면 아무것도 하지 않는다 — MAC 은 비교하지 않는다 (선택한 어댑터의 MAC 은 따로 갱신된다).
        /// 선택한 어댑터는 같은 개체를 그대로 두되 설명·이름·종류는 새 값으로 맞추고 선택을 유지한다. 사라졌으면 자동 변경을 정지하고 첫 어댑터를 고른다(목록이 비면 칸을 비운다).
        /// 작업 중이면 결과를 버리고 작업이 끝날 때 다시 읽게 표시해 두고, 새로 고침이 그사이 더 새 목록을 넣었으면(listEpoch) 버리며, 드롭다운이 열려 있으면 닫힌 뒤로 미룬다.
        /// </summary>
        private void ApplyAdapterList(List<NetworkAdapterInfo> list)
        {
            listQueryRunning = false;
            if (busy)
            {
                listDirty = true;   // 읽는 동안 작업이 시작됐다 — worker_RunWorkerCompleted 가 다시 예약한다 (IP 반환처럼 인터페이스 알림이 없는 작업도)
                return;
            }
            if (listDirty)
            {
                listDirty = false;   // 읽는 동안 또 바뀌었다 — 잠시 뒤 다시
                adapterTimer.Start();
            }
            if (list == null || listJobEpoch != listEpoch) return;   // 열거 실패, 또는 그사이 새로 고침이 더 새 목록을 넣었다
            if (Signature(list) == Signature(cboAdapters.Items)) return;
            if (cboAdapters.DroppedDown)
            {
                listDeferred = true;   // 열린 목록을 다시 채우면 강조·선택이 어긋난다 — 닫힌 뒤에 다시 읽는다
                return;
            }
            NetworkAdapterInfo selected = SelectedAdapter();
            int index = -1;
            suppressSelectionChanged = true;
            try
            {
                cboAdapters.BeginUpdate();
                cboAdapters.Items.Clear();
                for (int k = 0; k < list.Count; k++)
                {
                    NetworkAdapterInfo a = list[k];
                    bool same = selected != null && string.Equals(a.InterfaceGuid, selected.InterfaceGuid, StringComparison.OrdinalIgnoreCase);
                    if (same)
                    {
                        // 같은 개체를 유지하되(실시간으로 읽은 CurrentMac 보존) 비교 대상 필드는 새로 읽은 값으로 맞춘다 — 안 맞추면 서명이 영원히 달라 알림마다 다시 채운다
                        selected.Description = a.Description;
                        selected.ConnectionName = a.ConnectionName;
                        selected.Kind = a.Kind;
                        selected.IsVirtual = a.IsVirtual;
                        if (a.PermanentMacHint != null) selected.PermanentMacHint = a.PermanentMacHint;
                    }
                    int i = cboAdapters.Items.Add(same ? selected : a);   // Add 전에 바꿔야 콤보가 캐시하는 표시 문자열에 새 이름·종류가 들어간다
                    if (same) index = i;
                }
                cboAdapters.EndUpdate();
                if (index >= 0) cboAdapters.SelectedIndex = index;
            }
            finally
            {
                suppressSelectionChanged = false;
            }
            if (index >= 0 || selected == null)   // 선택 유지, 또는 비어 있던 목록에 어댑터가 생겼다 (빈→빈은 위의 서명 비교에서 이미 돌아갔다)
            {
                if (selected == null) cboAdapters.SelectedIndex = 0;   // 선택 변경 → 그 어댑터의 정보를 읽는다
                SetStatus(StatusKind.Ready, "어댑터 목록이 바뀌어 다시 읽었습니다.");
                return;
            }
            bool stopped = autoRunning;   // 선택한 어댑터가 사라졌다 (뽑혔거나 사용 안 함) — 자동 변경은 멈춘다
            if (stopped) StopAuto();
            if (list.Count > 0) cboAdapters.SelectedIndex = 0;   // 선택 변경 → 그 어댑터의 정보를 읽는다
            else ClearAdapterInfo();
            SetStatus(list.Count > 0 ? StatusKind.Ready : StatusKind.Failed, "선택한 어댑터가 사라졌습니다." + (stopped ? " 자동 변경을 정지했습니다." : ""));
        }

        /// <summary>드롭다운이 열려 있는 동안 미룬 목록 갱신을 닫힌 뒤에 다시 읽는다.</summary>
        private void cboAdapters_DropDownClosed(object sender, EventArgs e)
        {
            if (!listDeferred) return;
            listDeferred = false;
            Restart(adapterTimer);
        }

        /// <summary>목록 비교용 문자열: GUID·설명·연결 이름·종류·가상 여부 (MAC 제외)</summary>
        private static string Signature(System.Collections.IList items)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                NetworkAdapterInfo a = (NetworkAdapterInfo)items[i];
                sb.Append(a.InterfaceGuid).Append('\t').Append(a.Description).Append('\t').Append(a.ConnectionName).Append('\t').Append((int)a.Kind).Append(a.IsVirtual ? 'v' : '-').Append('\n');
            }
            return sb.ToString();
        }

        private void RefreshIpFromNotification()
        {
            Interlocked.Exchange(ref refreshQueued, 0);
            if (IsDisposed) return;
            RefreshIp();
            ipTimer.Stop();                 // 1초 뒤 한 번 더 읽고(settle — 늦게 기록되는 게이트웨이·DNS), 그 다음부터 안전망 주기로 돌아간다
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
            notifyIcon.ShowBalloonTip(3000, Program.AppTitle, "백그라운드에서 계속 실행 중입니다. 트레이 아이콘을 클릭하면 창이 다시 열립니다.", ToolTipIcon.Info);
        }

        /// <summary>트레이 아이콘: 왼쪽 버튼 한 번 클릭이면 창을 다시 열고, 오른쪽이면 열기/종료 메뉴를 띄운다 (MouseUp: 빠른 두 번 클릭의 둘째 클릭도 받는다).</summary>
        private void notifyIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) RestoreFromTray();
            else if (e.Button == MouseButtons.Right)
            {
                if (trayMenu == null)   // 처음 필요할 때 만든다
                {
                    trayMenu = new TrayMenu(this);
                    trayMenu.AddItem("열기", RestoreFromTray);
                    trayMenu.AddItem("종료", Close);
                }
                trayMenu.Popup(Cursor.Position);
            }
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
            NetworkAdapterInfo current = SelectedAdapter();
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
                NetworkAdapterInfo adapter = SelectedAdapter();
                bool wireless = adapter != null && adapter.Kind == AdapterKind.Wireless;
                txtNewMac.Text = MacAddressUtil.Format(MacAddressUtil.GenerateRandom(wireless));
                SetStatus(StatusKind.Ready, "랜덤 MAC 생성 (" + (wireless ? "무선" : "유선") + "용): " + txtNewMac.Text);
            }
            catch (Exception ex)
            {
                SetStatus(StatusKind.Failed, "랜덤 생성 오류: " + ex.Message);
            }
        }

        /// <summary>선택한 어댑터. 없으면 실패 상태를 표시하고 null 을 돌려준다 (버튼 작업 공통 확인).</summary>
        private NetworkAdapterInfo RequireSelectedAdapter()
        {
            NetworkAdapterInfo adapter = SelectedAdapter();
            if (adapter == null) SetStatus(StatusKind.Failed, "어댑터를 선택하세요.");
            return adapter;
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = RequireSelectedAdapter();
            if (adapter == null) return;
            string mac = MacAddressUtil.Normalize(txtNewMac.Text);
            string invalid = mac == null ? "새 MAC은 16진수 12자리여야 합니다. (예: 02-1A-2B-3C-4D-5E)"
                : !MacAddressUtil.IsUnicast(mac) ? "두 번째 자리는 짝수여야 합니다. (홀수는 멀티캐스트 주소)"
                : MacAddressUtil.IsAllZero(mac) ? "00-00-00-00-00-00은 사용할 수 없습니다."   // FF-FF-…는 두 번째 자리가 홀수라 위에서 이미 걸러진다
                : null;
            if (invalid != null)
            {
                SetStatus(StatusKind.Failed, invalid);
                txtNewMac.Focus();
                return;
            }
            if (MacAddressUtil.Normalize(txtCurrentMac.Text) == mac)   // Normalize 는 대문자 12자리(또는 null)를 돌려주고 mac 은 null 이 아니다
            {
                SetStatus(StatusKind.Ready, "새 MAC이 현재 MAC과 같습니다. 변경할 내용이 없습니다.");
                return;
            }

            // 확인 대화 상자 없이 바로 적용한다.
            OperationArgs args = new OperationArgs(OperationKind.Apply, adapter);
            args.NewMac = mac;
            StartOperation(args);
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = RequireSelectedAdapter();
            if (adapter == null) return;
            // 이미 공장 MAC이고 레지스트리에 NetworkAddress 값도 없으면 복구할 것이 없다 (변경 적용의 "같은 MAC" 안내와 같은 방식).
            string currentNormalized = MacAddressUtil.Normalize(txtCurrentMac.Text);
            string permanentNormalized = MacAddressUtil.Normalize(txtPermanentMac.Text);
            if (currentNormalized != null && currentNormalized == permanentNormalized && !HasNetworkAddressOverride(adapter))   // 둘 다 Normalize 결과(대문자 12자리 또는 null)라 == 로 충분하다 — 공장 MAC 이 null 이면 같지 않다
            {
                SetStatus(StatusKind.Ready, "현재 MAC이 이미 원래(공장) MAC과 같습니다. 복구할 내용이 없습니다.");
                return;
            }
            // 네 작업 가운데 원상복구만 확인 대화 상자를 거친다 (변경 적용·IP 갱신·IP 반환은 바로 실행; 자동 변경 실행 중에는 이 버튼이 잠겨 있다).
            string question = "현재 MAC " + txtCurrentMac.Text + " 을(를) 원래(공장) MAC " + txtPermanentMac.Text
                + " 으로 되돌립니다.\r\n어댑터가 재시작됩니다. 계속할까요?";
            if (MessageBox.Show(this, question, Program.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                SetStatus(StatusKind.Ready, "원상복구를 취소했습니다.");
                return;
            }
            StartOperation(new OperationArgs(OperationKind.Restore, adapter));
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

        /// <summary>IP 갱신·즉시 반환 버튼 공통 (누른 버튼으로 작업을 고른다)</summary>
        private void btnIp_Click(object sender, EventArgs e)
        {
            NetworkAdapterInfo adapter = RequireSelectedAdapter();
            if (adapter != null) StartOperation(new OperationArgs(sender == btnReleaseIp ? OperationKind.ReleaseIp : OperationKind.RenewIp, adapter));
        }

        private void chkIpLog_CheckedChanged(object sender, EventArgs e)
        {
            // 켜는 순간에는 아무것도 쓰지 않는다 — 이후 새로 받는 IP 부터 ApplyIpInfo 가 기록한다
            SetStatus(StatusKind.Ready, chkIpLog.Checked ? "지금부터 새로 받는 IP를 실행 파일 옆 " + IpMonitor.LogFileName + " 에 저장합니다." : "할당 IP 로그 파일 저장을 껐습니다.");
        }

        /// <summary>로그 보기에 맞춘다: 박스는 늘 보이되 꺼져 있으면 흐리게(비활성) 표시하고 기록하지 않는다. 켜는 순간에는 쓰지 않고 이후 새로 받는 IP 부터 기록한다. 로그 비우기는 언제나 된다.</summary>
        private void chkLogBox_CheckedChanged(object sender, EventArgs e)
        {
            txtIpLog.Enabled = chkLogBox.Checked;
        }

        private void chkAuto_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkAuto.Checked && autoRunning) StopAuto();
            UpdateControlStates();   // 시작 버튼과 고른 방식의 설정 칸은 체크해야 열린다
            if (chkAuto.Checked && !busy && !autoRunning) SetStatus(StatusKind.Ready, "방식과 시간을 정한 뒤 '시작'을 누르면 자동 변경이 시작됩니다.");   // 체크만으로 시작되지 않는다는 안내
        }

        private void chkAutoStopIp_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkAutoStopIp.Checked) txtAutoStopIp.Text = string.Empty;   // 체크를 풀면 입력한 IP/대역은 지운다
            UpdateControlStates();   // 지정 IP 칸은 체크해야 열린다
        }

        /// <summary>스위치 양쪽 글자를 눌러도 그쪽 방식이 된다 (실행 중에는 스위치처럼 잠긴다)</summary>
        private void lblMode_Click(object sender, EventArgs e)
        {
            if (modeSwitch.Enabled) modeSwitch.Checked = sender == lblModeTimer;
        }

        private void modeSwitch_CheckedChanged(object sender, EventArgs e)
        {
            ApplyMode();
            UpdateControlStates();   // 고른 방식의 칸만 열린다
            if (!busy) SetStatus(StatusKind.Ready, IntervalMode() ? "자동 변경 방식: 일정 시간마다 (IP와 관계없이)" : "자동 변경 방식: IP 할당 시 (IP를 받은 뒤 지연 초가 지나면)");
        }

        /// <summary>제한 스위치 양쪽 글자를 눌러도 그쪽 기준이 된다 (실행 중이거나 "사용"이 꺼져 있으면 스위치처럼 잠긴다)</summary>
        private void lblLimit_Click(object sender, EventArgs e)
        {
            if (limitSwitch.Enabled) limitSwitch.Checked = sender == lblLimitCount;
        }

        private void chkAutoLimit_CheckedChanged(object sender, EventArgs e)
        {
            UpdateControlStates();   // 제한 값 칸은 체크해야 열린다
        }

        private void limitSwitch_CheckedChanged(object sender, EventArgs e)
        {
            ApplyMode();   // 고른 기준의 값 칸만 보인다
            UpdateControlStates();
        }

        /// <summary>고른 방식에 맞춘 표시: 스위치 양쪽 글자는 고른 쪽만 검정, 설정 줄은 고른 방식의 것만 보이고, IP 할당 시 방식에만 있는 할당 대기 줄은 다른 방식에서 회색.</summary>
        private void ApplyMode()
        {
            bool interval = IntervalMode();
            Color on = SystemColors.ControlText, off = SystemColors.GrayText;
            Color ipColor = interval ? off : on;   // IP 할당 시 쪽 글자: 스위치 왼쪽 글자와 할당 대기 줄
            lblModeIp.ForeColor = ipColor;
            lblModeTimer.ForeColor = interval ? on : off;
            // 한 줄의 칸들은 오른쪽 칸부터 바꾼다 (Visible 은 바꿀 때마다 다시 배치하고 포커스를 옮길 수도 있어 순서를 바꾸지 않는다)
            lblAutoSuffix.Visible = !interval;
            nudAutoDelay.Visible = !interval;
            lblDelayPrefix.Visible = !interval;
            lblIntervalSuffix.Visible = interval;
            nudInterval.Visible = interval;
            lblIntervalPrefix.Visible = interval;
            lblWaitSuffix.ForeColor = ipColor;
            lblWaitPrefix.ForeColor = ipColor;
            lblWait.ForeColor = ipColor;
            bool count = limitSwitch.Checked;   // 자동 정지 제한: 시간 ↔ 횟수
            lblLimitTime.ForeColor = count ? off : on;
            lblLimitCount.ForeColor = count ? on : off;
            lblLimitTimeSuffix.Visible = !count;
            nudLimitTime.Visible = !count;
            lblLimitCountSuffix.Visible = count;
            nudLimitCount.Visible = count;
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
                SetStatus(StatusKind.Failed, "자동 정지 IP/대역 형식 오류 — 예: 123, 123.123, 123.123.123, 123.123.123.123");
                txtAutoStopIp.Focus();
                return;
            }
            waiting = false;   // 할당 대기 시간은 시작 시점부터 센다
            autoRunning = true;
            intervalRun = IntervalMode();
            autoChanges = 0;
            operationIsAuto = false;   // 시작을 누를 때 돌고 있던 작업(직접 작업이든 이전 실행의 자동 변경이든)은 이번 실행의 횟수에 세지 않는다
            limitNote = null;
            limitRun = chkAutoLimit.Checked;
            limitByCount = limitSwitch.Checked;
            UpdateControlStates();   // 실행 중에는 정지·로그 비우기·백그라운드 실행 외의 조작을 잠근다
            string limit = !limitRun ? "" : limitByCount ? " · " + nudLimitCount.Value + "번 바꾸면 정지" : " · " + FormatRemaining((int)nudLimitTime.Value) + " 뒤 정지";
            if (limitRun && !limitByCount)
            {
                // 시간 제한: 지금부터 정한 초 뒤 마감 — 전용 일회성 타이머가 그 시각에 깨운다 (다른 타이머의 위상·주기와 무관)
                limitDeadline = Stopwatch.GetTimestamp() + (long)nudLimitTime.Value * Stopwatch.Frequency;
                ArmLimitTimer();
            }
            if (intervalRun)
            {
                // 일정 시간마다: IP 와 관계없이 지금부터 N초 뒤에 첫 변경, 그 뒤로는 직전 변경 예정 시각 + N초 (변경에 걸린 시간과 무관하게 N초마다; 작업 중이면 끝나는 대로)
                int seconds = (int)nudInterval.Value;
                autoDueTick = Environment.TickCount;   // 작업 중에 시작했으면 예약은 작업이 끝날 때 걸리고, 그 기준점은 지금
                ScheduleIn(seconds);
                if (!busy) SetStatus(StatusKind.Ready, "자동 변경 시작 — IP와 관계없이 " + seconds + "초마다 새 MAC 적용" + limit);
            }
            else
            {
                btnAutoToggle.Text = "정지 (IP 대기)";
                if (!busy) SetStatus(StatusKind.Ready, "자동 변경 시작 — IP 할당 " + nudAutoDelay.Value + "초 뒤, 또는 할당 대기 " + nudWaitTimeout.Value + "초 초과 시 새 MAC 적용" + limit);
            }
            RefreshIp();   // 지금 상태를 읽어 ApplyIpInfo 가 결정한다: (IP 할당 시) 이미 IP가 있으면 지금부터 세고, 링크만 올라와 있으면 할당 대기 시계가 이 순간부터 센다; 지정 IP 가 이미 할당되어 있으면 두 방식 모두 바로 정지
        }

        private void StopAuto()
        {
            autoRunning = false;
            limitTimer.Stop();
            CancelAuto();
            UpdateControlStates();
            btnAutoToggle.Text = "시작";
            if (!busy) SetStatus(StatusKind.Ready, "자동 변경 정지");
        }

        /// <summary>
        /// 시간 제한 타이머를 마감까지 건다. WinForms 타이머는 24.8일(int 밀리초)까지만 잡히므로 그보다 멀면 거기까지 걸고 깨어나서 남은 시간으로 다시 건다.
        /// 마감이 이미 지났으면 바로 멈춘다 (작업 중이면 그 작업의 완료 메시지에 붙여 알린다).
        /// </summary>
        private void ArmLimitTimer()
        {
            long left = limitDeadline - Stopwatch.GetTimestamp();
            if (left <= 0)
            {
                StopAuto();
                string message = "정한 시간(" + FormatRemaining((int)nudLimitTime.Value) + ")이 지나 자동 변경을 정지했습니다.";
                if (busy) limitNote = " " + message;
                else SetStatus(StatusKind.Done, message);
                return;
            }
            long ms = left / Stopwatch.Frequency * 1000 + left % Stopwatch.Frequency * 1000 / Stopwatch.Frequency;   // 1년치도 넘치지 않게 초·나머지로 나눠 계산
            limitTimer.Interval = (int)Math.Min(Math.Max(ms, 1), int.MaxValue);
            limitTimer.Start();
        }

        private void limitTimer_Tick(object sender, EventArgs e)
        {
            limitTimer.Stop();
            if (autoRunning && limitRun && !limitByCount) ArmLimitTimer();
        }

        /// <summary>(IP 할당 시) 자동 변경 중이고 어댑터에 IP가 할당되어 있으면 지연 시간 뒤로 변경을 예약하고 남은 시간을 바로 버튼에 보여준다 (0초면 다음 1초 확인 때 바로).</summary>
        private void ScheduleAuto()
        {
            if (!autoRunning || busy || lastLoggedIp == null || intervalRun) return;
            ScheduleIn((int)nudAutoDelay.Value);
        }

        /// <summary>seconds 초 뒤로 변경을 예약하고 남은 시간을 버튼에 보여준다 (두 방식 공통).</summary>
        private void ScheduleIn(int seconds)
        {
            ScheduleUntil(unchecked(Environment.TickCount + seconds * 1000));
        }

        /// <summary>dueTick(Environment.TickCount 기준)에 변경을 예약하고 남은 시간을 버튼에 보여준다. 이미 지난 시각이면 다음 1초 확인 때 바로.
        /// 작업 중이면 예약하지 않는다 — 작업이 끝날 때 완료 처리가 다시 부른다.</summary>
        private void ScheduleUntil(int dueTick)
        {
            if (busy)
            {
                btnAutoToggle.Text = "정지";
                return;
            }
            autoPending = true;
            autoDueTick = dueTick;
            int remainingMs = unchecked(dueTick - Environment.TickCount);
            if (remainingMs > 0)
            {
                // 1초 타이머의 위상을 지금에 맞춘다: 다음 틱이 정확히 1초 뒤에 오므로 숫자마다 1초씩 표시되고 변경은 정확히 N초 뒤에 시작된다
                Restart(secondTimer);
                ShowCountdown(remainingMs);
            }
            else btnAutoToggle.Text = "정지";
        }

        /// <summary>남은 시간(올림한 초)을 정지 버튼에 보여준다.</summary>
        private void ShowCountdown(int remainingMs)
        {
            btnAutoToggle.Text = "정지 (" + FormatRemaining((remainingMs + 999) / 1000) + ")";
        }

        /// <summary>예약된 변경을 취소한다. 카운트다운을 보여주던 중이었다면 버튼을 "IP 대기"로 되돌린다 (할당 대기 숫자가 보이는 중이면 건드리지 않는다).
        /// 일정 시간마다 방식으로 실행 중일 때는 IP 가 사라져도 예약을 유지한다 (정지는 StopAuto 가 autoRunning 을 먼저 끈다).</summary>
        private void CancelAuto()
        {
            if (autoRunning && intervalRun) return;
            if (autoPending && autoRunning) btnAutoToggle.Text = "정지 (IP 대기)";
            autoPending = false;
        }

        /// <summary>예약 시각이 지났으면 어댑터 종류에 맞는 랜덤 MAC을 만들어 바로 적용한다. 아직이면 남은 시간을 정지 버튼에 보여준다.</summary>
        private void RunAutoIfDue()
        {
            if (!autoPending || !autoRunning || busy) return;
            int remainingMs = unchecked(autoDueTick - Environment.TickCount);   // TickCount 가 한 바퀴 돌아도 차이는 올바르다
            if (remainingMs > 0)
            {
                ShowCountdown(remainingMs);
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
            NetworkAdapterInfo adapter = SelectedAdapter();
            if (adapter == null)
            {
                StopAuto();   // 어댑터 목록이 비어 있으면 (일정 시간마다 방식) 멈춘 채 "정지" 로 남지 않도록 정지한다
                SetStatus(StatusKind.Failed, "어댑터를 선택하세요.");
                return;
            }
            btnAutoToggle.Text = "정지";
            string mac;
            try
            {
                mac = MacAddressUtil.GenerateRandom(adapter.Kind == AdapterKind.Wireless);
            }
            catch (Exception ex)
            {
                StopAuto();
                SetStatus(StatusKind.Failed, "자동 변경 중단 — 랜덤 생성 오류: " + ex.Message);
                return;
            }
            txtNewMac.Text = MacAddressUtil.Format(mac);
            OperationArgs args = new OperationArgs(OperationKind.Apply, adapter);
            args.NewMac = mac;
            args.Reason = reason;
            args.Auto = true;
            StartOperation(args);
        }

        /// <summary>
        /// 할당 대기 상태를 추적한다. 대기 중이면 시작 시각을 기억하고, 자동 변경 중에 설정한 할당 대기 초를 넘겼으면 true 를 돌려준다 (호출자가 새 MAC 적용).
        /// 작업 중에는 세지 않는다. 버튼의 경과 초는 render 일 때(1초 틱)와 대기가 시작되는 순간("0초")에만 쓴다 —
        /// 틱 사이에 온 IP 조회 결과가 숫자를 먼저 올리면 다음 틱까지의 간격이 들쭉날쭉해 보이기 때문.
        /// </summary>
        private bool TrackWaiting(bool isWaiting, int nowTick, bool render)
        {
            if (autoRunning && intervalRun) return false;   // 일정 시간마다 방식에서는 할당 대기를 세지도, 보여주지도 않는다
            if (!isWaiting || busy)
            {
                if (waiting && autoRunning && !busy) btnAutoToggle.Text = "정지 (IP 대기)";   // 링크가 끊겨 대기가 끝났다 — 숫자를 지운다 (IP 를 받은 경우는 곧 예약 표시가 덮는다)
                waiting = false;
                return false;
            }
            if (!waiting)
            {
                waiting = true;
                waitingSinceTick = nowTick;
                render = true;
                if (autoRunning)
                {
                    // 1초 타이머의 위상을 대기 시작 시각에 맞춘다: 다음 틱이 정확히 1초 뒤에 와서 0초→1초도 다른 숫자처럼 1초 만에 바뀌고(맞추지 않으면 첫 숫자만 1~2초),
                    // 할당 대기 초과도 정확히 N초에 판정된다. 그래프는 graphTimer 가 따로 돌므로 영향 없다
                    Restart(secondTimer);
                }
            }
            if (!autoRunning) return false;
            int elapsedMs = unchecked(nowTick - waitingSinceTick);
            if (render) btnAutoToggle.Text = "정지 (IP 대기 " + elapsedMs / 1000 + "초)";
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
            SetStatus(StatusKind.Done, StopTargetLabel(prefix) + " IP " + matched + " 가 할당되어 자동 변경을 정지했습니다.");
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
        }

        /// <summary>저장 옵션이 켜져 있으면 파일에 한 줄 추가한다. 실패하면 옵션을 끄고 오류를 상태 줄에 표시한다.</summary>
        private void AppendToFile(string line)
        {
            if (!chkIpLog.Checked) return;
            try
            {
                IpMonitor.AppendLine(Application.ExecutablePath, line);
            }
            catch (Exception ex)
            {
                chkIpLog.Checked = false;   // CheckedChanged 가 먼저 '껐습니다' 상태를 쓰고, 오류는 그 뒤에 표시한다
                SetStatus(StatusKind.Failed, "IP 로그 저장 실패: " + ex.Message);
            }
        }

        /// <summary>안전망 폴링(알림 직후에는 settle 재조회): 알림을 놓쳤을 때를 대비해 IP 구성을 다시 읽는다 (알림을 못 쓰는 OS 에서는 이것이 유일한 갱신 경로).</summary>
        private void ipTimer_Tick(object sender, EventArgs e)
        {
            RefreshIp();
            if (ipNotifier != null && ipTimer.Interval != IpSafetyPollMs)
            {
                ipTimer.Stop();
                ipTimer.Interval = IpSafetyPollMs;
                ipTimer.Start();
            }
        }

        /// <summary>
        /// 1초마다 선택한 어댑터의 송수신 카운터를 읽어 그래프에 한 샘플을 넣는다(어댑터가 내려가 있으면 속도 0).
        /// 이 타이머는 시작한 뒤 어디서도 멈추거나 재시작하지 않는다 — 재시작하면 그 간격 하나가 늘어나 그래프가 멈칫한다.
        /// </summary>
        private void graphTimer_Tick(object sender, EventArgs e)
        {
            if (SelectedAdapter() == null) return;
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

        /// <summary>
        /// 1초마다 자동 변경 카운트다운·할당 대기 시계의 숫자를 올려 예약 시각이 되면 변경을 시작한다.
        /// ScheduleUntil(예약 시각)과 TrackWaiting(할당 대기 시작 시각)이 이 타이머만 재시작한다 (그래프는 graphTimer 가 따로 돌므로 영향 없음).
        /// </summary>
        private void secondTimer_Tick(object sender, EventArgs e)
        {
            RunAutoIfDue();
            // 할당 대기 시계는 IP 를 다시 읽지 않고도 1초마다 센다 (대기 상태는 마지막 IP 조회가 정한다); 숫자는 여기서만 올린다
            if (waiting && autoRunning && !busy && TrackWaiting(true, Environment.TickCount, true)) StartWaitTimeoutChange();
        }

        // ------------------------------------------------------------------
        // 어댑터 목록 / 정보 갱신
        // ------------------------------------------------------------------
        private void LoadAdapters(string guidToSelect)
        {
            listEpoch++;   // 작업자 스레드가 이보다 먼저 읽기 시작한 목록은 버린다 (이쪽이 최신)
            Cursor = Cursors.WaitCursor;
            try
            {
                SetStatus(StatusKind.Progress, "어댑터 목록을 읽는 중...");
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
                    for (int k = 0; k < adapters.Count; k++)
                    {
                        NetworkAdapterInfo a = adapters[k];
                        int idx = cboAdapters.Items.Add(a);
                        if (guidToSelect != null && string.Equals(a.InterfaceGuid, guidToSelect, StringComparison.OrdinalIgnoreCase))
                            selectIndex = idx;
                    }
                    cboAdapters.EndUpdate();
                    if (adapters.Count > 0)   // 드롭다운에는 adapters 가 모두 들어 있다
                        cboAdapters.SelectedIndex = selectIndex >= 0 ? selectIndex : 0;
                }
                finally
                {
                    suppressSelectionChanged = false;
                }

                if (adapters.Count == 0)
                {
                    ClearAdapterInfo();
                    SetStatus(StatusKind.Failed, enumError ?? "네트워크 어댑터를 찾지 못했습니다.");
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
            NetworkAdapterInfo adapter = SelectedAdapter();
            if (adapter == null) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                SetStatus(StatusKind.Progress, "어댑터 정보를 읽는 중...");
                lblStatus.Update();
                string guid = adapter.InterfaceGuid;
                if (!string.Equals(guid, lastInfoGuid, StringComparison.OrdinalIgnoreCase))
                {
                    lastInfoGuid = guid;
                    lastLoggedIp = null;   // 다른 어댑터를 골랐을 때만 기준 초기화 (단순 새로 고침은 중복 기록하지 않음)
                    CancelAuto();          // 이전 어댑터의 예약이 새 어댑터에 적용되지 않도록
                }
                if (!string.Equals(guid, traffic.InterfaceGuid, StringComparison.OrdinalIgnoreCase))
                {
                    traffic.Attach(guid);   // 그래프와 누적량은 어댑터별 — 다른 어댑터를 고르면 새로 시작
                    trafficGraph.Clear();
                }

                string permanent = MacChangeService.ReadPermanentMac(adapter);
                txtPermanentMac.Text = permanent != null ? MacAddressUtil.Format(permanent) : "(조회 실패)";

                string current = MacChangeService.ReadCurrentMac(adapter);
                txtCurrentMac.Text = current != null ? MacAddressUtil.Format(current) : "(조회 실패)";
                ShowCurrentMac(adapter, current);

                if (current == null)
                    SetStatus(StatusKind.Failed, "현재 MAC을 읽지 못했습니다. (어댑터 비활성 또는 드라이버 거부)");
                else
                    SetStatus(StatusKind.Ready, permanent == null ? "공장 MAC은 읽지 못했습니다(드라이버 미지원). 현재 MAC만 표시합니다." : "어댑터 정보를 읽었습니다.");

                RefreshIp();
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

        /// <summary>어댑터가 하나도 없을 때: MAC·IP 칸과 그래프·누적량을 비운다 (목록 새로 고침과 목록 자동 갱신 공통).</summary>
        private void ClearAdapterInfo()
        {
            txtPermanentMac.Text = txtCurrentMac.Text = string.Empty;
            ClearIpBoxes();
            traffic.Attach(null);
            trafficGraph.Clear();
        }

        private void ClearIpBoxes()
        {
            txtCurrentIp.Text = txtMask.Text = txtGateway.Text = txtDns.Text = string.Empty;
        }

        /// <summary>현재 MAC을 모델과 드롭다운 항목 텍스트에 반영한다 (ComboBox는 Add 시점의 문자열을 캐시하므로 항목을 다시 넣어야 한다).</summary>
        private void ShowCurrentMac(NetworkAdapterInfo adapter, string current)
        {
            if (current == null) return;
            SetIfChanged(txtCurrentMac, MacAddressUtil.Format(current));   // 칸은 마지막으로 읽은 값을 늘 보여준다 — "(조회 실패)" 뒤에 같은 MAC 이 다시 읽혀도
            if (string.Equals(adapter.CurrentMac, current, StringComparison.OrdinalIgnoreCase)) return;
            adapter.CurrentMac = current;
            int i = cboAdapters.SelectedIndex;
            if (i < 0) return;
            suppressSelectionChanged = true;
            try { cboAdapters.Items[i] = adapter; }
            finally { suppressSelectionChanged = false; }
        }

        /// <summary>
        /// 선택한 어댑터의 IP 구성을 작업자 스레드에서 읽어 ApplyIpInfo 로 넘긴다 — 어댑터 전환 중에는 조회가 수백 ms 걸릴 수 있어
        /// UI 스레드에서 읽으면 1초 타이머(카운트다운·할당 대기 시계)가 밀린다. 조회 중에 다시 부르면 끝난 뒤 한 번 더 읽는다.
        /// </summary>
        private void RefreshIp()
        {
            NetworkAdapterInfo adapter = SelectedAdapter();
            if (adapter == null)
            {
                ClearIpBoxes();
                return;
            }
            if (ipQueryRunning)
            {
                ipQueryAgain = true;
                return;
            }
            ipQueryRunning = true;
            ipQueryGuid = adapter.InterfaceGuid;
            ipQueryEpoch = ipEpoch;
            ThreadPool.QueueUserWorkItem(delegate
            {
                IpMonitor.IpInfo info = null;
                bool failed = false;
                try { info = IpMonitor.ReadIpInfo(ipQueryGuid); }
                catch (Exception) { failed = true; }
                try { BeginInvoke(new Action<string, IpMonitor.IpInfo, bool, int>(ApplyIpInfo), ipQueryGuid, info, failed, ipQueryEpoch); }
                catch (Exception) { }   // 창이 이미 닫혔다
            });
        }

        /// <summary>읽어 온 IP 구성을 표시하고, 로그 옵션이 켜져 있으면 새로 할당된 IP를 파일에 기록한다 (UI 스레드).</summary>
        private void ApplyIpInfo(string guid, IpMonitor.IpInfo info, bool failed, int epoch)
        {
            ipQueryRunning = false;
            if (ipQueryAgain)
            {
                ipQueryAgain = false;
                RefreshIp();
            }
            NetworkAdapterInfo adapter = SelectedAdapter();
            if (adapter == null || adapter.InterfaceGuid != guid || epoch != ipEpoch) return;   // 그사이 다른 어댑터를 골랐거나 작업이 시작·완료됐다
            if (failed)
            {
                ClearIpBoxes();
                txtCurrentIp.Text = "(IP 조회 실패)";
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
            bool linked = ip != null && up;   // 어댑터가 IP 스택에 있고 링크가 올라와 있다
            bool shown = linked && ip.Length > 0;
            SetIfChanged(txtCurrentIp, IpDisplayText(ip, up, assigned));
            SetIfChanged(txtMask, !shown ? string.Empty : assigned.Length > 0 ? assignedMasks : info.Masks);
            SetIfChanged(txtGateway, !linked ? string.Empty : info.Gateways.Length > 0 ? info.Gateways : "(없음)");
            SetIfChanged(txtDns, !linked ? string.Empty : info.Dns.Length > 0 ? info.Dns : "(없음)");

            if (!shown)
            {
                lastLoggedIp = null;   // 어댑터가 내려갔거나 링크가 끊긴 경우만 기준 초기화 (다시 같은 IP를 받아도 새 할당으로 기록)
                CancelAuto();          // IP가 사라지면 예약도 취소 (다시 받으면 새로 센다)
                // 링크는 올라왔는데 IP가 없으면 "할당 대기" — 설정한 초를 넘기면 새 MAC (어댑터가 꺼졌거나 링크가 없으면 세지 않는다)
                if (TrackWaiting(linked, Environment.TickCount, false)) StartWaitTimeoutChange();
                return;
            }
            if (assigned.Length == 0)
            {
                CancelAuto();   // 임대가 빠져 169.254 만 남은 동안은 변경하지 않는다 (기준 IP 는 유지 — 같은 IP가 돌아오면 중복 기록하지 않음)
                if (TrackWaiting(true, Environment.TickCount, false)) StartWaitTimeoutChange();   // 자동 사설 주소만 있는 것도 할당 대기
                return;
            }
            TrackWaiting(false, 0, false);   // 실제 IP를 받았으면 대기 종료
            // 지정 IP 자동 정지: 자동 변경 중 대상 IP가 할당되면 바로 멈춘다 (로그 중복 판정과 상관없이 매번 확인, IP 줄은 아래에서 평소처럼 기록)
            AutoStopIfTargetAssigned(assigned);
            // 작업 중(busy)에는 기록·예약하지 않는다: 어댑터가 올라온 직후의 IP는 작업 완료 후 새 MAC과 함께 기록된다.
            if (busy) return;
            bool fresh = assigned != lastLoggedIp;
            if (fresh)
            {
                lastLoggedIp = assigned;
                string line = IpMonitor.BuildLogLine(assigned, assignedMasks, info.Gateways, adapter.CurrentMac,
                                                     chkLogTime.Checked, chkLogMask.Checked, chkLogGateway.Checked, chkLogMac.Checked);   // 박스와 파일에 같은 줄
                if (chkLogBox.Checked) txtIpLog.AppendText(line + Environment.NewLine);   // 꺼져 있는 동안에는 박스에 기록하지 않음
                AppendToFile(line);
            }
            // 새로 할당된 IP, 또는 자동 변경을 켠 뒤(예약이 아직 없을 때) 처음 확인한 IP → 지연 시간 뒤로 변경 예약 (자동 변경이 꺼져 있으면 ScheduleAuto 가 그냥 돌아온다)
            if (!(autoRunning && intervalRun) && (fresh || !autoPending))   // 일정 시간마다 방식으로 실행 중이면 IP 는 예약과 무관하다
            {
                ScheduleAuto();
                if (autoPending && (int)nudAutoDelay.Value == 0) RunAutoIfDue();   // 0초면 다음 타이머 틱을 기다리지 않고 바로 바꾼다
            }
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
            args.ReleaseIp = chkReleaseIp.Checked;
            operationReason = args.Reason;
            operationIsAuto = args.Auto;
            ipEpoch++;
            busy = true;
            UpdateControlStates();
            SetStatus(StatusKind.Progress, ReasonPrefix() + OperationLabel(args.Kind) + " 시작...");
            worker.RunWorkerAsync(args);
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            OperationArgs args = (OperationArgs)e.Argument;
            Action<string> log = delegate(string message) { worker.ReportProgress(0, message); };   // sender 는 늘 worker 다
            switch (args.Kind)
            {
                case OperationKind.Apply: e.Result = MacChangeService.Apply(args.Adapter, args.NewMac, args.ResetDns, args.ReleaseIp, log); break;
                case OperationKind.Restore: e.Result = MacChangeService.Restore(args.Adapter, args.ResetDns, args.ReleaseIp, log); break;
                case OperationKind.ReleaseIp: e.Result = MacChangeService.ReleaseIp(args.Adapter, log); break;
                default: e.Result = MacChangeService.RenewIp(args.Adapter, args.ResetDns, args.ReleaseIp, log); break;
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
                case OperationKind.ReleaseIp: return "IP 반환";
                default: return "IP 갱신";
            }
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            string message = e.UserState as string;
            if (message != null) SetStatus(StatusKind.Progress, message);
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ipEpoch++;
            busy = false;
            UpdateControlStates();
            if (listDirty)   // 작업 중에 인터페이스 알림이 왔다 (대개 우리가 재시작한 어댑터) — 잠시 뒤 목록을 읽어 바뀐 것이 있는지 본다
            {
                listDirty = false;
                adapterTimer.Start();
            }

            MacChangeResult result = e.Error == null ? e.Result as MacChangeResult : null;
            bool ok = result != null && result.Success, reboot = result != null && result.RebootRequired;
            string finalMessage = ReasonPrefix() + (e.Error != null ? e.Error.Message : result != null ? result.Message : "결과를 받지 못했습니다.");

            // 실패했거나 재부팅이 필요하면 자동 변경을 멈춘다 (같은 실패를 반복하거나 대화 상자가 겹치지 않도록)
            if (autoRunning && (!ok || reboot))
            {
                StopAuto();
                finalMessage += " 자동 변경을 정지했습니다.";
            }
            else if (autoRunning)
            {
                if (operationIsAuto) autoChanges++;
                if (limitRun && limitByCount && autoChanges >= (int)nudLimitCount.Value)   // 횟수 제한: 정한 횟수를 채웠으면 다음 변경을 예약하지 않고 멈춘다
                {
                    StopAuto();
                    finalMessage += " 새 MAC을 " + autoChanges + "번 적용해 자동 변경을 정지했습니다.";
                }
                else if (intervalRun)
                {
                    // 직전 변경 예정 시각 + N초: 변경에 걸린 시간과 무관하게 N초마다. 변경이 N초보다 오래 걸려 그 시각이 이미 지났으면 지금 바로 한 번만 바꾸고
                    // 격자는 지금부터 다시 센다 (놓친 격자점을 연달아 몰아서 실행하지 않는다)
                    int due = unchecked(autoDueTick + (int)nudInterval.Value * 1000), now = Environment.TickCount;
                    if (unchecked(due - now) < 0) due = now;
                    ScheduleUntil(due);
                }
                else btnAutoToggle.Text = "정지 (IP 대기)";   // 작업 중 "정지" 를 되돌린다 — 다음 IP 조회 결과가 "IP 대기 N초"/카운트다운으로 덮는다
            }

            // 어댑터가 실제로 중지되었다면 이후 받는 IP는 같은 값이라도 새 할당이므로 새 MAC과 함께 기록한다.
            if (result != null && result.AdapterRestarted) lastLoggedIp = null;
            // 작업(MAC 변경·원상복구·IP 갱신)이 끝나면 그래프는 그대로 두고 누적 송수신량만 0부터 다시 센다 (TMAC 과 같은 동작).
            if (ok && (result.AdapterRestarted || reboot))   // IP 반환은 어댑터를 거치지 않으므로(둘 다 false) 제외된다
            {
                traffic.ResetTotals();
                trafficGraph.ResetTotals();   // 다음 샘플을 기다리지 않고 바로 0으로 표시
            }

            // 변경/복구 후의 현재 MAC 은 작업자 스레드가 이미 읽어 결과에 담아 왔다 — UI 스레드에서 NDIS/WMI 를 다시 읽지 않는다 (느린 폴백이 틱을 밀 수 있다).
            // IP(와 어댑터가 올라와 있으면 MAC)는 작업자 스레드에서 다시 읽혀 곧 반영된다.
            NetworkAdapterInfo adapter = SelectedAdapter();
            if (adapter != null)
            {
                if (result != null)
                {
                    if (result.CurrentMac != null) ShowCurrentMac(adapter, result.CurrentMac);
                    else if (result.AdapterRestarted && !ok) txtCurrentMac.Text = "(조회 실패)";   // 어댑터를 멈췄는데 현재 MAC 을 읽지 못함 — 작업 전 값을 현재 값처럼 남기지 않는다
                    if (result.PermanentMac != null) SetIfChanged(txtPermanentMac, MacAddressUtil.Format(result.PermanentMac));
                }
                RefreshIp();
            }
            if (limitNote != null)   // 작업 중에 정한 시간이 다 돼 멈췄다
            {
                finalMessage += limitNote;
                limitNote = null;
            }
            SetStatus(ok ? StatusKind.Done : StatusKind.Failed, finalMessage);

            if (!ok || reboot) RestoreFromTray();   // 숨긴 채로 대화 상자를 띄우지 않는다
            if (reboot)
            {
                MessageBox.Show(this, result.Message + "\r\n\r\n지금 재부팅하거나, 장치 관리자에서 어댑터를 '사용 안 함' → '사용'으로 직접 재시작하세요.",
                    Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (!ok)
            {
                string text = finalMessage;
                if (result != null && !string.IsNullOrEmpty(result.Guidance)) text += "\r\n\r\n" + result.Guidance;
                MessageBox.Show(this, text, Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------------
        // UI 헬퍼
        // ------------------------------------------------------------------
        /// <summary>
        /// 작업 중(busy)과 자동 변경 실행 중(autoRunning)에 맞춰 컨트롤 사용 가능 여부를 한곳에서 맞춘다.
        /// 자동 변경 실행 중에는 정지(btnAutoToggle), 로그 비우기, 백그라운드 실행 외의 버튼·체크박스·어댑터 선택·입력 칸을 모두 잠근다.
        /// </summary>
        private void UpdateControlStates()
        {
            bool stopped = !autoRunning;   // 자동 변경이 실행 중이 아니다
            bool idle = stopped && !busy;
            cboAdapters.Enabled = idle;
            btnRefresh.Enabled = idle;
            btnRandom.Enabled = idle;
            btnApply.Enabled = idle;
            btnRestore.Enabled = idle;
            btnRenewIp.Enabled = idle;
            btnReleaseIp.Enabled = idle;
            chkIpLog.Enabled = idle;
            txtNewMac.Enabled = idle;   // 실행 중 입력해도 다음 자동 변경이 덮어쓰므로 함께 잠근다 (포커스가 정지 버튼으로 튀는 것도 막는다)
            chkAuto.Enabled = stopped;
            chkLogTime.Enabled = stopped;
            chkLogMask.Enabled = stopped;
            chkLogGateway.Enabled = stopped;
            chkLogMac.Enabled = stopped;
            chkLogBox.Enabled = stopped;
            btnAutoToggle.Enabled = chkAuto.Checked;                     // 실행 중에도 정지할 수 있어야 하므로 잠그지 않는다
            bool armed = stopped && chkAuto.Checked;                     // "사용"을 체크하고 실행 중이 아닐 때만 방식·제한 스위치와 설정 칸이 열린다
            // 아래 세 칸은 이 순서로 바꾼다 (포커스가 있는 칸을 잠그면 포커스가 다음 칸으로 옮겨 가므로, 순서가 바뀌면 포커스가 가는 곳이 달라질 수 있다)
            chkAutoLimit.Enabled = armed;
            limitSwitch.Enabled = armed;
            modeSwitch.Enabled = armed;
            Cursor hand = armed ? Cursors.Hand : Cursors.Default;   // 잠긴 동안에는 글자도 손 모양을 거둔다
            lblLimitCount.Cursor = hand;
            lblLimitTime.Cursor = hand;
            lblModeTimer.Cursor = hand;
            lblModeIp.Cursor = hand;
            bool ipMode = !IntervalMode();
            nudAutoDelay.Enabled = armed && ipMode;      // 지연 초와 할당 대기 초는 IP 할당 시 방식일 때
            nudWaitTimeout.Enabled = armed && ipMode;
            nudInterval.Enabled = armed && !ipMode;    // 간격은 일정 시간마다 방식일 때
            bool limitOn = armed && chkAutoLimit.Checked;
            nudLimitTime.Enabled = limitOn && !limitSwitch.Checked;
            nudLimitCount.Enabled = limitOn && limitSwitch.Checked;
            chkAutoStopIp.Enabled = stopped;
            txtAutoStopIp.Enabled = stopped && chkAutoStopIp.Checked;   // 체크해야 입력 칸이 열린다
            chkResetDns.Enabled = stopped;
            chkReleaseIp.Enabled = stopped;
            UseWaitCursor = busy;
        }

        /// <param name="state">준비(Ready) / 진행(Progress) / 완료(Done) / 실패(Failed)</param>
        /// <param name="message">상태 라벨에 덧붙일 한국어 메시지 (라벨에 다 들어가지 않아 잘려 보일 때만 전체 문장을 툴팁으로 보여 준다)</param>
        private void SetStatus(StatusKind state, string message)
        {
            string text = "상태: " + (state == StatusKind.Done ? "완료" : state == StatusKind.Failed ? "실패" : state == StatusKind.Progress ? "진행" : "준비");
            if (!string.IsNullOrEmpty(message)) text += " — " + message.Replace("\r\n", " ");
            if (lblStatus.Text == text) return;
            lblStatus.Text = text;
            lblStatus.ForeColor = state == StatusKind.Done ? Color.DarkGreen : state == StatusKind.Failed ? Color.Firebrick : state == StatusKind.Progress ? Color.DarkOrange : SystemColors.ControlText;
            // 라벨에 다 들어가면 툴팁 없음, 잘려 보이면(…) 전체 문장을 툴팁으로
            bool clipped = TextRenderer.MeasureText(text, lblStatus.Font).Width > lblStatus.ClientSize.Width - 4;
            toolTip.SetToolTip(lblStatus, clipped ? text : null);
        }
    }
}
