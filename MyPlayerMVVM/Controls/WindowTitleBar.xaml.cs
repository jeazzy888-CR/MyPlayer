using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MyPlayer_NET9.Controls
{
    /// <summary>
    /// Кастомная шапка безрамочного окна: перетаскивание, двойной клик,
    /// свернуть / развернуть-восстановить / закрыть.
    /// Модуль независим: сам находит родительское окно и ничего не знает про
    /// ViewModel. Чтобы подключить — положить контрол последним ребёнком
    /// корневого Grid (поверх всего остального).
    /// </summary>
    public partial class WindowTitleBar : UserControl
    {
        private Window? _window;
        private HwndSource? _hwndSource;

        /// <summary>Нажали на полосу в развёрнутом окне — ждём первого движения, чтобы "оторвать" окно.</summary>
        private bool _restoreDragPending;

        public WindowTitleBar()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // ===================== Подключение к окну =====================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Detach();

            _window = Window.GetWindow(this);
            if (_window == null) return;

            _window.StateChanged += Window_StateChanged;

            // Хук нужен, чтобы развёрнутое безрамочное окно не залезало под панель задач.
            _hwndSource = PresentationSource.FromVisual(_window) as HwndSource;
            _hwndSource?.AddHook(WndProc);

            MaximizeButton.IsEnabled = _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
            UpdateMaximizeButton();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void Detach()
        {
            if (_window != null)
                _window.StateChanged -= Window_StateChanged;
            _hwndSource?.RemoveHook(WndProc);
            _window = null;
            _hwndSource = null;
        }

        private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();

        private void UpdateMaximizeButton()
        {
            if (_window == null) return;

            bool maximized = _window.WindowState == WindowState.Maximized;
            MaximizeButton.Content = maximized ? "" : ""; // Restore / Maximize (Segoe MDL2)
            MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
        }

        // ===================== Кнопки =====================

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_window != null) _window.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => _window?.Close();

        private void ToggleMaximize()
        {
            if (_window == null || !MaximizeButton.IsEnabled) return;

            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // ===================== Перетаскивание =====================

        private void DragZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_window == null || e.ButtonState != MouseButtonState.Pressed) return;

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (_window.WindowState == WindowState.Maximized)
            {
                // Как в Windows: из развёрнутого состояния окно сначала восстанавливается
                // под курсором, и только потом начинается перетаскивание.
                _restoreDragPending = true;
                DragZone.CaptureMouse();
            }
            else
            {
                _window.DragMove();
            }
        }

        private void DragZone_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_restoreDragPending || _window == null) return;

            _restoreDragPending = false;
            DragZone.ReleaseMouseCapture();

            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point inWindow = e.GetPosition(_window);
            double ratio = _window.ActualWidth > 0 ? Math.Clamp(inWindow.X / _window.ActualWidth, 0, 1) : 0.5;

            Point screenPx = _window.PointToScreen(inWindow);
            DpiScale dpi = VisualTreeHelper.GetDpi(_window);
            double screenX = screenPx.X / dpi.DpiScaleX;
            double screenY = screenPx.Y / dpi.DpiScaleY;

            _window.WindowState = WindowState.Normal;
            _window.Left = screenX - _window.Width * ratio;
            _window.Top = screenY - inWindow.Y;

            try { _window.DragMove(); }
            catch (InvalidOperationException) { /* кнопку успели отпустить — не страшно */ }
        }

        private void DragZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _restoreDragPending = false;
            DragZone.ReleaseMouseCapture();
        }

        // ===================== Развёрнутое окно не должно закрывать панель задач =====================

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 2;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                ApplyWorkAreaToMaximizedWindow(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ApplyWorkAreaToMaximizedWindow(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    RECT work = info.rcWork;
                    RECT mon = info.rcMonitor;
                    mmi.ptMaxPosition.X = work.Left - mon.Left;
                    mmi.ptMaxPosition.Y = work.Top - mon.Top;
                    mmi.ptMaxSize.X = work.Right - work.Left;
                    mmi.ptMaxSize.Y = work.Bottom - work.Top;
                }
            }

            // Раз мы перехватили сообщение целиком, минимальный размер окна возвращаем сами.
            if (_window != null)
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(_window);
                if (_window.MinWidth > 0) mmi.ptMinTrackSize.X = (int)(_window.MinWidth * dpi.DpiScaleX);
                if (_window.MinHeight > 0) mmi.ptMinTrackSize.Y = (int)(_window.MinHeight * dpi.DpiScaleY);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
