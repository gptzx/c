using System;
using System.Text;
using System.Threading;

namespace MacChanger.Core
{
    public class MacChangeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        /// <summary>장치 관리자가 어댑터를 즉시 재시작하지 못해 재부팅 후에야 적용되는 경우 true</summary>
        public bool RebootRequired { get; set; }
        /// <summary>어댑터가 실제로 중지되었는지(= 이후 받는 IP는 새 할당). 비활성화 실패나 재부팅 보류면 false</summary>
        public bool AdapterRestarted { get; set; }
        /// <summary>실패 시 사용자에게 추가로 보여줄 안내문(무선 제약, 임의 하드웨어 주소 등)</summary>
        public string Guidance { get; set; }
    }

    /// <summary>
    /// 변경 적용 / 원상복구 / IP 갱신 절차를 순서대로 수행한다. 각 단계는 try/catch로 감싸고 한국어 진행 메시지를 log 콜백(상태 라벨)으로 보낸다.
    /// </summary>
    public static class MacChangeService
    {
        /// <summary>어댑터 활성화 후 NDIS 장치가 다시 열릴 때까지 기다리는 최대 시간</summary>
        public const int MacWaitTimeoutMs = 20000;
        public const int MacWaitPollMs = 1000;
        /// <summary>비활성화 직후 레지스트리를 쓰기 전 잠시 대기</summary>
        public const int AfterDisableDelayMs = 700;

        // ------------------------------------------------------------------
        // MAC 조회 (매번 새로 조회, 저장하지 않음)
        // ------------------------------------------------------------------

        /// <summary>원래(공장) MAC. IOCTL(OID_802_3_PERMANENT_ADDRESS) → 열거 시점에 읽어 둔 MSFT_NetAdapter.PermanentAddress 순으로 시도. 실패 시 null.</summary>
        public static string ReadPermanentMac(NetworkAdapterInfo adapter)
        {
            string mac = NdisQuery.QueryPermanentMac(adapter.InterfaceGuid);
            if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            string hint = adapter.PermanentMacHint;
            return hint != null && !MacAddressUtil.IsAllZero(hint) ? hint : null;
        }

        /// <summary>현재 MAC. IOCTL(OID_802_3_CURRENT_ADDRESS) → GetAdaptersAddresses → WMI 순으로 시도. 실패 시 null.</summary>
        public static string ReadCurrentMac(NetworkAdapterInfo adapter)
        {
            bool live;
            return ReadCurrentMac(adapter, out live);
        }

        /// <summary>live = NDIS 직접 조회로 읽었는지 (GetAdaptersAddresses/WMI 폴백이면 false).</summary>
        private static string ReadCurrentMac(NetworkAdapterInfo adapter, out bool live)
        {
            live = false;
            string ndis = NdisQuery.QueryCurrentMac(adapter.InterfaceGuid);
            if (ndis != null && !MacAddressUtil.IsAllZero(ndis))
            {
                live = true;
                return ndis;
            }
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaGetAdaptersAddresses(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception) { }
            try
            {
                string mac = AdapterEnumerator.GetCurrentMacViaWmi(adapter.InterfaceGuid);
                if (mac != null && !MacAddressUtil.IsAllZero(mac)) return mac;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// 어댑터가 다시 올라와 NDIS 장치(\\.\{GUID})에서 현재 MAC을 읽을 수 있을 때까지 폴링한다.
        /// 대기 중에는 살아 있는 소스(NDIS)만 사용한다 — GetAdaptersAddresses/MSFT_NetAdapter는 아직 시작되지 않은 어댑터에 대해
        /// 변경 전 MAC을 캐시 값으로 돌려줄 수 있기 때문이다. 시간 초과 후에만 마지막 수단으로 그 값들을 읽는다.
        /// </summary>
        private static string WaitForCurrentMac(NetworkAdapterInfo adapter, Action<string> log, out bool live)
        {
            live = false;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(MacWaitTimeoutMs);
            int attempt = 0;
            while (true)
            {
                attempt++;
                string mac = NdisQuery.QueryCurrentMac(adapter.InterfaceGuid);   // 미니포트가 아직 초기화되지 않았으면 null → 계속 대기
                if (mac != null && !MacAddressUtil.IsAllZero(mac))
                {
                    live = true;
                    return mac;
                }
                if (DateTime.UtcNow >= deadline) break;
                log("어댑터 초기화 대기 중... (" + attempt + ")");
                Thread.Sleep(MacWaitPollMs);
            }
            // 시간 초과: GetAdaptersAddresses/WMI 값으로 폴백 (NDIS 는 ReadCurrentMac 이 먼저 한 번 더 본다)
            return ReadCurrentMac(adapter, out live);
        }

        /// <summary>
        /// 변경 적용·원상복구·IP 갱신의 공통 절차: [1/4] 비활성화 → [2/4] 레지스트리 단계(registryStep) → [3/4] Tcpip 값 자동 정리 → [4/4] 활성화 → 현재 MAC 재조회.
        /// 실패 결과에는 어댑터가 실제로 중지되었는지(AdapterRestarted)가 이미 채워져 있다.
        /// 실패하면 실패 결과를 돌려주고, 성공하면 null을 돌려주며 out 값을 채운다 (deferred 이면 재시작이 보류되어 current 는 null).
        /// </summary>
        private static MacChangeResult RunCycle(NetworkAdapterInfo adapter, Action<string> log, string registryStepLabel, string registryFailLabel, Action registryStep,
            string enableFailHint, out bool restarted, out bool deferred, out string tcpipWarning, out string current, out bool live)
        {
            restarted = false;
            deferred = false;
            tcpipWarning = null;
            current = null;
            live = false;
            MacChangeResult fail = new MacChangeResult();

            log("[1/4] 어댑터 비활성화");
            bool disableDeferred;
            try
            {
                AdapterController.Disable(adapter, log, out disableDeferred);
            }
            catch (Exception ex)
            {
                fail.Message = "어댑터 비활성화 실패: " + ex.Message;
                return fail;
            }
            restarted = !disableDeferred;
            fail.AdapterRestarted = restarted;   // 이후 실패해도 어댑터는 이미 중지되었으므로 호출자가 그대로 돌려준다
            Thread.Sleep(AfterDisableDelayMs);

            log("[2/4] " + registryStepLabel);
            try
            {
                registryStep();
            }
            catch (Exception ex)
            {
                fail.Message = registryFailLabel + " 실패: " + ex.Message;
                log("오류로 중단 — 어댑터를 다시 활성화합니다");
                try
                {
                    bool needReboot;
                    AdapterController.Enable(adapter, log, out needReboot);
                }
                catch (Exception) { }
                return fail;
            }

            tcpipWarning = CleanTcpipIfDhcp(adapter.InterfaceGuid, "[3/4]", disableDeferred, log);

            log("[4/4] 어댑터 활성화");
            bool enableDeferred;
            try
            {
                AdapterController.Enable(adapter, log, out enableDeferred);
            }
            catch (Exception ex)
            {
                fail.Message = "어댑터 활성화 실패: " + ex.Message + "\r\n" + enableFailHint;
                return fail;
            }

            deferred = disableDeferred || enableDeferred;
            if (deferred) return null;   // 어댑터가 실제로 재시작되지 않았으므로 검증 생략 — 호출자가 재부팅 안내

            current = WaitForCurrentMac(adapter, log, out live);
            if (current == null)
            {
                fail.Message = "어댑터가 다시 올라오지 않았습니다(" + (MacWaitTimeoutMs / 1000) + "초 동안 MAC을 읽지 못함). "
                    + "장치 관리자 또는 네트워크 연결(ncpa.cpl)에서 어댑터 상태를 확인한 뒤 '새로 고침'을 누르세요.";
                return fail;
            }
            return null;
        }

        private static string SourceNote(bool live)
        {
            return live ? "" : " (NDIS 직접 조회가 불가능해 GetAdaptersAddresses/WMI 값 기준)";
        }

        // ------------------------------------------------------------------
        // 변경 적용
        // ------------------------------------------------------------------
        public static MacChangeResult Apply(NetworkAdapterInfo adapter, string newMac, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            MacChangeResult result = new MacChangeResult();
            string mac = MacAddressUtil.Normalize(newMac);
            if (mac == null)
            {
                result.Message = "새 MAC 형식이 올바르지 않습니다.";
                return result;
            }
            string guid = adapter.InterfaceGuid;

            bool restarted, deferred, live;
            string tcpipWarning, current;
            MacChangeResult fail = RunCycle(adapter, log, "레지스트리 NetworkAddress 쓰기", "레지스트리 쓰기",
                delegate { MacRegistry.SetNetworkAddress(guid, mac); },
                "레지스트리 값은 기록되었습니다. 네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.",
                out restarted, out deferred, out tcpipWarning, out current, out live);
            if (fail != null) return fail;
            result.AdapterRestarted = restarted;

            if (deferred)
            {
                result.Success = true;
                result.RebootRequired = true;
                result.Message = "레지스트리에 NetworkAddress = " + MacAddressUtil.Format(mac) + "를 기록했지만 장치 관리자가 어댑터를 즉시 재시작하지 못했습니다. "
                    + "재부팅 후 새 MAC이 적용됩니다." + WarningSuffix(tcpipWarning);
                return result;
            }
            if (string.Equals(current, mac, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "MAC 변경 완료: " + MacAddressUtil.Format(current) + SourceNote(live) + WarningSuffix(tcpipWarning);
                if (adapter.Kind == AdapterKind.Wireless && SafeRandomMacState(guid) == true)
                    result.Message += " — 주의: 이 Wi-Fi 인터페이스에 '임의 하드웨어 주소'가 켜져 있어 네트워크 연결 시 MAC이 다시 바뀔 수 있습니다. 설정에서 끄세요.";
                return result;
            }
            result.Message = "드라이버가 새 MAC을 적용하지 않았습니다. 현재 MAC: " + MacAddressUtil.Format(current) + SourceNote(live);
            result.Guidance = BuildGuidance(adapter, mac);
            return result;
        }

        // ------------------------------------------------------------------
        // 원상복구
        // ------------------------------------------------------------------
        public static MacChangeResult Restore(NetworkAdapterInfo adapter, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            MacChangeResult result = new MacChangeResult();
            string guid = adapter.InterfaceGuid;
            bool existed = false;

            bool restarted, deferred, live;
            string tcpipWarning, current;
            MacChangeResult fail = RunCycle(adapter, log, "레지스트리 NetworkAddress 값 삭제", "레지스트리 값 삭제",
                delegate { existed = MacRegistry.DeleteNetworkAddress(guid); },
                "네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.",
                out restarted, out deferred, out tcpipWarning, out current, out live);
            if (fail != null) return fail;
            result.AdapterRestarted = restarted;

            if (deferred)
            {
                result.Success = true;
                result.RebootRequired = true;
                result.Message = (existed ? "NetworkAddress 값을 삭제했지만 " : "NetworkAddress 값은 원래 없었고 ")
                    + "장치 관리자가 어댑터를 즉시 재시작하지 못했습니다. 재부팅 후 공장 MAC으로 돌아갑니다." + WarningSuffix(tcpipWarning);
                return result;
            }
            string permanent = ReadPermanentMac(adapter);
            if (permanent == null)
            {
                result.Success = true;
                result.Message = (existed ? "NetworkAddress 삭제 및 " : "") + "어댑터 재시작 완료. 현재 MAC: " + MacAddressUtil.Format(current)
                    + SourceNote(live) + " (공장 MAC을 조회할 수 없어 비교는 생략)" + WarningSuffix(tcpipWarning);
                return result;
            }
            if (string.Equals(current, permanent, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "공장 MAC으로 복구 완료: " + MacAddressUtil.Format(current) + SourceNote(live) + WarningSuffix(tcpipWarning);
                return result;
            }
            result.Message = "NetworkAddress를 삭제했지만 현재 MAC(" + MacAddressUtil.Format(current)
                + ")이 공장 MAC(" + MacAddressUtil.Format(permanent) + ")과 다릅니다." + SourceNote(live);
            result.Guidance = (adapter.Kind == AdapterKind.Wireless && SafeRandomMacState(guid) == true)
                ? "이 Wi-Fi 인터페이스에 Windows '임의 하드웨어 주소'가 켜져 있습니다. 설정 > 네트워크 및 인터넷 > Wi-Fi에서 끄면 공장 MAC이 사용됩니다."
                : "재부팅 후 다시 확인하거나, 무선 어댑터라면 Windows 설정의 '임의 하드웨어 주소'가 켜져 있는지 확인하세요.";
            return result;
        }

        // ------------------------------------------------------------------
        // IP 갱신
        // ------------------------------------------------------------------
        /// <summary>
        /// IP 갱신: NetworkAddress 는 그대로 두어 MAC 은 바꾸지 않고, 비활성화 → Tcpip 값 자동 정리(EnableDHCP = 1 인 경우) → 활성화로 IP 만 새로 받는다.
        /// (원상복구가 이미 공장 MAC 인 어댑터에서 하던 일과 같다.) 재시작 전후 MAC 을 비교해 실제로 유지되었는지 알려준다.
        /// </summary>
        public static MacChangeResult RenewIp(NetworkAdapterInfo adapter, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            MacChangeResult result = new MacChangeResult();
            string before = ReadCurrentMac(adapter);   // 어댑터가 꺼져 있으면 null — 그때는 "유지" 여부를 말하지 않는다

            bool restarted, deferred, live;
            string tcpipWarning, current;
            MacChangeResult fail = RunCycle(adapter, log, "레지스트리 NetworkAddress 유지 (MAC 변경 없음)", "레지스트리 단계",
                delegate { },
                "네트워크 연결(ncpa.cpl)에서 어댑터를 수동으로 '사용'으로 바꾸세요.",
                out restarted, out deferred, out tcpipWarning, out current, out live);
            if (fail != null) return fail;
            result.Success = true;
            result.AdapterRestarted = restarted;
            if (deferred)
            {
                result.RebootRequired = true;
                result.Message = "장치 관리자가 어댑터를 즉시 재시작하지 못했습니다. 재부팅 후 IP가 새로 할당됩니다." + WarningSuffix(tcpipWarning);
                return result;
            }
            string shown = MacAddressUtil.Format(current) + SourceNote(live);
            if (before == null)
                result.Message = "IP 갱신 완료: 어댑터를 재시작했습니다. 현재 MAC: " + shown + WarningSuffix(tcpipWarning);
            else if (string.Equals(before, current, StringComparison.OrdinalIgnoreCase))
                result.Message = "IP 갱신 완료: 어댑터를 재시작했습니다. 현재 MAC(유지): " + shown + WarningSuffix(tcpipWarning);
            else
            {
                // 보류 중이던 NetworkAddress 값이 이번 재시작에 적용되었거나 Wi-Fi 임의 하드웨어 주소가 바뀐 경우
                result.Message = "IP 갱신 완료: 어댑터를 재시작했지만 현재 MAC " + shown + " 이(가) 재시작 전(" + MacAddressUtil.Format(before) + ")과 다릅니다. "
                    + "보류 중이던 NetworkAddress 값이 이번 재시작에 적용되었거나, Wi-Fi '임의 하드웨어 주소'가 켜져 있을 수 있습니다." + WarningSuffix(tcpipWarning);
            }
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
                if (!keyExists || enableDhcp == null || enableDhcp.Value != 1)
                {
                    log(stepLabel + " Tcpip 값 정리 건너뜀 (" + (!keyExists ? "Tcpip 인터페이스 키 없음" : enableDhcp == null ? "EnableDHCP 값 없음" : "EnableDHCP = " + enableDhcp.Value + ", 고정 IP 설정 유지") + ")");
                    return null;
                }
                log(stepLabel + " EnableDHCP = 1 → Tcpip 값 자동 정리");
            }
            catch (Exception ex)
            {
                return "EnableDHCP 확인 실패: " + ex.Message;
            }

            // 인터페이스 값 삭제와 전역 값 삭제는 서로 독립이므로 한쪽이 실패해도 다른 쪽은 시도한다.
            string interfaceWarning = null;
            try
            {
                MacRegistry.CleanTcpipInterfaceValues(guid);
            }
            catch (Exception ex)
            {
                interfaceWarning = "인터페이스 Tcpip 값 정리 실패: " + ex.Message;
            }
            string globalWarning = null;
            try
            {
                MacRegistry.DeleteGlobalDhcpValues();
            }
            catch (Exception ex)
            {
                globalWarning = "전역 DhcpDomain/DhcpNameServer 삭제 실패: " + ex.Message;
            }
            if (interfaceWarning != null && globalWarning != null) return interfaceWarning + "; " + globalWarning;
            return interfaceWarning ?? globalWarning;
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

        private static string BuildGuidance(NetworkAdapterInfo adapter, string mac)
        {
            StringBuilder sb = new StringBuilder();
            if (adapter.Kind == AdapterKind.Wireless)
            {
                sb.AppendLine("무선 어댑터는 드라이버/OS 제약으로 두 번째 자리가 2/6/A/E(유니캐스트·로컬 관리 주소)가 아니면 변경이 무시될 수 있습니다.");
                if (!MacAddressUtil.IsLocallyAdministered(mac))
                    sb.AppendLine("무선 어댑터를 선택한 상태에서 '랜덤 MAC 주소'를 눌러 두 번째 자리가 2/6/A/E인 주소(X2/X6/XA/XE-XX-XX-XX-XX-XX)를 만든 뒤 다시 적용해 보세요.");
                if (SafeRandomMacState(adapter.InterfaceGuid) == true)
                    sb.AppendLine("이 Wi-Fi 인터페이스에 '임의 하드웨어 주소'가 켜져 있습니다. 설정 > 네트워크 및 인터넷 > Wi-Fi에서 끄고 다시 시도하세요.");
                else
                    sb.AppendLine("Windows 설정 > 네트워크 및 인터넷 > Wi-Fi의 '임의 하드웨어 주소'가 켜져 있으면 충돌할 수 있으니 끄고 다시 시도하세요.");
            }
            else
            {
                sb.AppendLine("일부 드라이버는 NetworkAddress 값을 지원하지 않거나 로컬 관리 주소(두 번째 자리 2/6/A/E)만 허용합니다.");
                sb.AppendLine("장치 관리자 > 어댑터 속성 > 고급 탭에 '네트워크 주소(Network Address)' 항목이 있는지 확인하세요.");
                if (!MacAddressUtil.IsLocallyAdministered(mac))
                    sb.AppendLine("적용한 MAC은 로컬 관리 주소가 아닙니다(두 번째 자리 2/6/A/E 아님). 드라이버가 이를 거부한다면 두 번째 자리를 2/6/A/E로 바꿔 보세요.");
            }
            sb.AppendLine("드라이버에 따라 재부팅 후에 적용되는 경우도 있습니다.");
            return sb.ToString().TrimEnd();
        }
    }
}
