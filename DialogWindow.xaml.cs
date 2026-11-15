using System.Windows;

namespace RxNeL;

/// <summary>主题化的应用内对话框，替代系统 MessageBox（后者是 Windows 原生灰色弹框，和 UI 不搭）。</summary>
public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    public void Setup(string title, string message, bool confirm, bool error)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkButton.Content = confirm ? "确定" : "知道了";

        if (error)
        {
            TitleText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            OkButton.Style = (Style)FindResource("GhostButton");
        }

        Loaded += (_, _) => OkButton.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public static class AppDialog
{
    public static void Info(Window? owner, string title, string message) =>
        Show(owner, title, message, confirm: false, error: false);

    public static void Error(Window? owner, string title, string message) =>
        Show(owner, title, message, confirm: false, error: true);

    public static bool Confirm(Window? owner, string title, string message) =>
        Show(owner, title, message, confirm: true, error: false);

    private static bool Show(Window? owner, string title, string message, bool confirm, bool error)
    {
        var dialog = new DialogWindow();

        if (owner is not null && owner.IsVisible)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.Setup(title, message, confirm, error);

        return dialog.ShowDialog() == true;
    }
}
