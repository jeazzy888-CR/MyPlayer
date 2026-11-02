using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using MyPlayer_NET9.Models;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// «Остальные песни»: во вкладке «Альбомы» показываются только альбомы от двух треков, а все одиночки
    /// собираются в одну карточку в самом низу (см. Models.OtherSongsAlbum).
    /// </summary>
    public partial class MainViewModel
    {
        /// <summary>Сколько треков минимум должно быть, чтобы группа считалась альбомом.</summary>
        public const int MinTracksInAlbum = 2;

        /// <summary>Карточка «Остальные песни»; null, если одиночных треков нет.</summary>
        public OtherSongsAlbum? OtherSongs { get; private set; }

        private static OtherSongsAlbum? BuildOtherSongs(List<Track> singles)
        {
            if (singles.Count == 0) return null;

            var ordered = singles
                .OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            // Для стопки берём разные обложки (по порядку списка); если их меньше трёх — повторяем имеющиеся,
            // чтобы стопка всё равно выглядела стопкой. Совсем без обложек — серые заглушки.
            var distinct = new List<BitmapImage>();
            foreach (var track in ordered)
            {
                if (track.Cover != null && !distinct.Contains(track.Cover))
                    distinct.Add(track.Cover);
                if (distinct.Count == 3) break;
            }

            BitmapImage? Layer(int i) => distinct.Count == 0 ? null : distinct[i % distinct.Count];

            return new OtherSongsAlbum
            {
                Title = OtherSongsAlbum.DisplayTitle,
                Artist = string.Empty, // исполнитель не подписывается
                Tracks = ordered,
                StackFront = Layer(0),
                StackMiddle = Layer(1),
                StackBack = Layer(2),
                Cover = Layer(0)
            };
        }
    }
}
