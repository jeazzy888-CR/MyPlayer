using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MyPlayer_NET9.Behaviors
{
    /// <summary>
    /// Вложенное свойство: плавная прокрутка колесом мыши / тачпадом для любого ScrollViewer
    /// (или элемента со ScrollViewer внутри — например ListBox).
    ///
    /// Как это устроено (так же делают браузеры):
    ///  1. Колесо НЕ запускает анимацию. Оно лишь сдвигает «цель» прокрутки (накапливая щелчки).
    ///  2. Раз в кадр (CompositionTarget.Rendering) позиция догоняет цель через каскад из трёх
    ///     одинаковых фильтров 1-го порядка. Считается по точной формуле, поэтому не зависит от
    ///     частоты кадров и её дрожания, а у позиции непрерывны и скорость, и УСКОРЕНИЕ: новые
    ///     щелчки посреди движения, ускорение и смена направления не дают рывков, цель не перелетается.
    ///  3. Пока ничего не движется, подписки на кадры нет вообще — в покое поведение бесплатно.
    ///  4. Во время движения позиция субпиксельная (дробная часть — TranslateTransform на содержимом),
    ///     в покое — строго целый физический пиксель: движение без ступенек, а текст в покое чёткий.
    /// </summary>
    public static class SmoothScrollBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        /// <summary>
        /// Сколько пикселей занимает один «щелчок» колеса (при стандартной настройке Windows — 3 строки).
        /// Если пользователь поменял число строк в настройках мыши, шаг масштабируется пропорционально;
        /// режим «на страницу» тоже учитывается.
        /// </summary>
        public static readonly DependencyProperty PixelsPerNotchProperty =
            DependencyProperty.RegisterAttached(
                "PixelsPerNotch",
                typeof(double),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(100.0));

        public static void SetPixelsPerNotch(DependencyObject element, double value) =>
            element.SetValue(PixelsPerNotchProperty, value);

        public static double GetPixelsPerNotch(DependencyObject element) =>
            (double)element.GetValue(PixelsPerNotchProperty);

        /// <summary>
        /// true, пока ScrollViewer едет под действием колеса. Наследуется вниз по дереву, поэтому элементы
        /// списка могут отключать hover-подсветку на время прокрутки: строки едут под неподвижным курсором,
        /// и без этого подсветка перепрыгивает со строки на строку каждый кадр.
        /// </summary>
        private static readonly DependencyPropertyKey IsScrollingPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "IsScrolling",
                typeof(bool),
                typeof(SmoothScrollBehavior),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static readonly DependencyProperty IsScrollingProperty = IsScrollingPropertyKey.DependencyProperty;

        public static bool GetIsScrolling(DependencyObject element) =>
            (bool)element.GetValue(IsScrollingProperty);

        /// <summary>
        /// true, пока подсветку строк под курсором нужно прятать: список прокрутили (колесом, ползунком, клавишами),
        /// а курсор с тех пор по-настоящему не двигали. Строки едут под неподвижным курсором — без этого подсветка
        /// прыгает со строки на строку. Как только курсор сдвинулся (больше чем на HoverMoveThreshold пикселей),
        /// подсветка возвращается. Наследуется вниз по дереву — на ней строят триггеры стили элементов списка.
        /// </summary>
        private static readonly DependencyPropertyKey IsHoverSuppressedPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "IsHoverSuppressed",
                typeof(bool),
                typeof(SmoothScrollBehavior),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static readonly DependencyProperty IsHoverSuppressedProperty = IsHoverSuppressedPropertyKey.DependencyProperty;

        public static bool GetIsHoverSuppressed(DependencyObject element) =>
            (bool)element.GetValue(IsHoverSuppressedProperty);

        /// <summary>Настоящее движение мыши — не меньше стольких независимых от разрешения пикселей (дрожание руки и синтетические события WPF не считаются).</summary>
        private const double HoverMoveThreshold = 3.0;

        /// <summary>Где был курсор, когда список последний раз прокрутился (на каждый элемент со списком — своя точка).</summary>
        private static readonly ConditionalWeakTable<UIElement, HoverAnchor> HoverAnchors = new();

        private sealed class HoverAnchor
        {
            public Point Position;
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not UIElement element) return;
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;

            // Запоминаем, где стоит курсор в момент прокрутки; прячем подсветку.
            HoverAnchors.GetValue(element, _ => new HoverAnchor()).Position = Mouse.GetPosition(element);
            element.SetValue(IsHoverSuppressedPropertyKey, true);
        }

        private static void OnHoverMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not UIElement element) return;
            if (!GetIsHoverSuppressed(element)) return;

            Point now = e.GetPosition(element);
            Point anchor = HoverAnchors.GetValue(element, _ => new HoverAnchor()).Position;

            // WPF сам шлёт «движение мыши», когда содержимое поехало под курсором, — позиция при этом не меняется.
            if (Math.Abs(now.X - anchor.X) >= HoverMoveThreshold || Math.Abs(now.Y - anchor.Y) >= HoverMoveThreshold)
                element.SetValue(IsHoverSuppressedPropertyKey, false);
        }

        // ===================== Подключение =====================

        /// <summary>У каждого ScrollViewer свой «движок»; таблица не мешает сборщику мусора.</summary>
        private static readonly ConditionalWeakTable<ScrollViewer, SmoothScroller> Scrollers = new();

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            if ((bool)e.NewValue)
            {
                element.PreviewMouseWheel += OnPreviewMouseWheel;
                element.PreviewMouseMove += OnHoverMouseMove;
                element.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
            }
            else
            {
                element.PreviewMouseWheel -= OnPreviewMouseWheel;
                element.PreviewMouseMove -= OnHoverMouseMove;
                element.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;

            // Ctrl (масштаб) и Shift (горизонталь) — не наши, оставляем стандартному поведению.
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;

            var scrollViewer = sender as ScrollViewer ?? FindScrollViewer(sender as DependencyObject);

            // Нечего прокручивать — не перехватываем, пусть событие уйдёт выше.
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0) return;

            double notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine; // один щелчок колеса = 120 = 1.0
            double pixels = -notches * PixelsPerNotch(sender as DependencyObject, scrollViewer);

            GetScroller(scrollViewer).AddImpulse(pixels);
            e.Handled = true;
        }

        /// <summary>
        /// Общий множитель скорости прокрутки для всех списков: 1.0 — как задано в PixelsPerNotch,
        /// меньше — медленнее (0.8 = на 20% короче путь за щелчок), больше — быстрее.
        /// </summary>
        private const double SpeedFactor = 0.8;

        private static double PixelsPerNotch(DependencyObject? owner, ScrollViewer scrollViewer)
        {
            double basePixels = (owner != null ? GetPixelsPerNotch(owner) : 100.0) * SpeedFactor;
            int lines = SystemParameters.WheelScrollLines;

            if (lines < 0) // в Windows выбрано «прокручивать на страницу»
                return Math.Max(basePixels, scrollViewer.ViewportHeight * 0.9 * SpeedFactor);

            return lines == 0 ? basePixels : basePixels * (lines / 3.0);
        }

        private static SmoothScroller GetScroller(ScrollViewer scrollViewer) =>
            Scrollers.GetValue(scrollViewer, sv => new SmoothScroller(sv));

        private static ScrollViewer? FindScrollViewer(DependencyObject? root)
        {
            if (root == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                if (FindScrollViewer(child) is ScrollViewer nested) return nested;
            }
            return null;
        }

        // ===================== Движок =====================

        private sealed class SmoothScroller
        {
            /// <summary>
            /// Единственная «ручка» ощущения, 1/с. Позиция — это цель, пропущенная через ТРИ одинаковых
            /// фильтра первого порядка (каскад). Это даёт:
            ///  • непрерывную скорость И ускорение (старт без «щелчка», у пружины 2-го порядка ускорение
            ///    в момент щелчка колеса скачет): рывок на старте в ~12 раз меньше;
            ///  • отсутствие перелёта цели при любых щелчках и смене направления (отклик каскада монотонен);
            ///  • скорость/длительность те же, что были у пружины ω=22: 90% пути за ≈0.18 с, пик скорости тот же.
            /// Больше — отзывчивее и резче (≈ 36), меньше — мягче (≈ 24).
            /// </summary>
            private const double Rate = 30.0;

            /// <summary>Цель не может убегать от текущей позиции дальше, чем на столько «экранов» — иначе после бешеного кручения долго «доезжать».</summary>
            private const double MaxLeadViewports = 4.0;

            /// <summary>Если кадр вдруг растянулся — не позволяем сделать из него гигантский шаг.</summary>
            private const double MaxFrameSeconds = 0.1;

            /// <summary>Номинальная длительность первого кадра движения (когда предыдущего момента времени ещё нет).</summary>
            private const double NominalFrameSeconds = 1.0 / 60.0;

            /// <summary>
            /// Конец движения: до цели осталось меньше этой доли ФИЗИЧЕСКОГО пикселя. Позиция передаётся
            /// ScrollViewer'у с субпиксельной точностью, поэтому экспоненциальный хвост едет плавно до самого конца,
            /// а финальная «доводка» (≤ 0.04 px) глазом не видна.
            /// </summary>
            private const double RestPhysicalPixels = 0.04;

            /// <summary>Hover-подсветка возвращается, когда до цели осталось меньше стольких физических пикселей (движение уже еле заметно).</summary>
            private const double HoverReturnPhysicalPixels = 2.0;

            /// <summary>Предохранитель: трансформация компенсации никогда не уезжает дальше этого числа пикселей (в норме она меньше одного).</summary>
            private const double MaxSubPixelShift = 200.0;

            /// <summary>Расхождение с реальной позицией ScrollViewer больше этого значения = прокрутили не мы (полоса, клавиши, ScrollIntoView).</summary>
            private const double ExternalMoveTolerance = 1.5;

            private readonly ScrollViewer _scrollViewer;

            // Каскад: y1 → y2 → y3. y3 — реальная (дробная) позиция.
            private double _y1, _y2, _y3;
            private double _target;       // куда едем

            private double _lastApplied;  // какое ЦЕЛОЕ смещение мы в последний раз передали ScrollViewer'у

            private bool _running;
            private bool _hoverReleased;
            private bool _hasLastTime;
            private TimeSpan _lastTime;

            /// <summary>Содержимое ScrollViewer'а; двигаем его на долю пикселя, чтобы прокрутка была субпиксельной.</summary>
            private FrameworkElement? _presenter;
            private TranslateTransform? _subPixel;

            public SmoothScroller(ScrollViewer scrollViewer)
            {
                _scrollViewer = scrollViewer;
                _scrollViewer.Unloaded += (_, _) => Stop();
            }

            /// <summary>Колесо: сдвигаем цель на deltaPixels (отрицательное — вверх). Движение подхватится на ближайшем кадре.</summary>
            public void AddImpulse(double deltaPixels)
            {
                // Внешнюю прокрутку во время движения ловит OnRendering (там раскладка гарантированно актуальна);
                // здесь, между кадрами, VerticalOffset ещё может отставать от того, что мы выставили, и сравнивать нельзя.
                if (!_running)
                {
                    // Начинаем «с нуля» от того места, где ScrollViewer реально стоит сейчас.
                    double start = _scrollViewer.VerticalOffset;
                    _y1 = _y2 = _y3 = _target = start;
                    _lastApplied = start;
                }

                double max = _scrollViewer.ScrollableHeight;
                double lead = Math.Max(_scrollViewer.ViewportHeight, 100.0) * MaxLeadViewports;

                double low = Math.Max(0, _y3 - lead);
                double high = Math.Min(max, _y3 + lead);
                if (high < low) high = low;

                // Цель — на сетке физических пикселей: так остановка приходится ровно в целый пиксель (текст чёткий).
                _target = Clamp(SnapToPixel(_target + deltaPixels), low, high);

                Start();
            }

            private bool MovedExternally() =>
                Math.Abs(_scrollViewer.VerticalOffset - Math.Min(_lastApplied, _scrollViewer.ScrollableHeight)) > ExternalMoveTolerance;

            private void Start()
            {
                if (_running) return;

                _running = true;
                _hoverReleased = false;
                _hasLastTime = false;
                EnsureSubPixelHost();
                _scrollViewer.SetValue(IsScrollingPropertyKey, true);
                CompositionTarget.Rendering += OnRendering;
            }

            private void Stop()
            {
                if (!_running) return;

                _running = false;
                CompositionTarget.Rendering -= OnRendering;
                if (_subPixel != null) _subPixel.Y = 0;
                _scrollViewer.SetValue(IsScrollingPropertyKey, false);
            }

            /// <summary>
            /// Находит содержимое ScrollViewer'а (ScrollContentPresenter) и вешает на него TranslateTransform.
            /// Если шаблон нестандартный или там уже есть чужая трансформация — остаёмся на целых пикселях (запасной вариант).
            /// </summary>
            private void EnsureSubPixelHost()
            {
                if (_subPixel != null) return;

                _scrollViewer.ApplyTemplate();
                var presenter = _scrollViewer.Template?.FindName("PART_ScrollContentPresenter", _scrollViewer) as FrameworkElement;
                if (presenter == null) return;
                if (presenter.RenderTransform != null && !presenter.RenderTransform.Value.IsIdentity)
                    return; // уже есть чужая трансформация — не трогаем

                _presenter = presenter;
                _subPixel = new TranslateTransform();
                presenter.RenderTransform = _subPixel;
            }

            private void OnRendering(object? sender, EventArgs e)
            {
                if (!_scrollViewer.IsLoaded)
                {
                    Stop();
                    return;
                }

                // RenderingTime — момент кадра по часам композитора. Событие может прийти несколько раз
                // за один кадр с одним и тем же временем — такие повторы пропускаем.
                TimeSpan now = e is RenderingEventArgs args
                    ? args.RenderingTime
                    : TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

                if (_hasLastTime && now == _lastTime) return;

                double dt = _hasLastTime ? (now - _lastTime).TotalSeconds : NominalFrameSeconds;
                _lastTime = now;
                _hasLastTime = true;

                if (dt <= 0) return;
                dt = Math.Min(dt, MaxFrameSeconds);

                // Пока мы ехали, прокрутили вручную (потянули ползунок, нажали PageDown, сработал ScrollIntoView,
                // список пересобрали) — уступаем дорогу, не боремся за позицию.
                if (MovedExternally())
                {
                    Stop();
                    return;
                }

                double max = _scrollViewer.ScrollableHeight;
                _target = Clamp(_target, 0, max);

                // Точное решение каскада из трёх одинаковых фильтров 1-го порядка при постоянной цели T на шаге dt
                // (цель меняется только между кадрами, поэтому решение точное, а не приближённое):
                //   u_i = y_i − T;  e = exp(−R·dt);  x = R·dt
                //   u1' = e·u1
                //   u2' = e·(u2 + x·u1)
                //   u3' = e·(u3 + x·u2 + x²/2·u1)
                // Ни частота кадров, ни разброс длительности кадров на результат не влияют.
                double e0 = Math.Exp(-Rate * dt);
                double x = Rate * dt;
                double u1 = _y1 - _target, u2 = _y2 - _target, u3 = _y3 - _target;

                _y1 = _target + e0 * u1;
                _y2 = _target + e0 * (u2 + x * u1);
                _y3 = _target + e0 * (u3 + x * u2 + 0.5 * x * x * u1);

                // Страховка на случай, если границы сжались (изменился размер содержимого).
                _y1 = Clamp(_y1, 0, max); _y2 = Clamp(_y2, 0, max); _y3 = Clamp(_y3, 0, max);

                double scale = VisualTreeHelper.GetDpi(_scrollViewer).DpiScaleY;
                double remainingPx = Math.Abs(_y3 - _target) * scale;

                if (!_hoverReleased && remainingPx < HoverReturnPhysicalPixels)
                {
                    _hoverReleased = true;
                    _scrollViewer.SetValue(IsScrollingPropertyKey, false);
                }

                if (remainingPx < RestPhysicalPixels)
                {
                    _y1 = _y2 = _y3 = _target;
                    ApplyRest(_target, max);
                    Stop();
                    return;
                }

                ApplyMoving(_y3, scale);
            }

            /// <summary>
            /// Движение: целая часть позиции — настоящее смещение ScrollViewer'а, дробная (меньше физ. пикселя) —
            /// TranslateTransform на содержимом. Позиция меняется непрерывно, без ступенек в целый пиксель.
            ///
            /// ВАЖНО: смещение ScrollViewer'а применяется только при раскладке (на следующем проходе), а трансформация — сразу.
            /// Если не раскладывать принудительно, то в момент перехода через пиксель содержимое на один кадр оказывается
            /// не там (ошибка в 1 px и более) — на медленной прокрутке это видно как дрожание. Поэтому после смены смещения
            /// раскладываем сразу и трансформацию считаем от РЕАЛЬНО применённого смещения: что бы ни случилось с раскладкой,
            /// содержимое рисуется ровно в позиции <paramref name="position"/>.
            /// </summary>
            private void ApplyMoving(double position, double scale)
            {
                if (_subPixel == null)
                {
                    SetOffset(Math.Round(position * scale) / scale); // запасной вариант без субпикселя
                    return;
                }

                double whole = Math.Floor(position * scale + 1e-6) / scale;
                SetOffset(whole);
                _scrollViewer.UpdateLayout();

                double actual = _scrollViewer.VerticalOffset;
                _subPixel.Y = Clamp(actual - position, -MaxSubPixelShift, MaxSubPixelShift);
            }

            /// <summary>Остановка: ровно целый пиксель (или самый край), трансформации нет — текст чёткий.</summary>
            private void ApplyRest(double position, double max)
            {
                double scale = VisualTreeHelper.GetDpi(_scrollViewer).DpiScaleY;
                double offset = position >= max - 1e-6 ? max : Math.Round(position * scale) / scale;
                SetOffset(offset);

                // Смещение и сброс трансформации — в один кадр, иначе в самом конце остановки содержимое «подпрыгивает» на пиксель.
                _scrollViewer.UpdateLayout();
                if (_subPixel != null) _subPixel.Y = 0;
            }

            private void SetOffset(double offset)
            {
                if (offset == _lastApplied) return;

                _scrollViewer.ScrollToVerticalOffset(offset);
                _lastApplied = offset;
            }

            private double SnapToPixel(double value)
            {
                double scale = VisualTreeHelper.GetDpi(_scrollViewer).DpiScaleY;
                return Math.Round(value * scale) / scale;
            }

            private static double Clamp(double value, double min, double max) =>
                Math.Max(min, Math.Min(max, value));
        }
    }
}
