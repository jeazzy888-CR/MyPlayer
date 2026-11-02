using System;
using System.IO;

namespace MyPlayer_NET9.Services.Persistence
{
    /// <summary>
    /// Где живут данные плеера (состояние, библиотека, обложки). Одно место решает судьбу обеих версий приложения:
    ///
    ///  • Портативная версия — рядом с MyPlayer.exe лежит файл-маркер <c>portable.flag</c>.
    ///    Данные хранятся в папке <c>Data</c> прямо рядом с программой: скопировал папку на флешку — всё поехало с ней,
    ///    ничего не остаётся в системе.
    ///  • Обычная версия (MSI) — маркера нет. Данные в <c>%AppData%\MyPlayer</c> (у каждого пользователя Windows свои),
    ///    а папка установки (Program Files) остаётся только для чтения, как и положено.
    ///
    /// Если портативная папка недоступна для записи (например, программа лежит на защищённом от записи носителе),
    /// тихо откатываемся на %AppData% — лучше сохранять в системе, чем не сохранять вообще.
    /// </summary>
    public static class AppPaths
    {
        public const string PortableMarkerFileName = "portable.flag";

        /// <summary>Включён портативный режим (данные рядом с программой).</summary>
        public static bool IsPortable { get; }

        /// <summary>Папка с данными плеера. Гарантированно существует.</summary>
        public static string DataDirectory { get; }

        static AppPaths()
        {
            string? directory = null;
            bool portable = false;

            string baseDirectory = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(baseDirectory, PortableMarkerFileName)))
            {
                string candidate = Path.Combine(baseDirectory, "Data");
                if (TryPrepareWritableDirectory(candidate))
                {
                    directory = candidate;
                    portable = true;
                }
            }

            if (directory == null)
            {
                directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MyPlayer");
                TryPrepareWritableDirectory(directory);
            }

            DataDirectory = directory;
            IsPortable = portable;
        }

        /// <summary>Создаёт папку и проверяет, что в неё можно писать.</summary>
        private static bool TryPrepareWritableDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);

                string probe = Path.Combine(path, ".write-test");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
