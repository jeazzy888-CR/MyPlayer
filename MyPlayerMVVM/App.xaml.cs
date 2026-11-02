using System.Windows;

namespace MyPlayer_NET9
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Плеер уже запущен — файлы из командной строки уходят ему, а мы закрываемся.
            if (!Services.SingleInstance.TryBecomePrimary(e.Args))
            {
                Shutdown();
                return;
            }

            // До создания окна: Windows должна знать, что это отдельное приложение-плеер.
            Services.AppIdentity.Apply();
            base.OnStartup(e);
        }
    }
}
