using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MyPlayer_NET9.Services.Persistence
{
    /// <summary>
    /// Чтение и запись файлов состояния. Не знает ничего про ViewModel и UI.
    ///
    /// Надёжность:
    ///  • запись атомарная: сначала во временный файл, потом подмена (File.Replace) — если питание пропало
    ///    посреди записи, остаётся целый старый файл, а не половина нового;
    ///  • перед подменой предыдущая версия сохраняется как *.bak — если основной файл всё же оказался повреждён,
    ///    читается резервная копия;
    ///  • неизменившееся состояние на диск не пишется (автосохранение каждые несколько секунд не изнашивает флешку);
    ///  • любая ошибка ввода-вывода гасится и пишется в отладочный вывод — сбой сохранения не должен ронять плеер.
    /// </summary>
    public sealed class StateStore
    {
        private const string LibraryFileName = "library.json";
        private const string StateFileName = "state.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true,
            // Кириллица в названиях остаётся читаемой, а не Да…
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _libraryPath;
        private readonly string _statePath;
        private readonly object _ioLock = new object();

        private string? _lastStateJson;
        private long _lastGeneration;
        private HashSet<string> _libraryCoverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CoverStore Covers { get; }

        public StateStore(string directory)
        {
            _libraryPath = Path.Combine(directory, LibraryFileName);
            _statePath = Path.Combine(directory, StateFileName);
            Covers = new CoverStore(Path.Combine(directory, "covers"));
        }

        // ===================== Чтение =====================

        public StateFile? LoadState() => Read<StateFile>(_statePath);

        public LibraryFile? LoadLibrary()
        {
            var library = Read<LibraryFile>(_libraryPath);
            if (library != null)
                _libraryCoverNames = CoverNamesOf(library);
            return library;
        }

        private static T? Read<T>(string path) where T : class
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;

                    using var stream = File.OpenRead(candidate);
                    var value = JsonSerializer.Deserialize<T>(stream, JsonOptions);
                    if (value != null) return value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StateStore.Read({Path.GetFileName(candidate)}): {ex.Message}");
                }
            }
            return null;
        }

        // ===================== Запись =====================

        /// <summary>
        /// Записывает состояние (и, если передана, библиотеку). Вызывается в фоновом потоке или при закрытии окна.
        /// <paramref name="generation"/> растёт с каждым снимком: если поздно запущенное старое сохранение
        /// догнало более новое, оно ничего не запишет и не затрёт свежие данные.
        /// Возвращает true, если всё, что нужно было записать, записано.
        /// </summary>
        public bool Save(StateFile state, LibraryFile? library, long generation)
        {
            lock (_ioLock)
            {
                if (generation < _lastGeneration)
                    return library == null;

                try
                {
                    bool libraryWritten = false;

                    if (library != null)
                    {
                        foreach (var record in library.Tracks)
                            record.Cover = Covers.Save(record.CoverImage);

                        WriteAtomic(_libraryPath, JsonSerializer.Serialize(library, JsonOptions));
                        _libraryCoverNames = CoverNamesOf(library);
                        libraryWritten = true;
                    }

                    foreach (var playlist in state.Playlists)
                        playlist.Cover = Covers.Save(playlist.CoverImage);

                    string json = JsonSerializer.Serialize(state, JsonOptions);
                    if (json != _lastStateJson)
                    {
                        WriteAtomic(_statePath, json);
                        _lastStateJson = json;
                    }

                    _lastGeneration = generation;

                    // Библиотека переписана — самое время убрать обложки, на которые больше никто не ссылается.
                    if (libraryWritten)
                    {
                        var keep = new HashSet<string>(_libraryCoverNames, StringComparer.OrdinalIgnoreCase);
                        foreach (var playlist in state.Playlists)
                            if (!string.IsNullOrEmpty(playlist.Cover)) keep.Add(playlist.Cover);
                        Covers.DeleteExcept(keep);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StateStore.Save: {ex.Message}");
                    return false;
                }
            }
        }

        private static void WriteAtomic(string path, string json)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(path))
                File.Replace(temp, path, path + ".bak");
            else
                File.Move(temp, path);
        }

        private static HashSet<string> CoverNamesOf(LibraryFile library) =>
            new HashSet<string>(
                library.Tracks.Select(t => t.Cover).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!),
                StringComparer.OrdinalIgnoreCase);
    }
}
