using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MyPlayer_NET9.Models;
using MyPlayer_NET9.Services.Persistence;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Сохранение и восстановление всего состояния плеера: «зашёл — и всё как было при выходе».
    ///
    /// Что сохраняется:
    ///  • библиотека (треки с тегами и обложками) — library.json + папка covers;
    ///  • порядок главного списка именно как на экране (сортировка и применённый Хаос), поиск;
    ///  • настройки Хаоса (режим, количество, исполнитель);
    ///  • плейлисты пользователя (название, обложка, треки, порядок);
    ///  • громкость; какой трек играл, на какой секунде, из какой очереди (список/альбом/плейлист);
    ///  • открытый экран (в том числе альбом/плейлист/полноэкранный плеер);
    ///  • размер, положение и состояние окна.
    /// Музыка при запуске сама НЕ включается: трек подготовлен на той секунде, где остановились, жмите Play.
    ///
    /// Куда: см. AppPaths (портативная версия — рядом с программой, обычная — в %AppData%).
    /// Когда: каждые 5 секунд в фоне (только если что-то изменилось) и при закрытии окна.
    /// </summary>
    public partial class MainViewModel
    {
        private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(5);

        private readonly StateStore _store = new StateStore(AppPaths.DataDirectory);

        private StateFile? _initialState;
        private bool _initialStateRead;

        /// <summary>Восстановление завершено — только после этого разрешено сохранение (иначе пустое состояние затёрло бы настоящее).</summary>
        private bool _stateReady;

        /// <summary>Сколько треков в библиотеке записано на диск. Не совпало с текущим — библиотеку нужно переписать.</summary>
        private int _savedLibraryCount = -1;

        private DispatcherTimer? _autosaveTimer;
        private int _saveInProgress;
        private long _saveGeneration;

        /// <summary>Позиция, которую нужно выставить, когда плеер откроет подготовленный при запуске трек.</summary>
        private TimeSpan? _pendingRestorePosition;

        /// <summary>Трек подготовлен при запуске и ещё не открыт плеером: если он не откроется — молча остаёмся стоять.</summary>
        private bool _isRestoringPlayback;

        /// <summary>Положение окна; обновляет окно (Views/MainWindow.Persistence).</summary>
        public WindowRecord? WindowPlacement { get; set; }

        /// <summary>Положение окна из прошлого сеанса — окну нужно до показа, поэтому состояние читается сразу.</summary>
        public WindowRecord? SavedWindow => GetInitialState()?.Window;

        private StateFile? GetInitialState()
        {
            if (!_initialStateRead)
            {
                _initialStateRead = true;
                try { _initialState = _store.LoadState(); }
                catch (Exception ex) { Debug.WriteLine($"LoadState: {ex.Message}"); }
            }
            return _initialState;
        }

        // ===================== Восстановление =====================

        /// <summary>Вызывается один раз после показа окна: окно сразу видно, библиотека подтягивается на глазах.</summary>
        public async Task InitializeStateAsync()
        {
            if (_stateReady) return;

            IsLoading = true;
            LoadStatusText = "Восстановление…";

            try
            {
                var state = GetInitialState();

                var library = await Task.Run(() => _store.LoadLibrary());
                if (library != null && library.Tracks.Count > 0)
                {
                    var tracks = await Task.Run(() => BuildTracks(library));
                    ApplyLibrary(tracks, library.Tracks.Count);
                }

                if (state != null)
                    ApplyState(state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeStateAsync: {ex}");
            }
            finally
            {
                IsLoading = false;
                LoadStatusText = string.Empty;
                _stateReady = true;
                StartAutosave();
            }
        }

        /// <summary>
        /// Превращает записи в треки (фоновый поток). Файл, которого нет на ДОСТУПНОМ диске, считаем удалённым и выбрасываем;
        /// если сам диск/сетевая папка сейчас недоступны (флешка не вставлена) — трек остаётся, вернётся вместе с диском.
        /// </summary>
        private Track[] BuildTracks(LibraryFile library)
        {
            var rootAvailable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<TrackRecord>(library.Tracks.Count);

            foreach (var record in library.Tracks)
            {
                if (string.IsNullOrWhiteSpace(record.Path)) continue;

                string root = GetRootOrEmpty(record.Path);
                if (!rootAvailable.TryGetValue(root, out bool available))
                {
                    available = root.Length > 0 && SafeDirectoryExists(root);
                    rootAvailable[root] = available;
                }

                if (available && !File.Exists(record.Path)) continue;

                kept.Add(record);
            }

            var result = new Track[kept.Count];

            Parallel.For(
                0, kept.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) },
                i =>
                {
                    var r = kept[i];
                    result[i] = new Track
                    {
                        Title = r.Title,
                        Artist = r.Artist,
                        Album = r.Album,
                        Duration = TimeSpan.FromMilliseconds(Math.Max(0, r.DurationMs)),
                        FilePath = r.Path,
                        TrackNumber = r.TrackNumber,
                        DiscNumber = r.DiscNumber,
                        DateAdded = r.DateAdded,
                        Cover = _store.Covers.Load(r.Cover)
                    };
                });

            return result;
        }

        private static string GetRootOrEmpty(string path)
        {
            try { return Path.GetPathRoot(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool SafeDirectoryExists(string path)
        {
            try { return Directory.Exists(path); }
            catch { return false; }
        }

        private void ApplyLibrary(Track[] tracks, int recordsOnDisk)
        {
            _allTracks = new List<Track>(tracks.Length);
            _knownPaths.Clear();

            foreach (var track in tracks)
            {
                if (_knownPaths.Add(track.FilePath))
                    _allTracks.Add(track);
            }

            Tracks.ReplaceAll(_allTracks);
            RenumberTracks();

            RefreshArtists();
            RefreshAlbums();

            // На диске лежало recordsOnDisk записей; если часть выброшена (файлы удалены) — число не совпадёт и библиотека будет переписана.
            _savedLibraryCount = recordsOnDisk;
        }

        private void ApplyState(StateFile state)
        {
            var index = new Dictionary<string, Track>();
            foreach (var track in _allTracks)
                index.TryAdd(track.Id, track);

            Track? FindTrack(string id) => index.TryGetValue(id, out var t) ? t : null;

            // Каждый блок — отдельно: сбой в одном не должен лишить пользователя всего остального.
            Safe(() => Volume = Math.Clamp(state.Volume, 0.0, 1.0));

            Safe(() =>
            {
                if (Enum.TryParse(state.Chaos.Mode, out ChaosMode mode)) CurrentChaosMode = mode;
                if (state.Chaos.Count > 0) ChaosCount = state.Chaos.Count;
                SelectedArtist = Artists.Contains(state.Chaos.Artist) ? state.Chaos.Artist : string.Empty;
            });

            Safe(() =>
            {
                if (!string.IsNullOrEmpty(state.SearchText))
                    SearchText = state.SearchText; // применит фильтр; порядок ниже перекроет его

                ApplyViewOrder(state.ViewOrder, FindTrack);
            });

            Safe(() => Playlists.RestorePlaylists(state.Playlists, FindTrack, _store.Covers));

            Safe(() => RestorePlayback(state.Playback, FindTrack));

            Safe(() => RestoreScreen(state.Screen));
        }

        private static void Safe(Action action)
        {
            try { action(); }
            catch (Exception ex) { Debug.WriteLine($"Restore: {ex}"); }
        }

        /// <summary>Порядок главного списка. Новые треки, которых не было в сохранённом порядке, встают в конец.</summary>
        private void ApplyViewOrder(List<string>? ids, Func<string, Track?> findTrack)
        {
            if (ids == null || ids.Count == 0) return;

            var ordered = new List<Track>(Tracks.Count);
            var seen = new HashSet<Track>();

            foreach (string id in ids)
            {
                var track = findTrack(id);
                if (track != null && seen.Add(track))
                    ordered.Add(track);
            }

            // Всё, что сейчас проходит поиск, но не было в сохранённом порядке (библиотека пополнилась), — в конец.
            foreach (var track in Tracks.ToList())
            {
                if (seen.Add(track))
                    ordered.Add(track);
            }

            Tracks.ReplaceAll(ordered.Where(MatchesSearch));
            RenumberTracks();
        }

        private void RestorePlayback(PlaybackRecord? playback, Func<string, Track?> findTrack)
        {
            if (playback == null || string.IsNullOrEmpty(playback.TrackId)) return;

            var track = findTrack(playback.TrackId);
            if (track == null) return;

            // Очередь: из какого списка шли «дальше/назад».
            _queueAlbum = null;
            switch (playback.Queue)
            {
                case "playlist":
                    var playlist = Playlists.FindById(playback.QueuePlaylistId);
                    if (playlist != null && playlist.Tracks.Contains(track))
                        _queueExternal = playlist.Tracks;
                    break;

                case "album":
                    var album = FindAlbum(playback.QueueAlbumTitle, playback.QueueAlbumArtist);
                    if (album != null && album.Tracks.Contains(track))
                        _queueAlbum = album;
                    break;
            }

            CurrentTrack = track;

            TimeSpan position = TimeSpan.FromMilliseconds(Math.Max(0, playback.PositionMs));
            if (track.Duration > TimeSpan.Zero && position > track.Duration)
                position = TimeSpan.Zero;

            // Готовим трек, но не запускаем: плеер открывает файл на паузе, позиция применится в MediaOpened.
            _isRestoringPlayback = true;
            _pendingRestorePosition = position;

            _player.Load(track.FilePath);
            _player.Pause();

            Duration = track.Duration;

            SetField(ref _position, position, nameof(Position));
            OnPropertyChanged(nameof(PositionSeconds));
            OnPropertyChanged(nameof(PositionText));
        }

        private Album? FindAlbum(string? title, string? artist)
        {
            if (title == null || artist == null) return null;

            if (OtherSongs != null && title == OtherSongs.Title && artist.Length == 0)
                return OtherSongs;

            return Albums.FirstOrDefault(a => a.Title == title && a.Artist == artist);
        }

        private void RestoreScreen(ScreenRecord? screen)
        {
            if (screen == null) return;

            if (!Enum.TryParse(screen.Current, out ScreenMode current)) return;

            if (current == ScreenMode.NowPlaying)
            {
                if (CurrentTrack == null) return;

                ScreenMode before = Enum.TryParse(screen.Before, out ScreenMode b) && b != ScreenMode.NowPlaying
                    ? b
                    : ScreenMode.Songs;

                _screenBeforeNowPlaying = ResolveScreen(before, screen);
                CurrentScreen = ScreenMode.NowPlaying;
                return;
            }

            CurrentScreen = ResolveScreen(current, screen);
        }

        /// <summary>Экран, который реально можно открыть: если альбома/плейлиста больше нет — безопасный родительский экран.</summary>
        private ScreenMode ResolveScreen(ScreenMode wanted, ScreenRecord screen)
        {
            switch (wanted)
            {
                case ScreenMode.AlbumDetail:
                    var album = FindAlbum(screen.AlbumTitle, screen.AlbumArtist);
                    if (album == null) return ScreenMode.AlbumsGrid;
                    SelectedAlbum = album;
                    return ScreenMode.AlbumDetail;

                case ScreenMode.PlaylistDetail:
                case ScreenMode.PlaylistPicker:
                    var playlist = Playlists.FindById(screen.PlaylistId);
                    if (playlist == null) return ScreenMode.PlaylistsGrid;
                    Playlists.SelectForRestore(playlist);
                    return ScreenMode.PlaylistDetail;

                case ScreenMode.NowPlaying:
                    return ScreenMode.Songs;

                default:
                    return wanted;
            }
        }

        // ===================== Снимок и сохранение =====================

        private StateFile CaptureState()
        {
            var state = new StateFile
            {
                Volume = Volume,
                SearchText = SearchText,
                ViewOrder = Tracks.Select(t => t.Id).ToList(),
                Chaos = new ChaosRecord
                {
                    Mode = CurrentChaosMode.ToString(),
                    Count = ChaosCount,
                    Artist = SelectedArtist
                },
                Screen = new ScreenRecord
                {
                    // Выбор трека для плейлиста — часть редактора, после перезапуска открываем сам плейлист.
                    Current = (CurrentScreen == ScreenMode.PlaylistPicker ? ScreenMode.PlaylistDetail : CurrentScreen).ToString(),
                    Before = (_screenBeforeNowPlaying == ScreenMode.PlaylistPicker ? ScreenMode.PlaylistDetail : _screenBeforeNowPlaying).ToString(),
                    AlbumTitle = SelectedAlbum?.Title,
                    AlbumArtist = SelectedAlbum?.Artist,
                    PlaylistId = Playlists.SelectedPlaylistId
                },
                Playlists = Playlists.CapturePlaylists(),
                Window = WindowPlacement ?? GetInitialState()?.Window
            };

            if (CurrentTrack != null)
            {
                var queuePlaylistId = Playlists.PlaylistIdOfQueue(_queueExternal);

                state.Playback = new PlaybackRecord
                {
                    TrackId = CurrentTrack.Id,
                    PositionMs = (long)Position.TotalMilliseconds,
                    Queue = queuePlaylistId != null ? "playlist" : _queueAlbum != null ? "album" : "main",
                    QueuePlaylistId = queuePlaylistId,
                    QueueAlbumTitle = queuePlaylistId == null ? _queueAlbum?.Title : null,
                    QueueAlbumArtist = queuePlaylistId == null ? _queueAlbum?.Artist : null
                };
            }

            return state;
        }

        private LibraryFile CaptureLibrary()
        {
            var library = new LibraryFile();
            foreach (var t in _allTracks)
            {
                library.Tracks.Add(new TrackRecord
                {
                    Path = t.FilePath,
                    Title = t.Title,
                    Artist = t.Artist,
                    Album = t.Album,
                    DurationMs = (long)t.Duration.TotalMilliseconds,
                    TrackNumber = t.TrackNumber,
                    DiscNumber = t.DiscNumber,
                    DateAdded = t.DateAdded,
                    CoverImage = t.Cover
                });
            }
            return library;
        }

        private void StartAutosave()
        {
            if (_autosaveTimer != null) return;

            _autosaveTimer = new DispatcherTimer { Interval = AutosaveInterval };
            _autosaveTimer.Tick += async (_, _) => await SaveStateAsync();
            _autosaveTimer.Start();
        }

        /// <summary>Периодическое сохранение: снимок берётся в потоке интерфейса (мгновенно), запись — в фоне.</summary>
        private async Task SaveStateAsync()
        {
            // Идёт загрузка папки — библиотека на полпути, подождём следующего тика.
            if (!_stateReady || IsLoading) return;
            if (Interlocked.Exchange(ref _saveInProgress, 1) == 1) return;

            try
            {
                var state = CaptureState();
                int libraryCount = _allTracks.Count;
                var library = libraryCount != _savedLibraryCount ? CaptureLibrary() : null;
                long generation = Interlocked.Increment(ref _saveGeneration);

                bool ok = await Task.Run(() => _store.Save(state, library, generation));

                if (ok && library != null)
                    _savedLibraryCount = libraryCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveStateAsync: {ex}");
            }
            finally
            {
                Volatile.Write(ref _saveInProgress, 0);
            }
        }

        /// <summary>Финальное сохранение при закрытии окна (синхронно — приложение сразу завершится).</summary>
        public void SaveStateNow()
        {
            if (!_stateReady) return;

            try
            {
                _autosaveTimer?.Stop();

                // Недописанный в редакторе плейлист сохраняется так же, как по кнопке «Готово».
                Playlists.CommitPendingEdit();

                var state = CaptureState();
                int libraryCount = _allTracks.Count;
                var library = libraryCount != _savedLibraryCount ? CaptureLibrary() : null;
                long generation = Interlocked.Increment(ref _saveGeneration);

                if (_store.Save(state, library, generation) && library != null)
                    _savedLibraryCount = libraryCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveStateNow: {ex}");
            }
        }
    }
}
