using System.Windows;
using System.Windows.Controls;

namespace MyPlayer_NET9.Controls
{
    /// <summary>
    /// Grid, который всегда квадратный: высота равна доступной ширине.
    /// Заменяет приём «Height = {Binding ActualWidth}»: тот работает только ПОСЛЕ первой раскладки
    /// и вызывает вторую, из-за чего каждая новая карточка при прокрутке раскладывалась дважды.
    /// Здесь квадрат получается сразу, за один проход.
    /// </summary>
    public class SquareGrid : Grid
    {
        protected override Size MeasureOverride(Size constraint)
        {
            double side = double.IsInfinity(constraint.Width) ? constraint.Height : constraint.Width;
            if (double.IsInfinity(side)) side = 0;

            base.MeasureOverride(new Size(side, side));
            return new Size(side, side);
        }
    }
}
