using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using MacChanger.Native;

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
            list.Sort(delegate(NetworkAdapterInfo a, NetworkAdapterInfo b)
            {
                int c = a.IsVirtual.CompareTo(b.IsVirtual);
                if (c != 0) return c;
                c = ((int)a.Kind).CompareTo((int)b.Kind);
                if (c != 0) return c;
                return string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
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

        private static bool PropBool(ManagementBaseObject mo, string name)
        {
            object o = Prop(mo, name);
            if (o == null) return false;
            try { return Convert.ToBoolean(o, CultureInfo.InvariantCulture); }
            catch { return false; }
        }

        private static long PropLong(ManagementBaseObject mo, string name)
        {
            object o = Prop(mo, name);
            if (o == null) return -1;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); }
            catch { return -1; }
        }

        private static ManagementObjectSearcher CreateSearcher(string scopePath, string query)
        {
            ManagementScope scope = new ManagementScope(scopePath);
            scope.Connect();
            return new ManagementObjectSearcher(scope, new ObjectQuery(query));
        }

        // ------------------------------------------------------------------
        // 1) MSFT_NetAdapter (root\StandardCimv2, Windows 8+)
        // ------------------------------------------------------------------
        public static List<NetworkAdapterInfo> EnumerateMsftNetAdapter()
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
        public static List<NetworkAdapterInfo> EnumerateWin32NetworkAdapter()
        {
            List<NetworkAdapterInfo> result = new List<NetworkAdapterInfo>();

            // Win32_NetworkAdapter.AdapterTypeID는 NDIS_MEDIUM 값이라 Wi-Fi도 0("Ethernet 802.3")으로 보고된다.
            // 활성 상태인 어댑터는 GetAdaptersAddresses의 IfType(IEEE 802.11)이 가장 믿을 만한 무선 판별 근거이므로 먼저 모아 둔다.
            Dictionary<string, AdapterKind> liveKinds = new Dictionary<string, AdapterKind>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (NetworkAdapterInfo g in EnumerateGetAdaptersAddresses())
                    liveKinds[g.InterfaceGuid] = g.Kind;
            }
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
                        if (!physical && mac == null) continue;
                        // 숨겨진 의사 어댑터(RAS Async Adapter, WAN Miniport 등)는 네트워크 연결 이름이 없다.
                        if (!physical && connectionName == null) continue;

                        NetworkAdapterInfo info = new NetworkAdapterInfo();
                        info.InterfaceGuid = guid;
                        info.Description = PropString(mo, "Description") ?? PropString(mo, "Name");
                        info.ConnectionName = connectionName;
                        info.CurrentMac = mac;
                        info.IsVirtual = !physical;

                        // AdapterTypeID: 0 = Ethernet 802.3, 9 = Wireless WAN (어댑터가 비활성화 상태이면 null)
                        long adapterTypeId = PropLong(mo, "AdapterTypeID");
                        AdapterKind liveKind;
                        bool hasLiveKind = liveKinds.TryGetValue(guid, out liveKind);

                        if (NetworkAdapterInfo.LooksBluetooth(info.Description) || NetworkAdapterInfo.LooksBluetooth(connectionName)
                            || (hasLiveKind && liveKind == AdapterKind.Bluetooth))
                            info.Kind = AdapterKind.Bluetooth;
                        else if ((hasLiveKind && liveKind == AdapterKind.Wireless) || adapterTypeId == 9
                            || NetworkAdapterInfo.LooksWireless(info.Description) || NetworkAdapterInfo.LooksWireless(connectionName)
                            || NetworkAdapterInfo.LooksWireless(PropString(mo, "AdapterType")))
                            info.Kind = AdapterKind.Wireless;
                        else if (adapterTypeId == 0 || physical || (hasLiveKind && liveKind == AdapterKind.Wired))
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
        //    (비활성화된 어댑터는 나오지 않는다. 열거 폴백과 현재 MAC 재조회에 사용)
        // ------------------------------------------------------------------
        public static List<NetworkAdapterInfo> EnumerateGetAdaptersAddresses()
        {
            List<NetworkAdapterInfo> result = new List<NetworkAdapterInfo>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                string guid = NetworkAdapterInfo.NormalizeGuid(ni.Id);
                if (guid == null) continue;
                string mac = MacAddressUtil.FromBytes(ni.GetPhysicalAddress().GetAddressBytes());
                if (mac == null) continue;   // 루프백/터널 등 MAC이 없는 인터페이스 제외

                NetworkAdapterInfo info = new NetworkAdapterInfo();
                info.InterfaceGuid = guid;
                info.Description = ni.Description;
                info.ConnectionName = ni.Name;
                info.CurrentMac = mac;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    info.Kind = AdapterKind.Wireless;
                else if (NetworkAdapterInfo.LooksBluetooth(ni.Description) || NetworkAdapterInfo.LooksBluetooth(ni.Name))
                    info.Kind = AdapterKind.Bluetooth;
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT
                      || ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx || ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet)
                    info.Kind = NetworkAdapterInfo.LooksWireless(ni.Description) ? AdapterKind.Wireless : AdapterKind.Wired;
                else
                    info.Kind = AdapterKind.Other;
                result.Add(info);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 단일 어댑터 MAC 재조회 헬퍼 (변경/복구 후 UI 갱신용)
        // ------------------------------------------------------------------
        public static string GetCurrentMacViaGetAdaptersAddresses(string interfaceGuid)
        {
            foreach (NetworkAdapterInfo a in EnumerateGetAdaptersAddresses())
                if (string.Equals(a.InterfaceGuid, interfaceGuid, StringComparison.OrdinalIgnoreCase)) return a.CurrentMac;
            return null;
        }

        public static string GetCurrentMacViaWmi(string interfaceGuid)
        {
            try
            {
                foreach (NetworkAdapterInfo a in EnumerateMsftNetAdapter())
                    if (string.Equals(a.InterfaceGuid, interfaceGuid, StringComparison.OrdinalIgnoreCase) && a.CurrentMac != null) return a.CurrentMac;
            }
            catch (Exception) { }
            foreach (NetworkAdapterInfo a in EnumerateWin32NetworkAdapter())
                if (string.Equals(a.InterfaceGuid, interfaceGuid, StringComparison.OrdinalIgnoreCase) && a.CurrentMac != null) return a.CurrentMac;
            return null;
        }

        public static string GetPermanentMacViaWmi(string interfaceGuid)
        {
            foreach (NetworkAdapterInfo a in EnumerateMsftNetAdapter())
                if (string.Equals(a.InterfaceGuid, interfaceGuid, StringComparison.OrdinalIgnoreCase)) return a.PermanentMacHint;
            return null;
        }
    }
}
