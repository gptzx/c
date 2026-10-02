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
            Restore
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
        /// <summary>선택한 어댑터에 대해 마지막으로 로그 파일에 기록한 IP. 어댑터가 내려가거나 작업을 시작하면 초기화한다.</summary>
        private string lastLoggedIp;
        /// <summary>직전 RefreshIp 에서 발생한 로그 저장 오류 (없으면 null)</summary>
        private string ipLogError;

        public MainForm()
        {
            InitializeComponent();
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
            toolTip.SetToolTip(btnRestore, "확인 창 없이 바로 NetworkAddress 값을 삭제하고(EnableDHCP = 1이면 Tcpip 값 자동 정리) 어댑터를 재시작하여 공장 MAC으로 되돌립니다.");
            toolTip.SetToolTip(chkIpLog, "켜 두면 선택한 어댑터에 새 IP가 할당될 때마다 실행 파일 옆 " + IpMonitor.LogFileName + " 에 '시각, IP, MAC' 한 줄을 기록합니다.");
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
            BeginInvoke(new MethodInvoker(delegate { LoadAdapters(null); ipTimer.Start(); }));
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
            // 확인 대화 상자 없이 바로 복구한다.
            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.Restore;
            args.Adapter = adapter;
            StartOperation(args);
        }

        private void chkIpLog_CheckedChanged(object sender, EventArgs e)
        {
            lastLoggedIp = null;
            if (!chkIpLog.Checked)
            {
                SetStatus("준비", "할당 IP 로그 기록을 껐습니다.");
                return;
            }
            RefreshIp();   // 현재 할당된 IP를 즉시 한 줄 기록
            if (ipLogError != null) SetStatus("실패", ipLogError);
            else SetStatus("준비", "할당 IP 로그를 실행 파일 옆 " + IpMonitor.LogFileName + " 에 기록합니다.");
        }

        private void ipTimer_Tick(object sender, EventArgs e)
        {
            RefreshIp();
            if (ipLogError != null) SetStatus("실패", ipLogError);
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
                try
                {
                    adapters = AdapterEnumerator.Enumerate();
                }
                catch (Exception ex)
                {
                    adapters = new List<NetworkAdapterInfo>();
                    SetStatus("실패", "어댑터 열거 오류: " + ex.Message);
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
                    txtPermanentMac.Text = txtCurrentMac.Text = txtCurrentIp.Text = string.Empty;
                    SetStatus("실패", "네트워크 어댑터를 찾지 못했습니다.");
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
                }

                string permanent = MacChangeService.ReadPermanentMac(adapter);
                txtPermanentMac.Text = permanent != null ? MacAddressUtil.Format(permanent) : "(조회 실패)";

                string current = MacChangeService.ReadCurrentMac(adapter);
                txtCurrentMac.Text = current != null ? MacAddressUtil.Format(current) : "(조회 실패)";

                // 드롭다운 항목 텍스트에도 최신 MAC을 반영한다 (ComboBox는 Add 시점의 문자열을 캐시하므로 항목을 다시 넣어야 한다).
                if (current != null && !string.Equals(adapter.CurrentMac, current, StringComparison.OrdinalIgnoreCase))
                {
                    adapter.CurrentMac = current;
                    int i = cboAdapters.SelectedIndex;
                    if (i >= 0)
                    {
                        suppressSelectionChanged = true;
                        try { cboAdapters.Items[i] = adapter; }
                        finally { suppressSelectionChanged = false; }
                    }
                }

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

        /// <summary>선택한 어댑터의 현재 IPv4를 표시하고, 로그 옵션이 켜져 있으면 새로 할당된 IP를 파일에 기록한다. 오류는 ipLogError 에 남긴다.</summary>
        private void RefreshIp()
        {
            ipLogError = null;
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
            {
                txtCurrentIp.Text = string.Empty;
                return;
            }
            string ip;
            bool up;
            try
            {
                ip = IpMonitor.ReadIPv4(adapter.InterfaceGuid, out up);
            }
            catch (Exception)
            {
                txtCurrentIp.Text = "(IP 조회 실패)";
                return;
            }

            string text;
            if (ip == null) text = "(어댑터 비활성 상태)";
            else if (ip.Length == 0) text = up ? "(IP 없음 — 할당 대기 중)" : "(연결 안 됨 — 링크 없음)";
            else text = ip + (IpMonitor.IsOnlyApipa(ip) ? "  (DHCP 응답 없음 — 자동 사설 주소)" : "");
            if (txtCurrentIp.Text != text) txtCurrentIp.Text = text;

            if (string.IsNullOrEmpty(ip))
            {
                lastLoggedIp = null;   // 끊겼다가 다시 같은 IP를 받아도 새 할당으로 기록
                return;
            }
            // 작업 중(busy)에는 기록하지 않는다: 어댑터가 올라온 직후의 IP는 작업 완료 후 새 MAC과 함께 기록된다.
            if (!busy && chkIpLog.Checked && !IpMonitor.IsOnlyApipa(ip) && ip != lastLoggedIp)
            {
                try
                {
                    IpMonitor.AppendAssignedIp(Application.ExecutablePath, ip, adapter.CurrentMac);
                    lastLoggedIp = ip;
                }
                catch (Exception ex)
                {
                    chkIpLog.Checked = false;   // CheckedChanged 가 먼저 상태를 쓰고, 호출자가 ipLogError 를 그 뒤에 표시한다
                    ipLogError = "IP 로그 저장 실패: " + ex.Message;
                }
            }
        }

        // ------------------------------------------------------------------
        // 백그라운드 작업
        // ------------------------------------------------------------------
        private void StartOperation(OperationArgs args)
        {
            if (worker.IsBusy) return;
            SetBusy(true);
            lastLoggedIp = null;   // 작업 후 받은 IP는 같은 값이라도 새 MAC과 함께 기록
            SetStatus("진행", (args.Kind == OperationKind.Apply ? "MAC 변경" : "원상복구") + " 시작...");
            worker.RunWorkerAsync(args);
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            OperationArgs args = (OperationArgs)e.Argument;
            BackgroundWorker w = (BackgroundWorker)sender;
            Action<string> log = delegate(string message) { w.ReportProgress(0, message); };
            e.Result = args.Kind == OperationKind.Apply
                ? MacChangeService.Apply(args.Adapter, args.NewMac, log)
                : MacChangeService.Restore(args.Adapter, log);
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
        }

        // ------------------------------------------------------------------
        // UI 헬퍼
        // ------------------------------------------------------------------
        private void SetBusy(bool value)
        {
            busy = value;
            cboAdapters.Enabled = !value;
            btnRefresh.Enabled = !value;
            txtNewMac.Enabled = !value;
            btnRandom.Enabled = !value;
            btnApply.Enabled = !value;
            btnRestore.Enabled = !value;
            UseWaitCursor = value;
        }

        /// <param name="state">준비 / 진행 / 완료 / 실패</param>
        /// <param name="message">상태 라벨에 덧붙일 한국어 메시지 (전체 내용은 툴팁으로도 볼 수 있다)</param>
        private void SetStatus(string state, string message)
        {
            string text = "상태: " + state;
            if (!string.IsNullOrEmpty(message)) text += " — " + message.Replace("\r\n", " ");
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
