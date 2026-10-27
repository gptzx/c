using System;
using MacChanger.Native;

namespace MacChanger.Core
{
    /// <summary>
    /// IP 구성 변화를 Windows 가 알려 주는 대로 받는다 (주기 폴링 대신). iphlpapi 의 NotifyIpInterfaceChange(인터페이스 상태: 링크 연결/끊김, 어댑터 추가/제거),
    /// NotifyUnicastIpAddressChange(IPv4 주소 추가/삭제/상태 변화), NotifyRouteChange2(기본 게이트웨이 등 경로 변화)에 콜백을 등록한다 — Vista 이상, CPU 소모 없음.
    /// 콜백은 시스템 스레드에서 오므로 Changed 이벤트의 구독자가 UI 스레드로 넘겨야 한다. 등록에 실패하면(XP 등) Available 이 false 이고 호출자가 폴링으로 대신한다.
    /// </summary>
    public sealed class IpChangeNotifier : IDisposable
    {
        /// <summary>IPv4 구성에 변화가 있었다 (어느 인터페이스인지는 구분하지 않는다 — 구독자가 선택한 어댑터를 다시 읽는다). 시스템 스레드에서 발생.</summary>
        public event EventHandler Changed;

        private IntPtr interfaceHandle, addressHandle, routeHandle;
        // 델리게이트를 필드에 붙들어 두어야 GC 가 콜백을 거두지 않는다 (네이티브 쪽이 포인터를 계속 쓴다). 세 등록이 같은 콜백을 쓴다.
        private readonly NativeMethods.MibChangeCallback callback;
        private bool disposed;

        /// <summary>인터페이스 알림과 주소 알림 가운데 하나라도 등록되었는지 (경로 알림만으로는 IP 할당을 알 수 없으므로 세지 않는다). false 면 이 OS 에서는 쓸 수 없다(폴링으로 대신).</summary>
        public bool Available { get; private set; }
        /// <summary>등록 실패 사유 (모두 성공했으면 null)</summary>
        public string Error { get; private set; }

        public IpChangeNotifier()
        {
            callback = OnChange;
            try
            {
                int r1 = NativeMethods.NotifyIpInterfaceChange(NativeMethods.AF_INET, callback, IntPtr.Zero, false, out interfaceHandle);
                int r2 = NativeMethods.NotifyUnicastIpAddressChange(NativeMethods.AF_INET, callback, IntPtr.Zero, false, out addressHandle);
                int r3 = NativeMethods.NotifyRouteChange2(NativeMethods.AF_INET, callback, IntPtr.Zero, false, out routeHandle);
                if (r1 != 0) interfaceHandle = IntPtr.Zero;
                if (r2 != 0) addressHandle = IntPtr.Zero;
                if (r3 != 0) routeHandle = IntPtr.Zero;
                Available = interfaceHandle != IntPtr.Zero || addressHandle != IntPtr.Zero;
                if (r1 != 0 || r2 != 0 || r3 != 0)
                    Error = "NotifyIpInterfaceChange=" + r1 + ", NotifyUnicastIpAddressChange=" + r2 + ", NotifyRouteChange2=" + r3;
            }
            catch (Exception ex)   // EntryPointNotFound(XP 이전) 등
            {
                Available = false;
                Error = ex.Message;
            }
        }

        /// <summary>세 알림 공통 콜백 (시스템 스레드)</summary>
        private void OnChange(IntPtr callerContext, IntPtr row, int notificationType)
        {
            if (disposed) return;
            EventHandler handler = Changed;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { }   // 네이티브 콜백 안에서 예외가 새어 나가면 프로세스가 죽는다
        }

        /// <summary>알림 등록 해제. 콜백 안에서 부르면 교착되므로 반드시 UI 스레드(창 닫힘)에서 부른다.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Cancel(ref interfaceHandle);
            Cancel(ref addressHandle);
            Cancel(ref routeHandle);
        }

        private static void Cancel(ref IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try { NativeMethods.CancelMibChangeNotify2(handle); }
            catch (Exception) { }
            handle = IntPtr.Zero;
        }
    }
}
