using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MacChanger.Native;

namespace MacChanger
{
    /// <summary>
    /// 트레이 아이콘의 팝업 메뉴. 기본 ContextMenuStrip(왼쪽 아이콘 여백, 각진 모서리, 오른쪽·아래에만 지는 그림자) 대신
    /// 창 전체를 픽셀 알파가 있는 레이어드 창(UpdateLayeredWindow)으로 직접 그린다:
    /// 모서리가 살짝 둥근 밝은 판 + 1px 테두리 + 판 둘레로 번지는 부드러운 그림자(아우라), 여백 없는 항목.
    /// 마우스를 올린 항목은 옅게 강조되고, 항목을 누르거나 Enter 를 치면 동작을 실행한다. 바깥을 누르거나 다른 창이 활성화되거나 Esc 를 치면 닫힌다.
    /// </summary>
    internal sealed class TrayMenu : Form
    {
        // 96 DPI 기준 치수 — 배율은 S() 가 곱한다
        private const int ItemHeight = 22;
        private const int PadX = 14;           // 항목 글자 왼쪽 여백
        private const int PadY = 4;            // 판 위아래 여백
        private const int MinPanelWidth = 100;
        private const int CornerRadius = 4;
        private const int ShadowMargin = 16;   // 판 둘레에 그림자를 그릴 여백 (창은 판보다 이만큼 사방으로 크다)
        private const int ShadowBlur = 5;      // 그림자 번짐 — 박스 블러 반지름, 세 번 겹치면 가우시안에 가깝다
        private const int ShadowOffsetY = 6;   // 그림자를 아래로 내려 아래쪽은 짙고(약 9%) 위쪽은 거의 없게
        private const int ShadowInset = 5;     // 그림자 상자를 판보다 조금 작게 — 옆면 그림자는 옅게(약 3%)
        private const int ShadowAlpha = 46;    // 그림자 가장 짙은 곳의 알파 (약 18%)

        // 회색 단계 (예시 플라이아웃에서 잰 값): 판 249, 테두리 229, 강조 234
        private const int PanelGray = 249;
        private const int BorderGray = 229;
        private const int HoverGray = 234;
        private const TextFormatFlags TextFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        private readonly Control owner;
        private readonly List<string> labels = new List<string>();
        private readonly List<Action> actions = new List<Action>();
        private readonly float scale;
        /// <summary>안전망: 앞 창이었다가 Deactivate 없이 밀려났거나, 애초에 앞 창이 되지 못했으면(키보드 없이 마우스만 동작) 바깥 클릭을 직접 살펴 닫는다</summary>
        private readonly Timer guard = new Timer();
        /// <summary>창 좌표에서 판(그림자 여백 안쪽)이 차지하는 영역</summary>
        private Rectangle panel;
        private int hover = -1;
        /// <summary>띄운 뒤 앞 창(포그라운드)이 된 적이 있는지</summary>
        private bool foreground;

        /// <summary>owner 의 글꼴로 항목을 그리고, 고른 항목의 동작은 owner 의 메시지 루프에서(이 창의 처리가 끝난 뒤) 실행한다.</summary>
        public TrayMenu(Control owner)
        {
            this.owner = owner;
            Font = owner.Font;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;   // 작업 표시줄(최상위 창) 위에 떠야 한다
            ImeMode = ImeMode.Disable;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            guard.Interval = 100;
            guard.Tick += guard_Tick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) guard.Dispose();
            base.Dispose(disposing);
        }

        public void AddItem(string text, Action action)
        {
            labels.Add(text);
            actions.Add(action);
        }

        /// <summary>커서 위치(anchor)에 메뉴를 띄운다. 작업 표시줄 쪽으로는 자리가 없으므로 그 반대쪽(위·왼쪽)으로 펼친다.</summary>
        public void Popup(Point anchor)
        {
            if (labels.Count == 0) return;
            Size size = MeasurePanel();
            Point at = PlacePanel(anchor, size, Screen.FromPoint(anchor).WorkingArea);
            int margin = S(ShadowMargin);
            panel = new Rectangle(margin, margin, size.Width, size.Height);
            hover = -1;
            foreground = false;
            Bounds = new Rectangle(at.X - margin, at.Y - margin, size.Width + 2 * margin, size.Height + 2 * margin);
            Render();
            Show();
            Activate();   // = SetForegroundWindow(Handle): 트레이 아이콘을 누른 직후에는 포그라운드를 가져올 수 있다 (TrackPopupMenu 와 같은 규칙)
            foreground = NativeMethods.GetForegroundWindow() == Handle;
            MouseButtonPressed();   // 메뉴를 연 클릭이 "그 뒤에 눌린 버튼"으로 남지 않게 지운다
        }

