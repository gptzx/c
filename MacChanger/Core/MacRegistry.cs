using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace MacChanger.Core
{
    /// <summary>
    /// HKLM 레지스트리 접근. 64비트 OS에서 32비트 프로세스로 실행되더라도 항상 RegistryView.Registry64로
    /// 네이티브(64비트) 뷰를 열어 WOW64 리다이렉션 문제를 피한다. (32비트 OS에서는 Registry64 지정이 무시된다.)
    /// 이 프로그램은 자기 자신의 설정을 위한 레지스트리 키를 절대 만들지 않는다.
    /// </summary>
    public static class MacRegistry
    {
        public const string NetClassGuid = "{4D36E972-E325-11CE-BFC1-08002BE10318}";
        public const string NetClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\" + NetClassGuid;
        public const string TcpipParametersKeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
        public const string TcpipInterfacesKeyPath = TcpipParametersKeyPath + @"\Interfaces";
        /// <summary>EnableDHCP=1 인 어댑터를 정리할 때 Tcpip\Parameters(전역)에서 함께 삭제하는 값</summary>
        public static readonly string[] GlobalDhcpValueNames = new string[] { "DhcpDomain", "DhcpNameServer" };
        public const string WlanSvcInterfacesKeyPath = @"SOFTWARE\Microsoft\WlanSvc\Interfaces";

        public const string NetCfgInstanceIdValueName = "NetCfgInstanceId";
        public const string NetworkAddressValueName = "NetworkAddress";
        public const string EnableDhcpValueName = "EnableDHCP";
        public const string NameServerValueName = "NameServer";
        public const string RandomMacStateValueName = "RandomMacState";

        private static RegistryKey OpenHklm64()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        /// <summary>
        /// 열려 있는 Control\Class\{4D36E972-...} 키 아래 00XX 하위 키를 모두 돌며 NetCfgInstanceId가 interfaceGuid와 같은 키 이름("0001" 등)을 찾는다.
        /// 없으면 null.
        /// </summary>
        private static string FindClassSubKeyName(RegistryKey classKey, string interfaceGuid)
        {
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
            return null;
        }

        /// <summary>SPDRP_DRIVER 값("{4D36E972-...}\0001")으로 지정된 드라이버 키에서 NetCfgInstanceId를 읽는다.</summary>
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

        private static RegistryKey OpenAdapterClassKey(string interfaceGuid, bool writable)
        {
            if (string.IsNullOrEmpty(interfaceGuid)) throw new ArgumentNullException("interfaceGuid");

            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey classKey = hklm.OpenSubKey(NetClassKeyPath, false))
            {
                if (classKey == null)
                    throw new InvalidOperationException("네트워크 클래스 키를 열 수 없습니다: HKLM\\" + NetClassKeyPath);
                string subKeyName = FindClassSubKeyName(classKey, interfaceGuid);
                if (subKeyName == null)
                    throw new InvalidOperationException("NetCfgInstanceId가 " + interfaceGuid + "인 클래스 하위 키(00XX)를 찾지 못했습니다.");
                RegistryKey key = classKey.OpenSubKey(subKeyName, writable);   // 부모 키를 닫아도 열어 둔 하위 키는 그대로 유효하다
                if (key == null)
                    throw new InvalidOperationException("클래스 하위 키를 열 수 없습니다: " + subKeyName);
                return key;
            }
        }

        /// <summary>NetworkAddress(REG_SZ, 하이픈 없는 12자리)를 쓰고 다시 읽어 검증한다.</summary>
        public static void SetNetworkAddress(string interfaceGuid, string mac12)
        {
            string normalized = MacAddressUtil.Normalize(mac12);
            if (normalized == null) throw new ArgumentException("MAC 형식이 올바르지 않습니다.", "mac12");

            using (RegistryKey key = OpenAdapterClassKey(interfaceGuid, true))
            {
                key.SetValue(NetworkAddressValueName, normalized, RegistryValueKind.String);
                key.Flush();
                string readBack = key.GetValue(NetworkAddressValueName) as string;
                if (!string.Equals(readBack, normalized, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("NetworkAddress를 썼지만 다시 읽은 값이 다릅니다: " + (readBack ?? "(null)"));
            }
        }

        /// <summary>클래스 키에 NetworkAddress 값이 (종류와 무관하게, DeleteNetworkAddress 와 같은 기준으로) 있으면 true. 원상복구 전에 복구할 것이 남아 있는지 확인하는 데 쓴다.</summary>
        public static bool HasNetworkAddress(string interfaceGuid)
        {
            using (RegistryKey key = OpenAdapterClassKey(interfaceGuid, false))
            {
                return key.GetValue(NetworkAddressValueName) != null;
            }
        }

        /// <summary>NetworkAddress 값을 삭제하고 실제로 사라졌는지 확인한다. 값이 있었으면 true.</summary>
        public static bool DeleteNetworkAddress(string interfaceGuid)
        {
            using (RegistryKey key = OpenAdapterClassKey(interfaceGuid, true))
            {
                if (key.GetValue(NetworkAddressValueName) == null) return false;
                // RegistryKey.DeleteValue는 호환성 때문에 일부 오류를 조용히 무시하므로 다시 읽어 확인한다.
                key.DeleteValue(NetworkAddressValueName, false);
                key.Flush();
                if (key.GetValue(NetworkAddressValueName) != null)
                    throw new InvalidOperationException("NetworkAddress 값을 삭제하지 못했습니다 (값이 아직 남아 있음).");
                return true;
            }
        }

        /// <summary>
        /// Tcpip\Parameters\Interfaces\{GUID} 의 EnableDHCP 값. 키가 없으면 keyExists=false. 값이 없거나 숫자가 아니면 null.
        /// </summary>
        public static int? GetEnableDhcp(string interfaceGuid, out bool keyExists)
        {
            keyExists = false;
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(TcpipInterfacesKeyPath + "\\" + interfaceGuid, false))
            {
                if (key == null) return null;
                keyExists = true;
                object value = key.GetValue(EnableDhcpValueName);
                if (value == null) return null;
                try { return Convert.ToInt32(value); }
                catch (FormatException) { return null; }
                catch (InvalidCastException) { return null; }
                catch (OverflowException) { return null; }
            }
        }

        /// <summary>Tcpip\Parameters(전역) 키에서 DhcpDomain, DhcpNameServer 값을 삭제한다 (있는 것만). 삭제 실패 시 예외.</summary>
        public static void DeleteGlobalDhcpValues()
        {
            List<string> failed = new List<string>();
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(TcpipParametersKeyPath, true))
            {
                if (key == null) throw new InvalidOperationException("Tcpip\\Parameters 키를 열 수 없습니다.");
                foreach (string name in GlobalDhcpValueNames)
                {
                    if (key.GetValue(name) == null) continue;
                    key.DeleteValue(name, false);
                    if (key.GetValue(name) != null) failed.Add(name);
                }
                key.Flush();
            }
            if (failed.Count > 0)
                throw new InvalidOperationException("삭제되지 않은 값: " + string.Join(", ", failed));
        }

        /// <summary>
        /// Tcpip\Parameters\Interfaces\{GUID} 키의 값을 정리한다. EnableDHCP 는 항상 남긴다.
        /// NameServer 에 값 데이터가 있고 resetDns 가 false 면 NameServer 도 함께 남기고,
        /// 값이 비어 있거나 항목이 없거나 resetDns 가 true 면 NameServer 도 지운다.
        /// 하위 키 가운데 DHCP 임대 캐시(Wi-Fi 가 SSID 별로 두는 DhcpIPAddress/DhcpServer/Lease 를 가진 키)는 통째로 지우고(leaseCachesDeleted 에 개수),
        /// 그 밖의 하위 키는 건드리지 않는다. NameServer 를 남겼으면 true, 키가 없으면 false. 삭제되지 않은 값/하위 키가 있으면 예외.
        /// </summary>
        public static bool CleanTcpipInterfaceValues(string interfaceGuid, bool resetDns, out int leaseCachesDeleted)
        {
            List<string> failed = new List<string>();
            bool keepNameServer;
            leaseCachesDeleted = 0;
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(TcpipInterfacesKeyPath + "\\" + interfaceGuid, true))
            {
                if (key == null) return false;
                keepNameServer = !resetDns && HasData(key.GetValue(NameServerValueName));
                foreach (string valueName in key.GetValueNames())
                {
                    if (string.Equals(valueName, EnableDhcpValueName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (keepNameServer && string.Equals(valueName, NameServerValueName, StringComparison.OrdinalIgnoreCase)) continue;
                    key.DeleteValue(valueName, false);
                    if (key.GetValue(valueName) != null) failed.Add(valueName.Length == 0 ? "(기본값)" : valueName);
                }
                leaseCachesDeleted = DeleteLeaseCaches(key, failed);
                key.Flush();
            }
            if (failed.Count > 0)
                throw new InvalidOperationException("삭제되지 않은 값: " + string.Join(", ", failed));
            return keepNameServer;
        }

        /// <summary>key 의 DHCP 임대 캐시 하위 키를 모두 지우고 지운 개수를 돌려준다. failed 가 null 이 아니면 지워지지 않은 하위 키 이름("이름\\")을 거기에 넣는다.</summary>
        private static int DeleteLeaseCaches(RegistryKey key, List<string> failed)
        {
            int deleted = 0;
            foreach (string subKeyName in key.GetSubKeyNames())
            {
                if (!IsLeaseCacheKey(key, subKeyName)) continue;
                key.DeleteSubKeyTree(subKeyName, false);
                using (RegistryKey check = key.OpenSubKey(subKeyName))
                {
                    if (check == null) deleted++;
                    else if (failed != null) failed.Add(subKeyName + "\\");
                }
            }
            return deleted;
        }

        /// <summary>
        /// 인터페이스 키의 DHCP 임대 캐시 하위 키만 지운다 (값은 건드리지 않는다). 재시작 직후 한 번 더 부르기 위한 것 —
        /// Wi-Fi 는 네트워크를 떠날 때(재시작으로 인터페이스가 내려갈 때) 현재 SSID 의 캐시를 쓰므로, 재시작 전 정리만으로는 그 항목이 남을 수 있다.
        /// 지운 개수를 돌려주고, 키가 없으면 0.
        /// </summary>
        public static int DeleteLeaseCacheSubKeys(string interfaceGuid)
        {
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(TcpipInterfacesKeyPath + "\\" + interfaceGuid, true))
            {
                return key == null ? 0 : DeleteLeaseCaches(key, null);
            }
        }

        /// <summary>하위 키가 DHCP 임대 캐시인지 — 임대 값(DhcpIPAddress, DhcpServer, Lease) 가운데 하나라도 있으면 캐시로 본다.</summary>
        private static bool IsLeaseCacheKey(RegistryKey parent, string subKeyName)
        {
            using (RegistryKey sub = parent.OpenSubKey(subKeyName))
            {
                if (sub == null) return false;
                foreach (string name in sub.GetValueNames())
                    if (string.Equals(name, "DhcpIPAddress", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "DhcpServer", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Lease", StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
        }

        /// <summary>레지스트리 값에 실제 데이터가 있는지 (null, 빈 문자열, 공백뿐인 문자열, 빈 배열은 데이터 없음).</summary>
        public static bool HasData(object value)
        {
            if (value == null) return false;
            string text = value as string;
            if (text != null) return text.Trim().Length > 0;
            string[] multi = value as string[];
            if (multi != null)
            {
                foreach (string item in multi)
                    if (item != null && item.Trim().Length > 0) return true;
                return false;
            }
            byte[] bytes = value as byte[];
            if (bytes != null) return bytes.Length > 0;
            return true;   // DWORD/QWORD 같은 숫자 값은 존재 자체가 데이터
        }

        /// <summary>
        /// Windows 10 이상의 Wi-Fi "임의 하드웨어 주소" 설정 상태를 최선 노력으로 확인한다.
        /// - 인터페이스 전역 설정: HKLM\SOFTWARE\Microsoft\WlanSvc\Interfaces\{GUID}\RandomMacState (REG_BINARY, 00=끔, 01=켬)
        /// - 네트워크(프로필)별 설정: %ProgramData%\Microsoft\Wlansvc\Profiles\Interfaces\{GUID}\*.xml의
        ///   &lt;MacRandomization&gt;&lt;enableRandomization&gt;true&lt;/enableRandomization&gt; (읽기만 하며 파일을 만들지 않는다)
        /// true = 켜져 있음(전역 또는 어느 한 프로필), false = 꺼져 있음, null = 알 수 없음(키 없음: Win7/8 또는 무선 아님).
        /// </summary>
        public static bool? GetWlanRandomMacState(string interfaceGuid)
        {
            bool? interfaceState = null;
            using (RegistryKey hklm = OpenHklm64())
            using (RegistryKey key = hklm.OpenSubKey(WlanSvcInterfacesKeyPath + "\\" + interfaceGuid, false))
            {
                if (key != null)
                    interfaceState = IsNonZero(key.GetValue(RandomMacStateValueName));
            }

            bool? profileState = null;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Wlansvc\Profiles\Interfaces\" + interfaceGuid);
                if (Directory.Exists(dir))
                {
                    profileState = false;
                    foreach (string file in Directory.GetFiles(dir, "*.xml"))
                    {
                        string xml = File.ReadAllText(file);
                        if (xml.IndexOf("<enableRandomization>true</enableRandomization>", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            profileState = true;
                            break;
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }

            if (interfaceState == null && profileState == null) return null;
            return (interfaceState == true) || (profileState == true);
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
