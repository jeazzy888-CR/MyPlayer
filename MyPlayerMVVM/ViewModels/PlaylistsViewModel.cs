using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using MyPlayer_NET9.Models;
using MyPlayer_NET9.Services;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Модуль «Плейлисты»: сетка плейлистов, редактор (создание/изменение) и выбор треков.
    /// Подключается к MainViewModel через IPlaylistHost и больше ничего о ней не знает.
    ///
    /// Один и тот же экран PlaylistDetail работает в двух режимах:
    ///   IsEditing = false — готовый плейлист (копия экрана альбома, только без нумерации);
    ///   IsEditing = true  — редактор: обложка/название можно менять, в конце списка зелёная строка «выбери трек».
    /// </summary>
    public partial class PlaylistsViewModel : ViewModelBase
    {
        private readonly IPlaylistHost _host;
        private readonly CoverCache _covers = new CoverCache();
        private readonly List<Playlist> _playlists = new List<Playlist>();

        /// <summary>Плейлисты, нарезанные на ряды по AlbumColumns штук; последней всегда идёт карточка «добавить».</summary>
        public BulkObservableCollection<List<object?>> PlaylistRows { get; } = new BulkObservableCollection<List<object?>>();

        /// <summary>Строки списка на экране плейлиста: треки + (в режиме редактора) зелёная строка «выбери трек».</summary>
        public BulkObservableCollection<object> EditorItems { get; } = new BulkObservableCollection<object>();

        /// <summary>Треки для экрана «Выберите композицию»: главный список без тех, что уже в плейлисте.</summary>
        public BulkObservableCollection<Track> PickerTracks { get; } = new BulkObservableCollection<Track>();

        private Playlist? _selectedPlaylist;
        public Playlist? SelectedPlaylist
        {
            get => _selectedPlaylist;
            private set => SetField(ref _selectedPlaylist, value);
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (SetField(ref _isEditing, value))
                    OnPropertyChanged(nameof(ShowDeleteButton));
            }
        }

        /// <summary>Плейлист создаётся прямо сейчас и ещё не сохранён в списке.</summary>
        private bool _isNewPlaylist;

        /// <summary>Кнопка «удалить» нужна только при правке уже сохранённого плейлиста.</summary>
        public bool ShowDeleteButton => IsEditing && !_isNewPlaylist;

        /// <summary>Просит окно прокрутить список плейлиста к концу (после добавления трека).</summary>
        public event EventHandler? ScrollToEndRequested;

        public RelayCommand ShowPlaylistsCommand { get; }
        public RelayCommand AddPlaylistCommand { get; }
        public RelayCommand OpenPlaylistCommand { get; }
        public RelayCommand PlayPlaylistCommand { get; }
        public RelayCommand PlayPlaylistTrackCommand { get; }
        public RelayCommand EditPlaylistCommand { get; }
        public RelayCommand DoneCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand DeletePlaylistCommand { get; }
        public RelayCommand ChooseCoverCommand { get; }
        public RelayCommand UseFirstTrackCoverCommand { get; }
        public RelayCommand PickTrackCommand { get; }
        public RelayCommand AddPickedTrackCommand { get; }
        public RelayCommand BackFromPickerCommand { get; }
        public RelayCommand RemoveTrackCommand { get; }

        public PlaylistsViewModel(IPlaylistHost host)
        {
            _host = host;
            _host.PropertyChanged += OnHostPropertyChanged;

            ShowPlaylistsCommand = new RelayCommand(_ => _host.Navigate(ScreenMode.PlaylistsGrid));

            AddPlaylistCommand = new RelayCommand(_ =>
            {
                SelectedPlaylist = new Playlist();
                _isNewPlaylist = true;
                IsEditing = true;
                RefreshEditorItems();
                _host.Navigate(ScreenMode.PlaylistDetail);
            });

            OpenPlaylistCommand = new RelayCommand(param =>
            {
                if (param is not Playlist playlist) return;
                SelectedPlaylist = playlist;
                _isNewPlaylist = false;
                IsEditing = false;
                RefreshEditorItems();
                _host.Navigate(ScreenMode.PlaylistDetail);
            });

            PlayPlaylistCommand = new RelayCommand(param =>
            {
                if (param is not Playlist playlist) return;

                if (_host.IsQueue(playlist.Tracks))
                    _host.TogglePlayPause();
                else if (playlist.Tracks.Count > 0)
                    _host.PlayQueue(playlist.Tracks, playlist.Tracks[0]);

                UpdatePlaybackStates();
            });

            PlayPlaylistTrackCommand = new RelayCommand(param =>
            {
                if (param is Track track && SelectedPlaylist != null && SelectedPlaylist.Tracks.Contains(track))
                {
                    _host.PlayQueue(SelectedPlaylist.Tracks, track);
                    UpdatePlaybackStates();
                }
            });

            EditPlaylistCommand = new RelayCommand(_ =>
            {
                if (SelectedPlaylist == null) return;
                IsEditing = true;
                RefreshEditorItems();
            });

            // «Готово»: сохранить и остаться на экране плейлиста (уже в режиме просмотра).
            DoneCommand = new RelayCommand(_ =>
            {
                FinishEditing();
                if (SelectedPlaylist != null && !_playlists.Contains(SelectedPlaylist))
                {
                    // Пустой черновик не сохранился — смотреть на экране нечего, возвращаемся к сетке.
                    SelectedPlaylist = null;
                    _host.Navigate(ScreenMode.PlaylistsGrid);
                }
            });

            // Стрелка «назад»: если редактировали — сохраняем, затем возвращаемся к сетке.
            BackCommand = new RelayCommand(_ =>
            {
                FinishEditing();
                _host.Navigate(ScreenMode.PlaylistsGrid);
            });

            DeletePlaylistCommand = new RelayCommand(_ => DeleteSelectedPlaylist(), _ => ShowDeleteButton);

            ChooseCoverCommand = new RelayCommand(_ => ChooseCoverFromFile(), _ => IsEditing);

            UseFirstTrackCoverCommand = new RelayCommand(
                _ => UseFirstTrackCover(),
                _ => IsEditing && SelectedPlaylist != null && SelectedPlaylist.Tracks.Count > 0);

            PickTrackCommand = new RelayCommand(_ => OpenPicker(), _ => IsEditing && SelectedPlaylist != null);

            AddPickedTrackCommand = new RelayCommand(param =>
            {
                if (param is Track track && SelectedPlaylist != null && IsEditing)
                    AddTrack(track);
            });

            BackFromPickerCommand = new RelayCommand(_ => _host.Navigate(ScreenMode.PlaylistDetail));

            RemoveTrackCommand = new RelayCommand(param =>
            {
                if (param is Track track && SelectedPlaylist != null && IsEditing)
                {
                    SelectedPlaylist.Tracks.Remove(track);
                    EditorItems.Remove(track);
                }
            });

            RebuildRows();
        }

        // ===================== Сетка плейлистов =====================

        private void RebuildRows()
        {
            // Карточка «добавить плейлист» всегда последняя: пока плейлистов нет — стоит одна, потом уезжает за ними.
            var items = new List<object>(_playlists) { AddPlaylistTile.Instance };
            PlaylistRows.ReplaceAll(TileRows.Build(items, MainViewModel.AlbumColumns));
        }

        private void RefreshEditorItems()
        {
            var items = new List<object>();
            if (SelectedPlaylist != null)
                items.AddRange(SelectedPlaylist.Tracks);
            if (IsEditing)
                items.Add(AddTrackRow.Instance);

            EditorItems.ReplaceAll(items);
        }

        // ===================== Редактор =====================

        /// <summary>
        /// Завершает редактирование. Новый плейлист попадает в сетку, только если в нём хоть что-то есть
        /// (название, обложка или треки) — случайно открытый и закрытый «пустой» черновик не оставляет следа.
        /// Без названия плейлист получает «Плейлист N».
        /// </summary>
        private void FinishEditing()
        {
            if (!IsEditing || SelectedPlaylist == null) return;

            var playlist = SelectedPlaylist;

            if (_isNewPlaylist)
            {
                bool isEmptyDraft = playlist.Tracks.Count == 0 &&
                                    string.IsNullOrWhiteSpace(playlist.Title) &&
                                    playlist.Cover == null;
                if (!isEmptyDraft)
                {
                    if (string.IsNullOrWhiteSpace(playlist.Title))
                        playlist.Title = $"Плейлист {_playlists.Count + 1}";

                    _playlists.Add(playlist);
                    RebuildRows();
                }
                _isNewPlaylist = false;
            }
            else if (string.IsNullOrWhiteSpace(playlist.Title))
            {
                playlist.Title = $"Плейлист {_playlists.IndexOf(playlist) + 1}";
            }

            IsEditing = false;
            RefreshEditorItems();
            UpdatePlaybackStates();
        }

        private void DeleteSelectedPlaylist()
        {
            var playlist = SelectedPlaylist;
            if (playlist == null || _isNewPlaylist) return;

            var answer = MessageBox.Show(
                $"Удалить плейлист «{playlist.Title}»? Сами треки останутся в библиотеке.",
                "Удаление плейлиста",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            _playlists.Remove(playlist);
            RebuildRows();

            IsEditing = false;
            SelectedPlaylist = null;
            EditorItems.ReplaceAll(Array.Empty<object>());
            _host.Navigate(ScreenMode.PlaylistsGrid);
        }

        private void ChooseCoverFromFile()
        {
            if (SelectedPlaylist == null) return;

            var dialog = new OpenFileDialog
            {
                Title = "Выберите обложку плейлиста",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var image = _covers.GetOrCreate(File.ReadAllBytes(dialog.FileName));
                if (image == null)
                {
                    MessageBox.Show("Не удалось прочитать это изображение.", "Обложка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedPlaylist.Cover = image;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть файл:\n{ex.Message}", "Обложка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>F2: обложка берётся у первого трека, у которого она есть (в первую очередь — у самого первого).</summary>
        private void UseFirstTrackCover()
        {
            if (SelectedPlaylist == null) return;

            var cover = SelectedPlaylist.Tracks.Select(t => t.Cover).FirstOrDefault(c => c != null);
            if (cover == null)
            {
                MessageBox.Show("У добавленных треков нет встроенных обложек.", "Обложка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedPlaylist.Cover = cover;
        }

        // ===================== Выбор трека =====================

        private void OpenPicker()
        {
            if (SelectedPlaylist == null) return;

            var already = new HashSet<Track>(SelectedPlaylist.Tracks);
            PickerTracks.ReplaceAll(_host.LibraryTracks.Where(t => !already.Contains(t)));
            _host.Navigate(ScreenMode.PlaylistPicker);
        }

        private void AddTrack(Track track)
        {
            var playlist = SelectedPlaylist;
            if (playlist == null || playlist.Tracks.Contains(track)) return;

            playlist.Tracks.Add(track);

            // Новый трек встаёт перед зелёной строкой «выбери трек», которая всегда последняя.
            EditorItems.Insert(Math.Max(0, EditorItems.Count - 1), track);

            _host.Navigate(ScreenMode.PlaylistDetail);
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }

        // ===================== Реакция на хост =====================

        private void OnHostPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IPlaylistHost.IsPlaying):
                case "CurrentTrack":
                    UpdatePlaybackStates();
                    break;

                case nameof(IPlaylistHost.CurrentScreen):
                    // Ушли из редактора на любой другой экран (кроме выбора трека и «Сейчас играет») —
                    // сохраняем то, что успели сделать, чтобы редактор не «зависал» открытым.
                    var screen = _host.CurrentScreen;
                    bool stillInside = screen is ScreenMode.PlaylistDetail
                        or ScreenMode.PlaylistPicker
                        or ScreenMode.NowPlaying;
                    if (!stillInside)
                        FinishEditing();
                    break;
            }
        }

        private void UpdatePlaybackStates()
        {
            foreach (var playlist in _playlists)
                playlist.IsPlaying = _host.IsPlaying && _host.IsQueue(playlist.Tracks);
        }
    }
}
