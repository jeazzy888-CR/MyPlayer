using System;
using System.Linq;
using Microsoft.Win32;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// «Добавить песни»: обычное окно выбора файлов — в нём видны сами песни, можно выбрать одну,
    /// несколько (Ctrl/Shift) или все сразу (Ctrl+A). Загрузка идёт тем же фоновым механизмом, что и для папки.
    /// </summary>
    public partial class MainViewModel
    {
        private RelayCommand? _openFilesCommand;

        public RelayCommand OpenFilesCommand =>
            _openFilesCommand ??= new RelayCommand(OpenFilesAndLoadTracks, _ => !IsLoading);

        private async void OpenFilesAndLoadTracks(object? parameter)
        {
            var extensions = string.Join(";", AudioExtensions.Select(e => "*" + e));

            var dialog = new OpenFileDialog
            {
                Title = "Выберите песни",
                Multiselect = true,
                CheckFileExists = true,
                Filter = $"Музыка|{extensions}|Все файлы|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            // Из «Все файлы» может прилететь что угодно — оставляем только поддерживаемые форматы.
            string[] files = dialog.FileNames
                .Where(f => AudioExtensions.Contains(System.IO.Path.GetExtension(f)))
                .ToArray();
            if (files.Length == 0) return;

            IsLoading = true;
            LoadStatusText = "Загрузка…";

            try
            {
                await LoadFilesAsync(files);
            }
            catch
            {
                // Файл мог пропасть посреди загрузки — то, что успело добавиться, остаётся.
            }
            finally
            {
                IsLoading = false;
                LoadStatusText = string.Empty;
            }
        }
    }
}
