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

        public MainForm()
        {
            InitializeComponent();
            toolTip.SetToolTip(chkFixFirstOctet, "켜면 랜덤 생성 시 첫 옥텟을 항상 02 로 고정합니다. 끄면 02/06/0A/0E 중 무작위로 고릅니다. (둘 다 유니캐스트·로컬관리 주소)");
            toolTip.SetToolTip(chkCleanTcpip, "변경 적용 시 HKLM\\...\\Tcpip\\Parameters\\Interfaces\\{GUID} 의 값 중 EnableDHCP 만 남기고 모두 삭제합니다. (고정 IP/DNS 설정이 지워집니다)");
            toolTip.SetToolTip(txtNewMac, "12자리 16진수. 구분자(-, :, .)는 있어도 되고 없어도 됩니다. 예: 02-1A-2B-3C-4D-5E");
            toolTip.SetToolTip(btnRestore, "NetworkAddress 레지스트리 값을 삭제하고 어댑터를 재시작하여 공장 MAC 으로 되돌립니다.");
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
                AppendLog("주의: 64비트 OS 에서 32비트 프로세스로 실행 중입니다. 레지스트리는 64비트 뷰로 접근하지만 SetupAPI 어댑터 재시작은 WMI 폴백을 사용할 수 있습니다.");
            LoadAdapters(null);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (busy)
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
                SetStatus("실패", "00-00-00-00-00-00 은 사용할 수 없습니다.");
                return;
            }
            string currentNormalized = MacAddressUtil.Normalize(txtCurrentMac.Text);
            if (currentNormalized != null && string.Equals(currentNormalized, mac, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("준비", "새 MAC 이 현재 MAC 과 같습니다. 변경할 내용이 없습니다.");
                return;
            }

            List<string> warnings = new List<string>();
            if (!MacAddressUtil.IsLocallyAdministered(mac))
                warnings.Add("입력한 MAC 은 로컬 관리 주소가 아닙니다(두 번째 자리 2/6/A/E 권장). 드라이버가 거부할 수 있습니다.");
            if (adapter.Kind == AdapterKind.Wireless)
            {
                if (MacAddressUtil.FirstOctet(mac) != 0x02)
                    warnings.Add("무선 어댑터는 드라이버/OS 제약으로 첫 옥텟이 02 가 아니면 변경이 무시될 수 있습니다.");
                bool? randomMac = SafeGetRandomMacState(adapter.InterfaceGuid);
                if (randomMac == true)
                    warnings.Add("이 Wi-Fi 인터페이스에 Windows '임의 하드웨어 주소' 설정이 켜져 있습니다. 변경과 충돌할 수 있으니 설정 > 네트워크 및 인터넷 > Wi-Fi 에서 끄는 것을 권장합니다.");
                else
                    warnings.Add("Windows 10 이상에서 '임의 하드웨어 주소'(Random hardware addresses)가 켜져 있으면 변경이 충돌할 수 있습니다.");
            }
            if (chkCleanTcpip.Checked)
                warnings.Add("'Tcpip 값 정리'가 켜져 있어 이 어댑터의 고정 IP/DNS 설정이 삭제됩니다 (EnableDHCP 만 유지).");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("다음 어댑터의 MAC 을 변경합니다. 변경 중 네트워크 연결이 잠시 끊어집니다.");
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
            string message = "다음 어댑터의 NetworkAddress 레지스트리 값을 삭제하고 어댑터를 재시작하여 공장 MAC 으로 되돌립니다.\r\n\r\n"
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

                if (cboAdapters.Items.Count == 0)
                {
                    ClearAdapterInfo();
                    SetStatus("실패", "네트워크 어댑터를 찾지 못했습니다.");
                    return;
                }
                if (selectIndex < 0) selectIndex = 0;
                if (cboAdapters.SelectedIndex == selectIndex)
                    RefreshSelectedAdapterInfo();   // 같은 인덱스면 SelectedIndexChanged 가 발생하지 않음
                else
                    cboAdapters.SelectedIndex = selectIndex;
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
            lblRegistry.Text = "레지스트리 NetworkAddress: -";
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
                AppendLog("선택: " + adapter.DisplayText + " [" + adapter.InterfaceGuid + ", " + adapter.Source + "]");

                string permanent = null;
                try { permanent = MacChangeService.ReadPermanentMac(adapter, AppendLog); }
                catch (Exception ex) { AppendLog("  공장 MAC 조회 오류: " + ex.Message); }
                txtPermanentMac.Text = permanent != null ? MacAddressUtil.Format(permanent) : "(조회 실패)";

                string current = null;
                try { current = MacChangeService.ReadCurrentMac(adapter, AppendLog); }
                catch (Exception ex) { AppendLog("  현재 MAC 조회 오류: " + ex.Message); }
                if (current == null && adapter.CurrentMac != null) current = adapter.CurrentMac;
                txtCurrentMac.Text = current != null ? MacAddressUtil.Format(current) : "(조회 실패)";

                try
                {
                    string subKey;
                    string regValue = MacRegistry.GetNetworkAddress(adapter.InterfaceGuid, out subKey);
                    lblRegistry.Text = "레지스트리 NetworkAddress (클래스 키 " + subKey + "):\r\n"
                        + (regValue != null ? MacAddressUtil.Format(regValue) : "(없음 — 공장 MAC 사용 중)");
                }
                catch (Exception ex)
                {
                    lblRegistry.Text = "레지스트리 NetworkAddress: 조회 실패\r\n" + ex.Message;
                    AppendLog("  레지스트리 조회 오류: " + ex.Message);
                }

                if (adapter.Kind == AdapterKind.Wireless)
                {
                    bool? randomMac = SafeGetRandomMacState(adapter.InterfaceGuid);
                    if (randomMac == true)
                        AppendLog("  경고: 이 Wi-Fi 인터페이스에 '임의 하드웨어 주소' 설정이 켜져 있습니다. MAC 변경과 충돌할 수 있습니다.");
                    AppendLog("  참고: 무선 어댑터는 첫 옥텟이 02 가 아니면 드라이버가 변경을 무시할 수 있습니다.");
                }

                SetStatus("준비", "어댑터 정보를 읽었습니다.");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
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
            if (e.Error != null)
            {
                SetStatus("실패", e.Error.Message);
                AppendLog("오류: " + e.Error);
            }
            else
            {
                result = e.Result as MacChangeResult;
                if (result == null)
                    SetStatus("실패", "결과를 받지 못했습니다.");
                else if (result.Success)
                    SetStatus("완료", result.Message);
                else
                    SetStatus("실패", result.Message);
            }
            AppendLog("---- 작업 종료 ----");

            // 변경/복구 후 현재 MAC 등을 다시 읽어 UI 갱신
            RefreshSelectedAdapterInfo();
            if (result != null)
            {
                if (result.Success) SetStatus("완료", result.Message);
                else SetStatus("실패", result.Message);
            }

            if (result != null && !result.Success)
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
