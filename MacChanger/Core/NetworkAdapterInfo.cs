using System;
using System.Text;

namespace MacChanger.Core
{
    public enum AdapterKind
    {
        Wired,
        Wireless,
        Bluetooth,
        Other
    }

    /// <summary>드롭다운에 표시되는 어댑터 1개의 정보. 값은 모두 속성 대신 필드로 둔다 (접근자 메서드가 없는 만큼 실행 파일이 작다).</summary>
    public class NetworkAdapterInfo
    {
        /// <summary>NetCfgInstanceId / InterfaceGuid. 항상 "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}" 대문자 형식.</summary>
        public string InterfaceGuid;

        /// <summary>어댑터(드라이버) 설명. 예: "Intel(R) Ethernet Connection I219-V"</summary>
        public string Description;

        /// <summary>네트워크 연결 이름. 예: "이더넷", "Wi-Fi"</summary>
        public string ConnectionName;

        /// <summary>가장 최근에 읽은 현재 MAC (12자리 hex, 없으면 null). 열거 시점에 채워지고 변경/복구 후 다시 읽어 갱신된다.</summary>
        public string CurrentMac;

        /// <summary>WMI(MSFT_NetAdapter.PermanentAddress)가 알려준 공장 MAC 힌트. 실제 표시는 매번 IOCTL로 다시 조회한다.</summary>
        public string PermanentMacHint;

        public AdapterKind Kind;

        public bool IsVirtual;

        public override string ToString()
        {
            string kind;
            switch (Kind)
            {
                case AdapterKind.Wired: kind = "유선"; break;
                case AdapterKind.Wireless: kind = "무선"; break;
                case AdapterKind.Bluetooth: kind = "블루투스"; break;
                default: kind = "기타"; break;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append('[').Append(kind).Append(']');
            if (IsVirtual) sb.Append("(가상)");
            sb.Append(' ');
            sb.Append(string.IsNullOrEmpty(Description) ? "(설명 없음)" : Description);
            if (!string.IsNullOrEmpty(ConnectionName) && ConnectionName != Description)
                sb.Append(" (").Append(ConnectionName).Append(')');
            sb.Append(" - ");
            sb.Append(CurrentMac != null ? MacAddressUtil.Format(CurrentMac) : "MAC 없음");
            return sb.ToString();
        }

        /// <summary>GUID 문자열을 "{...}" 대문자 형식으로 정규화한다. 파싱 실패 시 null.</summary>
        public static string NormalizeGuid(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            Guid g;
            if (!Guid.TryParse(value.Trim(), out g)) return null;
            return g.ToString("B").ToUpperInvariant();   // "B" = 중괄호로 감싼 "{xxxxxxxx-xxxx-…}"
        }

        /// <summary>설명/연결 이름에 무선 LAN 키워드가 있는지 (미디어 타입을 알 수 없을 때의 보조 판별)</summary>
        public static bool LooksWireless(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToUpperInvariant();
            return t.Contains("WIRELESS") || t.Contains("WI-FI") || t.Contains("WIFI") || t.Contains("WLAN")
                || t.Contains("802.11") || t.Contains("CENTRINO") || t.Contains("무선");
        }

        /// <summary>블루투스 PAN 어댑터 키워드 (Microsoft 스택은 PhysicalMediaType=10, InterfaceType=6으로 보고한다)</summary>
        public static bool LooksBluetooth(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToUpperInvariant();
            return t.Contains("BLUETOOTH") || t.Contains("PERSONAL AREA NETWORK") || t.Contains("블루투스");
        }
    }
}
