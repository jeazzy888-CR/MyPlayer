using System.Collections.Generic;
using System.ComponentModel;
using MyPlayer_NET9.Models;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Всё, что PlaylistsViewModel нужно от «главной» ViewModel: список треков, навигация и воспроизведение.
    /// Модуль плейлистов знает только этот узкий интерфейс, а не MainViewModel целиком.
    /// </summary>
    public interface IPlaylistHost : INotifyPropertyChanged
    {
        ScreenMode CurrentScreen { get; }
        bool IsPlaying { get; }

        /// <summary>Основной список треков в том виде, как он сейчас показан (с учётом сортировки и поиска).</summary>
        IReadOnlyList<Track> LibraryTracks { get; }

        void Navigate(ScreenMode screen);

        /// <summary>Начать играть с трека start; очередью («дальше/назад», автопереход) становится queue.</summary>
        void PlayQueue(IList<Track> queue, Track start);

        /// <summary>Играет ли (или стоит на паузе) именно эта очередь.</summary>
        bool IsQueue(IList<Track> queue);

        void TogglePlayPause();
    }
}
