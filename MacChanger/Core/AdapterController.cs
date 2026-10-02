using System;
using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MacChanger.Native;

namespace MacChanger.Core
{
    /// <summary>
    /// 어댑터 비활성화/활성화.
    /// 1) SetupAPI DIF_PROPERTYCHANGE (DICS_DISABLE / DICS_ENABLE) — devcon과 같은 방식
    /// 2) 실패 시 WMI Win32_NetworkAdapter.Disable()/Enable() → MSFT_NetAdapter.Disable()/Enable()
    /// </summary>
    public static class AdapterController
    {
        public const int WmiEnableRetryCount = 3;
        public const int WmiRetryDelayMs = 1500;

        /// <summary>
        /// 비활성화. needReboot가 true이면 장치 관리자(PnP)가 즉시 중지하지 못해 재부팅 필요 플래그(DI_NEEDREBOOT/DI_NEEDRESTART)를
        /// 설정한 것이므로 어댑터는 아직 동작 중이다 (devcon의 "Disabled on reboot"와 같은 상태).
        /// </summary>
        public static void Disable(NetworkAdapterInfo adapter, Action<string> log, out bool needReboot)
        {
            SetState(adapter, false, log, out needReboot);
        }

        public static void Enable(NetworkAdapterInfo adapter, Action<string> log, out bool needReboot)
        {
            SetState(adapter, true, log, out needReboot);
        }

        /// <summary>상태 변경. 모든 경로가 실패하면 예외. WMI 경로는 재부팅 필요 여부를 알려주지 않으므로 needReboot=false.</summary>
        private static void SetState(NetworkAdapterInfo adapter, bool enable, Action<string> log, out bool needReboot)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            string action = enable ? "활성화" : "비활성화";
            needReboot = false;

            string setupError;
            if (TrySetupApiChangeState(adapter.InterfaceGuid, enable, out needReboot, out setupError))
            {
                if (needReboot) log("장치 관리자가 즉시 " + action + "하지 못해 재부팅 필요 플래그를 설정했습니다");
                return;
            }
            needReboot = false;
            log("SetupAPI " + action + " 실패(" + setupError + ") → WMI로 재시도");

            int attempts = enable ? WmiEnableRetryCount : 1;
            string wmiError = null;
            for (int i = 0; i < attempts; i++)
            {
                if (i > 0)
                {
                    Thread.Sleep(WmiRetryDelayMs);
                    log("WMI " + action + " 재시도 " + (i + 1) + "/" + attempts);
                }
                if (TryWmiChangeState(adapter.InterfaceGuid, enable, out wmiError)) return;
            }
            throw new InvalidOperationException("어댑터 " + action + " 실패 — SetupAPI: " + setupError + " / WMI: " + wmiError);
        }

