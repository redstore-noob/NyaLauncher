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
    private PersonalizationWindow? _personalization;

    public event EventHandler<PersonalizationResult>? PersonalizationSaved;

    public SettingsHubPage()
    {
        InitializeComponent();
        LegacySettingsHost.Content = new SettingsPage();
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
        _personalization?.Reload(storageDirectory);
    }
}
