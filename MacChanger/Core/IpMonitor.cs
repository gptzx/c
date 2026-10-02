using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MacChanger.Core
{
    /// <summary>선택한 어댑터의 현재 IPv4 주소 조회와 "할당 IP 로그" 파일 기록.</summary>
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
        /// 어댑터의 IPv4 주소 목록(쉼표 구분). 어댑터가 IP 스택에 없으면(비활성화 등) null, 주소가 아직 없으면 빈 문자열.
        /// up = 링크가 올라와 있는지(OperationalStatus.Up).
        /// </summary>
        public static string ReadIPv4(string interfaceGuid, out bool up)
        {
            up = false;
            NetworkInterface ni = FindInterface(interfaceGuid);
            if (ni == null) return null;
            up = ni.OperationalStatus == OperationalStatus.Up;
            List<string> ips = new List<string>();
            foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork) ips.Add(u.Address.ToString());
            return string.Join(", ", ips.ToArray());
        }

        /// <summary>169.254.x.x (DHCP 응답이 없을 때 Windows가 붙이는 자동 사설 주소)만 있는지</summary>
        public static bool IsOnlyApipa(string ipv4List)
        {
            if (string.IsNullOrEmpty(ipv4List)) return false;
            foreach (string ip in ipv4List.Split(','))
                if (!ip.Trim().StartsWith("169.254.", StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>실행 파일 옆 로그 파일에 "시각(탭)IP(탭)MAC" 한 줄을 추가한다 (UTF-8). 로그 옵션이 켜져 있을 때만 호출된다.</summary>
        public static void AppendAssignedIp(string exePath, string ipv4List, string mac)
        {
            string path = Path.Combine(Path.GetDirectoryName(exePath), LogFileName);
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + ipv4List + "\t" + MacAddressUtil.Format(mac ?? "") + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(true));
        }
    }
}
