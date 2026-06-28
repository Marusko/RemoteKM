using System.Windows;

namespace RemoteKm.Host.Windows;

public enum MessageSeverity { Info, Warning, Error }

/// <summary>A small, styled, single-button (OK) message dialog — replaces WPF MessageBox.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, MessageSeverity severity)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        Title = "RemoteKM";
        TitleText.Text = title;
        MessageText.Text = message;

        var key = severity == MessageSeverity.Info ? "RkAccentBrush" : "RkDangerBrush";
        if (TryFindResource(key) is System.Windows.Media.Brush brush)
            SeverityDot.Fill = brush;
    }

    /// <summary>Shows the dialog modally.</summary>
    public static void Show(Window? owner, string title, string message,
        MessageSeverity severity = MessageSeverity.Info)
    {
        var dlg = new MessageDialog(title, message, severity);
        if (owner is not null)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }

    private void OnOk(object sender, RoutedEventArgs e) => Close();
}
