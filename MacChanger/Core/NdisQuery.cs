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
        /// <summary>oid(OID_802_3_PERMANENT_ADDRESS / OID_802_3_CURRENT_ADDRESS)로 읽은 6바이트 MAC(12자리 hex). 장치를 열 수 없거나 드라이버가 OID 를 거부하거나 주소가 모두 0 이면 null.</summary>
        public static string QueryMac(string interfaceGuid, uint oid)
        {
            string path = @"\\.\" + interfaceGuid;
            byte[] outBuffer = new byte[6];
            foreach (uint access in new uint[] { 0, NativeMethods.GENERIC_READ, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE })   // 접근 권한 없이 → 읽기 → 읽기/쓰기 순
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
                    if (!ok || returned < 6) return null;
                    string mac = MacAddressUtil.FromBytes(outBuffer);
                    return MacAddressUtil.IsAllZero(mac) ? null : mac;   // 모두 0 = 쓸 주소 없음 (미니포트 초기화 전이거나 드라이버가 주소를 알려 주지 않음)
                }
            }
            return null;
        }
    }
}
