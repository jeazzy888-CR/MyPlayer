using System.Windows;
using System.Windows.Controls;
using MyPlayer_NET9.Models;

namespace MyPlayer_NET9.Views
{
    /// <summary>
    /// Выбирает стиль контейнера строки списка плейлиста: у обычных треков — стиль как в альбоме
    /// (оранжевая подсветка играющего), у служебной строки «выбери трек» — зелёная подсветка.
    /// Стили подставляются из XAML, здесь только выбор по типу элемента.
    /// </summary>
    public class PlaylistRowStyleSelector : StyleSelector
    {
        public Style? TrackStyle { get; set; }
        public Style? AddRowStyle { get; set; }

        public override Style? SelectStyle(object item, DependencyObject container)
            => item is AddTrackRow ? AddRowStyle : TrackStyle;
    }
}
