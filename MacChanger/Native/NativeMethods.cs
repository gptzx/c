using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MacChanger.Native
{
    /// <summary>
    /// kernel32 / iphlpapi / setupapi P/Invoke 선언.
    /// 모든 구조체는 LayoutKind.Sequential로 선언하여 x86/x64 양쪽에서 Marshal.SizeOf가 올바른 크기를 돌려주도록 한다.
    /// </summary>
    internal static class NativeMethods
    {
        // ------------------------------------------------------------------
        // kernel32 : CreateFile / DeviceIoControl (NDIS OID 조회)
        // ------------------------------------------------------------------
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        /// <summary>CTL_CODE(FILE_DEVICE_PHYSICAL_NETCARD(0x17), 0, METHOD_OUT_DIRECT(2), FILE_ANY_ACCESS) = 0x170002</summary>
        public const uint IOCTL_NDIS_QUERY_GLOBAL_STATS = 0x00170002;
        public const uint OID_802_3_PERMANENT_ADDRESS = 0x01010101;
        public const uint OID_802_3_CURRENT_ADDRESS = 0x01010102;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref uint lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        // ------------------------------------------------------------------
        // iphlpapi : GetAdaptersAddresses (어댑터 열거 폴백)
        // ------------------------------------------------------------------
        public const uint AF_UNSPEC = 0;
        public const uint GAA_FLAG_SKIP_UNICAST = 0x0001;
        public const uint GAA_FLAG_SKIP_ANYCAST = 0x0002;
        public const uint GAA_FLAG_SKIP_MULTICAST = 0x0004;
        public const uint GAA_FLAG_SKIP_DNS_SERVER = 0x0008;
        public const uint GAA_FLAG_INCLUDE_ALL_INTERFACES = 0x0100;

        public const uint ERROR_SUCCESS = 0;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const uint ERROR_BUFFER_OVERFLOW = 111;
        public const uint ERROR_NO_DATA = 232;
        public const int ERROR_NO_MORE_ITEMS = 259;

        public const uint IF_TYPE_ETHERNET_CSMACD = 6;
        public const uint IF_TYPE_IEEE80211 = 71;

        [DllImport("iphlpapi.dll", SetLastError = false)]
        public static extern uint GetAdaptersAddresses(
            uint family,
            uint flags,
            IntPtr reserved,
            IntPtr pAdapterAddresses,
            ref uint pOutBufLen);

        /// <summary>
        /// IP_ADAPTER_ADDRESSES_LH의 앞부분(OperStatus까지)만 선언. 실제 구조체는 더 길지만
        /// 필요한 필드까지만 읽고 Next 포인터로 순회하므로 문제 없다.
        /// (Marshal.SizeOf: x86 72 bytes, x64 112 bytes — OperStatus 까지의 오프셋이 원본 SDK 레이아웃과 일치)
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct IP_ADAPTER_ADDRESSES_HEAD
        {
            public uint Length;
            public uint IfIndex;
            public IntPtr Next;
            public IntPtr AdapterName;          // PCHAR  (ANSI, "{GUID}")
            public IntPtr FirstUnicastAddress;
            public IntPtr FirstAnycastAddress;
            public IntPtr FirstMulticastAddress;
            public IntPtr FirstDnsServerAddress;
            public IntPtr DnsSuffix;            // PWCHAR
            public IntPtr Description;          // PWCHAR
            public IntPtr FriendlyName;         // PWCHAR
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] PhysicalAddress;
            public uint PhysicalAddressLength;
            public uint Flags;
            public uint Mtu;
            public uint IfType;
            public uint OperStatus;
        }

        // ------------------------------------------------------------------
        // setupapi : 어댑터 비활성화/활성화 (DIF_PROPERTYCHANGE)
        // ------------------------------------------------------------------
        public static readonly Guid GUID_DEVCLASS_NET = new Guid("4D36E972-E325-11CE-BFC1-08002BE10318");

        public const uint DIGCF_PRESENT = 0x00000002;

        public const uint SPDRP_DEVICEDESC = 0x00000000;
        public const uint SPDRP_DRIVER = 0x00000009;

        public const uint DIF_PROPERTYCHANGE = 0x00000012;
        public const uint DICS_ENABLE = 0x00000001;
        public const uint DICS_DISABLE = 0x00000002;
        public const uint DICS_FLAG_GLOBAL = 0x00000001;
        public const uint DICS_FLAG_CONFIGSPECIFIC = 0x00000002;

        public const uint DI_NEEDRESTART = 0x00000080;
        public const uint DI_NEEDREBOOT = 0x00000100;

        /// <summary>64비트 Windows에서 32비트 프로세스가 SetupDiCallClassInstaller를 호출하면 반환되는 오류</summary>
        public const int ERROR_IN_WOW64 = unchecked((int)0xE0000235);

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_CLASSINSTALL_HEADER
        {
            public uint cbSize;
            public uint InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SP_DEVINSTALL_PARAMS
        {
            public uint cbSize;
            public uint Flags;
            public uint FlagsEx;
            public IntPtr hwndParent;
            public IntPtr InstallMsgHandler;
            public IntPtr InstallMsgHandlerContext;
            public IntPtr FileQueue;
            public IntPtr ClassInstallReserved;
            public uint Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DriverPath;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiSetClassInstallParams(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            ref SP_PROPCHANGE_PARAMS classInstallParams,
            uint classInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiCallClassInstaller(
            uint installFunction,
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInstallParams(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            ref SP_DEVINSTALL_PARAMS deviceInstallParams);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}
