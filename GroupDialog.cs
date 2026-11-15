using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RxNeL.Social;

namespace RxNeL;

/// <summary>新建群聊：勾选在线好友 + 起个名字。</summary>
public sealed class GroupDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly List<CheckBox> _checks = new();

    public string GroupName { get; private set; } = "";
    public List<string> SelectedPeerIds { get; } = new();

    public GroupDialog(IReadOnlyList<PeerInfo> peers)
    {
        Title = "新建群聊";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["SurfaceBrush"];

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "新建群聊",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"]
        });

        _nameBox = new TextBox
        {
            Margin = new Thickness(0, 10, 0, 10),
            Height = 30,
            Style = (Style)Application.Current.Resources["InputBox"],
            Text = "群聊 " + System.DateTime.Now.ToString("HH:mm")
        };

        root.Children.Add(_nameBox);

        foreach (var peer in peers)
        {
            var check = new CheckBox
            {
                Content = $"{peer.Name}（{peer.PresenceText}）",
                Style = (Style)Application.Current.Resources["InputCheck"],
                Tag = peer.Id,
                Margin = new Thickness(0, 2, 0, 2)
            };

            _checks.Add(check);
            root.Children.Add(check);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button
        {
            Content = "取消",
            Style = (Style)Application.Current.Resources["GhostButton"],
            Height = 30,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 8, 0)
        };

        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var ok = new Button
        {
            Content = "创建",
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            Height = 30,
            MinWidth = 76
        };

        ok.Click += (_, _) =>
        {
            GroupName = string.IsNullOrWhiteSpace(_nameBox.Text) ? "群聊" : _nameBox.Text.Trim();

            SelectedPeerIds.Clear();
            SelectedPeerIds.AddRange(_checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag));

            DialogResult = true;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        root.Children.Add(buttons);
        Content = root;
    }
}
