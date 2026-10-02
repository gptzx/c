using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
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

        private static string QueryMac(string interfaceGuid, uint oid)
        {
            if (string.IsNullOrEmpty(interfaceGuid)) throw new ArgumentNullException("interfaceGuid");

            string path = @"\\.\" + interfaceGuid;
            uint[] accessModes = new uint[]
            {
                0,
                NativeMethods.GENERIC_READ,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE
            };

            Exception last = null;
            foreach (uint access in accessModes)
            {
                SafeFileHandle handle = null;
                try
                {
                    handle = NativeMethods.CreateFile(
                        path, access,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (handle == null || handle.IsInvalid)
                    {
                        last = new Win32Exception(Marshal.GetLastWin32Error());
                        continue;   // 다른 접근 모드로 다시 시도
                    }

                    uint oidValue = oid;
                    byte[] outBuffer = new byte[6];
                    uint returned;
                    bool ok = NativeMethods.DeviceIoControl(
                        handle, NativeMethods.IOCTL_NDIS_QUERY_GLOBAL_STATS,
                        ref oidValue, sizeof(uint),
                        outBuffer, (uint)outBuffer.Length,
                        out returned, IntPtr.Zero);
                    if (!ok)
                    {
                        // 장치는 열렸지만 OID를 지원하지 않는 경우: 다른 접근 모드를 더 시도해도 의미 없음
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    if (returned < 6)
                        throw new InvalidOperationException("NDIS 응답 길이가 6바이트 미만입니다 (" + returned + ").");
                    return MacAddressUtil.FromBytes(outBuffer);
                }
                finally
                {
                    if (handle != null) handle.Dispose();
                }
            }
            throw last ?? new InvalidOperationException("NDIS 장치를 열 수 없습니다: " + interfaceGuid);
        }
    }
}
