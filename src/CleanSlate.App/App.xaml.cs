using System.IO;
using System.Windows;
using System.Windows.Threading;
using CleanSlate.Safety;

namespace CleanSlate.App;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanSlate", "logs", "startup.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF: if the only window (disclaimer) closes, OnLastWindowClose would exit the app
        // before MainWindow.Show(). Keep the process alive explicitly.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            Log("startup");
            var settings = Disclaimer.DefaultSettingsPath();
            Log("settings=" + settings + " accepted=" + Disclaimer.IsAccepted(settings));

            if (!Disclaimer.IsAccepted(settings))
            {
                var disclaimer = new DisclaimerWindow();
                Log("show disclaimer");
                var accepted = disclaimer.ShowDialog();
                Log("disclaimer result=" + accepted);
                if (accepted != true)
                {
                    Log("user declined — shutdown");
                    Shutdown();
                    return;
                }
            }

            var window = new MainWindow();
            Log("main window constructed");
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            Log("main window shown title=" + window.Title);
        }
        catch (Exception ex)
        {
            Log("FATAL " + ex);
            MessageBox.Show(
                "CleanSlate 启动失败：\n" + ex.Message + "\n\n" + ex.GetType().Name + "\n" + ex.StackTrace,
                "CleanSlate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("ui-ex " + e.Exception);
        MessageBox.Show("发生未处理错误：\n" + e.Exception.Message, "CleanSlate", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log("fatal-ex " + ex);
            MessageBox.Show("严重错误：\n" + ex.Message, "CleanSlate", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(LogPath, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
