using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NyaLauncher.Avalonia.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        // ObservableCollection 绑定：增删账号时 ItemsControl 自动更新，无需手动刷新。
        AccountList.ItemsSource = AccountStore.Current;
        AccountLoginOverlay.AccountAdded += OnAccountAdded;
    }

    private void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        AccountLoginOverlay.Show();
    }

    private void OnAccountAdded(object? sender, LaunchAccount account)
    {
        AccountHintText.Text = $"已添加账号：{account.DisplayName}";
    }

    private void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is LaunchAccount account)
        {
            AccountStore.MoveToTop(account);
            AccountHintText.Text = $"已设为默认：{account.DisplayName}";
        }
    }

    private void OnDeleteAccountClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is LaunchAccount account)
        {
            AccountStore.Remove(account);
            AccountHintText.Text = AccountStore.Current.Count == 0
                ? $"已删除账号：{account.DisplayName}（账号列表已清空）"
                : $"已删除账号：{account.DisplayName}";
        }
    }
}
