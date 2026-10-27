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
        /// <summary>어댑터가 실제로 멈췄다 다시 올라왔는지(폴백에서는 멈추기라도 했는지 = 이후 받는 IP는 새 할당). 어댑터를 건드리기 전 실패나 재부팅 보류면 false</summary>
        public bool AdapterRestarted { get; set; }
        /// <summary>실패 시 사용자에게 추가로 보여줄 안내문(무선 제약, 임의 하드웨어 주소 등)</summary>
        public string Guidance { get; set; }
    }

    /// <summary>
    /// 변경 적용 / 원상복구 / IP 갱신 절차를 순서대로 수행한다. 각 단계는 try/catch로 감싸고 한국어 진행 메시지를 log 콜백(상태 라벨)으로 보낸다.
    /// </summary>
    public static class MacChangeService
    {
        /// <summary>어댑터 재시작 후 NDIS 장치가 다시 열릴 때까지 기다리는 최대 시간</summary>
        public const int MacWaitTimeoutMs = 20000;
        /// <summary>재시작 직후 드라이버가 올라올 때까지 NDIS 조회를 다시 시도하는 간격 — 짧을수록 MAC 변경 완료가 빨리 확인된다</summary>
        public const int MacWaitPollMs = 250;
        /// <summary>(폴백 경로) 비활성화 직후 활성화하기 전 잠시 대기</summary>
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
                if (attempt % (1000 / MacWaitPollMs) == 1) log("어댑터 초기화 대기 중... (" + ((attempt - 1) * MacWaitPollMs / 1000 + 1) + "초)");
                Thread.Sleep(MacWaitPollMs);
            }
            // 시간 초과: GetAdaptersAddresses/WMI 값으로 폴백 (NDIS 는 ReadCurrentMac 이 먼저 한 번 더 본다)
            return ReadCurrentMac(adapter, out live);
        }

        /// <summary>RunCycle 의 결과. Fail 이 있으면 실패(나머지는 쓰지 않는다). Deferred 면 재시작이 재부팅 뒤로 보류되어 Current 는 null.</summary>
        private sealed class Cycle
        {
            public MacChangeResult Fail;
            /// <summary>어댑터가 실제로 멈췄다 다시 올라왔는지 (모든 결과의 AdapterRestarted)</summary>
            public bool Restarted;
            public bool Deferred;
            public string TcpipWarning;
            /// <summary>재시작 뒤 읽은 현재 MAC 과 그것을 NDIS 직접 조회로 읽었는지</summary>
            public string Current;
            public bool Live;

            /// <summary>성공 결과: 메시지 뒤에 Tcpip 정리 경고를 붙이고, 재시작이 보류되었으면 RebootRequired.</summary>
            public MacChangeResult Success(string message)
            {
                MacChangeResult result = new MacChangeResult();
                result.Success = true;
                result.RebootRequired = Deferred;
                result.AdapterRestarted = Restarted;
                result.Message = message + WarningSuffix(TcpipWarning);
                return result;
            }

            /// <summary>실패 결과 (어댑터는 재시작되었지만 MAC 이 기대와 다름): 메시지와 안내문.</summary>
            public MacChangeResult Failure(string message, string guidance)
            {
                MacChangeResult result = new MacChangeResult();
                result.AdapterRestarted = Restarted;
                result.Message = message;
                result.Guidance = guidance;
                return result;
            }
        }

        /// <summary>
        /// 변경 적용·원상복구·IP 갱신의 공통 절차: [1/3] 레지스트리 단계(registryStep) → [2/3] Tcpip 값 자동 정리 → [3/3] 어댑터 재시작 → 현재 MAC 재조회.
        /// 재시작은 장치 관리자(어댑터 속성 창)가 고급 속성 변경을 반영할 때와 같은 DICS_PROPCHANGE 한 번이다 — "사용 안 함" 상태를 거치지 않고
        /// 장치를 멈췄다가 바로 다시 시작하므로 빠르고, 드라이버는 다시 올라오면서 레지스트리의 NetworkAddress 를 읽는다.
        /// SetupAPI 경로를 쓸 수 없는 환경에서만 예전 방식(비활성화 → 활성화, WMI 폴백 포함)으로 대신한다.
        /// registryDoneNote 는 레지스트리 단계가 끝난 뒤 재시작에 실패했을 때 붙일 안내("새 MAC은 기록되었습니다." 등, 없으면 null).
        /// 실패 결과(Fail)에는 어댑터가 실제로 재시작되었는지(AdapterRestarted)가 이미 채워져 있다.
        /// </summary>
        private static Cycle RunCycle(NetworkAdapterInfo adapter, Action<string> log, string registryStepLabel, Action registryStep, string registryDoneNote, bool resetDns)
        {
            Cycle cycle = new Cycle();
            MacChangeResult fail = new MacChangeResult();

            log("[1/3] " + registryStepLabel);
            try
            {
                registryStep();
            }
            catch (Exception ex)
            {
                fail.Message = registryStepLabel + " 실패: " + ex.Message;   // 어댑터는 건드리지 않았으므로 되돌릴 것이 없다
                cycle.Fail = fail;
                return cycle;
            }

            // 재시작 전에 정리한다: 장치가 다시 올라오면 DHCP 클라이언트가 이 키를 새로 읽으므로, 임대 값이 없으면 새 IP를 받는다.
            bool cleaned;
            cycle.TcpipWarning = CleanTcpipIfDhcp(adapter.InterfaceGuid, resetDns, log, out cleaned);
            if (cycle.TcpipWarning != null) log("[2/3] 경고: " + cycle.TcpipWarning);

            log("[3/3] 어댑터 재시작");
            try
            {
                AdapterController.Restart(adapter, log, out cycle.Deferred);
                cycle.Restarted = !cycle.Deferred;
            }
            catch (Exception ex)
            {
                log("재시작 실패(" + ex.Message + ") → 비활성화/활성화로 재시도");
                cycle.Fail = DisableEnableCycle(adapter, log, ex.Message, registryDoneNote, out cycle.Restarted, out cycle.Deferred);
                if (cycle.Fail != null) return cycle;
            }
            fail.AdapterRestarted = cycle.Restarted;
            if (cycle.Deferred) return cycle;   // 어댑터가 실제로 재시작되지 않았으므로 검증 생략 — 호출자가 재부팅 안내
            if (cleaned) SweepLeaseCachesAfterRestart(adapter.InterfaceGuid, log);   // Wi-Fi 가 인터페이스를 내리며 쓴 현재 SSID 캐시까지 지운다

            cycle.Current = WaitForCurrentMac(adapter, log, out cycle.Live);
            if (cycle.Current == null)
            {
                fail.Message = "어댑터가 " + (MacWaitTimeoutMs / 1000) + "초 안에 다시 올라오지 않았습니다. 네트워크 연결(ncpa.cpl)에서 어댑터 상태를 확인한 뒤 '새로 고침'을 누르세요.";
                cycle.Fail = fail;
            }
            return cycle;
        }

        /// <summary>
        /// 폴백: 비활성화 → 활성화 (예전 방식). 성공하면 null, 실패하면 실패 결과(AdapterRestarted 는 비활성화가 실제로 됐는지).
        /// restartError 는 앞서 실패한 재시작의 오류, registryDoneNote 는 이미 끝난 레지스트리 단계 안내("새 MAC은 기록되었습니다." 등, 없으면 null) — 실패 메시지에 붙인다.
        /// </summary>
        private static MacChangeResult DisableEnableCycle(NetworkAdapterInfo adapter, Action<string> log, string restartError, string registryDoneNote, out bool restarted, out bool deferred)
        {
            string note = string.IsNullOrEmpty(registryDoneNote) ? "" : registryDoneNote + " ";
            restarted = false;
            deferred = false;
            MacChangeResult fail = new MacChangeResult();
            bool disableDeferred;
            try
            {
                AdapterController.Disable(adapter, log, out disableDeferred);
            }
            catch (Exception ex)
            {
                fail.Message = "어댑터 재시작 실패: " + restartError + " / 비활성화도 실패: " + ex.Message + "\r\n" + note + RestartHint;
                return fail;
            }
            restarted = !disableDeferred;
            fail.AdapterRestarted = restarted;   // 이후 실패해도 어댑터는 이미 중지되었으므로 호출자가 그대로 돌려준다
            Thread.Sleep(AfterDisableDelayMs);
            bool enableDeferred;
            try
            {
                AdapterController.Enable(adapter, log, out enableDeferred);
            }
            catch (Exception ex)
            {
                // 비활성화가 재부팅 뒤로 보류되어 어댑터가 아직 동작 중이면 '사용 안 함 → 사용' 안내가 맞다
                fail.Message = "어댑터 활성화 실패: " + ex.Message + "\r\n" + note + (disableDeferred ? RestartHint : EnableHint);
                return fail;
            }
            deferred = disableDeferred || enableDeferred;
            return null;
        }

        /// <summary>어댑터를 재시작하지 못했을 때(아직 동작 중) 덧붙이는 안내</summary>
        private const string RestartHint = "네트워크 연결(ncpa.cpl)에서 어댑터를 직접 '사용 안 함' → '사용'으로 바꾸세요.";
        /// <summary>(폴백) 비활성화는 됐지만 활성화에 실패했을 때 덧붙이는 안내</summary>
        private const string EnableHint = "네트워크 연결(ncpa.cpl)에서 어댑터를 직접 '사용'으로 바꾸세요.";

        private static string SourceNote(bool live)
        {
            return live ? "" : " (드라이버 직접 조회 불가 — 시스템 값 기준)";
        }

        // ------------------------------------------------------------------
        // 변경 적용
        // ------------------------------------------------------------------
        public static MacChangeResult Apply(NetworkAdapterInfo adapter, string newMac, bool resetDns, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            string mac = MacAddressUtil.Normalize(newMac);
            if (mac == null)
            {
                MacChangeResult invalid = new MacChangeResult();
                invalid.Message = "새 MAC 형식이 올바르지 않습니다.";
                return invalid;
            }
            string guid = adapter.InterfaceGuid;

            Cycle cycle = RunCycle(adapter, log, "새 MAC 기록", delegate { MacRegistry.SetNetworkAddress(guid, mac); }, "새 MAC은 기록되었습니다.", resetDns);
            if (cycle.Fail != null) return cycle.Fail;
            if (cycle.Deferred)
                return cycle.Success("새 MAC " + MacAddressUtil.Format(mac) + " 을(를) 기록했지만 어댑터를 바로 재시작하지 못했습니다. 재부팅 후 적용됩니다.");
            if (string.Equals(cycle.Current, mac, StringComparison.OrdinalIgnoreCase))
            {
                MacChangeResult result = cycle.Success("MAC 변경 완료: " + MacAddressUtil.Format(cycle.Current) + SourceNote(cycle.Live));
                if (adapter.Kind == AdapterKind.Wireless && SafeRandomMacState(guid) == true)
                    result.Message += " — 주의: Wi-Fi '임의 하드웨어 주소'가 켜져 있어 연결 시 MAC이 다시 바뀔 수 있습니다. 설정에서 끄세요.";
                return result;
            }
            return cycle.Failure("드라이버가 새 MAC을 적용하지 않았습니다. 현재 MAC: " + MacAddressUtil.Format(cycle.Current) + SourceNote(cycle.Live),
                BuildGuidance(adapter, mac));
        }

        // ------------------------------------------------------------------
        // 원상복구
        // ------------------------------------------------------------------
        public static MacChangeResult Restore(NetworkAdapterInfo adapter, bool resetDns, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            string guid = adapter.InterfaceGuid;
            bool existed = false;

            Cycle cycle = RunCycle(adapter, log, "MAC 설정 삭제", delegate { existed = MacRegistry.DeleteNetworkAddress(guid); }, "MAC 설정은 지웠습니다.", resetDns);
            if (cycle.Fail != null) return cycle.Fail;
            if (cycle.Deferred)
                return cycle.Success((existed ? "MAC 설정을 지웠지만 " : "지울 MAC 설정은 없었고 ") + "어댑터를 바로 재시작하지 못했습니다. 재부팅 후 공장 MAC으로 돌아갑니다.");
            string permanent = ReadPermanentMac(adapter);
            if (permanent == null)
                return cycle.Success("복구 완료 (공장 MAC 조회 불가 — 비교 생략). 현재 MAC: " + MacAddressUtil.Format(cycle.Current) + SourceNote(cycle.Live));
            if (string.Equals(cycle.Current, permanent, StringComparison.OrdinalIgnoreCase))
                return cycle.Success("공장 MAC으로 복구 완료: " + MacAddressUtil.Format(cycle.Current) + SourceNote(cycle.Live));
            return cycle.Failure("복구 후에도 현재 MAC(" + MacAddressUtil.Format(cycle.Current) + ")이 공장 MAC(" + MacAddressUtil.Format(permanent) + ")과 다릅니다." + SourceNote(cycle.Live),
                (adapter.Kind == AdapterKind.Wireless && SafeRandomMacState(guid) == true)
                    ? "이 Wi-Fi 인터페이스에 Windows '임의 하드웨어 주소'가 켜져 있습니다. 설정 > 네트워크 및 인터넷 > Wi-Fi에서 끄면 공장 MAC이 사용됩니다."
                    : "재부팅 후 다시 확인하거나, 무선 어댑터라면 Windows 설정의 '임의 하드웨어 주소'가 켜져 있는지 확인하세요.");
        }

        // ------------------------------------------------------------------
        // IP 갱신
        // ------------------------------------------------------------------
        /// <summary>
        /// IP 갱신: NetworkAddress 는 그대로 두어 MAC 은 바꾸지 않고, Tcpip 값 자동 정리(EnableDHCP = 1 인 경우) → 어댑터 재시작(DICS_PROPCHANGE)으로 IP 만 새로 받는다.
        /// (원상복구가 이미 공장 MAC 인 어댑터에서 하던 일과 같다.) 재시작 전후 MAC 을 비교해 실제로 유지되었는지 알려준다.
        /// </summary>
        public static MacChangeResult RenewIp(NetworkAdapterInfo adapter, bool resetDns, Action<string> log)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            string before = ReadCurrentMac(adapter);   // 어댑터가 꺼져 있으면 null — 그때는 "유지" 여부를 말하지 않는다

            Cycle cycle = RunCycle(adapter, log, "MAC 설정 유지 (변경 없음)", delegate { }, null, resetDns);
            if (cycle.Fail != null) return cycle.Fail;
            if (cycle.Deferred) return cycle.Success("어댑터를 바로 재시작하지 못했습니다. 재부팅 후 IP가 새로 할당됩니다.");
            string shown = MacAddressUtil.Format(cycle.Current) + SourceNote(cycle.Live);
            if (before == null) return cycle.Success("IP 갱신 완료 — 현재 MAC: " + shown);
            if (string.Equals(before, cycle.Current, StringComparison.OrdinalIgnoreCase)) return cycle.Success("IP 갱신 완료 — 현재 MAC(유지): " + shown);
            // 보류 중이던 NetworkAddress 값이 이번 재시작에 적용되었거나 Wi-Fi 임의 하드웨어 주소가 바뀐 경우
            return cycle.Success("IP 갱신 완료 — 현재 MAC " + shown + " 이(가) 재시작 전(" + MacAddressUtil.Format(before) + ")과 다릅니다. "
                + "(보류 중이던 MAC 설정이 적용되었거나 Wi-Fi '임의 하드웨어 주소'가 켜져 있음)");
        }

        // ------------------------------------------------------------------
        // 내부 헬퍼
        // ------------------------------------------------------------------

        /// <summary>
        /// 재시작 직후 DHCP 임대 캐시 하위 키를 한 번 더 지운다. Wi-Fi 는 네트워크를 떠날 때(인터페이스가 내려갈 때) 현재 SSID 의 임대를 캐시에 쓰므로
        /// 재시작 전 정리만으로는 남을 수 있고, 남아 있으면 같은 SSID 에 다시 붙을 때 옛 IP 가 그대로 돌아온다. 실패는 진행 로그에만 남긴다.
        /// </summary>
        private static void SweepLeaseCachesAfterRestart(string guid, Action<string> log)
        {
            try
            {
                int n = MacRegistry.DeleteLeaseCacheSubKeys(guid);
                if (n > 0) log("재시작 후 SSID 별 임대 캐시 " + n + "개 추가 삭제");
            }
            catch (Exception ex)
            {
                log("재시작 후 임대 캐시 정리 실패: " + ex.Message);
            }
        }

        /// <summary>
        /// Tcpip 값 자동 정리 (재시작 직전에 수행). 인터페이스 키의 EnableDHCP가 1이면 그 키의 값(EnableDHCP 는 항상, NameServer 는 값이 있고
        /// resetDns 가 false 면 남김)과 전역 Tcpip\Parameters의 DhcpDomain/DhcpNameServer를 삭제한다. EnableDHCP가 0(고정 IP)이거나 값/키가 없으면 건너뛴다.
        /// 실패해도 예외를 던지지 않고 경고 문자열을 돌려준다 (없으면 null).
        /// </summary>
        private static string CleanTcpipIfDhcp(string guid, bool resetDns, Action<string> log, out bool cleaned)
        {
            const string stepLabel = "[2/3]";
            cleaned = false;
            try
            {
                bool keyExists;
                int? enableDhcp = MacRegistry.GetEnableDhcp(guid, out keyExists);
                if (!keyExists || enableDhcp == null || enableDhcp.Value != 1)
                {
                    log(stepLabel + " Tcpip 값 정리 건너뜀 (" + (!keyExists ? "IP 설정 키 없음" : enableDhcp == null ? "DHCP 사용 여부 값 없음" : "고정 IP — 설정 유지") + ")");
                    return null;
                }
                log(stepLabel + " DHCP 사용 중 → Tcpip 값 자동 정리");
                cleaned = true;
            }
            catch (Exception ex)
            {
                return "DHCP 사용 여부 확인 실패: " + ex.Message;
            }

            // 인터페이스 값 삭제와 전역 값 삭제는 서로 독립이므로 한쪽이 실패해도 다른 쪽은 시도한다.
            string interfaceWarning = null;
            try
            {
                int leaseCaches;
                bool keptNameServer = MacRegistry.CleanTcpipInterfaceValues(guid, resetDns, out leaseCaches);
                log(stepLabel + (keptNameServer
                    ? " Tcpip 값 정리: EnableDHCP 와 NameServer(직접 지정한 DNS)만 남김"
                    : resetDns ? " Tcpip 값 정리: EnableDHCP 만 남김 (DNS 초기화)"
                               : " Tcpip 값 정리: EnableDHCP 만 남김")
                    + (leaseCaches > 0 ? " + SSID 별 임대 캐시 " + leaseCaches + "개 삭제" : ""));
            }
            catch (Exception ex)
            {
                interfaceWarning = "어댑터 IP 설정 정리 실패: " + ex.Message;
            }
            string globalWarning = null;
            try
            {
                MacRegistry.DeleteGlobalDhcpValues();
            }
            catch (Exception ex)
            {
                globalWarning = "전역 DHCP DNS 값 삭제 실패: " + ex.Message;
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
