using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;

namespace MacChanger.Core
{
    /// <summary>
    /// 네트워크 어댑터 열거.
    /// 1) WMI root\StandardCimv2\MSFT_NetAdapter (Windows 8 이상)
    /// 2) WMI root\cimv2\Win32_NetworkAdapter (Windows 7 포함)
    /// 3) GetAdaptersAddresses (System.Net.NetworkInformation.NetworkInterface 가 감싸는 iphlpapi API)
    /// 순서로 시도하고, 앞 단계가 실패하거나 결과가 비어 있으면 다음 단계로 넘어간다.
    /// </summary>
    public static class AdapterEnumerator
    {
        public static List<NetworkAdapterInfo> Enumerate()
        {
            List<NetworkAdapterInfo> list;
            try
            {
                list = EnumerateMsftNetAdapter();
                if (list.Count > 0) return Sort(list);
            }
            catch (Exception) { }   // Windows 7 등: root\StandardCimv2 없음 → 폴백

            try
            {
                list = EnumerateWin32NetworkAdapter();
                if (list.Count > 0) return Sort(list);
            }
            catch (Exception) { }

            return Sort(EnumerateGetAdaptersAddresses());
        }

        /// <summary>물리 유선 → 물리 무선 → 블루투스 → 기타 순, 가상 어댑터는 뒤로.</summary>
        private static List<NetworkAdapterInfo> Sort(List<NetworkAdapterInfo> list)
        {
            list.Sort(CompareAdapters);
            return list;
        }

        /// <summary>정렬은 결과의 부호만 본다 (음수 = a 가 앞).</summary>
        private static int CompareAdapters(NetworkAdapterInfo a, NetworkAdapterInfo b)
        {
            if (a.IsVirtual != b.IsVirtual) return a.IsVirtual ? 1 : -1;
            if (a.Kind != b.Kind) return a.Kind - b.Kind;
            return string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase);
        }

        // ------------------------------------------------------------------
        // WMI 공통 헬퍼
        // ------------------------------------------------------------------
        private static object Prop(ManagementBaseObject mo, string name)
        {
            try { return mo[name]; }
            catch (ManagementException) { return null; }
        }

