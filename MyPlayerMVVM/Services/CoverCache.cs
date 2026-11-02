using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Media.Imaging;

namespace MyPlayer_NET9.Services
{
    /// <summary>
    /// Кэш обложек: одинаковые картинки (по хэшу байтов) декодируются ОДИН раз,
    /// и все треки с такой обложкой получают ссылку на один и тот же BitmapImage.
    /// Раньше каждый трек декодировал свою копию — на 500 треках это сотни мегабайт
    /// неуправляемой памяти под одни и те же пиксели.
    /// Ничего не знает про ViewModel и UI — только байты на входе, картинка на выходе.
    /// </summary>
    public class CoverCache
    {
        /// <summary>
        /// Ширина декодирования. Самое крупное место показа обложки — около 250px,
        /// с запасом на экраны с масштабом DPI хватает 400px.
        /// </summary>
        private const int DecodePixelWidth = 400;

        // Lazy гарантирует: даже если 12 треков одного альбома читаются параллельно,
        // обложка декодируется один раз, а остальные потоки ждут готовый результат.
        private readonly Dictionary<string, Lazy<BitmapImage?>> _cache = new Dictionary<string, Lazy<BitmapImage?>>();
        private readonly object _lock = new object();

        /// <summary>Сколько уникальных обложек сейчас в кэше (удобно смотреть в отладчике).</summary>
        public int Count
        {
            get { lock (_lock) return _cache.Count; }
        }

        /// <summary>
        /// Возвращает готовую (замороженную) картинку для этих байт. Потокобезопасно.
        /// Если такая обложка уже была — вернёт тот же объект. При повреждённых данных — null.
        /// </summary>
        public BitmapImage? GetOrCreate(byte[]? imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return null;

            string key = Convert.ToHexString(SHA1.HashData(imageBytes));

            Lazy<BitmapImage?> entry;
            lock (_lock)
            {
                if (!_cache.TryGetValue(key, out entry!))
                {
                    entry = new Lazy<BitmapImage?>(() => Decode(imageBytes), LazyThreadSafetyMode.ExecutionAndPublication);
                    _cache[key] = entry;
                }
            }

            return entry.Value;
        }

        public void Clear()
        {
            lock (_lock) _cache.Clear();
        }

        private static BitmapImage? Decode(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.DecodePixelWidth = DecodePixelWidth;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
