using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace MyPlayer_NET9.Services.Persistence
{
    /// <summary>
    /// Папка с обложками на диске: каждая уникальная картинка — один файл JPEG, имя файла = хэш содержимого.
    /// Благодаря этому 12 треков одного альбома ссылаются на один файл, а повторное сохранение ничего не пишет.
    /// Ничего не знает про ViewModel: картинка на входе — имя файла на выходе, и наоборот.
    ///
    /// Save вызывается только из StateStore под его замком (из одного потока за раз), Load потокобезопасен.
    /// </summary>
    public sealed class CoverStore
    {
        private readonly string _directory;

        /// <summary>
        /// Картинка → имя её файла. Если картинка уже сохранялась или была загружена отсюда — повторно не кодируем.
        /// Таблица «слабая»: картинки из неё свободно собираются сборщиком мусора.
        /// </summary>
        private readonly ConditionalWeakTable<BitmapImage, string> _names = new ConditionalWeakTable<BitmapImage, string>();

        private readonly ConcurrentDictionary<string, Lazy<BitmapImage?>> _loaded =
            new ConcurrentDictionary<string, Lazy<BitmapImage?>>(StringComparer.OrdinalIgnoreCase);

        public CoverStore(string directory)
        {
            _directory = directory;
        }

        /// <summary>Сохраняет картинку (если ещё не сохранена) и возвращает имя файла. null — картинки нет или сохранить не удалось.</summary>
        public string? Save(BitmapImage? image)
        {
            if (image == null) return null;

            if (_names.TryGetValue(image, out string? known))
                return known;

            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(image));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                byte[] bytes = stream.ToArray();

                string name = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()[..20] + ".jpg";
                string path = Path.Combine(_directory, name);

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(_directory);
                    string temp = path + ".tmp";
                    File.WriteAllBytes(temp, bytes);
                    File.Move(temp, path, overwrite: true);
                }

                _names.AddOrUpdate(image, name);
                return name;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoverStore.Save: {ex.Message}");
                return null;
            }
        }

        /// <summary>Загружает обложку по имени файла (замороженная, можно создавать в фоновом потоке). Одна и та же картинка — один и тот же объект.</summary>
        public BitmapImage? Load(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // Имя приходит из файла на диске — отсекаем любые пути, оставляем только имя.
            string safeName = Path.GetFileName(name);
            if (safeName.Length == 0) return null;

            return _loaded.GetOrAdd(safeName, n => new Lazy<BitmapImage?>(() => Decode(n))).Value;
        }

        private BitmapImage? Decode(string name)
        {
            try
            {
                string path = Path.Combine(_directory, name);
                if (!File.Exists(path)) return null;

                using var stream = new MemoryStream(File.ReadAllBytes(path));

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                _names.AddOrUpdate(image, name);
                return image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Удаляет файлы обложек, которых нет в списке нужных (обложки удалённых плейлистов/треков не копятся вечно).</summary>
        public void DeleteExcept(ISet<string> keep)
        {
            try
            {
                if (!Directory.Exists(_directory)) return;

                foreach (string file in Directory.EnumerateFiles(_directory))
                {
                    string name = Path.GetFileName(file);
                    bool isCover = name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
                    bool isLeftover = name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

                    if ((isCover && !keep.Contains(name)) || isLeftover)
                    {
                        try { File.Delete(file); } catch { /* занят/нет прав — удалим в следующий раз */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoverStore.DeleteExcept: {ex.Message}");
            }
        }
    }
}
