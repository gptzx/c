using System;
using System.Globalization;
using MacChanger.Native;

namespace MacChanger.Core
{
    /// <summary>
    /// 선택한 어댑터의 송수신 바이트 카운터(iphlpapi GetIfEntry2, 64비트)를 주기적으로 읽어 속도(바이트/초)와 누적량을 계산한다.
    /// 누적량은 어댑터를 선택한 시점(또는 MAC 변경·원상복구·IP 갱신 완료 시점)부터 0으로 세고, 어댑터 재시작으로 OS 카운터가 0부터 다시 시작하면 그때부터 센다.
    /// </summary>
    public sealed class TrafficMonitor
    {
        // 공개 값은 속성 대신 필드로 둔다 (접근자 메서드가 없는 만큼 실행 파일이 작다) — 값은 이 클래스만 바꾸고 밖에서는 읽기만 한다.
        /// <summary>감시 중인 어댑터 GUID (없으면 null)</summary>
        public string InterfaceGuid;
        /// <summary>InterfaceGuid 를 파싱한 값 (Attach 때 한 번만; 없거나 형식이 틀리면 hasGuid=false 로 Sample 은 바로 false)</summary>
        private Guid guid;
        private bool hasGuid;

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
        public long ReceivedTotal, SentTotal;
        /// <summary>직전 샘플 구간의 수신/송신 속도 (바이트/초)</summary>
        public long ReceivedSpeed, SentSpeed;

        /// <summary>감시할 어댑터를 바꾼다 (null 이면 감시 안 함). 속도·누적량은 0부터 다시 센다.</summary>
        public void Attach(string interfaceGuid)
        {
            InterfaceGuid = interfaceGuid;
            hasGuid = interfaceGuid != null && Guid.TryParse(interfaceGuid, out guid);   // 1초마다 다시 파싱하지 않는다
            interfaceLuid = 0;
            hasLast = false;
            hasBase = false;
            ReceivedTotal = 0;
            SentTotal = 0;
            ReceivedSpeed = 0;
            SentSpeed = 0;
        }

        /// <summary>누적 송수신량을 0으로 되돌린다 (MAC 변경·원상복구·IP 갱신 완료 시). 다음 샘플의 카운터 값이 새 기준이 된다.</summary>
        public void ResetTotals()
        {
            hasBase = false;
            ReceivedTotal = 0;
            SentTotal = 0;
        }

        /// <summary>카운터를 한 번 읽는다. 어댑터가 IP 스택에 없으면(비활성화 등) false 를 돌려주고 속도는 0, 누적량은 그대로 둔다.</summary>
        public bool Sample()
        {
            ReceivedSpeed = 0;
            SentSpeed = 0;
            if (!hasGuid) return false;
            if (interfaceLuid != 0 || NativeMethods.ConvertInterfaceGuidToLuid(ref guid, out interfaceLuid) == 0)
            {
                NativeMethods.MIB_IF_ROW2 row = new NativeMethods.MIB_IF_ROW2();
                row.InterfaceLuid = interfaceLuid;
                if (NativeMethods.GetIfEntry2(ref row) == 0)
                {
                    Update((long)row.InOctets, (long)row.OutOctets, Environment.TickCount);
                    return true;
                }
            }
            interfaceLuid = 0;   // LUID 를 못 구했거나 어댑터가 내려가 있음 — 다시 올라오면 LUID 부터 다시 구한다
            hasLast = false;     // 첫 샘플은 속도 계산 없이 기준만 잡는다
            return false;
        }

        /// <summary>읽은 누적 카운터로 속도와 누적량을 계산한다 (nowTick 은 Environment.TickCount 기준 밀리초). Sample 만 부르며, 속도는 Sample 이 이미 0 으로 해 두었다.</summary>
        private void Update(long received, long sent, int nowTick)
        {
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
            double value = bytes;
            long size = 1024;   // 단위 하나의 바이트 수 (KB → MB → GB → TB, 한 단계마다 << 10 = ×1024)
            int unit = 0;       // 0 KB, 1 MB, 2 GB, 3 TB
            // 단위는 정수 bytes 로 고르고 나눗셈은 끝에 한 번만 한다 (나눈 값으로 고르면 32비트 x87 정밀도 설정에 따라 1024 경계에서 단위가 달라질 수 있다)
            while (unit < 3 && bytes >= (size << 10)) { size <<= 10; unit++; }
            value /= size;
            return value.ToString("0.00", CultureInfo.InvariantCulture) + " KB MB GB TB".Substring(unit * 3, 3);
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
