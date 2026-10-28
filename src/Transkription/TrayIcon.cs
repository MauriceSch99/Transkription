using System.Windows;
using Forms = System.Windows.Forms;

namespace Transkription;

/// <summary>Symbol im Infobereich der Taskleiste, damit das Programm im Hintergrund weiterläuft.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;

    public TrayIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!;
        using (var stream = res.Stream)
            _image = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);

        var menu = new Forms.ContextMenuStrip();
        var open = menu.Items.Add("Transkription öffnen", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Wiedergabe / Pause", null, (_, _) => PlayPauseRequested?.Invoke());
        menu.Items.Add("Einstellungen…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "Transkription",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public event Action? OpenRequested;
    public event Action? PlayPauseRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public void SetText(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
