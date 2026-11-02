using System;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>Перемотка на заданный интервал — для горячих клавиш (Behaviors/PlayerHotkeys).</summary>
    public partial class MainViewModel
    {
        /// <summary>Не даём перемотать в самый конец: до конца всегда остаётся запас, трек заканчивается сам и штатно переходит к следующему.</summary>
        private static readonly TimeSpan SeekEndMargin = TimeSpan.FromMilliseconds(300);

        /// <summary>Сдвигает позицию воспроизведения на delta (отрицательное — назад). Играет или на паузе — не важно.</summary>
        public void SeekBy(TimeSpan delta)
        {
            if (CurrentTrack == null || Duration <= TimeSpan.Zero) return;

            TimeSpan limit = Duration - SeekEndMargin;
            if (limit < TimeSpan.Zero) limit = TimeSpan.Zero;

            TimeSpan target = Position + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > limit) target = limit;

            if (target == Position) return;

            // Трек после запуска приложения ещё не открыт плеером — сохранённая позиция применится при открытии, и она теперь эта.
            if (_pendingRestorePosition != null)
                _pendingRestorePosition = target;

            Position = target;
        }
    }
}
