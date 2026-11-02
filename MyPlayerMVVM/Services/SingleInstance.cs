using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyPlayer_NET9.Services
{
    /// <summary>
    /// Один экземпляр плеера. Двойной клик по mp3 при уже запущенном плеере не открывает вторую копию:
    /// второй процесс передаёт пути файлов первому (через именованный канал) и завершается.
    /// </summary>
    public static class SingleInstance
    {
        private static readonly string Id = "MyPlayer.Desktop." + Environment.UserName;
        private static Mutex? _mutex;
        private static Action<string[]>? _handler;
        private static readonly List<string[]> _pending = new();
        private static readonly object _lock = new();

        /// <summary>true — мы первый экземпляр и продолжаем запуск; false — всё передано работающему, надо выходить.</summary>
        public static bool TryBecomePrimary(string[] args)
        {
            _mutex = new Mutex(true, @"Local\" + Id, out bool createdNew);

            string[] paths = args
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => { try { return Path.GetFullPath(a); } catch { return a; } })
                .ToArray();

            if (createdNew)
            {
                if (paths.Length > 0) _pending.Add(paths);
                _ = Task.Run(ListenAsync);
                return true;
            }

            try
            {
                using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
                client.Connect(3000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                foreach (string p in paths) writer.WriteLine(p);
                writer.Flush();
                return false;
            }
            catch
            {
                // Первый экземпляр не отвечает (завис) — лучше открыться самим, чем не открыться вовсе.
                return true;
            }
        }

        /// <summary>Окно подписывается на файлы, пришедшие при запуске и позже. Вызывается в потоке интерфейса.</summary>
        public static void Attach(Action<string[]> handler)
        {
            string[][] buffered;
            lock (_lock)
            {
                _handler = handler;
                buffered = _pending.ToArray();
                _pending.Clear();
            }
            foreach (var paths in buffered) handler(paths);
        }

        private static async Task ListenAsync()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var list = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                        if (line.Length > 0) list.Add(line);

                    Action<string[]>? handler;
                    lock (_lock)
                    {
                        handler = _handler;
                        if (handler == null) _pending.Add(list.ToArray());
                    }
                    handler?.Invoke(list.ToArray());
                }
                catch
                {
                    await Task.Delay(200);
                }
            }
        }
    }
}
