using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MacChanger.Core
{
    /// <summary>선택한 어댑터의 현재 IPv4 주소 조회와 "할당 IP 로그" 줄 만들기 / 파일 기록.</summary>
    public static class IpMonitor
    {
        public const string LogFileName = "MacChanger-ip.log";

        /// <summary>GetAdaptersAddresses(NetworkInterface) 목록에서 GUID가 일치하는 인터페이스. 없으면(비활성화 등) null.</summary>
        public static NetworkInterface FindInterface(string interfaceGuid)
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                if (string.Equals(NetworkAdapterInfo.NormalizeGuid(ni.Id), interfaceGuid, StringComparison.OrdinalIgnoreCase)) return ni;
            return null;
        }

        /// <summary>
        /// 어댑터의 IPv4 주소 목록(쉼표 구분, 정렬됨). 어댑터가 IP 스택에 없으면(비활성화 등) null, 주소가 아직 없으면 빈 문자열.
        /// up = 링크가 올라와 있는지(OperationalStatus.Up), mac = 지금 사용 중인 MAC(12자리 hex, 알 수 없으면 null).
        /// </summary>
        public static string ReadIPv4(string interfaceGuid, out bool up, out string mac)
        {
            up = false;
            mac = null;
            NetworkInterface ni = FindInterface(interfaceGuid);
            if (ni == null) return null;
            up = ni.OperationalStatus == OperationalStatus.Up;
            mac = MacAddressUtil.FromBytes(ni.GetPhysicalAddress().GetAddressBytes());
            List<string> ips = new List<string>();
            foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork) ips.Add(u.Address.ToString());
            ips.Sort(StringComparer.Ordinal);
            return string.Join(", ", ips.ToArray());
        }

        /// <summary>
        /// 목록에서 169.254.x.x(DHCP 응답이 없을 때 Windows가 붙이는 자동 사설 주소)를 제외한 "실제 할당된" 주소 목록.
        /// 로그 중복 판정과 기록은 이 값으로만 한다 — DHCP 전환 중 169.254 주소가 잠깐 붙었다 떨어져도 같은 줄이 다시 기록되지 않는다.
        /// </summary>
        public static string WithoutApipa(string ipv4List)
        {
            if (string.IsNullOrEmpty(ipv4List)) return string.Empty;
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
