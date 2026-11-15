using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using RxNeL.Accounts;

namespace RxNeL.Views;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();

        AccountVault.Load();
        AccountList.ItemsSource = AccountVault.Accounts;

        UpdateListStatus();
    }

    private long _avatarUserId;
    private bool _showPassword;

    private async void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountList.SelectedItem is not AccountRecord record)
        {
            AvatarImage.Source = null;
            return;
        }

        UpdatePasswordText(record);

        AvatarStatus.Text = $"正在加载 {record.Username} 的头像…";
        await LoadAvatarAsync(record);
    }

    private void UpdatePasswordText(AccountRecord? record)
    {
        if (record is null)
        {
            SelectedPasswordText.Text = "密码：—";
            return;
        }

        SelectedPasswordText.Text = "密码：" + record.PasswordDisplay;
    }

    private async Task LoadAvatarAsync(AccountRecord record)
    {
        if (record.UserId <= 0)
        {
            AvatarImage.Source = null;
            AvatarStatus.Text = "这条记录没有 ID，点「重新验证用户名」补全后再看头像";
            return;
        }

        _avatarUserId = record.UserId;
        var bytes = await AccountVault_PreviewAsync(record.UserId);

        if (bytes is null || _avatarUserId != record.UserId)
        {
            AvatarStatus.Text = "头像获取失败（网络或接口限制）";
            return;
        }

        try
        {
            var bitmap = new BitmapImage();

            using (var stream = new System.IO.MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            AvatarImage.Source = bitmap;
            AvatarStatus.Text = $"{record.DisplayName ?? record.Username}（ID {record.UserId}）";
        }
        catch (Exception ex)
        {
            AvatarStatus.Text = $"头像解析失败：{ex.Message}";
        }
    }

    private static async Task<byte[]?> AccountVault_PreviewAsync(long userId) =>
        await AvatarClient.GetAvatarBytesAsync(userId, 352);

    private void UpdateListStatus()
    {
        ListStatus.Text = AccountVault.Accounts.Count == 0
            ? "暂无记录"
            : $"共 {AccountVault.Accounts.Count} 个账号";

        ListStatus.ToolTip = $"数据文件：{AccountVault.FilePath}（密码为 DPAPI 密文）";
    }

    /// <summary>用户名验真通过才保存；密码不做任何验证。</summary>
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            AddStatus.Text = "先填 Roblox 用户名";
            return;
        }

        AddStatus.Text = "正在核对用户名…";

        var result = await RobloxAuth.LookupAsync(username);

        if (!result.Exists)
        {
            AddStatus.Text = result.Message + "（用户名未通过，不添加）";
            return;
        }

        // 用接口返回的真实用户名，避免大小写/空格写法差异
        var realName = string.IsNullOrWhiteSpace(result.LoginName) ? username : result.LoginName!;
        var existed = AccountVault.Find(realName) is not null;

        AccountVault.AddOrUpdate(
            realName,
            PasswordBox.Password,
            "用户名已验证",
            result.UserId,
            result.DisplayName);

        PasswordBox.Clear();
        UpdateListStatus();
        AccountList.Items.Refresh();

        AddStatus.Text = $"{(existed ? "已更新" : "已保存")}：{realName}（{result.DisplayName}，ID {result.UserId}）" +
                         (string.IsNullOrEmpty(PasswordBox.Password) ? "，未填密码" : "，密码已加密保存");

        Runtime.Status(AddStatus.Text);
        Logger.Info($"账号记录：{realName}（ID {result.UserId}）");
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not AccountRecord record)
        {
            ListStatus.Text = "先选中一行";
            return;
        }

        ListStatus.Text = $"正在核对 {record.Username}…";

        var result = await RobloxAuth.LookupAsync(record.Username);

        record.Status = result.Exists ? "用户名已验证" : "用户名不存在";
        record.UserId = result.UserId > 0 ? result.UserId : record.UserId;
        record.DisplayName = result.DisplayName ?? record.DisplayName;
        record.UpdatedAt = DateTimeOffset.Now;

        AccountVault.Save();
        AccountList.Items.Refresh();

        ListStatus.Text = result.Message;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not AccountRecord record)
        {
            ListStatus.Text = "先选中一行";
            return;
        }

        if (!AppDialog.Confirm(Window.GetWindow(this), "删除账号", $"删除账号记录 {record.Username}？"))
            return;

        AccountVault.Remove(record);
        UpdateListStatus();
        ListStatus.Text = "已删除";
    }

    /// <summary>显示/隐藏所有账号的密码（明文只在内存里，关掉即清空）。</summary>
    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        AccountRecord.ShowPlain = _showPassword;

        if (_showPassword)
        {
            foreach (var record in AccountVault.Accounts)
                record.PlainPassword = AccountVault.GetPassword(record);
        }
        else
        {
            foreach (var record in AccountVault.Accounts)
                record.PlainPassword = null;
        }

        TogglePasswordButton.Content = _showPassword ? "隐藏密码" : "显示密码";
        AccountList.Items.Refresh();
        UpdatePasswordText(AccountList.SelectedItem as AccountRecord);

        ListStatus.Text = _showPassword ? "明文显示中" : "已隐藏";
    }

    /// <summary>截图/回归用：选中某一行。</summary>
    public void SelectRowForTest(int index)
    {
        if (index >= 0 && index < AccountVault.Accounts.Count)
            AccountList.SelectedIndex = index;
    }

    /// <summary>截图/回归用：等同于点一下「显示密码」。</summary>
    public void TogglePasswordForTest() => TogglePassword_Click(this, new RoutedEventArgs());

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not AccountRecord record)
        {
            ListStatus.Text = "先选中一行";
            return;
        }

        var password = AccountVault.GetPassword(record);

        if (string.IsNullOrEmpty(password))
        {
            ListStatus.Text = "这条记录没有保存密码";
            return;
        }

        Clipboard.SetText(password);
        ListStatus.Text = $"已复制 {record.Username} 的密码";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is AccountRecord record)
            Clipboard.SetText(record.Username);
    }

    private void OpenDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Paths.AppDir);
            Process.Start(new ProcessStartInfo { FileName = Paths.AppDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("打开数据目录失败", ex);
        }
    }
}
