using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MacChanger.Core
{
    /// <summary>선택한 어댑터의 현재 IPv4 구성 조회와 "할당 IP 로그" 줄 만들기 / 파일 기록.</summary>
    public static class IpMonitor
    {
        public const string LogFileName = "MacChanger-ip.txt";

        /// <summary>어댑터의 현재 IPv4 구성. 목록은 쉼표로 구분하며 주소가 없으면 빈 문자열.</summary>
        public sealed class IpInfo
        {
            /// <summary>링크가 올라와 있는지(OperationalStatus.Up)</summary>
            public bool Up;
            /// <summary>지금 사용 중인 MAC (12자리 hex, 알 수 없으면 null)</summary>
            public string Mac;
            /// <summary>IPv4 주소 목록(정렬됨) — 로그와 자동 변경의 기준 값</summary>
            public string Addresses;
            /// <summary>주소와 같은 순서의 서브넷 마스크 목록</summary>
            public string Masks;
            /// <summary>기본 게이트웨이 목록</summary>
            public string Gateways;
            /// <summary>DNS 서버 목록 (기본, 보조 … 순서)</summary>
            public string Dns;
        }

        /// <summary>GetAdaptersAddresses(NetworkInterface) 목록에서 GUID가 일치하는 인터페이스. 없으면(비활성화 등) null.</summary>
        public static NetworkInterface FindInterface(string interfaceGuid)
        {
            Guid target;
            if (!Guid.TryParse(interfaceGuid, out target)) return null;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                Guid id;
                if (Guid.TryParse(ni.Id, out id) && id == target) return ni;
            }
            return null;
        }

        /// <summary>어댑터의 IPv4 구성. 어댑터가 IP 스택에 없으면(비활성화 등) null.</summary>
        public static IpInfo ReadIpInfo(string interfaceGuid)
        {
            NetworkInterface ni = FindInterface(interfaceGuid);
            if (ni == null) return null;
            IpInfo info = new IpInfo();
            info.Up = ni.OperationalStatus == OperationalStatus.Up;
            info.Mac = MacAddressUtil.FromBytes(ni.GetPhysicalAddress().GetAddressBytes());

            IPInterfaceProperties props = ni.GetIPProperties();
            List<string> addresses = new List<string>();
            List<string> masks = new List<string>();
            UnicastIPAddressInformationCollection uc = props.UnicastAddresses;   // 인덱서로 읽는다 (foreach 와 같은 순서·같은 항목이고 실행 파일이 더 작다)
            for (int i = 0; i < uc.Count; i++)
            {
                UnicastIPAddressInformation u = uc[i];
                if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.Address.Equals(IPAddress.Any) || !IsUsable(u)) continue;   // 0.0.0.0 은 반환 뒤의 자리표시자
                // 주소 문자열 순(서수 비교)으로 자리를 찾아 마스크와 함께 끼워 넣는다 — 한 어댑터의 주소는 서로 다르므로 정렬 결과가 하나로 정해진다
                string address = u.Address.ToString();
                int at = addresses.Count;
                while (at > 0 && string.CompareOrdinal(addresses[at - 1], address) > 0) at--;
                addresses.Insert(at, address);
                masks.Insert(at, u.IPv4Mask != null ? u.IPv4Mask.ToString() : "?");
            }
            info.Addresses = string.Join(", ", addresses);
            info.Masks = string.Join(", ", masks);

            // 게이트웨이·DNS 컬렉션은 인덱서로 읽는다 (foreach 와 같은 순서·같은 항목이고 실행 파일이 더 작다)
            List<string> gateways = new List<string>();
            GatewayIPAddressInformationCollection gc = props.GatewayAddresses;
            for (int i = 0; i < gc.Count; i++)
                if (gc[i].Address.AddressFamily == AddressFamily.InterNetwork) gateways.Add(gc[i].Address.ToString());
            info.Gateways = string.Join(", ", gateways);

            List<string> dns = new List<string>();
            IPAddressCollection dc = props.DnsAddresses;
            for (int i = 0; i < dc.Count; i++)
                if (dc[i].AddressFamily == AddressFamily.InterNetwork) dns.Add(dc[i].ToString());
            info.Dns = string.Join(", ", dns);
            return info;
        }

        /// <summary>
        /// 중복 주소 감지(DAD)에서 중복으로 판정되었거나(Duplicate) 무효(Invalid)인 주소는 쓸 수 없으므로 뺀다. 임시(Tentative) 상태는 DHCP 가 막 붙인 정상 주소이므로 포함한다.
        /// 상태를 읽을 수 없는 환경이면 포함한다.
        /// </summary>
        private static bool IsUsable(UnicastIPAddressInformation u)
        {
            try
            {
                DuplicateAddressDetectionState state = u.DuplicateAddressDetectionState;
                return state != DuplicateAddressDetectionState.Duplicate && state != DuplicateAddressDetectionState.Invalid;
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// 주소 목록에서 169.254.x.x(DHCP 응답이 없을 때 Windows가 붙이는 자동 사설 주소)를 뺀 "실제 할당된" 주소 목록과, 같은 순서의 서브넷 마스크 목록.
        /// 화면 표시·로그 중복 판정·기록은 이 값으로 한다 — DHCP 전환 중 169.254 주소가 잠깐 붙었다 떨어져도 보이거나 다시 기록되지 않는다.
        /// </summary>
        public static void FilterAssigned(string addresses, string masks, out string assignedAddresses, out string assignedMasks)
        {
            if (string.IsNullOrEmpty(addresses))
            {
                assignedAddresses = assignedMasks = string.Empty;
                return;
            }
            if (addresses.IndexOf("169.254.", StringComparison.Ordinal) < 0)   // 보통의 경우: 그대로
            {
                assignedAddresses = addresses;
                assignedMasks = masks ?? string.Empty;
                return;
            }
            string[] ips = addresses.Split(',');
            string[] ms = string.IsNullOrEmpty(masks) ? new string[0] : masks.Split(',');
            List<string> keepIps = new List<string>();
            List<string> keepMasks = new List<string>();
            for (int i = 0; i < ips.Length; i++)
            {
                string ip = ips[i].Trim();
                if (ip.Length == 0 || ip.IndexOf("169.254.", StringComparison.Ordinal) == 0) continue;   // 169.254. 로 시작
                keepIps.Add(ip);
                keepMasks.Add(i < ms.Length ? ms[i].Trim() : "?");
            }
            assignedAddresses = string.Join(", ", keepIps);
            assignedMasks = string.Join(", ", keepMasks);
        }

        /// <summary>로그 한 줄: [시각(탭)]IP[(탭)서브넷 마스크][(탭)기본 게이트웨이][(탭)MAC] — IP 외의 항목은 옵션. 게이트웨이가 없으면 "(없음)".</summary>
        public static string BuildLogLine(string assignedIps, string masks, string gateways, string mac,
                                          bool includeTime, bool includeMask, bool includeGateway, bool includeMac)
        {
            StringBuilder sb = new StringBuilder();
            if (includeTime) sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(assignedIps);
            if (includeMask) sb.Append('\t').Append(masks);   // null 이면 아무것도 붙지 않는다 (Append(null) = 빈 문자열)
            if (includeGateway) sb.Append('\t').Append(string.IsNullOrEmpty(gateways) ? "(없음)" : gateways);
            if (includeMac) sb.Append('\t').Append(MacAddressUtil.Format(mac));   // Format(null) 도 빈 문자열
            return sb.ToString();
        }

        /// <summary>실행 파일 옆 로그 파일에 한 줄을 추가한다 (ANSI = 시스템 기본 코드 페이지, 한국어 Windows 에서는 CP949). 저장 옵션이 켜져 있을 때만 호출된다.</summary>
        public static void AppendLine(string exePath, string line)
        {
            string path = Path.Combine(Path.GetDirectoryName(exePath), LogFileName);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.Default);
        }
    }
}
