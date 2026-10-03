using System;
using System.Globalization;
using MacChanger.Native;

namespace MacChanger.Core
{
    /// <summary>
    /// 선택한 어댑터의 송수신 바이트 카운터(iphlpapi GetIfEntry2, 64비트)를 주기적으로 읽어 속도(바이트/초)와 누적량을 계산한다.
    /// 누적량은 어댑터를 선택한 시점(또는 MAC 변경 완료 시점)부터 0으로 세고, 어댑터 재시작으로 OS 카운터가 0부터 다시 시작하면 그때부터 센다.
    /// </summary>
    public sealed class TrafficMonitor
    {
        /// <summary>감시 중인 어댑터 GUID (없으면 null)</summary>
        public string InterfaceGuid { get; private set; }

        /// <summary>GUID 에서 구한 NET_LUID (아직 못 구했거나 조회가 실패하면 0 — 다음 샘플 때 다시 구한다)</summary>
        private ulong interfaceLuid;

        /// <summary>직전 샘플의 OS 누적 카운터와 시각 (속도 계산용)</summary>
        private bool hasLast;
        private long lastReceived, lastSent;
        private int lastTick;
        /// <summary>누적량 표시 기준이 되는 카운터 값 — 이 값부터 0으로 센다</summary>
        private bool hasBase;
        private long baseReceived, baseSent;

        /// <summary>기준 시점 이후 누적 수신/송신 바이트</summary>
        public long ReceivedTotal { get; private set; }
        public long SentTotal { get; private set; }
        /// <summary>직전 샘플 구간의 수신/송신 속도 (바이트/초)</summary>
        public long ReceivedSpeed { get; private set; }
        public long SentSpeed { get; private set; }

        /// <summary>감시할 어댑터를 바꾼다 (null 이면 감시 안 함). 속도·누적량은 0부터 다시 센다.</summary>
        public void Attach(string interfaceGuid)
        {
            InterfaceGuid = interfaceGuid;
            interfaceLuid = 0;
            hasLast = false;
            hasBase = false;
            ReceivedTotal = SentTotal = ReceivedSpeed = SentSpeed = 0;
        }

        /// <summary>누적 송수신량을 0으로 되돌린다 (MAC 변경 완료 시). 다음 샘플의 카운터 값이 새 기준이 된다.</summary>
        public void ResetTotals()
        {
            hasBase = false;
            ReceivedTotal = SentTotal = 0;
        }

        /// <summary>카운터를 한 번 읽는다. 어댑터가 IP 스택에 없으면(비활성화 등) false 를 돌려주고 속도는 0, 누적량은 그대로 둔다.</summary>
        public bool Sample()
        {
            ReceivedSpeed = SentSpeed = 0;
            Guid guid;
            if (InterfaceGuid == null || !Guid.TryParse(InterfaceGuid, out guid)) return false;
            if (interfaceLuid == 0 && NativeMethods.ConvertInterfaceGuidToLuid(ref guid, out interfaceLuid) != 0)
            {
                interfaceLuid = 0;
                hasLast = false;
                return false;
            }
            NativeMethods.MIB_IF_ROW2 row = new NativeMethods.MIB_IF_ROW2();
            row.InterfaceLuid = interfaceLuid;
            if (NativeMethods.GetIfEntry2(ref row) != 0)
            {
                interfaceLuid = 0;   // 어댑터가 내려가 있음 — 다시 올라오면 LUID 부터 다시 구한다
                hasLast = false;     // 첫 샘플은 속도 계산 없이 기준만 잡는다
                return false;
            }
            Update((long)row.InOctets, (long)row.OutOctets, Environment.TickCount);
            return true;
        }

        /// <summary>읽은 누적 카운터로 속도와 누적량을 계산한다 (nowTick 은 Environment.TickCount 기준 밀리초).</summary>
        public void Update(long received, long sent, int nowTick)
        {
            ReceivedSpeed = SentSpeed = 0;
            if (hasLast)
            {
                int ms = unchecked(nowTick - lastTick);
                // 카운터가 줄었으면(어댑터 재시작으로 0부터 다시 시작) 그 구간의 속도는 0으로 둔다
                if (ms > 0 && received >= lastReceived && sent >= lastSent)
                {
                    ReceivedSpeed = (received - lastReceived) * 1000 / ms;
                    SentSpeed = (sent - lastSent) * 1000 / ms;
                }
            }
            lastReceived = received;
            lastSent = sent;
            lastTick = nowTick;
            hasLast = true;

            if (!hasBase)
            {
                hasBase = true;   // 처음 읽을 때 / 초기화 요청 뒤: 지금 값부터 0으로 센다
                baseReceived = received;
                baseSent = sent;
            }
            else
            {
                if (received < baseReceived) baseReceived = 0;   // OS 카운터가 0부터 다시 시작 → 그때부터 센다
                if (sent < baseSent) baseSent = 0;
            }
            ReceivedTotal = received - baseReceived;
            SentTotal = sent - baseSent;
        }

        /// <summary>"23.75 KB" 형식 (1024 단위, 소수 둘째 자리). 1 MB 이상이면 MB / GB / TB.</summary>
        public static string FormatSize(long bytes)
        {
            const double K = 1024;
            double value = bytes;
            string unit;
            if (value >= K * K * K * K) { value /= K * K * K * K; unit = " TB"; }
            else if (value >= K * K * K) { value /= K * K * K; unit = " GB"; }
            else if (value >= K * K) { value /= K * K; unit = " MB"; }
            else { value /= K; unit = " KB"; }
            return value.ToString("0.00", CultureInfo.InvariantCulture) + unit;
        }

        /// <summary>누적량 표시: "23.75 KB (24315 바이트)"</summary>
        public static string FormatTotal(long bytes)
        {
            return FormatSize(bytes) + " (" + bytes.ToString(CultureInfo.InvariantCulture) + " 바이트)";
        }

        /// <summary>속도 표시: "6.05 KB/s (6193 바이트)"</summary>
        public static string FormatSpeed(long bytesPerSecond)
        {
            return FormatSize(bytesPerSecond) + "/s (" + bytesPerSecond.ToString(CultureInfo.InvariantCulture) + " 바이트)";
        }
    }
}
