using System;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyPlayer_NET9.Services
{
    /// <summary>
    /// Тонкая обёртка над System.Windows.Media.MediaPlayer.
    /// Не знает ничего про ViewModel или UI — только проигрывает файлы
    /// и сообщает о своём состоянии через события.
    /// </summary>
    public class AudioPlayerService : IDisposable
    {
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _positionTimer;
        private bool _isPlaying;

        public event EventHandler? TrackEnded;
        public event EventHandler<TimeSpan>? PositionChanged;

        /// <summary>Файл открыт, NaturalDuration стала известна (передаётся как аргумент, если определена).</summary>
        public event EventHandler<TimeSpan?>? MediaOpened;

        /// <summary>Не удалось открыть/проиграть файл (удалён, повреждён, неподдерживаемый кодек).</summary>
        public event EventHandler<string>? MediaFailed;

        public bool IsPlaying => _isPlaying;

        public TimeSpan Position
        {
            get => _player.Position;
            set => _player.Position = value;
        }

        public TimeSpan? Duration => _player.NaturalDuration.HasTimeSpan
            ? _player.NaturalDuration.TimeSpan
            : null;

        public double Volume
        {
            get => _player.Volume;
            set => _player.Volume = Math.Clamp(value, 0.0, 1.0);
        }

        public AudioPlayerService()
        {
            _player.MediaEnded += (s, e) => TrackEnded?.Invoke(this, EventArgs.Empty);
            _player.MediaOpened += (s, e) => MediaOpened?.Invoke(this, Duration);
            _player.MediaFailed += (s, e) =>
            {
                _isPlaying = false;
                _positionTimer.Stop();
                MediaFailed?.Invoke(this, e.ErrorException?.Message ?? "Не удалось открыть файл");
            };

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, _player.Position);
        }

        public void Load(string filePath)
        {
            _player.Open(new Uri(filePath, UriKind.Absolute));
        }

        public void Play()
        {
            _player.Play();
            _isPlaying = true;
            _positionTimer.Start();
        }

        public void Pause()
        {
            _player.Pause();
            _isPlaying = false;
            _positionTimer.Stop();
        }

        public void Stop()
        {
            _player.Stop();
            _isPlaying = false;
            _positionTimer.Stop();
        }

        public void Dispose()
        {
            _positionTimer.Stop();
            _player.Close();
        }
    }
}
