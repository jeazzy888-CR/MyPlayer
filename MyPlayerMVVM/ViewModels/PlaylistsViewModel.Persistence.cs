using System;
using System.Collections.Generic;
using System.Linq;
using MyPlayer_NET9.Models;
using MyPlayer_NET9.Services.Persistence;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>Сохранение и восстановление плейлистов пользователя (подключается к MainViewModel.Persistence).</summary>
    public partial class PlaylistsViewModel
    {
        /// <summary>Id открытого плейлиста; null — открыт «черновик» (ещё не сохранённый) или ничего.</summary>
        public string? SelectedPlaylistId =>
            SelectedPlaylist != null && _playlists.Contains(SelectedPlaylist) ? SelectedPlaylist.Id : null;

        public Playlist? FindById(string? id) =>
            string.IsNullOrEmpty(id) ? null : _playlists.FirstOrDefault(p => p.Id == id);

        /// <summary>Id плейлиста, чьи треки являются этой очередью (null — очередь не из плейлиста).</summary>
        public string? PlaylistIdOfQueue(IList<Track>? queue) =>
            queue == null ? null : _playlists.FirstOrDefault(p => ReferenceEquals(p.Tracks, queue))?.Id;

        /// <summary>Снимок всех плейлистов в порядке сетки. Вызывается в потоке интерфейса.</summary>
        public List<PlaylistRecord> CapturePlaylists() =>
            _playlists.Select(p => new PlaylistRecord
            {
                Id = p.Id,
                Title = p.Title,
                CoverImage = p.Cover,
                Tracks = p.Tracks.Select(t => t.Id).ToList()
            }).ToList();

        /// <summary>
        /// Восстанавливает плейлисты. Треки, которых больше нет в библиотеке (файл удалён), пропускаются;
        /// сам плейлист остаётся, даже если опустел.
        /// </summary>
        public void RestorePlaylists(IEnumerable<PlaylistRecord>? records, Func<string, Track?> findTrack, CoverStore covers)
        {
            if (records == null) return;

            _playlists.Clear();

            foreach (var record in records)
            {
                var playlist = new Playlist
                {
                    Title = record.Title ?? string.Empty,
                    Cover = covers.Load(record.Cover)
                };
                if (!string.IsNullOrEmpty(record.Id))
                    playlist.Id = record.Id;

                foreach (string trackId in record.Tracks ?? new List<string>())
                {
                    var track = findTrack(trackId);
                    if (track != null && !playlist.Tracks.Contains(track))
                        playlist.Tracks.Add(track);
                }

                _playlists.Add(playlist);
            }

            RebuildRows();
            UpdatePlaybackStates();
        }

        /// <summary>Открывает готовый плейлист в режиме просмотра (без навигации — экран выставляет вызывающий).</summary>
        public void SelectForRestore(Playlist playlist)
        {
            SelectedPlaylist = playlist;
            _isNewPlaylist = false;
            IsEditing = false;
            RefreshEditorItems();
        }

        /// <summary>Перед выходом: то, что пользователь успел набрать в редакторе, сохраняется, как при нажатии «Готово».</summary>
        public void CommitPendingEdit() => FinishEditing();
    }
}