        /// <summary>판의 왼쪽 위 좌표: 커서에서 오른쪽·아래로 펼치되 작업 영역을 넘으면 왼쪽·위로 뒤집고, 그래도 넘으면 안쪽으로 민다.</summary>
        private static Point PlacePanel(Point anchor, Size size, Rectangle work)
        {
            int x = anchor.X + size.Width <= work.Right ? anchor.X : anchor.X - size.Width;
            int y = anchor.Y + size.Height <= work.Bottom ? anchor.Y : anchor.Y - size.Height;
            x = Math.Max(work.Left, Math.Min(x, work.Right - size.Width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height));
            return new Point(x, y);
        }

        private Size MeasurePanel()
        {
            int width = S(MinPanelWidth);
            foreach (string label in labels)
                width = Math.Max(width, TextRenderer.MeasureText(label, Font, Size.Empty, TextFlags).Width + 2 * S(PadX));
            return new Size(width, labels.Count * S(ItemHeight) + 2 * S(PadY));
        }

        private int S(int value)
        {
            return (int)Math.Round(value * scale);
        }

        /// <summary>판(face 비트맵) 좌표에서 항목이 차지하는 영역</summary>
        private Rectangle ItemRect(int index)
        {
            return new Rectangle(0, S(PadY) + index * S(ItemHeight), panel.Width, S(ItemHeight));
        }

        /// <summary>창 좌표 p 에 있는 항목 번호 (판 밖·여백이면 -1)</summary>
        private int ItemAt(Point p)
        {
            int top = panel.Y + S(PadY);
            if (!panel.Contains(p) || p.Y < top) return -1;
            int index = (p.Y - top) / S(ItemHeight);
            return index < labels.Count ? index : -1;
        }

