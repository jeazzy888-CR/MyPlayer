using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace MyPlayer_NET9.Models
{
    /// <summary>
    /// Модель одного трека. Реализует INotifyPropertyChanged ради Number (меняется при пересортировке)
    /// и IsCurrent (отметка "сейчас играет") — это поля, которые ViewModel меняет
    /// уже после того, как трек показан в списке, поэтому UI должен об этом узнать.
    /// </summary>
    public class Track : INotifyPropertyChanged
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string FilePath { get; set; } = string.Empty;

        private string? _id;
        /// <summary>
        /// Стабильный идентификатор трека для сохранения состояния: хэш пути к файлу (без учёта регистра).
        /// Плейлисты и порядок списка хранятся списками таких id, а не путей — файл состояния получается компактным.
        /// </summary>
        public string Id => _id ??= Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(FilePath.ToLowerInvariant())))[..16];
        public DateTime DateAdded { get; set; } = DateTime.Now;

        /// <summary>Обложка из ID3-тега (APIC). Null, если в файле нет встроенной картинки.</summary>
        public BitmapImage? Cover { get; set; }

        private int _number;
        public int Number
        {
            get => _number;
            set
            {
                if (_number == value) return;
                _number = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Номер трека в альбоме из тега (0 — в файле номера нет).</summary>
        public uint TrackNumber { get; set; }

        /// <summary>Номер диска из тега (0 — не указан).</summary>
        public uint DiscNumber { get; set; }

        public bool HasTrackNumber => TrackNumber > 0;

        /// <summary>"1." для показа в треклисте альбома; пустая строка, если номера нет.</summary>
        public string TrackNumberText => TrackNumber > 0 ? $"{TrackNumber}." : string.Empty;

        private bool _isCurrent;
        /// <summary>Этот трек сейчас загружен в плеер (играет или на паузе). Выставляет ViewModel.</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent == value) return;
                _isCurrent = value;
                OnPropertyChanged();
            }
        }

        public string DurationText => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
