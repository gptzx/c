using System;
using System.Drawing;
using System.Windows.Forms;
using MacChanger.Core;

namespace MacChanger
{
    /// <summary>
    /// TMAC(Technitium MAC Address Changer) 식 송수신 그래프.
    /// 위쪽: 흰 바탕에 수신(빨강)·송신(초록) 속도 꺾은선 — 샘플 하나가 가로 1픽셀, 최신 샘플이 오른쪽 끝, 보이는 구간의 최대값을 위 끝에 맞춘다.
    /// 아래쪽: 수신 누적량 / 속도, 송신 누적량 / 속도 네 줄 (빨강 / 초록 글자).
    /// </summary>
    public sealed class TrafficGraph : Control
    {
        /// <summary>보관하는 샘플 수 — DPI/글꼴 배율이 크게 적용된 전체 폭 그래프(536 × 배율)보다도 넉넉히 두어, 버퍼가 다 찬 뒤에도 그래프 왼쪽이 비지 않고 로그 상자를 숨겨 넓어져도 이전 이력이 보인다</summary>
        private const int Capacity = 4096;
        private readonly long[] received = new long[Capacity];
        private readonly long[] sent = new long[Capacity];
        private int head;    // 다음 샘플을 쓸 위치
        private int count;
        private long receivedTotal, sentTotal, receivedSpeed, sentSpeed;

        private static readonly Color TableBack = Color.FromArgb(248, 248, 248);
        private static readonly Color TableLine = Color.FromArgb(240, 240, 240);
        private static readonly Color TableBorder = Color.FromArgb(100, 100, 100);
        private static readonly Color ReceivedLine = Color.Red;
        private static readonly Color SentLine = Color.FromArgb(10, 128, 10);
        private static readonly Color ReceivedText = Color.FromArgb(192, 0, 0);
        private static readonly Color SentText = Color.Green;
        private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        /// <summary>값 칸: 폭이 모자라면(반폭 그래프에 TB 단위 값 등) 끝을 "…" 으로 줄인다</summary>
        private const TextFormatFlags ValueFlags = TextFlags | TextFormatFlags.EndEllipsis;

        public TrafficGraph()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        /// <summary>1초 치 속도(바이트/초)를 이력에 넣고 아래 숫자를 갱신한다.</summary>
        public void AddSample(long receivedPerSecond, long sentPerSecond, long receivedBytes, long sentBytes)
        {
            received[head] = receivedPerSecond;
            sent[head] = sentPerSecond;
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
            receivedSpeed = receivedPerSecond;
            sentSpeed = sentPerSecond;
            receivedTotal = receivedBytes;
            sentTotal = sentBytes;
            Invalidate();
        }

        /// <summary>누적량 표시만 0으로 되돌린다 (MAC 변경 완료 시 — 그래프 이력과 속도는 유지).</summary>
        public void ResetTotals()
        {
            receivedTotal = sentTotal = 0;
            Invalidate();
        }

        /// <summary>이력과 숫자를 모두 지운다 (다른 어댑터를 선택했을 때).</summary>
        public void Clear()
        {
            head = count = 0;
            receivedTotal = sentTotal = receivedSpeed = sentSpeed = 0;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // OnPaint 가 전체를 그린다 (깜빡임 방지)
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 4 || h < 4) return;

            // 표: 테두리(회색) 안에 구분선 5개 + 글 4줄. 표의 위 테두리가 그래프의 아래 테두리를 겸한다.
            int rowHeight = Math.Max(Font.Height, TextRenderer.MeasureText(g, "수신", Font, Size.Empty, TextFlags).Height);
            int rowPitch = rowHeight + 1;
            int tableTop = Math.Max(4, h - 4 * rowPitch - 3);
            int innerWidth = w - 2;
            int graphHeight = tableTop - 1;   // 그래프 안쪽 높이 (y = 1 .. tableTop - 1), 값 0은 표의 위 테두리 줄에 놓여 가려진다

            g.FillRectangle(Brushes.White, 1, 1, innerWidth, graphHeight);
            DrawLines(g, innerWidth, graphHeight, tableTop);
            g.DrawLine(Pens.Black, 0, 0, w - 1, 0);
            g.DrawLine(Pens.Black, 0, 0, 0, tableTop - 1);
            g.DrawLine(Pens.Black, w - 1, 0, w - 1, tableTop - 1);

            using (SolidBrush back = new SolidBrush(TableBack))
                g.FillRectangle(back, 1, tableTop + 1, innerWidth, h - tableTop - 2);
            using (Pen border = new Pen(TableBorder))
                g.DrawRectangle(border, 0, tableTop, w - 1, h - 1 - tableTop);

            string[] labels = { "수신", "--속도", "송신", "--속도" };
            string[] values =
            {
                TrafficMonitor.FormatTotal(receivedTotal), TrafficMonitor.FormatSpeed(receivedSpeed),
                TrafficMonitor.FormatTotal(sentTotal), TrafficMonitor.FormatSpeed(sentSpeed)
            };
            int labelX = 5;
            int indent = rowHeight * 2 / 3;   // "--속도" 들여쓰기
            int separatorX = labelX + indent + TextRenderer.MeasureText(g, labels[1], Font, Size.Empty, TextFlags).Width + rowHeight / 2;   // 라벨 칸 | 값 칸 세로 구분선
            int valueX = separatorX + rowHeight / 2;
            int valueWidth = Math.Max(1, w - 1 - labelX - valueX);
            using (Pen line = new Pen(TableLine))
            {
                int y = tableTop + 1;
                for (int row = 0; row < 4; row++)
                {
                    g.DrawLine(line, 1, y, w - 2, y);
                    Color color = row < 2 ? ReceivedText : SentText;
                    TextRenderer.DrawText(g, labels[row], Font, new Point(labelX + (row % 2 == 1 ? indent : 0), y + 1), color, TextFlags);
                    TextRenderer.DrawText(g, values[row], Font, new Rectangle(valueX, y + 1, valueWidth, rowHeight), color, ValueFlags);
                    y += rowPitch;
                }
                g.DrawLine(line, 1, y, w - 2, y);
                g.DrawLine(line, separatorX, tableTop + 1, separatorX, y);
            }
        }

        /// <summary>보이는 구간(최근 innerWidth 개)의 최대값을 위 끝에 맞춰 송신·수신 꺾은선을 그린다.</summary>
        private void DrawLines(Graphics g, int innerWidth, int graphHeight, int baseline)
        {
            int n = Math.Min(count, innerWidth);
            if (n < 2) return;
            int first = (head - n + Capacity) % Capacity;   // 가장 오래된 샘플
            long max = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = (first + i) % Capacity;
                if (received[idx] > max) max = received[idx];
                if (sent[idx] > max) max = sent[idx];
            }
            if (max == 0) return;   // 전부 0: 선이 테두리에 가려지므로 그릴 것이 없다
            Point[] rx = new Point[n];
            Point[] tx = new Point[n];
            int x0 = 1 + innerWidth - n;   // 최신 샘플이 오른쪽 끝
            for (int i = 0; i < n; i++)
            {
                int idx = (first + i) % Capacity;
                rx[i] = new Point(x0 + i, baseline - (int)(received[idx] * graphHeight / max));
                tx[i] = new Point(x0 + i, baseline - (int)(sent[idx] * graphHeight / max));
            }
            using (Pen pen = new Pen(ReceivedLine)) g.DrawLines(pen, rx);
            using (Pen pen = new Pen(SentLine)) g.DrawLines(pen, tx);   // 보통 더 작은 송신 선이 가려지지 않도록 나중에 그린다
        }
    }
}