        // ------------------------------------------------------------------
        // SetupAPI
        // ------------------------------------------------------------------
        private static bool TrySetupApiChangeState(string interfaceGuid, bool enable, out bool needReboot, out string error)
        {
            needReboot = false;
            error = null;
            Guid netClass = NativeMethods.GUID_DEVCLASS_NET;
            IntPtr devInfoSet = IntPtr.Zero;
            try
            {
                devInfoSet = NativeMethods.SetupDiGetClassDevs(ref netClass, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
                if (devInfoSet == NativeMethods.INVALID_HANDLE_VALUE || devInfoSet == IntPtr.Zero)
                {
                    error = "SetupDiGetClassDevs: " + Win32Message(Marshal.GetLastWin32Error());
                    return false;
                }

                NativeMethods.SP_DEVINFO_DATA devInfo = new NativeMethods.SP_DEVINFO_DATA();
                devInfo.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                for (uint index = 0; ; index++)
                {
                    if (!NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, index, ref devInfo))
                    {
                        int e = Marshal.GetLastWin32Error();
                        error = (e == NativeMethods.ERROR_NO_MORE_ITEMS)
                            ? "SetupAPI 장치 목록에서 어댑터(" + interfaceGuid + ")를 찾지 못했습니다."
                            : "SetupDiEnumDeviceInfo: " + Win32Message(e);
                        return false;
                    }

                    // SPDRP_DRIVER = "{4D36E972-...}\00XX" → 해당 키의 NetCfgInstanceId와 비교
                    string driverKey = GetDeviceRegistryString(devInfoSet, ref devInfo, NativeMethods.SPDRP_DRIVER);
                    if (string.IsNullOrEmpty(driverKey)) continue;
                    string id;
                    try { id = MacRegistry.ReadNetCfgInstanceId(driverKey); }
                    catch (Exception) { continue; }
                    if (id == null || !string.Equals(id, interfaceGuid, StringComparison.OrdinalIgnoreCase)) continue;

                    return ChangeDeviceState(devInfoSet, ref devInfo, enable, out needReboot, out error);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (devInfoSet != IntPtr.Zero && devInfoSet != NativeMethods.INVALID_HANDLE_VALUE)
                    NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
            }
        }

        private static bool ChangeDeviceState(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfo, bool enable, out bool needReboot, out string error)
        {
            needReboot = false;
            error = null;

            NativeMethods.SP_PROPCHANGE_PARAMS p = new NativeMethods.SP_PROPCHANGE_PARAMS();
            p.ClassInstallHeader.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_CLASSINSTALL_HEADER));
            p.ClassInstallHeader.InstallFunction = NativeMethods.DIF_PROPERTYCHANGE;
            p.StateChange = enable ? NativeMethods.DICS_ENABLE : NativeMethods.DICS_DISABLE;
            p.HwProfile = 0;
            uint size = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_PROPCHANGE_PARAMS));

            if (enable)
            {
                // devcon과 동일: 전역(GLOBAL) 활성화를 먼저 시도하고 결과는 무시한 뒤, 현재 하드웨어 프로필에 적용한다.
                p.Scope = NativeMethods.DICS_FLAG_GLOBAL;
                if (NativeMethods.SetupDiSetClassInstallParams(devInfoSet, ref devInfo, ref p, size))
                    NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, devInfoSet, ref devInfo);
            }

            p.Scope = NativeMethods.DICS_FLAG_CONFIGSPECIFIC;
            p.HwProfile = 0;
            if (!NativeMethods.SetupDiSetClassInstallParams(devInfoSet, ref devInfo, ref p, size))
            {
                error = "SetupDiSetClassInstallParams: " + Win32Message(Marshal.GetLastWin32Error());
                return false;
            }
            if (!NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, devInfoSet, ref devInfo))
            {
                int e = Marshal.GetLastWin32Error();
                if (e == NativeMethods.ERROR_IN_WOW64)
                    error = "SetupDiCallClassInstaller: 64비트 Windows에서 32비트 프로세스로는 호출할 수 없습니다(ERROR_IN_WOW64). "
                          + "AnyCPU(64비트)로 빌드된 실행 파일을 사용하세요.";
                else
                    error = "SetupDiCallClassInstaller: " + Win32Message(e);
                return false;
            }

            needReboot = CheckNeedReboot(devInfoSet, ref devInfo);
            return true;
        }

        private static bool CheckNeedReboot(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfo)
        {
            try
            {
                NativeMethods.SP_DEVINSTALL_PARAMS ip = new NativeMethods.SP_DEVINSTALL_PARAMS();
                ip.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_DEVINSTALL_PARAMS));
                if (!NativeMethods.SetupDiGetDeviceInstallParams(devInfoSet, ref devInfo, ref ip)) return false;
                return (ip.Flags & (NativeMethods.DI_NEEDREBOOT | NativeMethods.DI_NEEDRESTART)) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetDeviceRegistryString(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfo, uint property)
        {
            uint regType;
            uint required;
            byte[] buffer = new byte[1024];
            if (!NativeMethods.SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfo, property, out regType, buffer, (uint)buffer.Length, out required))
            {
                int e = Marshal.GetLastWin32Error();
                if (e != NativeMethods.ERROR_INSUFFICIENT_BUFFER || required == 0) return null;
                buffer = new byte[required];
                if (!NativeMethods.SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfo, property, out regType, buffer, (uint)buffer.Length, out required))
                    return null;
            }
            int length = (int)Math.Min(required, (uint)buffer.Length);
            return Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0');
        }

        private static string Win32Message(int errorCode)
        {
            return new Win32Exception(errorCode).Message + " (0x" + errorCode.ToString("X8", CultureInfo.InvariantCulture) + ")";
        }

        // ------------------------------------------------------------------
        // WMI 폴백
        // ------------------------------------------------------------------
        private static bool TryWmiChangeState(string interfaceGuid, bool enable, out string error)
        {
            string method = enable ? "Enable" : "Disable";
            StringBuilder errors = new StringBuilder();

            // 1) root\cimv2 Win32_NetworkAdapter (Vista 이상)
            try
            {
                if (InvokeWmiMethod(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapter WHERE GUID = '" + interfaceGuid + "'", method, errors))
                {
                    error = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                errors.Append("Win32_NetworkAdapter: ").Append(ex.Message).Append("; ");
            }

            // 2) root\StandardCimv2 MSFT_NetAdapter (Windows 8 이상)
            try
            {
                if (InvokeWmiMethod(@"root\StandardCimv2", "SELECT * FROM MSFT_NetAdapter WHERE InterfaceGuid = '" + interfaceGuid + "'", method, errors))
                {
                    error = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                errors.Append("MSFT_NetAdapter: ").Append(ex.Message).Append("; ");
            }

            error = errors.Length > 0 ? errors.ToString().TrimEnd(' ', ';') : "WMI에서 어댑터를 찾지 못했습니다.";
            return false;
        }

        private static bool InvokeWmiMethod(string scopePath, string query, string method, StringBuilder errors)
        {
            using (ManagementObjectSearcher searcher = AdapterEnumerator.CreateSearcher(scopePath, query))
            using (ManagementObjectCollection items = searcher.Get())
            {
                bool found = false;
                foreach (ManagementObject mo in items)
                {
                    using (mo)
                    {
                        found = true;
                        ManagementBaseObject inParams = null;
                        try { inParams = mo.GetMethodParameters(method); }
                        catch (ManagementException) { inParams = null; }

                        uint code;
                        using (ManagementBaseObject outParams = mo.InvokeMethod(method, inParams, null))
                        {
                            object rv = outParams != null ? outParams["ReturnValue"] : null;
                            code = rv == null ? 0u : Convert.ToUInt32(rv, CultureInfo.InvariantCulture);
                        }
                        if (code == 0) return true;
                        errors.Append(scopePath).Append(" ").Append(method).Append("() 반환값 ").Append(code).Append("; ");
                    }
                }
                if (!found) errors.Append(scopePath).Append(": 어댑터 없음; ");
                return false;
            }
        }
    }
}
