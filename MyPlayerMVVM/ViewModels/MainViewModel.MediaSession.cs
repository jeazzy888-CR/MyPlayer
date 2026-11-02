using System;
using MyPlayer_NET9.Services;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Подключение плеера к системным медиа-элементам Windows: панель «Сейчас играет» (регулятор громкости, экран блокировки),
    /// мультимедийные клавиши и кнопки гарнитур. См. Services/SystemMediaControls.
    /// </summary>
    public partial class MainViewModel
    {
        private SystemMediaControls? _mediaControls;

        /// <summary>Вызывается окном, когда у него появился дескриптор (SourceInitialized).</summary>
        public void AttachMediaControls(IntPtr windowHandle)
        {
            if (_mediaControls != null) return;

            var controls = SystemMediaControls.TryCreate(windowHandle);
            if (controls == null) return;
            _mediaControls = controls;

            controls.PlayRequested += () => { if (!IsPlaying) ExecutePlayPause(null); };
            controls.PauseRequested += () => { if (IsPlaying) ExecutePlayPause(null); };
            controls.NextRequested += () => { if (NextCommand.CanExecute(null)) NextCommand.Execute(null); };
            controls.PreviousRequested += () => { if (PreviousCommand.CanExecute(null)) PreviousCommand.Execute(null); };
            controls.SeekRequested += position =>
            {
                if (CurrentTrack == null || Duration <= TimeSpan.Zero) return;
                if (_pendingRestorePosition != null) _pendingRestorePosition = position;
                Position = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
            };

            PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(CurrentTrack):
                        SyncMediaTrack();
                        break;
                    case nameof(IsPlaying):
                        _mediaControls?.SetPlaying(IsPlaying);
                        SyncMediaTimeline();
                        break;
                    case nameof(Position):
                    case nameof(Duration):
                        SyncMediaTimeline();
                        break;
                }
            };

            SyncMediaTrack();
        }

        private void SyncMediaTrack()
        {
            if (_mediaControls == null) return;

            var track = CurrentTrack;
            if (track == null)
            {
                _mediaControls.Clear();
                return;
            }

            _mediaControls.SetTrack(track.Title, track.Artist, track.Album, track.Cover);
            _mediaControls.SetPlaying(IsPlaying);
            SyncMediaTimeline();
        }

        private void SyncMediaTimeline()
        {
            if (_mediaControls == null || CurrentTrack == null) return;
            _mediaControls.SetTimeline(Position, Duration, IsPlaying);
        }
    }
}
