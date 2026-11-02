using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyPlayer_NET9.Models;
using MyPlayer_NET9.ViewModels;

namespace MyPlayer_NET9.Views
{
    /// <summary>
    /// Code-behind содержит ТОЛЬКО то, что невозможно (или неоправданно сложно)
    /// выразить через Binding/Command в MVVM: переключение "страниц" внутри
    /// вложенного Popup-меню Хаоса. (Перетаскивание окна и кнопки свернуть/развернуть/
    /// закрыть живут отдельным модулем — Controls/WindowTitleBar.)
    /// Никакой бизнес-логики здесь нет — она вся в MainViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private string? _pendingArtist;

        public MainWindow()
        {
            InitializeComponent();

            // Модуль плейлистов просит прокрутить список к концу после добавления трека
            // (прокрутка — дело View, ViewModel про ListBox ничего не знает).
            if (DataContext is MainViewModel vm)
            {
                vm.Playlists.ScrollToEndRequested += (_, _) => ScrollPlaylistTracksToEnd();

                // Каждый раз, когда открывается альбом, его список треков начинается с самого верха
                // (а не с того места, где остановились в прошлом альбоме).
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SelectedAlbum) ||
                        (e.PropertyName == nameof(MainViewModel.CurrentScreen) && vm.CurrentScreen == ScreenMode.AlbumDetail))
                        ScrollAlbumTracksToTop();
                };

                // Горячие клавиши, положение окна, восстановление и сохранение состояния (MainWindow.Persistence.cs).
                InitializeServices(vm);
            }
        }

        /// <summary>Сразу и ещё раз после раскладки (список мог только что стать видимым): прокрутка в самое начало.</summary>
        private void ScrollAlbumTracksToTop()
        {
            ResetAlbumScroll();
            Dispatcher.BeginInvoke(new System.Action(ResetAlbumScroll), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ResetAlbumScroll() => FindDescendant<ScrollViewer>(AlbumTracksListBox)?.ScrollToTop();

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T found) return found;
                if (FindDescendant<T>(child) is T nested) return nested;
            }
            return null;
        }

        private void ScrollPlaylistTracksToEnd()
        {
            // Экран только что стал видимым — ждём, пока список разметится.
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (PlaylistTracksListBox.Items.Count > 0)
                    PlaylistTracksListBox.ScrollIntoView(PlaylistTracksListBox.Items[PlaylistTracksListBox.Items.Count - 1]);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===================== Добавление треков =====================

        private void AddTracksButton_Click(object sender, RoutedEventArgs e) =>
            AddTracksPopup.IsOpen = !AddTracksPopup.IsOpen;

        /// <summary>Пункт меню выбран — закрываем список; саму загрузку запускает команда пункта.</summary>
        private void AddTracksItem_Click(object sender, RoutedEventArgs e) =>
            AddTracksPopup.IsOpen = false;

        // ===================== Настройки =====================

        /// <summary>Раздел «Настройки» ещё в разработке — обычное окно сообщения Windows.</summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "🚧 В разработке 2.0...",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ===================== Универсальные Popup-хелперы =====================

        private void TogglePopup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Popup popup)
                popup.IsOpen = !popup.IsOpen;
        }

        private void ClosePopup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Popup popup)
                popup.IsOpen = false;
        }

        private void OpenChaosSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowChaosMenu(ChaosMainMenu);
            ChaosSettingsPopup.IsOpen = true;
        }

        /// <summary>
        /// При каждом закрытии popup'а меню Хаоса возвращаем навигацию
        /// на первый экран, чтобы при следующем открытии не оставалось
        /// "зависшего" под-меню.
        /// </summary>
        private void ChaosSettingsPopup_Closed(object? sender, System.EventArgs e)
        {
            ShowChaosMenu(ChaosMainMenu);
            _pendingArtist = null;
        }

        // ===================== Навигация внутри меню Хаоса =====================

        private void ShowChaosMenu(StackPanel target)
        {
            ChaosMainMenu.Visibility = Visibility.Collapsed;
            ChaosBringUpMenu.Visibility = Visibility.Collapsed;
            ChaosArtistMenu.Visibility = Visibility.Collapsed;
            ChaosArtistCountMenu.Visibility = Visibility.Collapsed;

            target.Visibility = Visibility.Visible;
        }

        private void OpenChaosBringUpMenu_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.SetChaosModeBringUpCommand.Execute(null);

            ShowChaosMenu(ChaosBringUpMenu);
        }

        private void OpenChaosArtistMenu_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.SetChaosModeArtistCommand.Execute(null);

            ShowChaosMenu(ChaosArtistMenu);
        }

        private void BackToChaosMainMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowChaosMenu(ChaosMainMenu);
        }

        private void BackToChaosArtistMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowChaosMenu(ChaosArtistMenu);
        }

        private void SetBringUpCount_Click(object sender, RoutedEventArgs e)
        {
            // Шестерёнка только запоминает настройку. Само перемешивание запускается
            // отдельно, кнопкой "ХАОС".
            if (sender is Button btn && DataContext is MainViewModel vm && int.TryParse(btn.Tag?.ToString(), out int count))
            {
                vm.ChaosCount = count;
                ChaosSettingsPopup.IsOpen = false;
            }
        }

        private void SelectArtist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string artist)
            {
                _pendingArtist = artist;
                SelectedArtistLabel.Text = artist.ToUpperInvariant();
                ShowChaosMenu(ChaosArtistCountMenu);
            }
        }

        private void SetArtistCount_Click(object sender, RoutedEventArgs e)
        {
            // Аналогично: только сохраняем исполнителя и количество, ничего не запускаем.
            if (sender is Button btn && DataContext is MainViewModel vm &&
                int.TryParse(btn.Tag?.ToString(), out int count) &&
                _pendingArtist != null)
            {
                vm.SelectedArtist = _pendingArtist;
                vm.ChaosCount = count;
                ChaosSettingsPopup.IsOpen = false;
            }
        }

        // ===================== Список треков =====================

        /// <summary>
        /// Один клик по строке трека — сразу запускает воспроизведение.
        /// Ищем конкретный ListBoxItem под курсором (а не просто SelectedItem),
        /// чтобы клик по пустому месту списка ничего не запускал.
        /// </summary>
        private void SongsListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
                item.DataContext is Models.Track track &&
                DataContext is MainViewModel vm)
            {
                vm.PlayTrackCommand.Execute(track);
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null && current is not T)
                current = VisualTreeHelper.GetParent(current);

            return current as T;
        }

        /// <summary>
        /// Тот же принцип, что и для основного списка — один клик по треку внутри альбома запускает его,
        /// но очередью становится сам альбом (стрелки и автопереход идут по его трекам).
        /// </summary>
        private void AlbumTracksListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
                item.DataContext is Models.Track track &&
                DataContext is MainViewModel vm)
            {
                vm.PlayAlbumTrackCommand.Execute(track);
            }
        }

        /// <summary>Клик по треку в плейлисте — играем, очередью становится сам плейлист. Зелёная строка и крестик сюда не попадают.</summary>
        private void PlaylistTracksListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
                item.DataContext is Models.Track track &&
                DataContext is MainViewModel vm)
            {
                vm.Playlists.PlayPlaylistTrackCommand.Execute(track);
            }
        }

        /// <summary>Клик по треку на экране «Выберите композицию» — добавляем его в плейлист и возвращаемся в редактор.</summary>
        private void PickerListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
                item.DataContext is Models.Track track &&
                DataContext is MainViewModel vm)
            {
                vm.Playlists.AddPickedTrackCommand.Execute(track);
            }
        }

        // ===================== Перемотка =====================

        /// <summary>
        /// Пользователь взялся за ползунок — ставим плеер на паузу на время
        /// перетаскивания, чтобы не было треска от постоянных скачков позиции.
        /// </summary>
        private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.BeginSeek();
        }

        /// <summary>Ползунок отпущен — возобновляем воспроизведение с новой позиции.</summary>
        private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.EndSeek();
        }
    }
}
