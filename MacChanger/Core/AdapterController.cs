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
    /// 어댑터 재시작 / 비활성화 / 활성화.
    /// - Restart: SetupAPI DIF_PROPERTYCHANGE + DICS_PROPCHANGE 한 번 — 장치 관리자(어댑터 속성 창)가 고급 속성 변경을 반영할 때,
    ///   그리고 devcon restart 가 쓰는 방식. "사용 안 함" 상태를 거치지 않고 장치를 멈췄다가 바로 다시 시작한다.
    /// - Disable/Enable: DICS_DISABLE / DICS_ENABLE (devcon 과 같은 방식), 실패 시 WMI Win32_NetworkAdapter → MSFT_NetAdapter 의 Disable()/Enable().
    /// </summary>
    public static class AdapterController
    {
        public const int WmiEnableRetryCount = 3;
        public const int WmiRetryDelayMs = 1500;

        /// <summary>
        /// 재시작(DICS_PROPCHANGE). needReboot 가 true 이면 PnP 관리자가 장치를 멈추지 못해(드라이버 거부 등) 재부팅 필요 플래그만 설정한 것이므로
        /// 어댑터는 옛 설정 그대로 동작 중이다. 어댑터가 "사용 안 함"(CONFIGFLAG_DISABLED) 상태면 PROPCHANGE 로는 올라오지 않으므로 대신 활성화(DICS_ENABLE)한다 —
        /// 시작하면서 NetworkAddress 를 읽으므로 결과는 같다. SetupAPI 경로가 실패하면 예외 — 호출자가 비활성화/활성화로 대신한다 (WMI 에는 재시작이 없다).
        /// </summary>
        public static void Restart(NetworkAdapterInfo adapter, Action<string> log, out bool needReboot)
        {
            if (adapter == null) throw new ArgumentNullException("adapter");
            string error;
            if (!TrySetupApiChangeState(adapter.InterfaceGuid, NativeMethods.DICS_PROPCHANGE, out needReboot, out error))
                throw new InvalidOperationException(error);
            if (needReboot) log("장치 관리자가 즉시 재시작하지 못해 재부팅 필요 플래그를 설정했습니다");
        }

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

            string setupError;
            if (TrySetupApiChangeState(adapter.InterfaceGuid, enable ? NativeMethods.DICS_ENABLE : NativeMethods.DICS_DISABLE, out needReboot, out setupError))
            {
                if (needReboot) log("장치 관리자가 즉시 " + action + "하지 못해 재부팅 필요 플래그를 설정했습니다");
                return;
            }
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
        private static bool TrySetupApiChangeState(string interfaceGuid, uint stateChange, out bool needReboot, out string error)
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

                    if (stateChange == NativeMethods.DICS_PROPCHANGE && IsDisabled(devInfoSet, ref devInfo))
                        stateChange = NativeMethods.DICS_ENABLE;   // "사용 안 함" 어댑터는 재시작 대신 활성화해야 올라온다
                    return ChangeDeviceState(devInfoSet, ref devInfo, stateChange, out needReboot, out error);
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

        /// <summary>DIF_PROPERTYCHANGE 호출 (devcon 과 같은 순서): ENABLE 만 전역을 먼저 시도하고, 모든 상태를 현재 하드웨어 프로필(HwProfile 0)에 적용한다.</summary>
        private static bool ChangeDeviceState(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfo, uint stateChange, out bool needReboot, out string error)
        {
            needReboot = false;
            error = null;

            NativeMethods.SP_PROPCHANGE_PARAMS p = new NativeMethods.SP_PROPCHANGE_PARAMS();
            p.ClassInstallHeader.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_CLASSINSTALL_HEADER));
            p.ClassInstallHeader.InstallFunction = NativeMethods.DIF_PROPERTYCHANGE;
            p.StateChange = stateChange;
            uint size = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_PROPCHANGE_PARAMS));

            if (stateChange == NativeMethods.DICS_ENABLE)
            {
                // devcon과 동일: 전역(GLOBAL) 활성화를 먼저 시도하고 결과는 무시한 뒤, 현재 하드웨어 프로필에 적용한다.
                p.Scope = NativeMethods.DICS_FLAG_GLOBAL;
                if (NativeMethods.SetupDiSetClassInstallParams(devInfoSet, ref devInfo, ref p, size))
                    NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, devInfoSet, ref devInfo);
            }

            p.Scope = NativeMethods.DICS_FLAG_CONFIGSPECIFIC;   // PROPCHANGE 는 Scope 와 무관하게 장치를 멈췄다 다시 시작한다 (MS 문서)
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

        /// <summary>장치의 ConfigFlags 에 CONFIGFLAG_DISABLED 가 켜져 있는지 ("사용 안 함"). 읽지 못하면 false.</summary>
        private static bool IsDisabled(IntPtr devInfoSet, ref NativeMethods.SP_DEVINFO_DATA devInfo)
        {
            uint regType;
            uint required;
            byte[] buffer = new byte[4];
            if (!NativeMethods.SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfo, NativeMethods.SPDRP_CONFIGFLAGS, out regType, buffer, (uint)buffer.Length, out required))
                return false;
            if (required < 4) return false;
            return (BitConverter.ToUInt32(buffer, 0) & NativeMethods.CONFIGFLAG_DISABLED) != 0;
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
                        catch (ManagementException) { }

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
