using System.Windows;

namespace RemoteKm.Host.Windows;

/// <summary>A small, styled yes/no confirmation dialog.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText = "Confirm")
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    /// <summary>Shows the dialog modally and returns true if the user confirmed.</summary>
    public static bool Ask(Window? owner, string title, string message, string confirmText = "Confirm")
    {
        var dlg = new ConfirmDialog(title, message, confirmText);
        if (owner is not null)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
