using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace MyPlayer_NET9.Models
{
    /// <summary>
    /// Альбом — группа треков с одинаковыми тегами Album + Artist.
    /// Реализует INotifyPropertyChanged только ради IsPlaying: это единственное,
    /// что ViewModel меняет у уже показанной карточки (чтобы иконка Play/Pause
    /// в углу обложки обновлялась вживую, без пересоздания карточки).
    /// </summary>
    public class Album : INotifyPropertyChanged
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public BitmapImage? Cover { get; set; }
        public List<Track> Tracks { get; set; } = new List<Track>();

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying == value) return;
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }

        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768"; // Pause / Play (Segoe MDL2)

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Особая карточка в самом низу вкладки «Альбомы»: сюда собраны все песни, которым не нашлось альбома
    /// с двумя и более треками. Исполнителя у неё нет, а обложка нарисована стопкой — из-под передней
    /// выглядывают края обложек других треков (StackFront/StackMiddle/StackBack, от переднего плана к заднему).
    /// Для остального приложения это обычный Album: открывается тем же экраном, играет той же кнопкой.
    /// </summary>
    public sealed class OtherSongsAlbum : Album
    {
        public const string DisplayTitle = "Остальные песни";

        public BitmapImage? StackFront { get; set; }
        public BitmapImage? StackMiddle { get; set; }
        public BitmapImage? StackBack { get; set; }
    }
}
