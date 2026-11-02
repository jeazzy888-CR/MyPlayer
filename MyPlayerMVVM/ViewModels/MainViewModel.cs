using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MyPlayer_NET9.Models;
using MyPlayer_NET9.Services;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Режимы работы кнопки "Хаос".
    /// </summary>
    public enum ChaosMode
    {
        Random,
        BringUpFromBottom,
        BringUpArtist
    }

    /// <summary>
    /// "Экран" основной панели. Никакой отдельной навигации (Frame/Page) не заводим —
    /// четыре вида контента живут в одном Grid и переключаются по этому enum через
    /// EnumToVisibilityConverter, а старый список треков остаётся ровно тем же,
    /// каким был — просто получает Visibility-биндинг. NowPlaying отличается тем,
    /// что перекрывает вообще всё окно (не только контентную область).
    /// </summary>
    public enum ScreenMode
    {
        Songs,
        AlbumsGrid,
        AlbumDetail,
        NowPlaying,
        PlaylistsGrid,
        PlaylistDetail,
        /// <summary>Полноэкранный список «Выберите композицию» (как NowPlaying — перекрывает всё окно).</summary>
        PlaylistPicker
    }

    public partial class MainViewModel : ViewModelBase, IDisposable, IPlaylistHost
    {
        private readonly AudioPlayerService _player = new AudioPlayerService();
        private readonly Random _random = new Random();
        private readonly CoverCache _covers = new CoverCache();

        /// <summary>Играл ли трек до начала перетаскивания ползунка — чтобы знать, возобновлять ли после.</summary>
        private bool _wasPlayingBeforeSeek;

        /// <summary>Сколько файлов подряд не открылось — защита от бесконечного перескакивания по битой очереди.</summary>
        private int _consecutiveFailures;

        /// <summary>
        /// Альбом, из которого сейчас играет очередь. Пока он задан, "следующий/предыдущий трек"
        /// и автопереход ходят по трекам ЭТОГО альбома. null — очередь это основной список Tracks.
        /// </summary>
        private Album? _queueAlbumValue;
        private Album? _queueAlbum
        {
            get => _queueAlbumValue;
            // Любой выбор альбома (или основного списка) в качестве очереди сбрасывает очередь плейлиста.
            set { _queueAlbumValue = value; _queueExternal = null; }
        }

        /// <summary>Очередь, заданная модулем плейлистов (IPlaylistHost.PlayQueue). Если не null — она главнее альбома.</summary>
        private IList<Track>? _queueExternal;

        /// <summary>Полный список треков — источник правды, не зависящий от текущей сортировки/поиска.</summary>
        private List<Track> _allTracks = new List<Track>();

        /// <summary>Пути уже загруженных файлов — чтобы повторное добавление той же папки не плодило дубли.</summary>
        private readonly HashSet<string> _knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Какие файлы считаем музыкой при сканировании папки.</summary>
        private static readonly HashSet<string> AudioExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma" };

        /// <summary>Сколько файлов читаем за один заход — после каждого захода список на экране обновляется.</summary>
        private const int LoadChunkSize = 25;

        private bool _isLoading;
        /// <summary>Идёт фоновая загрузка папки.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetField(ref _isLoading, value);
        }

        private string _loadStatusText = string.Empty;
        /// <summary>Короткий статус загрузки: "Загрузка: 120 из 500".</summary>
        public string LoadStatusText
        {
            get => _loadStatusText;
            private set => SetField(ref _loadStatusText, value);
        }

        public BulkObservableCollection<Track> Tracks { get; } = new BulkObservableCollection<Track>();
        public BulkObservableCollection<string> Artists { get; } = new BulkObservableCollection<string>();
        public BulkObservableCollection<Album> Albums { get; } = new BulkObservableCollection<Album>();

        /// <summary>Сколько карточек альбомов в одном ряду сетки (должно совпадать с UniformGrid Columns в XAML).</summary>
        public const int AlbumColumns = 6;

        /// <summary>
        /// Альбомы, нарезанные на ряды по AlbumColumns штук. Сетка альбомов в XAML — это
        /// виртуализированный список РЯДОВ: на экране создаются только видимые ряды.
        /// </summary>
        public BulkObservableCollection<List<Album?>> AlbumRows { get; } = new BulkObservableCollection<List<Album?>>();

        // ===================== Навигация по экранам =====================

        private ScreenMode _currentScreen = ScreenMode.Songs;
        public ScreenMode CurrentScreen
        {
            get => _currentScreen;
            private set
            {
                if (SetField(ref _currentScreen, value))
                    OnPropertyChanged(nameof(ScreenTitle));
            }
        }

        private Album? _selectedAlbum;
        public Album? SelectedAlbum
        {
            get => _selectedAlbum;
            private set
            {
                if (SetField(ref _selectedAlbum, value))
                    OnPropertyChanged(nameof(ScreenTitle));
            }
        }

        /// <summary>Заголовок над основной панелью — меняется вместе с экраном.</summary>
        public string ScreenTitle => CurrentScreen switch
        {
            ScreenMode.AlbumsGrid => "Альбомы",
            ScreenMode.AlbumDetail => SelectedAlbum?.Title ?? "Альбом",
            ScreenMode.PlaylistsGrid => "Плейлисты",
            _ => "Музыка"
        };

        // ===================== Состояние воспроизведения =====================

        private Track? _currentTrack;
        public Track? CurrentTrack
        {
            get => _currentTrack;
            private set
            {
                var previous = _currentTrack;
                if (SetField(ref _currentTrack, value))
                {
                    if (previous != null) previous.IsCurrent = false;
                    if (value != null) value.IsCurrent = true;
                    UpdateAlbumPlaybackStates();
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (SetField(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseGlyph));
                    UpdateAlbumPlaybackStates();
                }
            }
        }

        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768"; // Pause / Play (Segoe MDL2)

        private double _volume = 0.7;
        public double Volume
        {
            get => _volume;
            set
            {
                if (SetField(ref _volume, value))
                    _player.Volume = value;
            }
        }

        private TimeSpan _position;
        public TimeSpan Position
        {
            get => _position;
            set
            {
                if (SetField(ref _position, value))
                {
                    _player.Position = value;
                    OnPropertyChanged(nameof(PositionSeconds));
                    OnPropertyChanged(nameof(PositionText));
                }
            }
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            private set
            {
                if (SetField(ref _duration, value))
                {
                    OnPropertyChanged(nameof(DurationSeconds));
                    OnPropertyChanged(nameof(DurationText));
                }
            }
        }

        /// <summary>
        /// Обёртки в double-секундах специально для Slider: он умеет биндиться
        /// только к простым числовым свойствам с сеттером, а TimeSpan.TotalSeconds
        /// сеттера не имеет — отсюда и "мёртвый" ползунок в прошлой версии.
        /// </summary>
        public double PositionSeconds
        {
            get => Position.TotalSeconds;
            set => Position = TimeSpan.FromSeconds(value);
        }

        public double DurationSeconds => Duration.TotalSeconds;

        /// <summary>Текстовое "мм:сс" для таймеров полноэкранного плеера — тот же формат, что и у Track.DurationText.</summary>
        public string PositionText => $"{(int)Position.TotalMinutes}:{Position.Seconds:00}";
        public string DurationText => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}";

        /// <summary>
        /// Вызывается, когда пользователь берётся за ползунок прогресса.
        /// Ставим плеер на паузу, чтобы во время перетаскивания не было
        /// "треска" от постоянных скачков позиции на живом звуке.
        /// </summary>
        public void BeginSeek()
        {
            _wasPlayingBeforeSeek = IsPlaying;
            if (IsPlaying)
            {
                _player.Pause();
                IsPlaying = false;
            }
        }

        /// <summary>
        /// Вызывается по отпусканию ползунка. Если трек играл до начала
        /// перемотки — возобновляем воспроизведение с новой позиции.
        /// </summary>
        public void EndSeek()
        {
            if (_wasPlayingBeforeSeek)
            {
                _player.Play();
                IsPlaying = true;
            }
        }

        // ===================== Поиск =====================

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
                    ApplySearchFilter();
            }
        }

        // ===================== Настройки Хаоса =====================

        private ChaosMode _chaosMode = ChaosMode.Random;
        public ChaosMode CurrentChaosMode
        {
            get => _chaosMode;
            set => SetField(ref _chaosMode, value);
        }

        private int _chaosCount = 5;
        public int ChaosCount
        {
            get => _chaosCount;
            set => SetField(ref _chaosCount, value);
        }

        private string _selectedArtist = string.Empty;
        public string SelectedArtist
        {
            get => _selectedArtist;
            set => SetField(ref _selectedArtist, value);
        }

        // ===================== Команды =====================

        public RelayCommand PlayPauseCommand { get; }
        public RelayCommand PlayTrackCommand { get; }
        public RelayCommand PlayAlbumTrackCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
        public RelayCommand ChaosCommand { get; }
        public RelayCommand SortByTitleCommand { get; }
        public RelayCommand SortByDurationCommand { get; }
        public RelayCommand SortByDateAddedCommand { get; }
        public RelayCommand SetChaosModeRandomCommand { get; }
        public RelayCommand SetChaosModeBringUpCommand { get; }
        public RelayCommand SetChaosModeArtistCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand ShowSongsCommand { get; }
        public RelayCommand ShowAlbumsCommand { get; }
        public RelayCommand BackToAlbumsCommand { get; }
        public RelayCommand OpenAlbumCommand { get; }
        public RelayCommand PlayAlbumCommand { get; }
        public RelayCommand ShowNowPlayingCommand { get; }
        public RelayCommand BackFromNowPlayingCommand { get; }

        /// <summary>Модуль «Плейлисты» (сетка, редактор, выбор треков).</summary>
        public PlaylistsViewModel Playlists { get; }

        /// <summary>Экран, на который вернёмся, выйдя из полноэкранного плеера.</summary>
        private ScreenMode _screenBeforeNowPlaying = ScreenMode.Songs;

        public MainViewModel()
        {
            Playlists = new PlaylistsViewModel(this);

            PlayPauseCommand = new RelayCommand(ExecutePlayPause, _ => CurrentTrack != null || Tracks.Count > 0);
            // Клик по треку в основном списке — очередь становится основным списком
            PlayTrackCommand = new RelayCommand(param =>
            {
                if (param is Track t)
                {
                    _queueAlbum = null;
                    PlayTrack(t);
                }
            });
            // Клик по треку в открытом альбоме — очередь становится этим альбомом
            PlayAlbumTrackCommand = new RelayCommand(param =>
            {
                if (param is Track t)
                {
                    _queueAlbum = SelectedAlbum != null && SelectedAlbum.Tracks.Contains(t) ? SelectedAlbum : null;
                    PlayTrack(t);
                }
            });
            NextCommand = new RelayCommand(PlayNext, _ => GetQueue().Count > 0);
            PreviousCommand = new RelayCommand(PlayPrevious, _ => GetQueue().Count > 0);
            ChaosCommand = new RelayCommand(ExecuteChaos, _ => Tracks.Count > 1);

            SortByTitleCommand = new RelayCommand(_ => SortBy(t => t.Title));
            SortByDurationCommand = new RelayCommand(_ => SortBy(t => t.Duration));
            SortByDateAddedCommand = new RelayCommand(_ => SortBy(t => t.DateAdded));

            SetChaosModeRandomCommand = new RelayCommand(_ => CurrentChaosMode = ChaosMode.Random);
            SetChaosModeBringUpCommand = new RelayCommand(_ => CurrentChaosMode = ChaosMode.BringUpFromBottom);
            SetChaosModeArtistCommand = new RelayCommand(_ => CurrentChaosMode = ChaosMode.BringUpArtist);

            OpenFolderCommand = new RelayCommand(OpenFolderAndLoadTracks, _ => !IsLoading);

            ShowSongsCommand = new RelayCommand(_ => CurrentScreen = ScreenMode.Songs);
            ShowAlbumsCommand = new RelayCommand(_ => CurrentScreen = ScreenMode.AlbumsGrid);
            BackToAlbumsCommand = new RelayCommand(_ => CurrentScreen = ScreenMode.AlbumsGrid);
            OpenAlbumCommand = new RelayCommand(param =>
            {
                if (param is Album album)
                {
                    SelectedAlbum = album;
                    CurrentScreen = ScreenMode.AlbumDetail;
                }
            });
            PlayAlbumCommand = new RelayCommand(param =>
            {
                if (param is Album album)
                    ExecutePlayAlbum(album);
            });

            ShowNowPlayingCommand = new RelayCommand(_ =>
            {
                if (CurrentScreen == ScreenMode.NowPlaying) return;
                _screenBeforeNowPlaying = CurrentScreen;
                CurrentScreen = ScreenMode.NowPlaying;
            }, _ => CurrentTrack != null);
            BackFromNowPlayingCommand = new RelayCommand(_ => CurrentScreen = _screenBeforeNowPlaying);

            // Стартовая громкость VM (0.7) должна дойти до плеера — у MediaPlayer по умолчанию 0.5.
            _player.Volume = _volume;

            // Если в теге не было длительности — берём реальную, как только файл открылся.
            _player.MediaOpened += (s, d) =>
            {
                _consecutiveFailures = 0;
                _isRestoringPlayback = false;

                // После запуска приложения трек только «подготовлен» — сохранённая позиция применяется, когда плеер его открыл.
                if (_pendingRestorePosition is TimeSpan restored)
                {
                    _pendingRestorePosition = null;
                    _player.Position = restored;
                }

                if (d is TimeSpan real && (Duration == TimeSpan.Zero || CurrentTrack?.Duration == TimeSpan.Zero))
                    Duration = real;
            };

            // Битый/удалённый файл не должен подвешивать плеер в состоянии «играет» — переходим к следующему.
            _player.MediaFailed += (s, msg) =>
            {
                IsPlaying = false;

                // Не открылся трек, подготовленный при запуске (файл удалён/диск отключён), — молча стоим, музыку сами не включаем.
                if (_isRestoringPlayback)
                {
                    _isRestoringPlayback = false;
                    return;
                }

                // Ограничитель: если подряд не открылось столько файлов, сколько в очереди — останавливаемся, а не крутим круг.
                if (++_consecutiveFailures < GetQueue().Count) PlayNext(null);
            };

            _player.TrackEnded += (s, e) => PlayNext(null);
            _player.PositionChanged += (s, pos) =>
            {
                SetField(ref _position, pos, nameof(Position));
                OnPropertyChanged(nameof(PositionSeconds));
                OnPropertyChanged(nameof(PositionText));
            };
        }

        // ===================== Загрузка треков =====================

        /// <summary>
        /// Диалог выбора ПАПКИ: достаточно указать папку — все песни из неё (и из вложенных папок)
        /// загрузятся сами. Окно при этом не замирает: файлы читаются в фоне небольшими порциями,
        /// и список на экране пополняется по мере чтения.
        /// </summary>
        private async void OpenFolderAndLoadTracks(object? parameter)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку с музыкой" };
            if (dialog.ShowDialog() != true) return;

            IsLoading = true;
            LoadStatusText = "Поиск файлов…";

            try
            {
                await LoadFolderAsync(dialog.FolderName);
            }
            catch
            {
                // Папка могла стать недоступной посреди загрузки — то, что успело загрузиться, остаётся.
            }
            finally
            {
                IsLoading = false;
                LoadStatusText = string.Empty;
            }
        }

        private async Task LoadFolderAsync(string folder)
        {
            // 1. Поиск файлов — в фоне.
            string[] files = await Task.Run(() =>
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System
                };

                return Directory.EnumerateFiles(folder, "*", options)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            });

            await LoadFilesAsync(files);
        }

        /// <summary>
        /// Читает теги выбранных файлов порциями и добавляет их в библиотеку (уже известные пропускаются).
        /// Общая часть для «Добавить папку» и «Добавить песни».
        /// </summary>
        private async Task LoadFilesAsync(string[] files)
        {
            string[] fresh = files.Where(f => !_knownPaths.Contains(f)).ToArray();

            // 2. Чтение тегов порциями: порция читается параллельно в фоне,
            //    результат сразу добавляется в список на экране.
            for (int offset = 0; offset < fresh.Length; offset += LoadChunkSize)
            {
                int end = Math.Min(offset + LoadChunkSize, fresh.Length);
                string[] chunk = fresh[offset..end];

                Track[] tracks = await Task.Run(() => ReadTracks(chunk));

                foreach (var track in tracks)
                    _knownPaths.Add(track.FilePath);

                _allTracks.AddRange(tracks);
                AppendToView(tracks);

                LoadStatusText = $"Загрузка: {end} из {fresh.Length}";
            }

            // 3. Исполнители и альбомы пересобираем один раз — в конце.
            RefreshArtists();
            RefreshAlbums();
        }

        /// <summary>Читает теги порции файлов параллельно (вызывается в фоновом потоке).</summary>
        private Track[] ReadTracks(string[] paths)
        {
            var result = new Track[paths.Length];

            Parallel.For(
                0, paths.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) },
                i => result[i] = CreateTrackFromFile(paths[i]));

            return result;
        }

        /// <summary>Дописывает новые треки в конец текущего списка на экране (с учётом поиска), не трогая порядок остальных.</summary>
        private void AppendToView(IEnumerable<Track> newTracks)
        {
            foreach (var track in newTracks)
            {
                if (!MatchesSearch(track)) continue;
                Tracks.Add(track);
                track.Number = Tracks.Count;
            }
        }

        /// <summary>
        /// Читает название, исполнителя, альбом, длительность и обложку из ID3-тегов файла.
        /// Если файл без тегов или они повреждены — тихо откатывается на имя
        /// файла и нулевую длительность, не прерывая загрузку остальных треков.
        /// </summary>
        private Track CreateTrackFromFile(string path)
        {
            string title = Path.GetFileNameWithoutExtension(path);
            string artist = "Неизвестный исполнитель";
            string album = string.Empty;
            TimeSpan duration = TimeSpan.Zero;
            uint trackNumber = 0;
            uint discNumber = 0;
            BitmapImage? cover = null;

            try
            {
                using var tagFile = TagLib.File.Create(path);

                if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                    title = tagFile.Tag.Title;

                if (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer))
                    artist = tagFile.Tag.FirstPerformer;

                if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                    album = tagFile.Tag.Album;

                duration = tagFile.Properties.Duration;
                trackNumber = tagFile.Tag.Track;
                discNumber = tagFile.Tag.Disc;
                cover = ExtractCover(tagFile.Tag);
            }
            catch
            {
                // Файл без корректных ID3-тегов — не страшно, продолжаем с именем файла.
            }

            return new Track
            {
                Title = title,
                Artist = artist,
                Album = album,
                Duration = duration,
                Cover = cover,
                FilePath = path,
                TrackNumber = trackNumber,
                DiscNumber = discNumber,
                DateAdded = DateTime.Now
            };
        }

        /// <summary>
        /// Достаёт обложку из встроенной картинки ID3-тега (APIC-фрейм) через общий кэш:
        /// одинаковые обложки у разных треков — это один и тот же объект в памяти.
        /// Если картинки нет или она повреждена — null; в UI это означает серую заглушку.
        /// </summary>
        private BitmapImage? ExtractCover(TagLib.Tag tag)
        {
            if (tag.Pictures == null || tag.Pictures.Length == 0) return null;
            return _covers.GetOrCreate(tag.Pictures[0].Data.Data);
        }

        private void RefreshArtists()
        {
            Artists.ReplaceAll(_allTracks.Select(t => t.Artist).Distinct().OrderBy(a => a).ToList());
        }

        /// <summary>Перегруппировывает все треки в альбомы по тегам Album + Artist.</summary>
        private void RefreshAlbums()
        {
            var groups = _allTracks
                .GroupBy(t => new
                {
                    Album = string.IsNullOrWhiteSpace(t.Album) ? "Неизвестный альбом" : t.Album,
                    t.Artist
                })
                .OrderBy(g => g.Key.Artist)
                .ThenBy(g => g.Key.Album);

            var albums = new List<Album>();
            var singles = new List<Track>(); // треки из «альбомов» короче MinTracksInAlbum — уйдут в «Остальные песни»
            foreach (var group in groups)
            {
                // Порядок в альбоме: сначала треки с номером (по диску и номеру), затем без номера — в порядке добавления.
                var tracksInAlbum = group
                    .Select((track, index) => (track, index))
                    .OrderBy(x => x.track.TrackNumber == 0)
                    .ThenBy(x => x.track.DiscNumber == 0 ? 1u : x.track.DiscNumber)
                    .ThenBy(x => x.track.TrackNumber)
                    .ThenBy(x => x.index)
                    .Select(x => x.track)
                    .ToList();
                if (tracksInAlbum.Count < MinTracksInAlbum)
                {
                    singles.AddRange(tracksInAlbum);
                    continue;
                }

                albums.Add(new Album
                {
                    Title = group.Key.Album,
                    Artist = group.Key.Artist,
                    Cover = tracksInAlbum.Select(t => t.Cover).FirstOrDefault(c => c != null),
                    Tracks = tracksInAlbum
                });
            }

            Albums.ReplaceAll(albums);

            OtherSongs = BuildOtherSongs(singles);

            // Для виртуализированной сетки: те же альбомы, нарезанные на ряды по AlbumColumns,
            // а «Остальные песни» — отдельным рядом в самом низу.
            var rows = TileRows.Build(albums, AlbumColumns);
            if (OtherSongs != null)
            {
                var lastRow = new List<Album?>(AlbumColumns) { OtherSongs };
                while (lastRow.Count < AlbumColumns) lastRow.Add(null);
                rows.Add(lastRow);
            }
            AlbumRows.ReplaceAll(rows);

            // Альбомы пересоздаются целиком — находим в новом списке тот же альбом, чтобы очередь не потерялась.
            if (_queueAlbum != null)
            {
                if (_queueAlbum is OtherSongsAlbum)
                {
                    _queueAlbum = OtherSongs;
                }
                else
                {
                    string title = _queueAlbum.Title, artist = _queueAlbum.Artist;
                    _queueAlbum = Albums.FirstOrDefault(a => a.Title == title && a.Artist == artist);
                }
            }

            UpdateAlbumPlaybackStates();
        }

        /// <summary>
        /// Держит Album.IsPlaying в согласии с реальным состоянием плеера — вычисляется
        /// заново при каждой смене трека/паузы, а не выставляется вручную в разных местах,
        /// поэтому разъехаться с реальностью это состояние не может.
        /// </summary>
        private void UpdateAlbumPlaybackStates()
        {
            foreach (var album in Albums)
                album.IsPlaying = IsPlaying && IsCurrentTrackIn(album);

            if (OtherSongs != null)
                OtherSongs.IsPlaying = IsPlaying && IsCurrentTrackIn(OtherSongs);
        }

        /// <summary>Играющий (или стоящий на паузе) трек относится к этому альбому.</summary>
        private bool IsCurrentTrackIn(Album album)
        {
            if (CurrentTrack == null) return false;

            // У «Остальных песен» нет общих тегов Album/Artist — смотрим по составу.
            if (album is OtherSongsAlbum)
                return album.Tracks.Contains(CurrentTrack);

            return string.Equals(CurrentTrack.Album, album.Title, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(CurrentTrack.Artist, album.Artist, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Клик по Play/Pause на карточке альбома. Если это уже "активный" альбом
        /// (его трек сейчас играет или на паузе) — просто переключаем play/pause,
        /// сохраняя позицию. Иначе — стартуем альбом с первого трека.
        /// </summary>
        private void ExecutePlayAlbum(Album album)
        {
            bool isSameAlbumActive = IsCurrentTrackIn(album);

            if (isSameAlbumActive)
            {
                ExecutePlayPause(null);
                return;
            }

            if (album.Tracks.Count == 0) return;
            _queueAlbum = album;
            PlayTrack(album.Tracks[0]);
        }

        // ===================== Воспроизведение =====================

        private void ExecutePlayPause(object? parameter)
        {
            if (CurrentTrack == null && Tracks.Count > 0)
            {
                _queueAlbum = null;
                PlayTrack(Tracks[0]);
                return;
            }

            if (IsPlaying)
            {
                _player.Pause();
                IsPlaying = false;
            }
            else
            {
                _player.Play();
                IsPlaying = true;
            }
        }

        private void PlayTrack(Track track)
        {
            // Пользователь выбрал трек сам — подготовленная при запуске позиция больше не актуальна.
            _pendingRestorePosition = null;
            _isRestoringPlayback = false;

            CurrentTrack = track;
            _player.Load(track.FilePath);
            _player.Play();
            IsPlaying = true;

            // Длительность из ID3-тега доступна сразу; NaturalDuration у MediaPlayer
            // появляется только после асинхронного MediaOpened — используем её
            // как запасной вариант, если тег не дал ничего.
            Duration = track.Duration > TimeSpan.Zero ? track.Duration : (_player.Duration ?? TimeSpan.Zero);
        }

        /// <summary>Очередь воспроизведения: треки альбома, если играем из альбома, иначе основной список.</summary>
        private IList<Track> GetQueue() =>
            _queueExternal ?? (_queueAlbum != null ? (IList<Track>)_queueAlbum.Tracks : Tracks);

        // ===================== IPlaylistHost (для модуля плейлистов) =====================

        public IReadOnlyList<Track> LibraryTracks => Tracks;

        public void Navigate(ScreenMode screen) => CurrentScreen = screen;

        public void PlayQueue(IList<Track> queue, Track start)
        {
            _queueAlbumValue = null;
            _queueExternal = queue;
            PlayTrack(start);
        }

        public bool IsQueue(IList<Track> queue) => ReferenceEquals(_queueExternal, queue);

        public void TogglePlayPause() => ExecutePlayPause(null);

        private void PlayNext(object? parameter)
        {
            var queue = GetQueue();
            if (queue.Count == 0 || CurrentTrack == null) return;
            int index = queue.IndexOf(CurrentTrack);
            int nextIndex = (index + 1) % queue.Count;
            PlayTrack(queue[nextIndex]);
        }

        private void PlayPrevious(object? parameter)
        {
            var queue = GetQueue();
            if (queue.Count == 0 || CurrentTrack == null) return;
            int index = queue.IndexOf(CurrentTrack);
            int prevIndex = (index - 1 + queue.Count) % queue.Count;
            PlayTrack(queue[prevIndex]);
        }

        // ===================== Поиск =====================

        private bool MatchesSearch(Track track)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string needle = SearchText.Trim();
            return track.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                   track.Artist.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplySearchFilter()
        {
            Tracks.ReplaceAll(_allTracks.Where(MatchesSearch));
            RenumberTracks();
        }

        // ===================== Сортировка =====================

        private void SortBy<TKey>(Func<Track, TKey> keySelector)
        {
            var sorted = Tracks.OrderBy(keySelector).ToList();
            ReplaceTracks(sorted);
        }

        // ===================== Логика Хаоса =====================

        private void ExecuteChaos(object? parameter)
        {
            switch (CurrentChaosMode)
            {
                case ChaosMode.Random:
                    ShuffleTracks();
                    break;
                case ChaosMode.BringUpFromBottom:
                    BringUpFromBottom(ChaosCount);
                    break;
                case ChaosMode.BringUpArtist:
                    if (!string.IsNullOrEmpty(SelectedArtist))
                        BringUpArtist(SelectedArtist, ChaosCount);
                    break;
            }
        }

        /// <summary>Режим 1: полностью случайное перемешивание всего плейлиста.</summary>
        private void ShuffleTracks()
        {
            var shuffled = Tracks.OrderBy(_ => _random.Next()).ToList();
            ReplaceTracks(shuffled);
        }

        /// <summary>Режим 2: последние N треков списка поднимаются наверх, порядок остальных не меняется.</summary>
        private void BringUpFromBottom(int count)
        {
            if (Tracks.Count <= count) return;

            var items = Tracks.ToList();
            var bottom = items.Skip(items.Count - count).Take(count).ToList();
            var remaining = items.Take(items.Count - count).ToList();

            var result = new List<Track>(bottom);
            result.AddRange(remaining);
            ReplaceTracks(result);
        }

        /// <summary>Режим 3: до N треков выбранного исполнителя поднимаются наверх, остальное — без изменений.</summary>
        private void BringUpArtist(string artist, int count)
        {
            var items = Tracks.ToList();
            var artistTracks = items.Where(t => t.Artist == artist).ToList();
            var otherTracks = items.Where(t => t.Artist != artist).ToList();

            var topArtistTracks = artistTracks.Take(Math.Min(count, artistTracks.Count)).ToList();
            var remainingArtistTracks = artistTracks.Skip(topArtistTracks.Count).ToList();

            var result = new List<Track>(topArtistTracks);
            result.AddRange(otherTracks);
            result.AddRange(remainingArtistTracks);
            ReplaceTracks(result);
        }

        // ===================== Вспомогательные методы =====================

        private void ReplaceTracks(List<Track> newOrder)
        {
            Tracks.ReplaceAll(newOrder);
            RenumberTracks();
        }

        private void RenumberTracks()
        {
            for (int i = 0; i < Tracks.Count; i++)
                Tracks[i].Number = i + 1;
        }

        public void Dispose()
        {
            _player.Dispose();
        }
    }
}
