using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using MacChanger.Native;

namespace MacChanger.Core
{
    /// <summary>
    /// 네트워크 어댑터 열거.
    /// 1) WMI root\StandardCimv2\MSFT_NetAdapter (Windows 8 이상)
    /// 2) WMI root\cimv2\Win32_NetworkAdapter (Windows 7 포함)
    /// 3) iphlpapi GetAdaptersAddresses
    /// 순서로 시도하고, 앞 단계가 실패하거나 결과가 비어 있으면 다음 단계로 넘어간다.
    /// </summary>
    public static class AdapterEnumerator
    {
        public const string SourceMsft = "MSFT_NetAdapter";
        public const string SourceWin32 = "Win32_NetworkAdapter";
        public const string SourceGaa = "GetAdaptersAddresses";

        public static List<NetworkAdapterInfo> Enumerate(Action<string> log)
        {
            if (log == null) log = delegate { };

            List<NetworkAdapterInfo> list;

            try
            {
                list = EnumerateMsftNetAdapter();
                if (list.Count > 0)
                {
                    log("어댑터 열거: " + SourceMsft + " (" + list.Count + "개)");
                    return Sort(list);
                }
                log(SourceMsft + ": 결과 없음 → " + SourceWin32 + " 로 폴백");
            }
            catch (Exception ex)
            {
                log(SourceMsft + " 조회 실패(Windows 7 등에서는 정상): " + ex.Message + " → " + SourceWin32 + " 로 폴백");
            }

            try
            {
                list = EnumerateWin32NetworkAdapter();
                if (list.Count > 0)
                {
                    log("어댑터 열거: " + SourceWin32 + " (" + list.Count + "개)");
                    return Sort(list);
                }
                log(SourceWin32 + ": 결과 없음 → " + SourceGaa + " 로 폴백");
            }
            catch (Exception ex)
            {
                log(SourceWin32 + " 조회 실패: " + ex.Message + " → " + SourceGaa + " 로 폴백");
            }

            try
            {
                list = EnumerateGetAdaptersAddresses();
                log("어댑터 열거: " + SourceGaa + " (" + list.Count + "개)");
                return Sort(list);
            }
            catch (Exception ex)
            {
                log(SourceGaa + " 조회 실패: " + ex.Message);
            }

            return new List<NetworkAdapterInfo>();
        }