        private static string PropString(ManagementBaseObject mo, string name)
        {
            object o = Prop(mo, name);
            if (o == null) return null;
            string s = Convert.ToString(o, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>CIM boolean 속성 (Hidden, Virtual, PhysicalAdapter — WMI 가 System.Boolean 으로 돌려준다). 없으면 false.</summary>
        private static bool PropBool(ManagementBaseObject mo, string name)
        {
            object o = Prop(mo, name);
            return o is bool && (bool)o;
        }

        /// <summary>CIM uint16/uint32 속성 (WMI 가 UInt16/UInt32 로 돌려주므로 Int64 로 바꿀 때 실패하지 않는다). 없으면 -1.</summary>
        private static long PropLong(ManagementBaseObject mo, string name)
        {
            object o = Prop(mo, name);
            return o == null ? -1 : Convert.ToInt64(o, CultureInfo.InvariantCulture);
        }

        internal static ManagementObjectSearcher CreateSearcher(string scopePath, string query)
        {
            ManagementScope scope = new ManagementScope(scopePath);
            scope.Connect();
            return new ManagementObjectSearcher(scope, new ObjectQuery(query));
        }

        // ------------------------------------------------------------------
        // 1) MSFT_NetAdapter (root\StandardCimv2, Windows 8+)
        // ------------------------------------------------------------------
        private static List<NetworkAdapterInfo> EnumerateMsftNetAdapter()
        {
            List<NetworkAdapterInfo> result = new List<NetworkAdapterInfo>();
            using (ManagementObjectSearcher searcher = CreateSearcher(@"root\StandardCimv2", "SELECT * FROM MSFT_NetAdapter"))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject mo in items)
                {
                    using (mo)
                    {
                        if (PropBool(mo, "Hidden")) continue;
                        string guid = NetworkAdapterInfo.NormalizeGuid(PropString(mo, "InterfaceGuid"));
                        if (guid == null) guid = NetworkAdapterInfo.NormalizeGuid(PropString(mo, "DeviceID"));
                        if (guid == null) continue;

                        NetworkAdapterInfo info = new NetworkAdapterInfo();
                        info.InterfaceGuid = guid;
                        info.Description = PropString(mo, "InterfaceDescription") ?? PropString(mo, "DriverDescription");
                        info.ConnectionName = PropString(mo, "Name");
                        info.CurrentMac = MacAddressUtil.Normalize(PropString(mo, "MacAddress"));
                        info.PermanentMacHint = MacAddressUtil.Normalize(PropString(mo, "PermanentAddress"));
                        info.IsVirtual = PropBool(mo, "Virtual");
                        info.Kind = ClassifyNdis(PropLong(mo, "PhysicalMediaType"), PropLong(mo, "NdisPhysicalMedium"), PropLong(mo, "InterfaceType"), info.Description);
                        result.Add(info);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// NDIS_PHYSICAL_MEDIUM: 1=WirelessLan, 8=WirelessWan, 9=Native802_11, 10=Bluetooth, 14=802.3
        /// IANA ifType: 6=ethernetCsmacd, 71=ieee80211
        /// </summary>
        private static AdapterKind ClassifyNdis(long physicalMediaType, long ndisPhysicalMedium, long interfaceType, string description)
        {
            if (IsWirelessMedium(physicalMediaType) || IsWirelessMedium(ndisPhysicalMedium) || interfaceType == 71)
                return AdapterKind.Wireless;
            if (physicalMediaType == 14 || ndisPhysicalMedium == 14)
                return AdapterKind.Wired;
            // Microsoft 블루투스 PAN(BTHPAN)은 PhysicalMediaType=10, InterfaceType=6으로 보고되므로 802.3 판정보다 먼저 거른다.
            if (physicalMediaType == 10 || ndisPhysicalMedium == 10 || NetworkAdapterInfo.LooksBluetooth(description))
                return AdapterKind.Bluetooth;
            if (NetworkAdapterInfo.LooksWireless(description))
                return AdapterKind.Wireless;
            if (interfaceType == 6)
                return AdapterKind.Wired;
            return AdapterKind.Other;
        }

        private static bool IsWirelessMedium(long medium)
        {
            return medium == 1 || medium == 8 || medium == 9;
        }

        // ------------------------------------------------------------------
        // 2) Win32_NetworkAdapter (root\cimv2)
        // ------------------------------------------------------------------
        private static List<NetworkAdapterInfo> EnumerateWin32NetworkAdapter()
        {
            List<NetworkAdapterInfo> result = new List<NetworkAdapterInfo>();

            // Win32_NetworkAdapter.AdapterTypeID는 NDIS_MEDIUM 값이라 Wi-Fi도 0("Ethernet 802.3")으로 보고된다.
            // 활성 상태인 어댑터는 GetAdaptersAddresses의 IfType(IEEE 802.11)이 가장 믿을 만한 무선 판별 근거이므로 먼저 모아 둔다.
            List<NetworkAdapterInfo> live = null;
            try { live = EnumerateGetAdaptersAddresses(); }
            catch (Exception) { }

            using (ManagementObjectSearcher searcher = CreateSearcher(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapter WHERE GUID IS NOT NULL"))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject mo in items)
                {
                    using (mo)
                    {
                        string guid = NetworkAdapterInfo.NormalizeGuid(PropString(mo, "GUID"));
                        if (guid == null) continue;

                        bool physical = PropBool(mo, "PhysicalAdapter");
                        string mac = MacAddressUtil.Normalize(PropString(mo, "MACAddress"));
                        string connectionName = PropString(mo, "NetConnectionID");
                        // 비활성화된 물리 어댑터는 MACAddress가 null이므로 PhysicalAdapter로도 포함시킨다.
                        // 숨겨진 의사 어댑터(RAS Async Adapter, WAN Miniport 등)는 네트워크 연결 이름이 없다.
                        if (!physical && (mac == null || connectionName == null)) continue;

                        NetworkAdapterInfo info = new NetworkAdapterInfo();
                        info.InterfaceGuid = guid;
                        info.Description = PropString(mo, "Description") ?? PropString(mo, "Name");
                        info.ConnectionName = connectionName;
                        info.CurrentMac = mac;
                        info.IsVirtual = !physical;

                        // AdapterTypeID: 0 = Ethernet 802.3, 9 = Wireless WAN (어댑터가 비활성화 상태이면 null)
                        long adapterTypeId = PropLong(mo, "AdapterTypeID");
                        // 활성 목록에서 GUID 가 같은 어댑터의 종류 (같은 GUID 가 여럿이면 마지막 것). 활성 상태가 아니면 Other — 아래 비교는 모두 거짓이 된다.
                        AdapterKind liveKind = AdapterKind.Other;
                        if (live != null)
                            for (int i = 0; i < live.Count; i++)
                                if (string.Equals(live[i].InterfaceGuid, guid, StringComparison.OrdinalIgnoreCase)) liveKind = live[i].Kind;

                        if (NetworkAdapterInfo.LooksBluetooth(info.Description) || NetworkAdapterInfo.LooksBluetooth(connectionName)
                            || liveKind == AdapterKind.Bluetooth)
                            info.Kind = AdapterKind.Bluetooth;
                        else if (liveKind == AdapterKind.Wireless || adapterTypeId == 9
                            || NetworkAdapterInfo.LooksWireless(info.Description) || NetworkAdapterInfo.LooksWireless(connectionName)
                            || NetworkAdapterInfo.LooksWireless(PropString(mo, "AdapterType")))
                            info.Kind = AdapterKind.Wireless;
                        else if (adapterTypeId == 0 || physical || liveKind == AdapterKind.Wired)
                            info.Kind = AdapterKind.Wired;
                        else
                            info.Kind = AdapterKind.Other;
                        result.Add(info);
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 3) GetAdaptersAddresses — NetworkInterface.GetAllNetworkInterfaces() 가 Vista 이상에서 이 API 를 호출한다.
        //    (비활성화된 어댑터는 나오지 않는다. 열거 폴백과 Win32_NetworkAdapter 경로의 무선 판별 보강에 사용)
        // ------------------------------------------------------------------
        private static List<NetworkAdapterInfo> EnumerateGetAdaptersAddresses()
        {
            List<NetworkAdapterInfo> result = new List<NetworkAdapterInfo>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                string guid = NetworkAdapterInfo.NormalizeGuid(ni.Id);
                if (guid == null) continue;
                byte[] addr = ni.GetPhysicalAddress().GetAddressBytes();
                if (addr.Length != 6) continue;   // 루프백/터널(Teredo 등 8바이트) 등 MAC이 없는 인터페이스 제외
                string mac = MacAddressUtil.FromBytes(addr);

                NetworkAdapterInfo info = new NetworkAdapterInfo();
                info.InterfaceGuid = guid;
                info.Description = ni.Description;
                info.ConnectionName = ni.Name;
                info.CurrentMac = mac;
                NetworkInterfaceType type = ni.NetworkInterfaceType;
                if (type == NetworkInterfaceType.Wireless80211)
                    info.Kind = AdapterKind.Wireless;
                else if (NetworkAdapterInfo.LooksBluetooth(ni.Description) || NetworkAdapterInfo.LooksBluetooth(ni.Name))
                    info.Kind = AdapterKind.Bluetooth;
                else if (type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.FastEthernetT
                      || type == NetworkInterfaceType.FastEthernetFx || type == NetworkInterfaceType.GigabitEthernet)
                    info.Kind = NetworkAdapterInfo.LooksWireless(ni.Description) ? AdapterKind.Wireless : AdapterKind.Wired;
                else
                    info.Kind = AdapterKind.Other;
                result.Add(info);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 단일 어댑터 현재 MAC 조회 폴백 (MacChangeService.ReadCurrentMac 이 NDIS 조회에 실패했을 때)
        // ------------------------------------------------------------------
        public static string GetCurrentMacViaGetAdaptersAddresses(string interfaceGuid)
        {
            NetworkInterface ni = IpMonitor.FindInterface(interfaceGuid);
            return ni == null ? null : MacAddressUtil.FromBytes(ni.GetPhysicalAddress().GetAddressBytes());
        }

        public static string GetCurrentMacViaWmi(string interfaceGuid)
        {
            try
            {
                string mac = FindMac(EnumerateMsftNetAdapter(), interfaceGuid);
                if (mac != null) return mac;
            }
            catch (Exception) { }
            return FindMac(EnumerateWin32NetworkAdapter(), interfaceGuid);
        }

        /// <summary>목록에서 GUID 가 같은 어댑터의 MAC (없거나 MAC 을 모르면 null)</summary>
        private static string FindMac(List<NetworkAdapterInfo> list, string interfaceGuid)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].InterfaceGuid, interfaceGuid, StringComparison.OrdinalIgnoreCase) && list[i].CurrentMac != null) return list[i].CurrentMac;
            return null;
        }
    }
}
