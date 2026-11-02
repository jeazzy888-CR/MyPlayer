using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace MyPlayer_NET9.Services.Persistence
{
    // Простые классы-«конверты» для JSON. Никакой логики и никаких ссылок на ViewModel:
    // ViewModel заполняет их (снимок состояния) и читает обратно (восстановление).
    // Поля с [JsonIgnore] — живые картинки, которые StateStore перед записью превращает в имя файла обложки.

    /// <summary>library.json — библиотека: все известные треки с уже прочитанными тегами (чтобы при запуске не перечитывать файлы).</summary>
    public sealed class LibraryFile
    {
        public int Version { get; set; } = 1;
        public List<TrackRecord> Tracks { get; set; } = new List<TrackRecord>();
    }

    public sealed class TrackRecord
    {
        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public uint TrackNumber { get; set; }
        public uint DiscNumber { get; set; }
        public DateTime DateAdded { get; set; }

        /// <summary>Имя файла обложки в папке covers (null — у трека нет обложки).</summary>
        public string? Cover { get; set; }

        [JsonIgnore]
        public BitmapImage? CoverImage { get; set; }
    }

    /// <summary>state.json — всё остальное: порядок списка, Хаос, плейлисты, громкость, что играло, какой экран открыт, окно.</summary>
    public sealed class StateFile
    {
        public int Version { get; set; } = 1;

        public double Volume { get; set; } = 0.7;

        public string SearchText { get; set; } = string.Empty;

        /// <summary>
        /// Порядок главного списка ТАК, КАК ОН СЕЙЧАС НА ЭКРАНЕ: сюда попадают и сортировка, и применённый Хаос.
        /// Хранятся id треков (см. Track.Id).
        /// </summary>
        public List<string> ViewOrder { get; set; } = new List<string>();

        public ChaosRecord Chaos { get; set; } = new ChaosRecord();
        public ScreenRecord Screen { get; set; } = new ScreenRecord();
        public PlaybackRecord Playback { get; set; } = new PlaybackRecord();
        public List<PlaylistRecord> Playlists { get; set; } = new List<PlaylistRecord>();
        public WindowRecord? Window { get; set; }
    }

    public sealed class ChaosRecord
    {
        public string Mode { get; set; } = "Random";
        public int Count { get; set; } = 5;
        public string Artist { get; set; } = string.Empty;
    }

    public sealed class ScreenRecord
    {
        /// <summary>Имя значения ScreenMode.</summary>
        public string Current { get; set; } = "Songs";

        /// <summary>Экран, на который вернёмся из полноэкранного плеера (имеет смысл, если Current = NowPlaying).</summary>
        public string Before { get; set; } = "Songs";

        public string? AlbumTitle { get; set; }
        public string? AlbumArtist { get; set; }
        public string? PlaylistId { get; set; }
    }

    public sealed class PlaybackRecord
    {
        public string? TrackId { get; set; }
        public long PositionMs { get; set; }

        /// <summary>Откуда играет очередь: "main" (главный список), "album" или "playlist".</summary>
        public string Queue { get; set; } = "main";

        public string? QueueAlbumTitle { get; set; }
        public string? QueueAlbumArtist { get; set; }
        public string? QueuePlaylistId { get; set; }
    }

    public sealed class PlaylistRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Cover { get; set; }
        public List<string> Tracks { get; set; } = new List<string>();

        [JsonIgnore]
        public BitmapImage? CoverImage { get; set; }
    }

    public sealed class WindowRecord
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
}
