using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MyPlayer_NET9.Behaviors;
using MyPlayer_NET9.Services.Persistence;
using MyPlayer_NET9.ViewModels;

namespace MyPlayer_NET9.Views
{
    /// <summary>
    /// Всё, что окно делает для сохранения состояния и горячих клавиш, — в одном месте и одной строкой подключается
    /// из конструктора (InitializeServices). Сама логика — в MainViewModel.Persistence и Behaviors/PlayerHotkeys.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Не меньше стольких пикселей окна должно оставаться на каком-нибудь мониторе, иначе положение считаем негодным (монитор отключили).</summary>
        private const double MinVisibleSize = 100;

        private void InitializeServices(MainViewModel vm)
        {
            PlayerHotkeys.Attach(this, vm);

            // Подключение к системным медиа-элементам Windows («Сейчас играет», мультимедийные клавиши) — нужен дескриптор окна.
            SourceInitialized += (_, _) => vm.AttachMediaControls(new WindowInteropHelper(this).Handle);

            ApplySavedPlacement(vm.SavedWindow);

            // Файлы, открытые из проводника (при запуске и когда плеер уже работает).
            Services.SingleInstance.Attach(paths => Dispatcher.InvokeAsync(async () =>
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                if (paths.Length > 0) await vm.OpenExternalAsync(paths);
            }));

            LocationChanged += (_, _) => CapturePlacement(vm);
            SizeChanged += (_, _) => CapturePlacement(vm);
            StateChanged += (_, _) => CapturePlacement(vm);

            // Окно уже на экране — библиотека, плейлисты и позиция подтягиваются следом, интерфейс не замирает.
            Loaded += async (_, _) => await vm.InitializeStateAsync();

            Closing += (_, _) =>
            {
                CapturePlacement(vm);
                vm.SaveStateNow();
            };
        }

        private void ApplySavedPlacement(WindowRecord? saved)
        {
            if (saved == null || saved.Width < MinWidth || saved.Height < MinHeight) return;
            if (!IsReachable(saved)) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
            Width = saved.Width;
            Height = saved.Height;

            // Разворачиваем уже после показа: у окна без рамки «во весь экран» считается по рабочей области (не закрывает панель задач),
            // а это настраивается, когда окно уже создано.
            if (saved.Maximized)
            {
                Loaded += (_, _) => Dispatcher.BeginInvoke(
                    new Action(() => WindowState = WindowState.Maximized),
                    DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>Окно с таким положением можно увидеть и ухватить за заголовок (монитор мог пропасть или смениться разрешение).</summary>
        private static bool IsReachable(WindowRecord w)
        {
            double screenLeft = SystemParameters.VirtualScreenLeft;
            double screenTop = SystemParameters.VirtualScreenTop;
            double screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
            double screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

            double overlapWidth = Math.Min(w.Left + w.Width, screenRight) - Math.Max(w.Left, screenLeft);
            double overlapHeight = Math.Min(w.Top + w.Height, screenBottom) - Math.Max(w.Top, screenTop);

            bool titleBarVisible = w.Top >= screenTop - 10 && w.Top <= screenBottom - 50;

            return overlapWidth >= MinVisibleSize && overlapHeight >= MinVisibleSize && titleBarVisible;
        }

        private void CapturePlacement(MainViewModel vm)
        {
            // Свёрнутое окно имеет координаты -32000 — прошлое нормальное положение лучше.
            if (WindowState == WindowState.Minimized || !IsLoaded) return;

            bool maximized = WindowState == WindowState.Maximized;
            Rect bounds = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

            vm.WindowPlacement = new WindowRecord
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = maximized
            };
        }
    }
}
