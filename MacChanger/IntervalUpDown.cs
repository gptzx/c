using System.Windows.Forms;

namespace MacChanger
{
    /// <summary>
    /// IP 확인 주기 입력 칸: 위/아래 버튼(키보드 화살표·마우스 휠 포함)으로는 Increment 단위로만 움직이고 ButtonMinimum 아래로 내려가지 않지만,
    /// 직접 입력하면 Minimum~Maximum 범위 안의 어떤 값(1ms 단위)이든 받는다.
    /// </summary>
    public sealed class IntervalUpDown : NumericUpDown
    {
        /// <summary>버튼으로 내려갈 수 있는 가장 작은 값 (직접 입력에는 적용되지 않는다)</summary>
        public decimal ButtonMinimum { get; set; }

        public override void UpButton()
        {
            if (ReadOnly) return;
            decimal next = Value < ButtonMinimum ? ButtonMinimum : Value + Increment;   // 100 미만에서 올리면 먼저 100으로 맞춘다
            Value = next > Maximum ? Maximum : next;
        }

        public override void DownButton()
        {
            if (ReadOnly || Value <= ButtonMinimum) return;   // 버튼으로는 ButtonMinimum 아래로 내려가지 않는다
            decimal next = Value - Increment;
            Value = next < ButtonMinimum ? ButtonMinimum : next;
        }
    }
}
