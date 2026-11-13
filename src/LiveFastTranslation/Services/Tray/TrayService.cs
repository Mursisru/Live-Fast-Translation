using LiveFastTranslation.Services.Infrastructure;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace LiveFastTranslation.Services.Tray;

/// <summary>Notification-area icon and context menu. Must be created on the WPF UI thread.</summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _aiModeItem;
    private readonly Drawing.Icon _icon;
    private readonly Stream? _iconStream;
    private bool _disposed;

    public TrayService()
    {
        _icon = LoadIcon(out _iconStream);
        _aiModeItem = new Forms.ToolStripMenuItem("AI-режим")
        {
            CheckOnClick = false
        };

        var menu = BuildMenu();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Live-Fast Translation",
            ContextMenuStrip = menu,
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => TranslateSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? TranslateSelectionRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ToggleAiModeRequested;

    public event EventHandler? ExitRequested;

    public bool NotificationsEnabled { get; set; } = true;

    public bool IsVisible => _notifyIcon.Visible;

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void UpdateAiMode(bool enabled)
    {
        _aiModeItem.Checked = enabled;
    }

    public void UpdateHotkeyHint(string hotkey)
    {
        var text = string.IsNullOrWhiteSpace(hotkey)
            ? "Live-Fast Translation"
            : $"Live-Fast Translation — {hotkey}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Notify(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (!NotificationsEnabled || !_notifyIcon.Visible || _disposed)
        {
            return;
        }

        try
        {
            _notifyIcon.ShowBalloonTip(3500, title, message, icon);
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Не удалось показать уведомление в трее: {exception.Message}");
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9F)
        };

        var translateItem = new Forms.ToolStripMenuItem("Перевести выделенное")
        {
            Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold)
        };
        translateItem.Click += (_, _) => TranslateSelectionRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new Forms.ToolStripMenuItem("Настройки…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _aiModeItem.Click += (_, _) => ToggleAiModeRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(translateItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_aiModeItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private static Drawing.Icon LoadIcon(out Stream? iconStream)
    {
        iconStream = null;
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource?.Stream is not null)
            {
                var buffer = new MemoryStream();
                using (resource.Stream)
                {
                    resource.Stream.CopyTo(buffer);
                }

                buffer.Position = 0;
                iconStream = buffer;
                return new Drawing.Icon(buffer);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Не удалось загрузить иконку приложения: {exception.Message}");
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        _iconStream?.Dispose();
    }
}
