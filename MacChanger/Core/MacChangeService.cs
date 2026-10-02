using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MacChanger.Core
{
    public class MacChangeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        /// <summary>작업 후 다시 읽은 현재 MAC (12자리 hex, 조회 실패 시 null)</summary>
        public string CurrentMac { get; set; }
        /// <summary>장치 관리자가 어댑터를 즉시 재시작하지 못해 재부팅 후에야 적용되는 경우 true</summary>
        public bool RebootRequired { get; set; }
        /// <summary>실패 시 사용자에게 추가로 보여줄 안내문(무선 제약, 임의 하드웨어 주소 등)</summary>
        public string Guidance { get; set; }
    }

    /// <summary>
    /// 변경 적용 / 원상복구 절차를 순서대로 수행한다. 각 단계는 try/catch로 감싸고 한국어 메시지를 로그로 남긴다.
    /// </summary>
    public static class MacChangeService
    {
        /// <summary>어댑터 활성화 후 NDIS 장치가 다시 열릴 때까지 기다리는 최대 시간</summary>
        public const int MacWaitTimeoutMs = 20000;
        public const int MacWaitPollMs = 1000;
        /// <summary>비활성화 직후 레지스트리를 쓰기 전 잠시 대기</summary>
        public const int AfterDisableDelayMs = 700;

        private static readonly Action<string> NoLog = delegate { };

        // ------------------------------------------------------------------
        // MAC 조회 (매번 새로 조회, 저장하지 않음)
        // ------------------------------------------------------------------

        /// <summary>원래(공장) MAC. IOCTL(OID_802_3_PERMANENT_ADDRESS) → WMI MSFT_NetAdapter.PermanentAddress 순으로 시도.</summary>
        public static string ReadPermanentMac(NetworkAdapterInfo adapter, Action<string> log)
        {
            if (log == null) log = NoLog;
            try
            {
                string mac = NdisQuery.QueryPermanentMac(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
                log("  NDIS 공장 MAC 응답이 비어 있음 → WMI로 재시도");
            }
            catch (Exception ex)
            {
                log("  NDIS 공장 MAC 조회 실패(" + ex.Message + ") → WMI로 재시도");
            }
            try
            {
                string mac = AdapterEnumerator.GetPermanentMacViaWmi(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  WMI 공장 MAC 조회 실패: " + ex.Message);
            }
            return null;
        }

        /// <summary>현재 MAC. IOCTL(OID_802_3_CURRENT_ADDRESS) → GetAdaptersAddresses → WMI 순으로 시도.</summary>
        public static string ReadCurrentMac(NetworkAdapterInfo adapter, Action<string> log)
        {
            if (log == null) log = NoLog;
            try
            {
                string mac = NdisQuery.QueryCurrentMac(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  NDIS 현재 MAC 조회 실패(" + ex.Message + ") → GetAdaptersAddresses로 재시도");
            }
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaGetAdaptersAddresses(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  GetAdaptersAddresses 조회 실패(" + ex.Message + ") → WMI로 재시도");
            }
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaWmi(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  WMI 현재 MAC 조회 실패: " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 어댑터가 다시 올라와 NDIS 장치(\\.\{GUID})에서 현재 MAC을 읽을 수 있을 때까지 폴링한다.
        /// 대기 중에는 살아 있는 소스(NDIS)만 사용한다 — GetAdaptersAddresses/MSFT_NetAdapter는 아직 시작되지 않은 어댑터에 대해
        /// 변경 전 MAC을 캐시 값으로 돌려줄 수 있기 때문이다. 시간 초과 후에만 마지막 수단으로 그 값들을 읽는다.
        /// live = NDIS에서 직접 읽었는지 여부.
        /// </summary>
        public static string WaitForCurrentMac(NetworkAdapterInfo adapter, int timeoutMs, Action<string> log, out bool live)
        {
            if (log == null) log = NoLog;
            live = false;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    string mac = NdisQuery.QueryCurrentMac(adapter.InterfaceGuid);
                    if (mac != null && !MacAddressUtil.IsAllZero(mac))
                    {
                        live = true;
                        return mac;
                    }
                }
                catch (Exception)
                {
                    // 아직 미니포트가 초기화되지 않았거나 드라이버가 OID 조회를 지원하지 않음 → 계속 대기
                }
                if (DateTime.UtcNow >= deadline) break;
                if (attempt == 1 || attempt % 5 == 0) log("  어댑터 초기화 대기 중... (" + attempt + ")");
                Thread.Sleep(MacWaitPollMs);
            }

            log("  " + (timeoutMs / 1000) + "초 동안 NDIS 장치를 열지 못함 → GetAdaptersAddresses/WMI 값으로 확인");
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaGetAdaptersAddresses(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  GetAdaptersAddresses 조회 실패: " + ex.Message);
            }
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaWmi(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  WMI 현재 MAC 조회 실패: " + ex.Message);
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 변경 적용
        // ------------------------------------------------------------------
        public static MacChangeResult Apply(NetworkAdapterInfo adapter, string newMac, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            if (log == null) log = NoLog;
            MacChangeResult result = new MacChangeResult();

            string mac = MacAddressUtil.Normalize(newMac);
            if (mac == null)
            {
                result.Message = "새 MAC 형식이 올바르지 않습니다.";
                return result;
            }
            string guid = adapter.InterfaceGuid;
            const int totalSteps = 4;

            // (a) 어댑터 비활성화
            log("[1/" + totalSteps + "] 어댑터 비활성화: " + adapter.Description);
            bool disableDeferred;
            try
            {
                AdapterController.Disable(adapter, log, out disableDeferred);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 비활성화 실패: " + ex.Message;
                return result;
            }
            if (disableDeferred)
                log("  주의: 어댑터가 아직 동작 중입니다(재부팅 시 중지 예약). 레지스트리는 기록하되 Tcpip 정리와 즉시 검증은 건너뜁니다.");
            Thread.Sleep(AfterDisableDelayMs);

            // (b) NetworkAddress 쓰기
            log("[2/" + totalSteps + "] 레지스트리 NetworkAddress 쓰기");
            try
            {
                string subKey = MacRegistry.SetNetworkAddress(guid, mac);
                log("  HKLM\\" + MacRegistry.NetClassKeyPath + "\\" + subKey + "\\NetworkAddress = " + mac);
            }
            catch (Exception ex)
            {
                result.Message = "레지스트리 쓰기 실패: " + ex.Message;
                TryReEnableAfterFailure(adapter, log);
                return result;
            }

            // (c) Tcpip 값 자동 정리 — EnableDHCP = 1 인 경우에만 (인터페이스 값 + 전역 DhcpDomain/DhcpNameServer)
            string tcpipWarning = CleanTcpipIfDhcp(guid, "[3/" + totalSteps + "]", disableDeferred, log);

            // (d) 어댑터 활성화
            log("[4/" + totalSteps + "] 어댑터 활성화");
            bool enableDeferred;
            try
            {
                AdapterController.Enable(adapter, log, out enableDeferred);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 활성화 실패: " + ex.Message
                    + "\r\n레지스트리 값은 기록되었습니다. 네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.";
                return result;
            }

            if (disableDeferred || enableDeferred)
            {
                // 어댑터가 실제로 재시작되지 않았으므로 지금 읽은 MAC은 변경 전 값이다. 검증 대신 재부팅 안내.
                result.Success = true;
                result.RebootRequired = true;
                result.CurrentMac = ReadCurrentMac(adapter, NoLog);
                result.Message = "레지스트리에 NetworkAddress = " + MacAddressUtil.Format(mac) + "를 기록했지만 장치 관리자가 어댑터를 즉시 재시작하지 못했습니다. "
                    + "재부팅 후 새 MAC이 적용됩니다." + WarningSuffix(tcpipWarning);
                return result;
            }

            // 검증
            log("  현재 MAC 다시 읽는 중...");
            bool live;
            string current = WaitForCurrentMac(adapter, MacWaitTimeoutMs, log, out live);
            result.CurrentMac = current;
            if (current == null)
            {
                result.Message = "어댑터가 다시 올라오지 않았습니다(" + (MacWaitTimeoutMs / 1000) + "초 동안 MAC을 읽지 못함). "
                    + "장치 관리자 또는 네트워크 연결(ncpa.cpl)에서 어댑터 상태를 확인한 뒤 '새로 고침'을 누르세요.";
                return result;
            }
            string sourceNote = live ? "" : " (NDIS 직접 조회가 불가능해 GetAdaptersAddresses/WMI 값 기준)";
            if (string.Equals(current, mac, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "MAC 변경 완료: " + MacAddressUtil.Format(current) + sourceNote + WarningSuffix(tcpipWarning);
                if (adapter.Kind == AdapterKind.Wireless && SafeRandomMacState(guid) == true)
                    result.Message += " — 주의: 이 Wi-Fi 인터페이스에 '임의 하드웨어 주소'가 켜져 있어 네트워크 연결 시 MAC이 다시 바뀔 수 있습니다. 설정에서 끄세요.";
                return result;
            }

            result.Message = "드라이버가 새 MAC을 적용하지 않았습니다. 현재 MAC: " + MacAddressUtil.Format(current) + sourceNote;
            result.Guidance = BuildGuidance(adapter, mac);
            return result;
        }

        // ------------------------------------------------------------------
        // 원상복구
        // ------------------------------------------------------------------
        public static MacChangeResult Restore(NetworkAdapterInfo adapter, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            if (log == null) log = NoLog;
            MacChangeResult result = new MacChangeResult();
            string guid = adapter.InterfaceGuid;

            log("[1/4] 어댑터 비활성화: " + adapter.Description);
            bool disableDeferred;
            try
            {
                AdapterController.Disable(adapter, log, out disableDeferred);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 비활성화 실패: " + ex.Message;
                return result;
            }
            if (disableDeferred)
                log("  주의: 어댑터가 아직 동작 중입니다(재부팅 시 중지 예약). 레지스트리 값은 삭제하되 즉시 검증은 건너뜁니다.");
            Thread.Sleep(AfterDisableDelayMs);

            log("[2/4] 레지스트리 NetworkAddress 값 삭제");
            bool existed = false;
            try
            {
                string subKey;
                existed = MacRegistry.DeleteNetworkAddress(guid, out subKey);
                log(existed
                    ? "  HKLM\\" + MacRegistry.NetClassKeyPath + "\\" + subKey + "\\NetworkAddress 삭제됨"
                    : "  NetworkAddress 값이 없음 (이미 공장 MAC 상태)");
            }
            catch (Exception ex)
            {
                result.Message = "레지스트리 값 삭제 실패: " + ex.Message;
                TryReEnableAfterFailure(adapter, log);
                return result;
            }

            // 변경 적용 때와 동일한 Tcpip 값 자동 정리
            string tcpipWarning = CleanTcpipIfDhcp(guid, "[3/4]", disableDeferred, log);

            log("[4/4] 어댑터 활성화");
            bool enableDeferred;
            try
            {
                AdapterController.Enable(adapter, log, out enableDeferred);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 활성화 실패: " + ex.Message
                    + "\r\n네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.";
                return result;
            }

            if (disableDeferred || enableDeferred)
            {
                result.Success = true;
                result.RebootRequired = true;
                result.CurrentMac = ReadCurrentMac(adapter, NoLog);
                result.Message = (existed ? "NetworkAddress 값을 삭제했지만 " : "NetworkAddress 값은 원래 없었고 ")
                    + "장치 관리자가 어댑터를 즉시 재시작하지 못했습니다. 재부팅 후 공장 MAC으로 돌아갑니다."
                    + WarningSuffix(tcpipWarning);
                return result;
            }

            log("  현재 MAC 다시 읽는 중...");
            bool live;
            string current = WaitForCurrentMac(adapter, MacWaitTimeoutMs, log, out live);
            result.CurrentMac = current;
            if (current == null)
            {
                result.Message = "어댑터가 다시 올라오지 않았습니다(" + (MacWaitTimeoutMs / 1000) + "초 동안 MAC을 읽지 못함). "
                    + "장치 관리자 또는 네트워크 연결(ncpa.cpl)에서 어댑터 상태를 확인한 뒤 '새로 고침'을 누르세요.";
                return result;
            }
            string sourceNote = live ? "" : " (NDIS 직접 조회가 불가능해 GetAdaptersAddresses/WMI 값 기준)";

            string permanent = ReadPermanentMac(adapter, NoLog);
            if (permanent == null)
            {
                result.Success = true;
                result.Message = (existed ? "NetworkAddress 삭제 및 " : "") + "어댑터 재시작 완료. 현재 MAC: " + MacAddressUtil.Format(current)
                    + sourceNote + " (공장 MAC을 조회할 수 없어 비교는 생략)" + WarningSuffix(tcpipWarning);
                return result;
            }
            if (string.Equals(current, permanent, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "공장 MAC으로 복구 완료: " + MacAddressUtil.Format(current) + sourceNote + WarningSuffix(tcpipWarning);
                return result;
            }
            result.Message = "NetworkAddress를 삭제했지만 현재 MAC(" + MacAddressUtil.Format(current)
                + ")이 공장 MAC(" + MacAddressUtil.Format(permanent) + ")과 다릅니다." + sourceNote;
            result.Guidance = (adapter.Kind == AdapterKind.Wireless && SafeRandomMacState(guid) == true)
                ? "이 Wi-Fi 인터페이스에 Windows '임의 하드웨어 주소'가 켜져 있습니다. 설정 > 네트워크 및 인터넷 > Wi-Fi에서 끄면 공장 MAC이 사용됩니다."
                : "재부팅 후 다시 확인하거나, 무선 어댑터라면 Windows 설정의 '임의 하드웨어 주소'가 켜져 있는지 확인하세요.";
            return result;
        }

        // ------------------------------------------------------------------
        // 내부 헬퍼
        // ------------------------------------------------------------------

        /// <summary>
        /// Tcpip 값 자동 정리. 인터페이스 키의 EnableDHCP가 1이면 그 키의 값(EnableDHCP 제외)과 전역
        /// Tcpip\Parameters의 DhcpDomain/DhcpNameServer를 삭제한다. EnableDHCP가 0(고정 IP)이거나 값/키가 없으면 건너뛴다.
        /// 실패해도 예외를 던지지 않고 경고 문자열을 돌려준다 (없으면 null).
        /// </summary>
        private static string CleanTcpipIfDhcp(string guid, string stepLabel, bool adapterStillRunning, Action<string> log)
        {
            if (adapterStillRunning)
            {
                log(stepLabel + " Tcpip 값 정리 건너뜀 (어댑터가 아직 동작 중)");
                return null;
            }
            try
            {
                bool keyExists;
                int? enableDhcp = MacRegistry.GetEnableDhcp(guid, out keyExists);
                if (!keyExists)
                {
                    log(stepLabel + " Tcpip 값 정리 건너뜀 (Tcpip 인터페이스 키 없음)");
                    return null;
                }
                if (enableDhcp == null)
                {
                    log(stepLabel + " Tcpip 값 정리 건너뜀 (EnableDHCP 값 없음)");
                    return null;
                }
                if (enableDhcp.Value != 1)
                {
                    log(stepLabel + " Tcpip 값 정리 건너뜀 (EnableDHCP = " + enableDhcp.Value + ", 고정 IP 설정 유지)");
                    return null;
                }

                log(stepLabel + " EnableDHCP = 1 → Tcpip 값 자동 정리");
                List<string> deleted;
                int count = MacRegistry.CleanTcpipInterfaceValues(guid, out keyExists, out deleted);
                log("  Interfaces\\" + guid + ": " + (count == 0 ? "삭제할 값 없음" : count + "개 값 삭제 (" + string.Join(", ", deleted.ToArray()) + ")"));
                List<string> globalDeleted = MacRegistry.DeleteGlobalDhcpValues();
                log("  Tcpip\\Parameters: " + (globalDeleted.Count == 0 ? "DhcpDomain/DhcpNameServer 값 없음" : string.Join(", ", globalDeleted.ToArray()) + " 삭제"));
                return null;
            }
            catch (Exception ex)
            {
                string warning = "Tcpip 값 정리 실패: " + ex.Message;
                log("  " + warning + " (계속 진행)");
                return warning;
            }
        }

        private static string WarningSuffix(string warning)
        {
            return warning != null ? " (경고: " + warning + ")" : "";
        }

        private static bool? SafeRandomMacState(string interfaceGuid)
        {
            try { return MacRegistry.GetWlanRandomMacState(interfaceGuid); }
            catch (Exception) { return null; }
        }

        private static void TryReEnableAfterFailure(NetworkAdapterInfo adapter, Action<string> log)
        {
            log("  오류로 중단 — 어댑터를 다시 활성화합니다");
            try
            {
                AdapterController.Enable(adapter, log);
            }
            catch (Exception ex)
            {
                log("  어댑터 재활성화 실패: " + ex.Message + " — 네트워크 연결(ncpa.cpl)에서 수동으로 '사용'으로 바꾸세요.");
            }
        }

        private static string BuildGuidance(NetworkAdapterInfo adapter, string mac)
        {
            StringBuilder sb = new StringBuilder();
            if (adapter.Kind == AdapterKind.Wireless)
            {
                sb.AppendLine("무선 어댑터는 드라이버/OS 제약으로 첫 옥텟이 02(또는 06/0A/0E)가 아니면 변경이 무시될 수 있습니다.");
                if (MacAddressUtil.FirstOctet(mac) != 0x02)
                    sb.AppendLine("'첫 옥텟 02 고정' 옵션을 켜고 '랜덤 생성'으로 다시 만든 뒤 적용해 보세요.");
                if (SafeRandomMacState(adapter.InterfaceGuid) == true)
                    sb.AppendLine("이 Wi-Fi 인터페이스에 '임의 하드웨어 주소'가 켜져 있습니다. 설정 > 네트워크 및 인터넷 > Wi-Fi에서 끄고 다시 시도하세요.");
                else
                    sb.AppendLine("Windows 설정 > 네트워크 및 인터넷 > Wi-Fi의 '임의 하드웨어 주소'가 켜져 있으면 충돌할 수 있으니 끄고 다시 시도하세요.");
            }
            else
            {
                sb.AppendLine("일부 드라이버는 NetworkAddress 값을 지원하지 않거나 로컬 관리 주소(두 번째 자리 2/6/A/E)만 허용합니다.");
                sb.AppendLine("장치 관리자 > 어댑터 속성 > 고급 탭에 '네트워크 주소(Network Address)' 항목이 있는지 확인하세요.");
            }
            if (!MacAddressUtil.IsLocallyAdministered(mac))
                sb.AppendLine("입력한 MAC은 로컬 관리 주소가 아닙니다. 두 번째 자리를 2/6/A/E로 바꿔 보세요.");
            sb.AppendLine("드라이버에 따라 재부팅 후에 적용되는 경우도 있습니다.");
            return sb.ToString().TrimEnd();
        }
    }
}
