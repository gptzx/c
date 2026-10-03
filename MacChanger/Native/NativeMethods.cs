using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MacChanger.Native
{
    /// <summary>
    /// kernel32 / setupapi P/Invoke 선언.
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
        // kernel32 / user32 : 실행 파일에 내장된 아이콘 로드 (창/작업표시줄 아이콘)
        // ------------------------------------------------------------------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        // ------------------------------------------------------------------
        // iphlpapi — 인터페이스 송수신 카운터 (GetIfEntry2, Windows Vista 이상, 64비트 카운터)
        // ------------------------------------------------------------------
        /// <summary>
        /// MIB_IF_ROW2 (netioapi.h, 1352바이트). 문자열/주소 배열 등은 쓰지 않으므로 필요한 필드만 실제 오프셋에 놓는다
        /// (InterfaceLuid 0, InterfaceIndex 8, InOctets 1208, OutOctets 1280 — x86/x64 공통).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 1352)]
        public struct MIB_IF_ROW2
        {
            [FieldOffset(0)] public ulong InterfaceLuid;
            [FieldOffset(8)] public uint InterfaceIndex;
            [FieldOffset(1208)] public ulong InOctets;
            [FieldOffset(1280)] public ulong OutOctets;
        }

        /// <summary>인터페이스 GUID → NET_LUID. 성공하면 NO_ERROR(0).</summary>
        [DllImport("iphlpapi.dll")]
        public static extern int ConvertInterfaceGuidToLuid(ref Guid interfaceGuid, out ulong interfaceLuid);

        /// <summary>InterfaceLuid(또는 InterfaceIndex)로 지정한 인터페이스의 MIB_IF_ROW2 를 읽는다. 성공하면 NO_ERROR(0).</summary>
        [DllImport("iphlpapi.dll")]
        public static extern int GetIfEntry2(ref MIB_IF_ROW2 row);

        // ------------------------------------------------------------------
        // setupapi : 어댑터 비활성화/활성화 (DIF_PROPERTYCHANGE)
        // ------------------------------------------------------------------
        public static readonly Guid GUID_DEVCLASS_NET = new Guid("4D36E972-E325-11CE-BFC1-08002BE10318");

        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint SPDRP_DRIVER = 0x00000009;

        public const uint DIF_PROPERTYCHANGE = 0x00000012;
        public const uint DICS_ENABLE = 0x00000001;
        public const uint DICS_DISABLE = 0x00000002;
        public const uint DICS_FLAG_GLOBAL = 0x00000001;
        public const uint DICS_FLAG_CONFIGSPECIFIC = 0x00000002;

        public const uint DI_NEEDRESTART = 0x00000080;
        public const uint DI_NEEDREBOOT = 0x00000100;

        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_NO_MORE_ITEMS = 259;
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
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

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
        public static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

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
