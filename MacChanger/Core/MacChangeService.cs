using System;
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
        /// <summary>실패 시 사용자에게 추가로 보여줄 안내문(무선 제약, 임의 하드웨어 주소 등)</summary>
        public string Guidance { get; set; }
    }

    /// <summary>
    /// 변경 적용 / 원상복구 절차를 순서대로 수행한다. 각 단계는 try/catch 로 감싸고 한국어 메시지를 로그로 남긴다.
    /// </summary>
    public static class MacChangeService
    {
        /// <summary>어댑터 활성화 후 현재 MAC 이 다시 읽힐 때까지 기다리는 최대 시간</summary>
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
                log("  NDIS 공장 MAC 응답이 비어 있음 → WMI 로 재시도");
            }
            catch (Exception ex)
            {
                log("  NDIS 공장 MAC 조회 실패(" + ex.Message + ") → WMI 로 재시도");
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
                log("  NDIS 현재 MAC 조회 실패(" + ex.Message + ") → GetAdaptersAddresses 로 재시도");
            }
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaGetAdaptersAddresses(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception ex)
            {
                log("  GetAdaptersAddresses 조회 실패(" + ex.Message + ") → WMI 로 재시도");
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

        /// <summary>어댑터가 다시 올라와 MAC 을 읽을 수 있을 때까지 폴링한다.</summary>
        public static string WaitForCurrentMac(NetworkAdapterInfo adapter, int timeoutMs, Action<string> log)
        {
            if (log == null) log = NoLog;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int attempt = 0;
            while (true)
            {
                attempt++;
                string mac = ReadCurrentMac(adapter, NoLog);
                if (mac != null) return mac;
                if (DateTime.UtcNow >= deadline)
                {
                    log("  현재 MAC 조회 시간 초과(" + (timeoutMs / 1000) + "초)");
                    return null;
                }
                if (attempt == 1 || attempt % 5 == 0) log("  어댑터 초기화 대기 중... (" + attempt + ")");
                Thread.Sleep(MacWaitPollMs);
            }
        }

        // ------------------------------------------------------------------
        // 변경 적용
        // ------------------------------------------------------------------
        public static MacChangeResult Apply(NetworkAdapterInfo adapter, string newMac, bool cleanTcpip, Action<string> log)
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
            int totalSteps = 4;

            // (a) 어댑터 비활성화
            log("[1/" + totalSteps + "] 어댑터 비활성화: " + adapter.Description);
            try
            {
                AdapterController.Disable(adapter, log);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 비활성화 실패: " + ex.Message;
                return result;
            }
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

            // (c) Tcpip Interfaces 값 정리
            string tcpipWarning = null;
            if (cleanTcpip)
            {
                log("[3/" + totalSteps + "] Tcpip\\Parameters\\Interfaces\\" + guid + " 값 정리 (EnableDHCP 만 유지)");
                try
                {
                    bool exists;
                    System.Collections.Generic.List<string> deleted;
                    int count = MacRegistry.CleanTcpipInterfaceValues(guid, out exists, out deleted);
                    if (!exists) log("  Tcpip 인터페이스 키가 없어 건너뜀");
                    else if (count == 0) log("  삭제할 값 없음");
                    else log("  " + count + "개 값 삭제: " + string.Join(", ", deleted.ToArray()));
                }
                catch (Exception ex)
                {
                    tcpipWarning = "Tcpip 값 정리 실패: " + ex.Message;
                    log("  " + tcpipWarning + " (계속 진행)");
                }
            }
            else
            {
                log("[3/" + totalSteps + "] Tcpip 값 정리 건너뜀 (옵션 해제)");
            }

            // (d) 어댑터 활성화
            log("[4/" + totalSteps + "] 어댑터 활성화");
            try
            {
                AdapterController.Enable(adapter, log);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 활성화 실패: " + ex.Message
                    + "\r\n레지스트리 값은 기록되었습니다. 네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.";
                return result;
            }

            // 검증
            log("  현재 MAC 다시 읽는 중...");
            string current = WaitForCurrentMac(adapter, MacWaitTimeoutMs, log);
            result.CurrentMac = current;
            if (current == null)
            {
                result.Message = "어댑터가 다시 올라온 뒤 현재 MAC 을 읽지 못했습니다. 잠시 후 '새로 고침'으로 확인하세요.";
                return result;
            }
            if (string.Equals(current, mac, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "MAC 변경 완료: " + MacAddressUtil.Format(current)
                    + (tcpipWarning != null ? " (경고: " + tcpipWarning + ")" : "");
                return result;
            }

            result.Message = "드라이버가 새 MAC 을 적용하지 않았습니다. 현재 MAC: " + MacAddressUtil.Format(current);
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

            log("[1/3] 어댑터 비활성화: " + adapter.Description);
            try
            {
                AdapterController.Disable(adapter, log);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 비활성화 실패: " + ex.Message;
                return result;
            }
            Thread.Sleep(AfterDisableDelayMs);

            log("[2/3] 레지스트리 NetworkAddress 값 삭제");
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

            log("[3/3] 어댑터 활성화");
            try
            {
                AdapterController.Enable(adapter, log);
            }
            catch (Exception ex)
            {
                result.Message = "어댑터 활성화 실패: " + ex.Message
                    + "\r\n네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.";
                return result;
            }

            log("  현재 MAC 다시 읽는 중...");
            string current = WaitForCurrentMac(adapter, MacWaitTimeoutMs, log);
            result.CurrentMac = current;
            if (current == null)
            {
                result.Message = "어댑터가 다시 올라온 뒤 현재 MAC 을 읽지 못했습니다. 잠시 후 '새로 고침'으로 확인하세요.";
                return result;
            }

            string permanent = ReadPermanentMac(adapter, NoLog);
            if (permanent == null)
            {
                result.Success = true;
                result.Message = (existed ? "NetworkAddress 삭제 및 " : "") + "어댑터 재시작 완료. 현재 MAC: " + MacAddressUtil.Format(current)
                    + " (공장 MAC 을 조회할 수 없어 비교는 생략)";
                return result;
            }
            if (string.Equals(current, permanent, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "공장 MAC 으로 복구 완료: " + MacAddressUtil.Format(current);
                return result;
            }
            result.Message = "NetworkAddress 를 삭제했지만 현재 MAC(" + MacAddressUtil.Format(current)
                + ")이 공장 MAC(" + MacAddressUtil.Format(permanent) + ")과 다릅니다.";
            result.Guidance = "재부팅 후 다시 확인하거나, 무선 어댑터라면 Windows 설정의 '임의 하드웨어 주소'가 켜져 있는지 확인하세요.";
            return result;
        }

        // ------------------------------------------------------------------
        // 내부 헬퍼
        // ------------------------------------------------------------------
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
                sb.AppendLine("Windows 설정 > 네트워크 및 인터넷 > Wi-Fi 의 '임의 하드웨어 주소'가 켜져 있으면 충돌할 수 있으니 끄고 다시 시도하세요.");
            }
            else
            {
                sb.AppendLine("일부 드라이버는 NetworkAddress 값을 지원하지 않거나 로컬 관리 주소(두 번째 자리 2/6/A/E)만 허용합니다.");
                sb.AppendLine("장치 관리자 > 어댑터 속성 > 고급 탭에 '네트워크 주소(Network Address)' 항목이 있는지 확인하세요.");
            }
            if (!MacAddressUtil.IsLocallyAdministered(mac))
                sb.AppendLine("입력한 MAC 은 로컬 관리 주소가 아닙니다. 두 번째 자리를 2/6/A/E 로 바꿔 보세요.");
            sb.AppendLine("드라이버에 따라 재부팅 후에 적용되는 경우도 있습니다.");
            return sb.ToString().TrimEnd();
        }
    }
}
