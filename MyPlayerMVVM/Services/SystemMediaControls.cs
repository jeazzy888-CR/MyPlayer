using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Win = global::Windows;

namespace MyPlayer_NET9.Services
{
    /// <summary>
    /// Идентификатор приложения для Windows. Без него система видит просто «какой-то процесс»;
    /// с ним — отдельное приложение (с собственным именем в панели «Сейчас играет», группировкой окон на панели задач и т.д.).
    /// Вызывается один раз при запуске, до создания окна.
    /// </summary>
    public static class AppIdentity
    {
        public const string AppUserModelId = "MyPlayer.Desktop";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        public static void Apply()
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
            catch (Exception ex) { Debug.WriteLine($"AppIdentity: {ex.Message}"); }
        }
    }

    /// <summary>
    /// «Официальное» подключение плеера к Windows через System Media Transport Controls — тот же механизм, что у Spotify,
    /// Telegram и браузеров. Благодаря ему:
    ///  • в панели громкости (значок звука), на экране блокировки и в оверлее мультимедийных клавиш виден «Сейчас играет»:
    ///    название, исполнитель, альбом и обложка, кнопки ⏮ ⏯ ⏭ и полоса прогресса, по которой можно перематывать;
    ///  • работают мультимедийные клавиши клавиатуры (Play/Pause, Next, Previous), кнопки на гарнитурах и наушниках;
    ///  • Windows считает приложение медиаплеером.
    /// Ничего не знает про ViewModel: наружу — события-запросы, внутрь — данные о треке и состоянии.
    /// Все события приходят в потоке интерфейса.
    /// </summary>
    public sealed class SystemMediaControls
    {
        private readonly Win.Media.SystemMediaTransportControls _controls;
        private readonly Dispatcher _dispatcher;

        // Что последний раз сообщили системе по времени — чтобы не слать обновление на каждый тик позиции.
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan _reportedPosition;
        private TimeSpan _reportedDuration;
        private TimeSpan _reportedAt;
        private bool _reportedPlaying;

        /// <summary>Расхождение между ожидаемой и реальной позицией, начиная с которого сообщаем системе заново (перемотка).</summary>
        private static readonly TimeSpan DriftThreshold = TimeSpan.FromSeconds(1.2);

        public event Action? PlayRequested;
        public event Action? PauseRequested;
        public event Action? NextRequested;
        public event Action? PreviousRequested;
        public event Action<TimeSpan>? SeekRequested;

        private SystemMediaControls(Win.Media.SystemMediaTransportControls controls)
        {
            _controls = controls;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _controls.IsEnabled = true;
            _controls.IsPlayEnabled = true;
            _controls.IsPauseEnabled = true;
            _controls.IsNextEnabled = true;
            _controls.IsPreviousEnabled = true;
            _controls.IsStopEnabled = false;

            _controls.ButtonPressed += OnButtonPressed;
            _controls.PlaybackPositionChangeRequested += (_, e) =>
            {
                var position = e.RequestedPlaybackPosition;
                _dispatcher.BeginInvoke(new Action(() => SeekRequested?.Invoke(position)));
            };
        }

        /// <summary>Создаёт подключение к окну. Если система его не поддерживает (старая Windows, сбой) — null, плеер работает как раньше.</summary>
        public static SystemMediaControls? TryCreate(IntPtr windowHandle)
        {
            try
            {
                var controls = Win.Media.SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
                return new SystemMediaControls(controls);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemMediaControls: {ex.Message}");
                return null;
            }
        }

        private void OnButtonPressed(Win.Media.SystemMediaTransportControls sender, Win.Media.SystemMediaTransportControlsButtonPressedEventArgs e)
        {
            var button = e.Button;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                switch (button)
                {
                    case Win.Media.SystemMediaTransportControlsButton.Play: PlayRequested?.Invoke(); break;
                    case Win.Media.SystemMediaTransportControlsButton.Pause: PauseRequested?.Invoke(); break;
                    case Win.Media.SystemMediaTransportControlsButton.Next: NextRequested?.Invoke(); break;
                    case Win.Media.SystemMediaTransportControlsButton.Previous: PreviousRequested?.Invoke(); break;
                }
            }));
        }

        /// <summary>Что сейчас играет: название, исполнитель, альбом и обложка (может быть null).</summary>
        public void SetTrack(string title, string artist, string album, BitmapSource? cover)
        {
            try
            {
                var updater = _controls.DisplayUpdater;
                updater.Type = Win.Media.MediaPlaybackType.Music;
                updater.MusicProperties.Title = title ?? string.Empty;
                updater.MusicProperties.Artist = artist ?? string.Empty;
                updater.MusicProperties.AlbumTitle = album ?? string.Empty;
                updater.Thumbnail = CreateThumbnail(cover);
                updater.Update();

                // Новый трек — прошлые сведения о времени недействительны.
                _reportedDuration = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemMediaControls.SetTrack: {ex.Message}");
            }
        }

        /// <summary>Ничего не играет: панель «Сейчас играет» очищается.</summary>
        public void Clear()
        {
            try
            {
                _controls.PlaybackStatus = Win.Media.MediaPlaybackStatus.Closed;
                _controls.DisplayUpdater.ClearAll();
                _controls.DisplayUpdater.Update();
                _reportedDuration = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemMediaControls.Clear: {ex.Message}");
            }
        }

        public void SetPlaying(bool playing)
        {
            try
            {
                _controls.PlaybackStatus = playing
                    ? Win.Media.MediaPlaybackStatus.Playing
                    : Win.Media.MediaPlaybackStatus.Paused;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemMediaControls.SetPlaying: {ex.Message}");
            }
        }

        /// <summary>
        /// Положение на временной шкале. Вызывать можно на каждый тик позиции: система сама «доигрывает» шкалу,
        /// поэтому реально передаём данные, только если сменился трек/состояние или позиция ушла от ожидаемой (перемотка).
        /// </summary>
        public void SetTimeline(TimeSpan position, TimeSpan duration, bool playing)
        {
            if (duration <= TimeSpan.Zero) return;

            TimeSpan now = _clock.Elapsed;
            TimeSpan expected = _reportedPlaying ? _reportedPosition + (now - _reportedAt) : _reportedPosition;

            bool changed = duration != _reportedDuration
                           || playing != _reportedPlaying
                           || (position - expected).Duration() > DriftThreshold;
            if (!changed) return;

            try
            {
                _controls.UpdateTimelineProperties(new Win.Media.SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position = position > duration ? duration : position,
                    MaxSeekTime = duration,
                    EndTime = duration
                });

                _reportedPosition = position;
                _reportedDuration = duration;
                _reportedAt = now;
                _reportedPlaying = playing;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemMediaControls.SetTimeline: {ex.Message}");
            }
        }

        // Последняя закодированная обложка — один и тот же альбом не кодируем заново на каждом треке.
        private BitmapSource? _lastCover;
        private Win.Storage.Streams.RandomAccessStreamReference? _lastThumbnail;

        private Win.Storage.Streams.RandomAccessStreamReference? CreateThumbnail(BitmapSource? cover)
        {
            if (cover == null) return null;
            if (ReferenceEquals(cover, _lastCover)) return _lastThumbnail;

            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(cover));

                var memory = new MemoryStream();
                encoder.Save(memory);
                memory.Position = 0;

                var reference = Win.Storage.Streams.RandomAccessStreamReference.CreateFromStream(memory.AsRandomAccessStream());
                _lastCover = cover;
                _lastThumbnail = reference;
                return reference;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemMediaControls.CreateThumbnail: {ex.Message}");
                return null;
            }
        }
    }
}
