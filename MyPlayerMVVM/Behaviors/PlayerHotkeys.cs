using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MyPlayer_NET9.ViewModels;

namespace MyPlayer_NET9.Behaviors
{
    /// <summary>
    /// Управление плеером с клавиатуры — отдельный модуль, который просто подключается к окну
    /// (<see cref="Attach"/>) и ходит в ViewModel только через её публичные команды.
    ///
    ///   Пробел          — пауза / воспроизведение
    ///   ←  /  →         — предыдущий / следующий трек
    ///   A  /  D         — перемотка назад / вперёд
    ///
    /// Перемотка: короткое нажатие = ±5 секунд. Если клавишу держать, через 0.4 с начинается плавная
    /// «прокрутка» звука, которая разгоняется от 10 до 45 секунд в секунду за 3 секунды (чем длиннее трек —
    /// тем быстрее, до ×3 для треков от 15 минут: часовой микс иначе перематывать мучительно).
    ///
    /// Клавиши работают, пока фокус не в поле ввода (поиск, название плейлиста) — там набираем текст как обычно.
    /// Буквы A/D распознаются по физической клавише, поэтому работают и в русской раскладке (Ф / В).
    /// </summary>
    public sealed class PlayerHotkeys
    {
        private const double TapSeekSeconds = 5.0;

        /// <summary>Сколько держать клавишу, прежде чем короткий скачок перейдёт в непрерывную перемотку.</summary>
        private const double HoldDelaySeconds = 0.4;

        private const double HoldStartRate = 10.0;   // сек/сек в начале удержания
        private const double HoldMaxRate = 45.0;     // сек/сек после разгона
        private const double RampSeconds = 3.0;      // за сколько разгоняемся

        /// <summary>Трек такой длины (сек) и короче перематывается с базовой скоростью; длиннее — пропорционально быстрее (до ×3).</summary>
        private const double ReferenceTrackSeconds = 300.0;
        private const double MaxLengthFactor = 3.0;

        private static readonly TimeSpan SeekTickInterval = TimeSpan.FromMilliseconds(80);

        private readonly MainViewModel _vm;
        private readonly DispatcherTimer _seekTimer;
        private readonly Stopwatch _held = new Stopwatch();

        private Key? _seekKey;
        private int _seekDirection;
        private double _lastTickSeconds;

        private PlayerHotkeys(Window window, MainViewModel vm)
        {
            _vm = vm;

            _seekTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = SeekTickInterval };
            _seekTimer.Tick += OnSeekTick;

            window.PreviewKeyDown += OnPreviewKeyDown;
            window.PreviewKeyUp += OnPreviewKeyUp;

            // Окно потеряло активность, пока клавиша зажата, — KeyUp мы уже не увидим, останавливаем перемотку сами.
            window.Deactivated += (_, _) => StopSeek();
        }

        public static PlayerHotkeys Attach(Window window, MainViewModel viewModel) =>
            new PlayerHotkeys(window, viewModel);

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Любые сочетания (Ctrl+A, Alt+←, …) — не наши.
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            // Печатаем текст — клавиши принадлежат полю ввода.
            if (IsTypingInTextField()) return;

            switch (e.Key)
            {
                case Key.Space:
                    // Handled ставим всегда: иначе пробел «нажмёт» кнопку, на которой остался фокус после клика мышью.
                    if (!e.IsRepeat) Run(_vm.PlayPauseCommand);
                    e.Handled = true;
                    break;

                case Key.Left:
                    if (!e.IsRepeat) Run(_vm.PreviousCommand);
                    e.Handled = true;
                    break;

                case Key.Right:
                    if (!e.IsRepeat) Run(_vm.NextCommand);
                    e.Handled = true;
                    break;

                case Key.A:
                    if (!e.IsRepeat) BeginSeek(Key.A, -1);
                    e.Handled = true;
                    break;

                case Key.D:
                    if (!e.IsRepeat) BeginSeek(Key.D, +1);
                    e.Handled = true;
                    break;
            }
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (_seekKey != null && e.Key == _seekKey)
                StopSeek();
        }

        private static void Run(ICommand command)
        {
            if (command.CanExecute(null))
                command.Execute(null);
        }

        private static bool IsTypingInTextField()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            return focused is TextBoxBase
                || focused is PasswordBox
                || focused is ComboBox { IsEditable: true };
        }

        // ===================== Перемотка =====================

        private void BeginSeek(Key key, int direction)
        {
            _seekKey = key;
            _seekDirection = direction;

            // Короткое нажатие — сразу шаг; если клавишу не отпустят, таймер подхватит непрерывную перемотку.
            _vm.SeekBy(TimeSpan.FromSeconds(direction * TapSeekSeconds));

            _held.Restart();
            _lastTickSeconds = 0;
            _seekTimer.Start();
        }

        private void StopSeek()
        {
            _seekTimer.Stop();
            _held.Stop();
            _seekKey = null;
        }

        private void OnSeekTick(object? sender, EventArgs e)
        {
            double now = _held.Elapsed.TotalSeconds;
            double dt = now - _lastTickSeconds;
            _lastTickSeconds = now;

            if (now < HoldDelaySeconds || dt <= 0) return;

            // Плавный разгон (smoothstep): от HoldStartRate к HoldMaxRate без «ступеньки».
            double t = Math.Min(1.0, (now - HoldDelaySeconds) / RampSeconds);
            double eased = t * t * (3.0 - 2.0 * t);
            double rate = HoldStartRate + (HoldMaxRate - HoldStartRate) * eased;

            double lengthFactor = Math.Clamp(_vm.Duration.TotalSeconds / ReferenceTrackSeconds, 1.0, MaxLengthFactor);

            _vm.SeekBy(TimeSpan.FromSeconds(_seekDirection * rate * lengthFactor * dt));
        }
    }
}
