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
        public const string LogFileName = "MacChanger-ip.log";

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
            List<UnicastIPAddressInformation> unicast = new List<UnicastIPAddressInformation>();
            foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork) unicast.Add(u);
            unicast.Sort(delegate(UnicastIPAddressInformation a, UnicastIPAddressInformation b)
            {
                return string.CompareOrdinal(a.Address.ToString(), b.Address.ToString());
            });
            foreach (UnicastIPAddressInformation u in unicast)
            {
                addresses.Add(u.Address.ToString());
                masks.Add(u.IPv4Mask != null ? u.IPv4Mask.ToString() : "?");
            }
            info.Addresses = string.Join(", ", addresses.ToArray());
            info.Masks = string.Join(", ", masks.ToArray());

            List<string> gateways = new List<string>();
            foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                if (g.Address.AddressFamily == AddressFamily.InterNetwork) gateways.Add(g.Address.ToString());
            info.Gateways = string.Join(", ", gateways.ToArray());

            List<string> dns = new List<string>();
            foreach (IPAddress d in props.DnsAddresses)
                if (d.AddressFamily == AddressFamily.InterNetwork) dns.Add(d.ToString());
            info.Dns = string.Join(", ", dns.ToArray());
            return info;
        }

        /// <summary>
        /// 목록에서 169.254.x.x(DHCP 응답이 없을 때 Windows가 붙이는 자동 사설 주소)를 제외한 "실제 할당된" 주소 목록.
        /// 로그 중복 판정과 기록은 이 값으로만 한다 — DHCP 전환 중 169.254 주소가 잠깐 붙었다 떨어져도 같은 줄이 다시 기록되지 않는다.
        /// </summary>
        public static string WithoutApipa(string ipv4List)
        {
            if (string.IsNullOrEmpty(ipv4List)) return string.Empty;
            if (ipv4List.IndexOf("169.254.", StringComparison.Ordinal) < 0) return ipv4List;   // 보통의 경우: 그대로
            List<string> keep = new List<string>();
            foreach (string ip in ipv4List.Split(','))
            {
                string t = ip.Trim();
                if (t.Length > 0 && !t.StartsWith("169.254.", StringComparison.Ordinal)) keep.Add(t);
            }
            return string.Join(", ", keep.ToArray());
        }

        /// <summary>로그 한 줄: [시각(탭)]IP[(탭)MAC] — 시간과 MAC 은 옵션.</summary>
        public static string BuildLogLine(string assignedIps, string mac, bool includeTime, bool includeMac)
        {
            StringBuilder sb = new StringBuilder();
            if (includeTime) sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(assignedIps);
            if (includeMac) sb.Append('\t').Append(MacAddressUtil.Format(mac ?? ""));
            return sb.ToString();
        }

        /// <summary>실행 파일 옆 로그 파일에 한 줄을 추가한다 (UTF-8). 저장 옵션이 켜져 있을 때만 호출된다.</summary>
        public static void AppendLine(string exePath, string line)
        {
            string path = Path.Combine(Path.GetDirectoryName(exePath), LogFileName);
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(true));
        }
    }
}
