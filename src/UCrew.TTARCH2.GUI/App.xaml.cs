using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace UCrew.TTARCH2.GUI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show(
            e.Exception.ToString(),
            "UCrew TTARCH2 Studio - Açılış Hatası",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteCrashLog(exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "UCrew.TTARCH2.GUI.crash.log");
            StringBuilder text = new();
            text.AppendLine(DateTime.Now.ToString("O"));
            text.AppendLine(exception.ToString());
            text.AppendLine(new string('-', 80));
            File.AppendAllText(logPath, text.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Crash reporting must never hide the original failure.
        }
    }
}
