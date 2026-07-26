using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.ViewModels;
using System.Diagnostics;

namespace StaminaManager.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isSynchronizingControls = true;
    private bool _areControlEventsAttached;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    public SettingsViewModel ViewModel { get; }

    public static bool Not(bool value) => !value;

    private void SettingsPage_Loaded(
        object sender,
        RoutedEventArgs args)
    {
        SynchronizeControls();
        if (_areControlEventsAttached)
        {
            return;
        }

        ThemeToggle.Toggled += ThemeToggle_Toggled;
        BackdropSelector.SelectionChanged +=
            BackdropSelector_SelectionChanged;
        CloseBehaviorSelector.SelectionChanged +=
            CloseBehaviorSelector_SelectionChanged;
        StartupToggle.Toggled += StartupToggle_Toggled;
        NotificationsToggle.Toggled += NotificationsToggle_Toggled;
        NotificationLeadInput.ValueChanged +=
            NotificationLeadInput_ValueChanged;
        _areControlEventsAttached = true;
    }

    private async void ThemeToggle_Toggled(
        object sender,
        RoutedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.SetThemeAsync(
                ThemeToggle.IsOn ? AppTheme.Dark : AppTheme.Light);
        });
    }

    private async void BackdropSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isSynchronizingControls
            || BackdropSelector.SelectedIndex < 0)
        {
            return;
        }

        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.SetBackdropAsync(
                (BackdropKind)BackdropSelector.SelectedIndex);
        });
    }

    private async void CloseBehaviorSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isSynchronizingControls
            || CloseBehaviorSelector.SelectedIndex < 0)
        {
            return;
        }

        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.SetCloseBehaviorAsync(
                (CloseBehavior)CloseBehaviorSelector.SelectedIndex);
        });
    }

    private async void StartupToggle_Toggled(
        object sender,
        RoutedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.SetStartupEnabledAsync(StartupToggle.IsOn);
        });
    }

    private async void NotificationsToggle_Toggled(
        object sender,
        RoutedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.SetNotificationsEnabledAsync(
                NotificationsToggle.IsOn);
        });
    }

    private async void NotificationLeadInput_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.SetNotificationLeadMinutesAsync(args.NewValue);
        });
    }

    private async void OpenWindowsNotificationSettingsButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        await ExecuteSettingChangeAsync(async () =>
        {
            await ViewModel.OpenWindowsNotificationSettingsAsync();
        });
    }

    private async void ExportBackupButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        FileSavePicker picker = new(
            XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"StaminaManager-{DateTime.Now:yyyyMMdd}",
        };
        picker.FileTypeChoices.Add(
            "Stamina Manager バックアップ",
            new List<string> { ".staminabackup" });
        PickFileResult? selected = await picker.PickSaveFileAsync();
        if (selected is null)
        {
            return;
        }

        await ExecuteSettingChangeAsync(() =>
            ViewModel.ExportBackupAsync(selected.Path));
    }

    private async void ImportBackupButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        FileOpenPicker picker = new(
            XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".staminabackup");
        PickFileResult? selected = await picker.PickSingleFileAsync();
        if (selected is null)
        {
            return;
        }

        try
        {
            BackupPreview preview = await ViewModel.PreviewRestoreAsync(
                selected.Path);
            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Title = "バックアップを復元しますか？",
                PrimaryButtonText = "現在データを置き換える",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
                Content = CreateRestorePreview(preview),
            };
            ContentDialogResult result = await confirmation.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await ExecuteSettingChangeAsync(() =>
                ViewModel.RestoreBackupAsync(
                    selected.Path,
                    isReplacementConfirmed: true));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                "Backup restore preparation failed: "
                + exception.GetType().Name);
            ViewModel.ReportUnexpectedFailure();
        }
    }

    private StackPanel CreateRestorePreview(BackupPreview preview)
    {
        StackPanel content = new()
        {
            Spacing = 8,
            MaxWidth = 520,
        };
        content.Children.Add(new TextBlock
        {
            Text = "現在のゲーム、画像、設定をバックアップの内容で置き換えます。"
                + " 端末固有の通知台帳は保持されます。",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (string line in new[]
        {
            $"ゲーム: {preview.GameCount}件",
            $"画像: {preview.ImageCount}件",
            $"テーマ: {ViewModel.Theme} → {preview.Theme}",
            $"背景: {ViewModel.SelectedBackdrop} → {preview.Backdrop}",
            $"通知: {FormatEnabled(ViewModel.AreNotificationsEnabled)}"
                + $" → {FormatEnabled(preview.NotificationsEnabled)}",
            $"閉じる動作: {ViewModel.CloseBehavior} → {preview.CloseBehavior}",
            $"自動起動: {FormatEnabled(ViewModel.IsStartupEnabled)}"
                + $" → {FormatEnabled(preview.StartupEnabled)}",
        })
        {
            content.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return content;
    }

    private static string FormatEnabled(bool isEnabled) =>
        isEnabled ? "オン" : "オフ";

    private void SettingsInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args) => ViewModel.DismissInfoBar();

    internal Task ExecuteSettingChangeAsync(Func<Task> settingChange) =>
        SettingsChangeExecutor.ExecuteAsync(
            settingChange,
            ViewModel.ReportUnexpectedFailure,
            SynchronizeControls);

    private void SynchronizeControls()
    {
        _isSynchronizingControls = true;
        try
        {
            ThemeToggle.IsOn = ViewModel.IsDarkTheme;
            BackdropSelector.SelectedIndex =
                ViewModel.SelectedBackdropIndex;
            CloseBehaviorSelector.SelectedIndex =
                ViewModel.CloseBehaviorIndex;
            StartupToggle.IsOn = ViewModel.IsStartupEnabled;
            NotificationsToggle.IsOn =
                ViewModel.AreNotificationsEnabled;
            NotificationLeadInput.Value =
                ViewModel.NotificationLeadMinutes;
        }
        finally
        {
            _isSynchronizingControls = false;
        }
    }
}

internal static class SettingsChangeExecutor
{
    internal static async Task ExecuteAsync(
        Func<Task> settingChange,
        Action reportFailure,
        Action synchronizeControls)
    {
        ArgumentNullException.ThrowIfNull(settingChange);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(synchronizeControls);

        try
        {
            await settingChange();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                "Unexpected settings change failure: "
                + exception.GetType().Name);
            reportFailure();
        }
        finally
        {
            synchronizeControls();
        }
    }
}
