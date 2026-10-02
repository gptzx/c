using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Forms;
using MacChanger.Core;

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
            public bool CleanTcpip;
        }

        private bool busy;
        /// <summary>드롭다운 항목 텍스트를 갱신(Items[i] = item)할 때 SelectedIndexChanged가 다시 발생하는 것을 막는다.</summary>
        private bool suppressSelectionChanged;

        public MainForm()
        {
            InitializeComponent();
            toolTip.SetToolTip(chkFixFirstOctet, "켜면 랜덤 생성 시 첫 옥텟을 항상 02로 고정합니다. 끄면 02/06/0A/0E 중 무작위로 고릅니다. (둘 다 유니캐스트·로컬관리 주소)");
            toolTip.SetToolTip(chkCleanTcpip, "변경 적용 시 HKLM\\...\\Tcpip\\Parameters\\Interfaces\\{GUID}의 값 중 EnableDHCP만 남기고 모두 삭제합니다. (고정 IP/DNS 설정이 지워집니다)");
            toolTip.SetToolTip(txtNewMac, "12자리 16진수. 구분자(-, :, .)는 있어도 되고 없어도 됩니다. 예: 02-1A-2B-3C-4D-5E");
            toolTip.SetToolTip(btnRestore, "NetworkAddress 레지스트리 값을 삭제하고 어댑터를 재시작하여 공장 MAC으로 되돌립니다.");
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
            AppendLog("OS: " + Environment.OSVersion + " / " + (Environment.Is64BitOperatingSystem ? "64비트" : "32비트") + " OS, "
                + (Environment.Is64BitProcess ? "64비트" : "32비트") + " 프로세스");
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                AppendLog("주의: 64비트 OS에서 32비트 프로세스로 실행 중입니다. 레지스트리는 64비트 뷰로 접근하지만 SetupAPI 어댑터 재시작은 WMI 폴백을 사용할 수 있습니다.");
            SetStatus("진행", "어댑터 목록을 읽는 중...");
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // 창이 먼저 그려진 뒤에 (WMI 조회가 몇 초 걸릴 수 있으므로) 목록을 읽는다.
            BeginInvoke(new MethodInvoker(delegate { LoadAdapters(null); }));
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
                string mac = MacAddressUtil.GenerateRandom(chkFixFirstOctet.Checked);
                txtNewMac.Text = MacAddressUtil.Format(mac);
                SetStatus("준비", "랜덤 MAC 생성됨: " + txtNewMac.Text);
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
                SetStatus("실패", "첫 옥텟의 최하위 비트가 1(멀티캐스트 주소)입니다. 첫 옥텟을 짝수(예: 02)로 바꾸세요.");
                txtNewMac.Focus();
                return;
            }
            if (MacAddressUtil.IsAllZero(mac))
            {
                SetStatus("실패", "00-00-00-00-00-00은 사용할 수 없습니다.");
                return;
            }
            string currentNormalized = MacAddressUtil.Normalize(txtCurrentMac.Text);
            if (currentNormalized != null && string.Equals(currentNormalized, mac, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("준비", "새 MAC이 현재 MAC과 같습니다. 변경할 내용이 없습니다.");
                return;
            }

            List<string> warnings = new List<string>();
            if (!MacAddressUtil.IsLocallyAdministered(mac))
                warnings.Add("입력한 MAC은 로컬 관리 주소가 아닙니다(두 번째 자리 2/6/A/E 권장). 드라이버가 거부할 수 있습니다.");
            if (adapter.Kind == AdapterKind.Wireless)
            {
                if (MacAddressUtil.FirstOctet(mac) != 0x02)
                    warnings.Add("무선 어댑터는 드라이버/OS 제약으로 첫 옥텟이 02가 아니면 변경이 무시될 수 있습니다.");
                bool? randomMac = SafeGetRandomMacState(adapter.InterfaceGuid);
                if (randomMac == true)
                    warnings.Add("이 Wi-Fi 인터페이스에 Windows '임의 하드웨어 주소' 설정이 켜져 있습니다. 변경과 충돌할 수 있으니 설정 > 네트워크 및 인터넷 > Wi-Fi에서 끄는 것을 권장합니다.");
                else
                    warnings.Add("Windows 10 이상에서 '임의 하드웨어 주소'(Random hardware addresses)가 켜져 있으면 변경이 충돌할 수 있습니다.");
            }
            if (chkCleanTcpip.Checked)
                warnings.Add("'Tcpip 값 정리'가 켜져 있어 이 어댑터의 고정 IP/DNS 설정이 삭제됩니다 (EnableDHCP만 유지).");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("다음 어댑터의 MAC을 변경합니다. 변경 중 네트워크 연결이 잠시 끊어집니다.");
            sb.AppendLine();
            sb.AppendLine("어댑터: " + adapter.Description);
            sb.AppendLine("현재 MAC: " + (txtCurrentMac.Text.Length > 0 ? txtCurrentMac.Text : "-"));
            sb.AppendLine("새 MAC: " + MacAddressUtil.Format(mac));
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                foreach (string w in warnings) sb.AppendLine("※ " + w);
            }
            sb.AppendLine();
            sb.Append("계속하시겠습니까?");

            if (MessageBox.Show(this, sb.ToString(), "변경 적용", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.Apply;
            args.Adapter = adapter;
            args.NewMac = mac;
            args.CleanTcpip = chkCleanTcpip.Checked;
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
            string message = "다음 어댑터의 NetworkAddress 레지스트리 값을 삭제하고 어댑터를 재시작하여 공장 MAC으로 되돌립니다.\r\n\r\n"
                + "어댑터: " + adapter.Description + "\r\n"
                + "공장 MAC: " + (txtPermanentMac.Text.Length > 0 ? txtPermanentMac.Text : "-") + "\r\n\r\n계속하시겠습니까?";
            if (MessageBox.Show(this, message, "원상복구", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            OperationArgs args = new OperationArgs();
            args.Kind = OperationKind.Restore;
            args.Adapter = adapter;
            StartOperation(args);
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
                    adapters = AdapterEnumerator.Enumerate(AppendLog);
                }
                catch (Exception ex)
                {
                    adapters = new List<NetworkAdapterInfo>();
                    AppendLog("어댑터 열거 오류: " + ex.Message);
                }

                suppressSelectionChanged = true;
                int selectIndex = -1;
                try
                {
                    cboAdapters.BeginUpdate();
                    cboAdapters.Items.Clear();
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
                    ClearAdapterInfo();
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

        private void ClearAdapterInfo()
        {
            txtPermanentMac.Text = string.Empty;
            txtCurrentMac.Text = string.Empty;
            SetRegistryLabel("레지스트리 NetworkAddress: -", null);
        }

        private void RefreshSelectedAdapterInfo()
        {
            NetworkAdapterInfo adapter = SelectedAdapter;
            if (adapter == null)
            {
                ClearAdapterInfo();
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                SetStatus("진행", "어댑터 정보를 읽는 중...");
                lblStatus.Update();
                AppendLog("선택: [" + adapter.KindLabel + "] " + adapter.Description + " [" + adapter.InterfaceGuid + ", " + adapter.Source + "]");

                string permanent = null;
                string permanentError = null;
                try { permanent = MacChangeService.ReadPermanentMac(adapter, AppendLog); }
                catch (Exception ex) { permanentError = ex.Message; AppendLog("  공장 MAC 조회 오류: " + ex.Message); }
                txtPermanentMac.Text = permanent != null ? MacAddressUtil.Format(permanent) : "(조회 실패)";

                string current = null;
                string currentError = null;
                try { current = MacChangeService.ReadCurrentMac(adapter, AppendLog); }
                catch (Exception ex) { currentError = ex.Message; AppendLog("  현재 MAC 조회 오류: " + ex.Message); }
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

                try
                {
                    string subKey;
                    string regValue = MacRegistry.GetNetworkAddress(adapter.InterfaceGuid, out subKey);
                    string prefix = "레지스트리 NetworkAddress (클래스 키 " + subKey + "):\r\n";
                    if (regValue != null)
                        SetRegistryLabel(prefix + MacAddressUtil.Format(regValue), null);
                    else if (permanent != null && current != null && !string.Equals(permanent, current, StringComparison.OrdinalIgnoreCase))
                        SetRegistryLabel(prefix + "(없음 — 현재 MAC이 공장 MAC과 다름: 임의 하드웨어 주소 등 OS 설정 영향)", null);
                    else
                        SetRegistryLabel(prefix + "(없음 — 공장 MAC 사용 중)", null);
                }
                catch (Exception ex)
                {
                    SetRegistryLabel("레지스트리 NetworkAddress: 조회 실패 (로그 참조)", ex.Message);
                    AppendLog("  레지스트리 조회 오류: " + ex.Message);
                }

                if (adapter.Kind == AdapterKind.Wireless)
                {
                    bool? randomMac = SafeGetRandomMacState(adapter.InterfaceGuid);
                    if (randomMac == true)
                        AppendLog("  경고: 이 Wi-Fi 인터페이스에 '임의 하드웨어 주소' 설정이 켜져 있습니다. MAC 변경과 충돌할 수 있습니다.");
                    AppendLog("  참고: 무선 어댑터는 첫 옥텟이 02가 아니면 드라이버가 변경을 무시할 수 있습니다.");
                }

                if (current == null)
                    SetStatus("실패", "현재 MAC을 읽지 못했습니다" + (currentError != null ? ": " + currentError : " (어댑터가 비활성화되었거나 드라이버가 조회를 거부). 로그를 확인하세요."));
                else if (permanent == null)
                    SetStatus("준비", "공장 MAC을 읽지 못했습니다" + (permanentError != null ? ": " + permanentError : " (드라이버가 OID 조회를 지원하지 않음). 현재 MAC은 읽었습니다."));
                else
                    SetStatus("준비", "어댑터 정보를 읽었습니다.");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void SetRegistryLabel(string text, string tooltipDetail)
        {
            lblRegistry.Text = text;
            toolTip.SetToolTip(lblRegistry, tooltipDetail ?? text);
        }

        private static bool? SafeGetRandomMacState(string interfaceGuid)
        {
            try { return MacRegistry.GetWlanRandomMacState(interfaceGuid); }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // 백그라운드 작업
        // ------------------------------------------------------------------
        private void StartOperation(OperationArgs args)
        {
            if (worker.IsBusy) return;
            SetBusy(true);
            SetStatus("진행", (args.Kind == OperationKind.Apply ? "MAC 변경" : "원상복구") + " 작업을 시작합니다...");
            AppendLog("---- " + (args.Kind == OperationKind.Apply ? "변경 적용" : "원상복구") + " 시작 ----");
            worker.RunWorkerAsync(args);
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            OperationArgs args = (OperationArgs)e.Argument;
            BackgroundWorker w = (BackgroundWorker)sender;
            Action<string> log = delegate(string message) { w.ReportProgress(0, message); };

            MacChangeResult result;
            if (args.Kind == OperationKind.Apply)
                result = MacChangeService.Apply(args.Adapter, args.NewMac, args.CleanTcpip, log);
            else
                result = MacChangeService.Restore(args.Adapter, log);
            e.Result = result;
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            string message = e.UserState as string;
            if (message == null) return;
            AppendLog(message);
            if (message.StartsWith("[", StringComparison.Ordinal))
                SetStatus("진행", message);
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);

            MacChangeResult result = null;
            string finalState;
            string finalMessage;
            if (e.Error != null)
            {
                finalState = "실패";
                finalMessage = e.Error.Message;
                AppendLog("오류: " + e.Error);
            }
            else
            {
                result = e.Result as MacChangeResult;
                if (result == null)
                {
                    finalState = "실패";
                    finalMessage = "결과를 받지 못했습니다.";
                }
                else
                {
                    finalState = result.Success ? "완료" : "실패";
                    finalMessage = result.Message;
                }
            }
            AppendLog("---- 작업 종료: " + finalState + " — " + finalMessage + " ----");

            // 변경/복구 후 현재 MAC 등을 다시 읽어 UI를 갱신한 뒤, 작업 결과 상태를 최종적으로 표시한다.
            RefreshSelectedAdapterInfo();
            SetStatus(finalState, finalMessage);

            if (result != null && result.RebootRequired)
            {
                MessageBox.Show(this, result.Message + "\r\n\r\n지금 재부팅하거나, 장치 관리자에서 어댑터를 '사용 안 함' → '사용'으로 직접 재시작하세요.",
                    Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (result != null && !result.Success)
            {
                string text = result.Message;
                if (!string.IsNullOrEmpty(result.Guidance)) text += "\r\n\r\n" + result.Guidance;
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
            chkFixFirstOctet.Enabled = !value;
            chkCleanTcpip.Enabled = !value;
            btnApply.Enabled = !value;
            btnRestore.Enabled = !value;
            UseWaitCursor = value;
        }

        /// <param name="state">준비 / 진행 / 완료 / 실패</param>
        /// <param name="message">상태 라벨에 덧붙일 한국어 메시지</param>
        private void SetStatus(string state, string message)
        {
            string text = "상태: " + state;
            if (!string.IsNullOrEmpty(message)) text += " — " + message.Replace("\r\n", " ");
            lblStatus.Text = text;
            switch (state)
            {
                case "완료": lblStatus.ForeColor = System.Drawing.Color.DarkGreen; break;
                case "실패": lblStatus.ForeColor = System.Drawing.Color.Firebrick; break;
                case "진행": lblStatus.ForeColor = System.Drawing.Color.DarkOrange; break;
                default: lblStatus.ForeColor = System.Drawing.SystemColors.ControlText; break;
            }
            toolTip.SetToolTip(lblStatus, text);
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            if (txtLog.TextLength > 200000) txtLog.Clear();
            txtLog.AppendText(line + Environment.NewLine);
        }
    }
}
