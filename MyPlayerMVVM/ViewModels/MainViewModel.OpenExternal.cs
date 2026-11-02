using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Открытие файлов «снаружи»: двойной клик по mp3 в проводнике, «Открыть с помощью», перетаскивание на exe.
    /// Файлы добавляются в библиотеку (если их там не было) и первый из них сразу играет.
    /// </summary>
    public partial class MainViewModel
    {
        public async Task OpenExternalAsync(string[] paths)
        {
            string[] files = paths
                .Where(p => AudioExtensions.Contains(Path.GetExtension(p)) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0) return;

            // Библиотека и состояние должны закончить восстановление, иначе сохранённое затрёт то, что мы добавим.
            while (!_stateReady || IsLoading)
                await Task.Delay(50);

            IsLoading = true;
            LoadStatusText = "Загрузка…";
            try
            {
                string[] fresh = files
                    .Where(f => !_allTracks.Any(t => string.Equals(t.FilePath, f, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (fresh.Length > 0)
                    await LoadFilesAsync(fresh);
            }
            catch
            {
                // Файл мог пропасть — играем то, что есть.
            }
            finally
            {
                IsLoading = false;
                LoadStatusText = string.Empty;
            }

            var track = _allTracks.FirstOrDefault(t => string.Equals(t.FilePath, files[0], StringComparison.OrdinalIgnoreCase));
            if (track == null) return;

            _queueAlbum = null;   // очередь — основной список
            PlayTrack(track);
        }
    }
}
