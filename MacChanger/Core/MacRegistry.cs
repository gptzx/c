using System;
using System.Collections.Generic;
using System.Security;
using Microsoft.Win32;

namespace MacChanger.Core
{
    /// <summary>
    /// HKLM 레지스트리 접근. 64비트 OS 에서 32비트 프로세스로 실행되더라도 항상 RegistryView.Registry64 로
    /// 네이티브(64비트) 뷰를 열어 WOW64 리다이렉션 문제를 피한다. (32비트 OS 에서는 Registry64 지정이 무시된다.)
    /// 이 프로그램은 자기 자신의 설정을 위한 레지스트리 키를 절대 만들지 않는다.
    /// </summary>
    public static class MacRegistry
    {
        public const string NetClassGuid = "{4D36E972-E325-11CE-BFC1-08002BE10318}";
        public const string NetClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\" + NetClassGuid;
        public const string TcpipInterfacesKeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        public const string WlanSvcInterfacesKeyPath = @"SOFTWARE\Microsoft\WlanSvc\Interfaces";

        public const string NetCfgInstanceIdValueName = "NetCfgInstanceId";
        public const string NetworkAddressValueName = "NetworkAddress";
        public const string EnableDhcpValueName = "EnableDHCP";
        public const string RandomMacStateValueName = "RandomMacState";

        private static RegistryKey OpenHklm64()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        /// <summary>
        /// Control\Class\{4D36E972-...} 아래 00XX 하위 키를 모두 돌며 NetCfgInstanceId 가 interfaceGuid 와 같은 키 이름("0001" 등)을 찾는다.
        /// 없으면 null.
        /// </summary>
        public static string FindClassSubKeyName(string interfaceGuid)
        {
            if (string.IsNullOrEmpty(interfaceGuid)) throw new ArgumentNullException("interfaceGuid");

            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey classKey = hklm.OpenSubKey(NetClassKeyPath, false))
            {
                if (classKey == null)
                    throw new InvalidOperationException("네트워크 클래스 키를 열 수 없습니다: HKLM\\" + NetClassKeyPath);

                foreach (string name in classKey.GetSubKeyNames())
                {
                    string id = null;
                    try
                    {
                        using (RegistryKey sub = classKey.OpenSubKey(name, false))
                        {
                            if (sub != null) id = sub.GetValue(NetCfgInstanceIdValueName) as string;
                        }
                    }
                    catch (SecurityException) { continue; }        // 예: "Properties" 하위 키는 관리자도 열 수 없다
                    catch (UnauthorizedAccessException) { continue; }

                    if (id == null) continue;
                    string normalized = NetworkAdapterInfo.NormalizeGuid(id);
                    if (normalized != null && string.Equals(normalized, interfaceGuid, StringComparison.OrdinalIgnoreCase))
                        return name;
                }
            }
            return null;
        }

