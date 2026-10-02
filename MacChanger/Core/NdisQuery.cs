using System;
using MacChanger.Native;
using Microsoft.Win32.SafeHandles;

namespace MacChanger.Core
{
    /// <summary>
    /// \\.\{GUID} NDIS 장치에 IOCTL_NDIS_QUERY_GLOBAL_STATS를 보내 OID_802_3_PERMANENT_ADDRESS(공장 MAC) /
    /// OID_802_3_CURRENT_ADDRESS(현재 MAC)를 읽는다. 파일에 저장하지 않고 호출할 때마다 조회한다.
    /// </summary>
    public static class NdisQuery
    {
        public static string QueryPermanentMac(string interfaceGuid)
        {
            return QueryMac(interfaceGuid, NativeMethods.OID_802_3_PERMANENT_ADDRESS);
        }

        public static string QueryCurrentMac(string interfaceGuid)
        {
            return QueryMac(interfaceGuid, NativeMethods.OID_802_3_CURRENT_ADDRESS);
        }

        private static readonly uint[] AccessModes = new uint[]
        {
            0,
            NativeMethods.GENERIC_READ,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE
        };

        /// <summary>6바이트 MAC(12자리 hex). 장치를 열 수 없거나 드라이버가 OID 를 거부하면 null.</summary>
        private static string QueryMac(string interfaceGuid, uint oid)
        {
            string path = @"\\.\" + interfaceGuid;
            byte[] outBuffer = new byte[6];
            foreach (uint access in AccessModes)
            {
                using (SafeFileHandle handle = NativeMethods.CreateFile(
                    path, access, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid) continue;   // 다른 접근 모드로 다시 시도
                    uint oidValue = oid;
                    uint returned;
                    bool ok = NativeMethods.DeviceIoControl(
                        handle, NativeMethods.IOCTL_NDIS_QUERY_GLOBAL_STATS,
                        ref oidValue, sizeof(uint), outBuffer, (uint)outBuffer.Length, out returned, IntPtr.Zero);
                    // 장치는 열렸지만 OID 를 지원하지 않는 경우: 다른 접근 모드를 더 시도해도 의미 없음
                    return ok && returned >= 6 ? MacAddressUtil.FromBytes(outBuffer) : null;
                }
            }
            return null;
        }
    }
}
