using System;
using Avalonia.Controls;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Pages;

public enum SettingsSection
{
    Launcher,
    Personalization
}

public partial class SettingsHubPage : UserControl
{
    private readonly SettingsPage _launcherSettings;
    private PersonalizationWindow? _personalization;

    public event EventHandler<PersonalizationResult>? PersonalizationSaved;

    /// <summary>转发自 <see cref="SettingsPage.AccountManageRequested"/>，供主窗口跳转到账户管理页面。</summary>
    public event EventHandler? AccountManageRequested;

    public SettingsHubPage()
    {
        InitializeComponent();
        _launcherSettings = new SettingsPage();
        _launcherSettings.AccountManageRequested += (_, _) =>
            AccountManageRequested?.Invoke(this, EventArgs.Empty);
        LegacySettingsHost.Content = _launcherSettings;
    }

    public SettingsHubPage(
        FeatureAreaRegistry registry,
        string storageDirectory) : this()
    {
        _personalization = new PersonalizationWindow(registry, storageDirectory);
        _personalization.Saved += (_, result) =>
            PersonalizationSaved?.Invoke(this, result);
        PersonalizationHost.Content = _personalization;
    }

    public void SelectSection(SettingsSection section)
    {
        SettingsTabs.SelectedIndex = section == SettingsSection.Personalization ? 1 : 0;
    }

    public void ReloadPersonalization(string storageDirectory)
    {
        _launcherSettings.ReloadMemorySettings();
        _personalization?.Reload(storageDirectory);
    }
}