        /// <summary>SPDRP_DRIVER 값("{4D36E972-...}\0001")으로 지정된 드라이버 키에서 NetCfgInstanceId 를 읽는다.</summary>
        public static string ReadNetCfgInstanceId(string driverKeyRelativePath)
        {
            if (string.IsNullOrEmpty(driverKeyRelativePath)) return null;
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driverKeyRelativePath, false))
            {
                if (key == null) return null;
                return NetworkAdapterInfo.NormalizeGuid(key.GetValue(NetCfgInstanceIdValueName) as string);
            }
        }

        private static RegistryKey OpenAdapterClassKey(string interfaceGuid, bool writable, out string subKeyName)
        {
            subKeyName = FindClassSubKeyName(interfaceGuid);
            if (subKeyName == null)
                throw new InvalidOperationException("NetCfgInstanceId 가 " + interfaceGuid + " 인 클래스 하위 키(00XX)를 찾지 못했습니다.");

            using (RegistryKey hklm = OpenHklm64())
            {
                RegistryKey key = hklm.OpenSubKey(NetClassKeyPath + "\\" + subKeyName, writable);
                if (key == null)
                    throw new InvalidOperationException("클래스 하위 키를 열 수 없습니다: " + subKeyName);
                return key;
            }
        }

        /// <summary>현재 레지스트리에 설정된 NetworkAddress 값. 없으면 null.</summary>
        public static string GetNetworkAddress(string interfaceGuid, out string subKeyName)
        {
            using (RegistryKey key = OpenAdapterClassKey(interfaceGuid, false, out subKeyName))
            {
                return key.GetValue(NetworkAddressValueName) as string;
            }
        }

        /// <summary>NetworkAddress(REG_SZ, 하이픈 없는 12자리) 를 쓰고 다시 읽어 검증한다. 기록한 하위 키 이름을 돌려준다.</summary>
        public static string SetNetworkAddress(string interfaceGuid, string mac12)
        {
            string normalized = MacAddressUtil.Normalize(mac12);
            if (normalized == null) throw new ArgumentException("MAC 형식이 올바르지 않습니다.", "mac12");

            string subKeyName;
            using (RegistryKey key = OpenAdapterClassKey(interfaceGuid, true, out subKeyName))
            {
                key.SetValue(NetworkAddressValueName, normalized, RegistryValueKind.String);
                key.Flush();
                string readBack = key.GetValue(NetworkAddressValueName) as string;
                if (!string.Equals(readBack, normalized, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("NetworkAddress 를 썼지만 다시 읽은 값이 다릅니다: " + (readBack ?? "(null)"));
            }
            return subKeyName;
        }

        /// <summary>NetworkAddress 값을 삭제한다. 값이 있었으면 true.</summary>
        public static bool DeleteNetworkAddress(string interfaceGuid, out string subKeyName)
        {
            using (RegistryKey key = OpenAdapterClassKey(interfaceGuid, true, out subKeyName))
            {
                if (key.GetValue(NetworkAddressValueName) == null) return false;
                key.DeleteValue(NetworkAddressValueName, false);
                key.Flush();
                return true;
            }
        }

        /// <summary>
        /// Tcpip\Parameters\Interfaces\{GUID} 키에서 EnableDHCP 를 제외한 모든 값(value)을 삭제한다. 하위 키는 건드리지 않는다.
        /// 키가 없으면 keyExists=false, 0 을 돌려준다. 삭제한 값 이름 목록을 deletedNames 로 돌려준다.
        /// </summary>
        public static int CleanTcpipInterfaceValues(string interfaceGuid, out bool keyExists, out List<string> deletedNames)
        {
            keyExists = false;
            deletedNames = new List<string>();
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(TcpipInterfacesKeyPath + "\\" + interfaceGuid, true))
            {
                if (key == null) return 0;
                keyExists = true;
                foreach (string valueName in key.GetValueNames())
                {
                    if (string.Equals(valueName, EnableDhcpValueName, StringComparison.OrdinalIgnoreCase)) continue;
                    key.DeleteValue(valueName, false);
                    deletedNames.Add(valueName.Length == 0 ? "(기본값)" : valueName);
                }
                key.Flush();
                return deletedNames.Count;
            }
        }

        /// <summary>
        /// Windows 10 이상의 Wi-Fi "임의 하드웨어 주소" 설정 상태를 최선 노력으로 확인한다.
        /// true = 켜져 있음(인터페이스 전역 또는 어느 한 프로필), false = 꺼져 있음, null = 알 수 없음(키 없음: Win7/8 또는 무선 아님).
        /// </summary>
        public static bool? GetWlanRandomMacState(string interfaceGuid)
        {
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(WlanSvcInterfacesKeyPath + "\\" + interfaceGuid, false))
            {
                if (key == null) return null;
                bool enabled = IsNonZero(key.GetValue(RandomMacStateValueName));
                try
                {
                    using (RegistryKey profiles = key.OpenSubKey("Profiles", false))
                    {
                        if (profiles != null)
                        {
                            foreach (string profile in profiles.GetSubKeyNames())
                            {
                                using (RegistryKey meta = profiles.OpenSubKey(profile + "\\MetaData", false))
                                {
                                    if (meta != null && IsNonZero(meta.GetValue(RandomMacStateValueName)))
                                        enabled = true;
                                }
                            }
                        }
                    }
                }
                catch (SecurityException) { }
                catch (UnauthorizedAccessException) { }
                return enabled;
            }
        }

        private static bool IsNonZero(object value)
        {
            if (value == null) return false;
            byte[] bytes = value as byte[];
            if (bytes != null)
            {
                foreach (byte b in bytes) if (b != 0) return true;
                return false;
            }
            try { return Convert.ToInt64(value) != 0; }
            catch { return false; }
        }
    }
}