        private static List<NetworkAdapterInfo> Sort(List<NetworkAdapterInfo> list)
        {
            list.Sort(delegate(NetworkAdapterInfo a, NetworkAdapterInfo b)
            {
                int c = a.IsVirtual.CompareTo(b.IsVirtual);
                if (c != 0) return c;
                c = KindOrder(a.Kind).CompareTo(KindOrder(b.Kind));
                if (c != 0) return c;
                return string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static int KindOrder(AdapterKind kind)
        {
            switch (kind)
            {
                case AdapterKind.Wired: return 0;
                case AdapterKind.Wireless: return 1;
                default: return 2;
            }
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

        private static bool PropBool(ManagementBaseObject mo, string name, bool defaultValue)
        {
            object o = Prop(mo, name);
            if (o == null) return defaultValue;
            try { return Convert.ToBoolean(o, CultureInfo.InvariantCulture); }
            catch { return defaultValue; }
        }

        private static long PropLong(ManagementBaseObject mo, string name, long defaultValue)
        {
            object o = Prop(mo, name);
            if (o == null) return defaultValue;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); }
            catch { return defaultValue; }
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
                        if (PropBool(mo, "Hidden", false)) continue;
                        string guid = NetworkAdapterInfo.NormalizeGuid(PropString(mo, "InterfaceGuid"));
                        if (guid == null) guid = NetworkAdapterInfo.NormalizeGuid(PropString(mo, "DeviceID"));
                        if (guid == null) continue;

                        NetworkAdapterInfo info = new NetworkAdapterInfo();
                        info.Source = SourceMsft;
                        info.InterfaceGuid = guid;
                        info.Description = PropString(mo, "InterfaceDescription") ?? PropString(mo, "DriverDescription");
                        info.ConnectionName = PropString(mo, "Name");
                        info.CurrentMac = MacAddressUtil.Normalize(PropString(mo, "MacAddress"));
                        info.PermanentMacHint = MacAddressUtil.Normalize(PropString(mo, "PermanentAddress"));
                        info.IsVirtual = PropBool(mo, "Virtual", false);
                        info.PnpDeviceId = PropString(mo, "PnPDeviceID");

                        long physicalMediaType = PropLong(mo, "PhysicalMediaType", -1);
                        long ndisPhysicalMedium = PropLong(mo, "NdisPhysicalMedium", -1);
                        long interfaceType = PropLong(mo, "InterfaceType", -1);
                        info.Kind = ClassifyNdis(physicalMediaType, ndisPhysicalMedium, interfaceType, info.Description);
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
            if (IsWirelessMedium(physicalMediaType) || IsWirelessMedium(ndisPhysicalMedium) || interfaceType == NativeMethods.IF_TYPE_IEEE80211)
                return AdapterKind.Wireless;
            if (physicalMediaType == 14 || ndisPhysicalMedium == 14)
                return AdapterKind.Wired;
            if (NetworkAdapterInfo.LooksWireless(description))
                return AdapterKind.Wireless;
            if (interfaceType == NativeMethods.IF_TYPE_ETHERNET_CSMACD)
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
            using (ManagementObjectSearcher searcher = CreateSearcher(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapter WHERE GUID IS NOT NULL"))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject mo in items)
                {
                    using (mo)
                    {
                        string guid = NetworkAdapterInfo.NormalizeGuid(PropString(mo, "GUID"));
                        if (guid == null) continue;

                        bool physical = PropBool(mo, "PhysicalAdapter", false);
                        string mac = MacAddressUtil.Normalize(PropString(mo, "MACAddress"));
                        // 비활성화된 물리 어댑터는 MACAddress 가 null 이므로 PhysicalAdapter 로도 포함시킨다.
                        if (!physical && mac == null) continue;

                        NetworkAdapterInfo info = new NetworkAdapterInfo();
                        info.Source = SourceWin32;
                        info.InterfaceGuid = guid;
                        info.Description = PropString(mo, "Description") ?? PropString(mo, "Name");
                        info.ConnectionName = PropString(mo, "NetConnectionID");
                        info.CurrentMac = mac;
                        info.IsVirtual = !physical;
                        info.PnpDeviceId = PropString(mo, "PNPDeviceID");

                        // AdapterTypeID: 0 = Ethernet 802.3, 9 = Wireless (어댑터가 비활성화 상태이면 null)
                        long adapterTypeId = PropLong(mo, "AdapterTypeID", -1);
                        if (adapterTypeId == 9 || NetworkAdapterInfo.LooksWireless(info.Description) || NetworkAdapterInfo.LooksWireless(PropString(mo, "AdapterType")))
                            info.Kind = AdapterKind.Wireless;
                        else if (adapterTypeId == 0 || physical)
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
        // 3) GetAdaptersAddresses (iphlpapi)
        // ------------------------------------------------------------------
        public static List<NetworkAdapterInfo> EnumerateGetAdaptersAddresses()
        {
            List<NetworkAdapterInfo> result = new List<NetworkAdapterInfo>();
            uint flags = NativeMethods.GAA_FLAG_SKIP_UNICAST | NativeMethods.GAA_FLAG_SKIP_ANYCAST
                       | NativeMethods.GAA_FLAG_SKIP_MULTICAST | NativeMethods.GAA_FLAG_SKIP_DNS_SERVER
                       | NativeMethods.GAA_FLAG_INCLUDE_ALL_INTERFACES;
            uint size = 32 * 1024;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    buffer = Marshal.AllocHGlobal((int)size);
                    uint rc = NativeMethods.GetAdaptersAddresses(NativeMethods.AF_UNSPEC, flags, IntPtr.Zero, buffer, ref size);
                    if (rc == NativeMethods.ERROR_SUCCESS) break;
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    if (rc == NativeMethods.ERROR_NO_DATA) return result;
                    if (rc != NativeMethods.ERROR_BUFFER_OVERFLOW) throw new Win32Exception((int)rc);
                }
                if (buffer == IntPtr.Zero) throw new InvalidOperationException("GetAdaptersAddresses 버퍼 크기를 결정하지 못했습니다.");

                IntPtr current = buffer;
                while (current != IntPtr.Zero)
                {
                    NativeMethods.IP_ADAPTER_ADDRESSES_HEAD a =
                        (NativeMethods.IP_ADAPTER_ADDRESSES_HEAD)Marshal.PtrToStructure(current, typeof(NativeMethods.IP_ADAPTER_ADDRESSES_HEAD));
                    current = a.Next;

                    string guid = NetworkAdapterInfo.NormalizeGuid(Marshal.PtrToStringAnsi(a.AdapterName));
                    if (guid == null) continue;
                    if (a.PhysicalAddressLength != 6) continue;   // 루프백/터널 등 MAC 이 없는 인터페이스 제외

                    NetworkAdapterInfo info = new NetworkAdapterInfo();
                    info.Source = SourceGaa;
                    info.InterfaceGuid = guid;
                    info.Description = Marshal.PtrToStringUni(a.Description);
                    info.ConnectionName = Marshal.PtrToStringUni(a.FriendlyName);
                    info.CurrentMac = MacAddressUtil.FromBytes(a.PhysicalAddress, (int)a.PhysicalAddressLength);
                    if (a.IfType == NativeMethods.IF_TYPE_IEEE80211) info.Kind = AdapterKind.Wireless;
                    else if (a.IfType == NativeMethods.IF_TYPE_ETHERNET_CSMACD) info.Kind = NetworkAdapterInfo.LooksWireless(info.Description) ? AdapterKind.Wireless : AdapterKind.Wired;
                    else info.Kind = AdapterKind.Other;
                    result.Add(info);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
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
