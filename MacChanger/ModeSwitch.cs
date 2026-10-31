using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacChanger
{
    /// <summary>
    /// 두 방식 가운데 하나를 고르는 토글 스위치 (자동 변경: 왼쪽 = IP 할당 시, 오른쪽 = 일정 시간마다).
    /// 둥근 홈(트랙) 위의 손잡이가 고른 쪽으로 옮겨 간다. 클릭·Space·Enter 는 반대쪽으로, ←/→ 는 그쪽으로.
    /// </summary>
    internal sealed class ModeSwitch : Control
    {
        public event EventHandler CheckedChanged;
        private bool isChecked;

        public ModeSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;   // 화면 읽기 프로그램용: 이름과 역할 (글자가 없는 컨트롤이므로 직접 준다)
            AccessibleName = "자동 변경 방식";
        }

        /// <summary>false = 왼쪽(기본), true = 오른쪽</summary>
        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Height - 1, w = Width - 1;
            // 홈: 높이만큼 둥근 알약 모양 (양 끝 원 + 가운데 사각형). 쓸 수 있으면 강조색, 잠겨 있으면 회색
            using (Brush fill = new SolidBrush(Enabled ? SystemColors.Highlight : SystemColors.ControlDark))
            {
                g.FillEllipse(fill, 0, 0, h, h);
                g.FillEllipse(fill, w - h, 0, h, h);
                g.FillRectangle(fill, h / 2, 0, w - h, h + 1);
            }
            // 손잡이: 홈 안쪽에 2px 여백을 둔 흰 원, 고른 쪽 끝에 놓인다
            int d = h - 4;
            g.FillEllipse(Brushes.White, isChecked ? w - 2 - d : 2, 2, d, d);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            MouseEventArgs mouse = e as MouseEventArgs;   // 마우스 클릭은 왼쪽 버튼만 (다른 버튼·체크박스와 같게)
            if (mouse == null || mouse.Button == MouseButtons.Left) Checked = !Checked;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        /// <summary>←/→ 는 포커스 이동이 아니라 이 컨트롤이 받는다</summary>
        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Space:
                case Keys.Enter: Checked = !Checked; break;
                case Keys.Left: Checked = false; break;
                case Keys.Right: Checked = true; break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
