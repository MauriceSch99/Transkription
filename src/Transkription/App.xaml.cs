using System.Windows;
using System.Windows.Threading;
using Transkription.Settings;

namespace Transkription;

public partial class App : Application
{
    private const string MutexName = "Transkription.Einzelinstanz";
    private const string ShowEventName = "Transkription.Anzeigen";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    private static string PendingFile => Path.Combine(AppSettings.Folder, "zu-oeffnen.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var files = e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var minimized = e.Args.Contains("--minimized");

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Programm läuft schon: Dateien übergeben und vorhandenes Fenster nach vorne holen.
            try
            {
                if (files.Count > 0)
                {
                    Directory.CreateDirectory(AppSettings.Folder);
                    File.AppendAllLines(PendingFile, files.Select(Path.GetFullPath));
                }
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // Andere Instanz nicht erreichbar.
            }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        var window = new MainWindow(AppSettings.Load());
        MainWindow = window;
        window.Initialize(showWindow: !minimized, files);
        SessionEnding += (_, _) => window.ExitApplication();

        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => window.OpenFromOtherInstance(TakePendingFiles()));
        })
        {
            IsBackground = true,
            Name = "Einzelinstanz",
        };
        listener.Start();
    }

    private static List<string> TakePendingFiles()
    {
        try
        {
            if (!File.Exists(PendingFile)) return new List<string>();
            var lines = File.ReadAllLines(PendingFile).Where(l => l.Length > 0).ToList();
            File.Delete(PendingFile);
            return lines;
        }
        catch
        {
            return new List<string>();
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten:\n\n" + e.Exception.Message,
            "Transkription", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
