using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MacChanger.Native;

namespace MacChanger.Core
{
    /// <summary>
    /// DHCP 서버에 IP 주소를 반환한다 — ipconfig /release 가 쓰는 iphlpapi IpReleaseAddress 를 직접 호출한다 (외부 명령 없이).
    /// GetInterfaceInfo 가 주는 IPv4 어댑터 목록("\DEVICE\TCPIP_{GUID}" 이름 + 인덱스)에서 어댑터를 찾아 그 항목으로 반환한다.
    /// DHCP 클라이언트가 서버에 DHCPRELEASE 를 보내고 주소를 내리므로, 어댑터가 다시 올라오거나 IP 갱신을 하기 전까지 IP 가 없다.
    /// </summary>
    public static class DhcpRelease
    {
        /// <summary>반환 시도. 성공하면 true. 실패하면 false 와 이유(IPv4 어댑터 목록에서 못 찾음, API 오류 등).</summary>
        public static bool Release(string interfaceGuid, out string error)
        {
            NativeMethods.IP_ADAPTER_INDEX_MAP map;
            if (!TryFindAdapter(interfaceGuid, out map, out error)) return false;
            int rc = NativeMethods.IpReleaseAddress(ref map);
            if (rc == 0) return true;
            error = ApiError("IpReleaseAddress", rc);
            return false;
        }

        /// <summary>GetInterfaceInfo 목록에서 GUID 가 맞는 항목을 찾는다.</summary>
        private static bool TryFindAdapter(string interfaceGuid, out NativeMethods.IP_ADAPTER_INDEX_MAP map, out string error)
        {
            map = new NativeMethods.IP_ADAPTER_INDEX_MAP();
            error = null;
            uint size = 0;
            int rc = NativeMethods.GetInterfaceInfo(IntPtr.Zero, ref size);
            if (rc == NativeMethods.ERROR_NO_DATA || size == 0)
            {
                error = "IPv4 를 쓰는 어댑터가 없습니다 (GetInterfaceInfo).";
                return false;
            }
            if (rc != 0 && rc != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
            {
                error = ApiError("GetInterfaceInfo", rc);
                return false;
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                rc = NativeMethods.GetInterfaceInfo(buffer, ref size);
                if (rc != 0)
                {
                    error = ApiError("GetInterfaceInfo", rc);
                    return false;
                }
                int count = Marshal.ReadInt32(buffer);   // LONG NumAdapters, 바로 뒤에 항목 배열 (260바이트씩, 패딩 없음)
                int entrySize = Marshal.SizeOf(typeof(NativeMethods.IP_ADAPTER_INDEX_MAP));
                for (int i = 0; i < count; i++)
                {
                    IntPtr entryPtr = IntPtr.Add(buffer, 4 + i * entrySize);
                    NativeMethods.IP_ADAPTER_INDEX_MAP entry = (NativeMethods.IP_ADAPTER_INDEX_MAP)Marshal.PtrToStructure(entryPtr, typeof(NativeMethods.IP_ADAPTER_INDEX_MAP));
                    if (NameMatchesGuid(entry.Name, interfaceGuid))
                    {
                        map = entry;
                        return true;
                    }
                }
                error = "IPv4 어댑터 목록에서 어댑터(" + interfaceGuid + ")를 찾지 못했습니다 (어댑터가 꺼져 있거나 IPv4 가 없음).";
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ApiError(string api, int rc)
        {
            return api + ": " + new Win32Exception(rc).Message + " (" + rc + ")";
        }

        /// <summary>"\DEVICE\TCPIP_{GUID}" 이름이 어댑터 GUID 에 해당하는지 (중괄호 유무·대소문자 무시).</summary>
        public static bool NameMatchesGuid(string adapterName, string interfaceGuid)
        {
            if (string.IsNullOrEmpty(adapterName) || string.IsNullOrEmpty(interfaceGuid)) return false;
            string bare = interfaceGuid.Trim('{', '}');
            return bare.Length > 0 && adapterName.IndexOf("{" + bare + "}", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