        // ------------------------------------------------------------------
        // 그리기 — 합성은 픽셀 단위로 직접 한다 (GDI+ 의 안티앨리어싱 채우기는 Windows 에서 픽셀 중심이 정수 좌표에 놓여 1px 테두리가 두 픽셀에 번진다)
        // ------------------------------------------------------------------
        /// <summary>창 전체 비트맵(미리 곱한 알파): 픽셀마다 그림자 → 테두리(바깥 둥근 사각형) → 판(1px 안쪽 둥근 사각형에 오려 넣은 불투명 비트맵) 순으로 덮는다.</summary>
        private Bitmap RenderBitmap()
        {
            int w = Width, h = Height, radius = S(CornerRadius);
            Rectangle box = panel;
            box.Inflate(-S(ShadowInset), -S(ShadowInset));
            box.Offset(0, S(ShadowOffsetY));
            float[] across = BlurredBox(w, box.Left, box.Right, S(ShadowBlur));   // 축에 나란한 상자의 블러는 가로 곡선 × 세로 곡선
            float[] down = BlurredBox(h, box.Top, box.Bottom, S(ShadowBlur));
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Bitmap face = DrawFace())
            {
                BitmapData src = face.LockBits(new Rectangle(0, 0, panel.Width, panel.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                BitmapData dst = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int[] faceRow = new int[panel.Width], row = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        int fy = y - panel.Y;
                        if (fy >= 0 && fy < panel.Height) Marshal.Copy(IntPtr.Add(src.Scan0, fy * src.Stride), faceRow, 0, panel.Width);
                        for (int x = 0; x < w; x++)
                        {
                            int fx = x - panel.X;
                            float a = ShadowAlpha * across[x] * down[y], r = 0, g = 0, b = 0;   // 미리 곱한 값 0..255: 검은 그림자부터
                            float outer = Coverage(fx, fy, panel.Width, panel.Height, radius);
                            if (outer > 0)
                            {
                                a += outer * (255 - a);
                                r += outer * (BorderGray - r); g += outer * (BorderGray - g); b += outer * (BorderGray - b);
                                float inner = Coverage(fx - 1, fy - 1, panel.Width - 2, panel.Height - 2, radius - 1);
                                if (inner > 0)
                                {
                                    int f = faceRow[fx];
                                    a += inner * (255 - a);
                                    r += inner * (((f >> 16) & 255) - r); g += inner * (((f >> 8) & 255) - g); b += inner * ((f & 255) - b);
                                }
                            }
                            row[x] = ((int)(a + 0.5f) << 24) | ((int)(r + 0.5f) << 16) | ((int)(g + 0.5f) << 8) | (int)(b + 0.5f);
                        }
                        Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, y * dst.Stride), w);
                    }
                }
                finally
                {
                    bmp.UnlockBits(dst);
                    face.UnlockBits(src);
                }
            }
            return bmp;
        }

        /// <summary>판 크기의 불투명 비트맵: 바탕과 강조(둥근 사각형)는 픽셀로 직접 쓰고, 항목 글자는 화면과 같은 방식(화면 호환 버퍼 + GDI)으로 그린다.</summary>
        private Bitmap DrawFace()
        {
            Bitmap face = new Bitmap(panel.Width, panel.Height, PixelFormat.Format32bppRgb);
            Rectangle mark = hover >= 0 ? ItemRect(hover) : Rectangle.Empty;
            mark.Inflate(-S(4), -S(1));
            BitmapData data = face.LockBits(new Rectangle(0, 0, panel.Width, panel.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int[] row = new int[panel.Width];
                for (int y = 0; y < panel.Height; y++)
                {
                    for (int x = 0; x < panel.Width; x++)
                    {
                        float c = hover >= 0 ? Coverage(x - mark.X, y - mark.Y, mark.Width, mark.Height, S(CornerRadius)) : 0f;
                        int v = (int)(PanelGray + c * (HoverGray - PanelGray) + 0.5f);
                        row[x] = (v << 16) | (v << 8) | v;
                    }
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), panel.Width);
                }
            }
            finally
            {
                face.UnlockBits(data);
            }
            // 글자는 예전 메뉴(ContextMenuStrip)와 똑같이 화면 호환 버퍼(DDB)에 GDI 로 그린다 — 메모리 DIB 에 그리면 GDI 가 글자를 매끄럽게(ClearType) 그리지 않는다.
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
            using (BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(screen, new Rectangle(0, 0, panel.Width, panel.Height)))
            {
                buffer.Graphics.DrawImageUnscaled(face, 0, 0);   // 바탕·강조를 버퍼에 복사한 뒤 그 위에 글자
                for (int i = 0; i < labels.Count; i++)
                {
                    Rectangle r = ItemRect(i);
                    r.Inflate(-S(PadX), 0);
                    TextRenderer.DrawText(buffer.Graphics, labels[i], Font, r, Color.Black, TextFlags);
                }
                using (Graphics g = Graphics.FromImage(face)) buffer.Render(g);   // 버퍼(DDB)의 픽셀을 그대로 face 로
            }
            return face;
        }

        /// <summary>왼쪽 위가 (0,0)인 w×h 둥근 사각형(반지름 r)이 픽셀 (x,y)를 덮는 비율. 모서리 안쪽 픽셀만 4×4 표본으로 재고 나머지는 0 또는 1.</summary>
        private static float Coverage(int x, int y, int w, int h, int r)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return 0f;
            if ((x >= r && x < w - r) || (y >= r && y < h - r)) return 1f;
            float cx = x < r ? r : w - r, cy = y < r ? r : h - r;   // 가까운 모서리 원의 중심
            int inside = 0;
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    float dx = x + (i + 0.5f) / 4 - cx, dy = y + (j + 0.5f) / 4 - cy;
                    if (dx * dx + dy * dy <= r * r) inside++;
                }
            return inside / 16f;
        }

        /// <summary>길이 length 의 1차원 곡선: [from, to) 는 1, 밖은 0 인 상자를 반지름 radius 의 박스 블러로 세 번 흐린 값.</summary>
        private static float[] BlurredBox(int length, int from, int to, int radius)
        {
            float[] values = new float[length];
            for (int i = Math.Max(0, from); i < Math.Min(length, to); i++) values[i] = 1f;
            for (int pass = 0; pass < 3; pass++) values = BoxBlur(values, radius);
            return values;
        }

        private static float[] BoxBlur(float[] source, int radius)
        {
            float[] result = new float[source.Length];
            float window = 2 * radius + 1, sum = 0;
            for (int i = 0; i < radius && i < source.Length; i++) sum += source[i];
            for (int i = 0; i < source.Length; i++)
            {
                if (i + radius < source.Length) sum += source[i + radius];
                result[i] = sum / window;
                if (i - radius >= 0) sum -= source[i - radius];
            }
            return result;
        }

        /// <summary>비트맵을 창에 그대로 올린다 (레이어드 창은 WM_PAINT 를 받지 않으므로 강조가 바뀔 때마다 다시 올린다).</summary>
        private void Render()
        {
            using (Bitmap bmp = RenderBitmap())
            {
                IntPtr memory = NativeMethods.CreateCompatibleDC(IntPtr.Zero);   // NULL = 화면과 호환되는 메모리 DC
                IntPtr hBitmap = IntPtr.Zero, previous = IntPtr.Zero;
                try
                {
                    hBitmap = bmp.GetHbitmap(Color.FromArgb(0));   // 알파를 유지한 32비트 DIB
                    previous = NativeMethods.SelectObject(memory, hBitmap);
                    NativeMethods.POINT at = new NativeMethods.POINT(Left, Top);
                    NativeMethods.POINT origin = new NativeMethods.POINT(0, 0);
                    NativeMethods.SIZE size = new NativeMethods.SIZE(bmp.Width, bmp.Height);
                    NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                    blend.SourceConstantAlpha = 255;
                    blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;
                    NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref at, ref size, memory, ref origin, 0, ref blend, NativeMethods.ULW_ALPHA);
                }
                finally
                {
                    if (previous != IntPtr.Zero) NativeMethods.SelectObject(memory, previous);
                    if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
                    NativeMethods.DeleteDC(memory);
                }
            }
        }

        // ------------------------------------------------------------------
        // 입력
        // ------------------------------------------------------------------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(ItemAt(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(-1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!panel.Contains(e.Location)) Hide();   // 그림자 여백을 누르면 바깥을 누른 것 — 메뉴처럼 그 클릭은 삼키고 닫는다
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) Choose(ItemAt(e.Location));
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            guard.Enabled = Visible;
            if (!Visible) hover = -1;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Hide(); return true;
                case Keys.Down: SetHover((hover + 1) % labels.Count); return true;
                case Keys.Up: SetHover(hover < 0 ? labels.Count - 1 : (hover + labels.Count - 1) % labels.Count); return true;
                case Keys.Enter:
                case Keys.Space: Choose(hover); return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void guard_Tick(object sender, EventArgs e)
        {
            if (!Visible) return;
            if (NativeMethods.GetForegroundWindow() == Handle)
            {
                foreground = true;   // 정상: 바깥을 누르면 Deactivate 가 닫는다
                return;
            }
            if (foreground || (MouseButtonPressed() && !Bounds.Contains(Cursor.Position))) Hide();   // 앞 창에서 밀려났거나, 앞 창이 아닌 채 바깥을 눌렀다
        }

        /// <summary>마우스 버튼이 지금 눌려 있거나 지난 호출 뒤에 눌린 적이 있는지 (호출하면 "눌린 적" 표시는 지워진다)</summary>
        private static bool MouseButtonPressed()
        {
            int state = (ushort)NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) | (ushort)NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) | (ushort)NativeMethods.GetAsyncKeyState(NativeMethods.VK_MBUTTON);
            return (state & 0x8001) != 0;
        }

        private void SetHover(int index)
        {
            if (index == hover) return;
            hover = index;
            Render();
        }

        /// <summary>항목의 동작을 실행한다 — 창을 먼저 닫고, 동작은 owner 쪽에서 이 창의 메시지 처리가 끝난 뒤에 돌린다 (종료가 이 창을 Dispose 해도 안전하다).</summary>
        private void Choose(int index)
        {
            if (index < 0) return;
            Hide();
            owner.BeginInvoke(actions[index]);
        }
    }
}
