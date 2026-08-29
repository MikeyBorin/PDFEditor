using System;
using System.Linq;
using System.Windows;

namespace PDFEditor;

public partial class App : Application
{
    public string? PendingOpenPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal("Dispatcher.UnhandledException", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        var arg = e.Args.FirstOrDefault();
        if (!string.IsNullOrEmpty(arg) && System.IO.File.Exists(arg))
            PendingOpenPath = arg;
    }

    private static void LogFatal(string source, Exception? ex)
    {
        try
        {
            var log = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
            System.IO.File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n\r\n");
        }
        catch { }
    }
}
