using System;
using System.Text;

namespace MacChanger.Core
{
    public enum AdapterKind
    {
        Wired,
        Wireless,
        Other
    }

    /// <summary>드롭다운에 표시되는 어댑터 1개의 정보.</summary>
    public class NetworkAdapterInfo
    {
        /// <summary>NetCfgInstanceId / InterfaceGuid. 항상 "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}" 대문자 형식.</summary>
        public string InterfaceGuid { get; set; }

        /// <summary>어댑터(드라이버) 설명. 예: "Intel(R) Ethernet Connection I219-V"</summary>
        public string Description { get; set; }

        /// <summary>네트워크 연결 이름. 예: "이더넷", "Wi-Fi"</summary>
        public string ConnectionName { get; set; }

        /// <summary>열거 시점의 현재 MAC (12자리 hex, 없으면 null)</summary>
        public string CurrentMac { get; set; }

        /// <summary>WMI(MSFT_NetAdapter.PermanentAddress)가 알려준 공장 MAC 힌트. 실제 표시는 매번 IOCTL 로 다시 조회한다.</summary>
        public string PermanentMacHint { get; set; }

        public AdapterKind Kind { get; set; }

        public bool IsVirtual { get; set; }

        public string PnpDeviceId { get; set; }

        /// <summary>어느 열거 경로에서 얻었는지 ("MSFT_NetAdapter" / "Win32_NetworkAdapter" / "GetAdaptersAddresses")</summary>
        public string Source { get; set; }

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case AdapterKind.Wired: return "유선";
                    case AdapterKind.Wireless: return "무선";
                    default: return "기타";
                }
            }
        }

        public string DisplayText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append('[').Append(KindLabel).Append(']');
                if (IsVirtual) sb.Append("(가상)");
                sb.Append(' ');
                sb.Append(string.IsNullOrEmpty(Description) ? "(설명 없음)" : Description);
                if (!string.IsNullOrEmpty(ConnectionName) && ConnectionName != Description)
                    sb.Append(" (").Append(ConnectionName).Append(')');
                sb.Append(" - ");
                sb.Append(CurrentMac != null ? MacAddressUtil.Format(CurrentMac) : "MAC 없음");
                return sb.ToString();
            }
        }

        public override string ToString()
        {
            return DisplayText;
        }

        /// <summary>GUID 문자열을 "{...}" 대문자 형식으로 정규화한다. 파싱 실패 시 null.</summary>
        public static string NormalizeGuid(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            Guid g;
            if (!Guid.TryParse(value.Trim(), out g)) return null;
            return "{" + g.ToString("D").ToUpperInvariant() + "}";
        }

        public static bool LooksWireless(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToUpperInvariant();
            return t.Contains("WIRELESS") || t.Contains("WI-FI") || t.Contains("WIFI") || t.Contains("WLAN")
                || t.Contains("802.11") || t.Contains("무선");
        }
    }
}
