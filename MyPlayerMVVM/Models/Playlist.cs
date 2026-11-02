using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace MyPlayer_NET9.Models
{
    /// <summary>
    /// Плейлист — произвольный список треков, собранный пользователем.
    /// Свойства Title / Artist / Cover / PlayPauseGlyph названы так же, как у Album, —
    /// поэтому сетка плейлистов использует ту же карточку (AlbumCardStyle), что и сетка альбомов.
    /// </summary>
    public class Playlist : INotifyPropertyChanged
    {
        /// <summary>Постоянный идентификатор плейлиста (переживает перезапуск; по нему восстанавливаются выбранный плейлист и очередь).</summary>
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

        private string _title = string.Empty;
        /// <summary>Пустая строка — название ещё не выбрано (в редакторе тогда видна подсказка).</summary>
        public string Title
        {
            get => _title;
            set
            {
                value ??= string.Empty;
                if (_title == value) return;
                _title = value;
                OnPropertyChanged();
            }
        }

        /// <summary>У плейлиста нет исполнителя — пустая строка прячет вторую подпись на карточке.</summary>
        public string Artist => string.Empty;

        private BitmapImage? _cover;
        public BitmapImage? Cover
        {
            get => _cover;
            set
            {
                if (ReferenceEquals(_cover, value)) return;
                _cover = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Треки плейлиста. Сама коллекция служит очередью воспроизведения.</summary>
        public ObservableCollection<Track> Tracks { get; } = new ObservableCollection<Track>();

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

        public string PlayPauseGlyph => IsPlaying ? "" : ""; // Pause / Play (Segoe MDL2)

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Маркер первой карточки сетки: «+ добавить плейлист».</summary>
    public sealed class AddPlaylistTile
    {
        public static readonly AddPlaylistTile Instance = new AddPlaylistTile();
        private AddPlaylistTile() { }
    }

    /// <summary>Маркер зелёной строки «+ выбери трек» в конце списка треков редактора.</summary>
    public sealed class AddTrackRow
    {
        public static readonly AddTrackRow Instance = new AddTrackRow();
        private AddTrackRow() { }
    }
}
