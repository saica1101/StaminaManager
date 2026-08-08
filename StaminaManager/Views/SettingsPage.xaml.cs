using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Windows;
using StaminaManager.ViewModels;
using System.Diagnostics;

namespace StaminaManager.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsAppearanceChangeRouter _appearanceChangeRouter;
    private bool _isSynchronizingControls = true;
    private bool _areControlEventsAttached;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        DeferredSettingsChangeExecutor executor = new(
            new DispatcherQueueUiWorkQueue(DispatcherQueue));
        _appearanceChangeRouter = new SettingsAppearanceChangeRouter(
            executor,
            theme => ViewModel.SetThemeAsync(theme),
            backdrop => ViewModel.SetBackdropAsync(backdrop),
            ViewModel.ReportUnexpectedFailure,
            SynchronizeControls);
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

        AppTheme requestedTheme = ThemeToggle.IsOn
            ? AppTheme.Dark
            : AppTheme.Light;
        await _appearanceChangeRouter.ChangeThemeAsync(requestedTheme);
    }

    private async void BackdropSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        int selectedIndex = BackdropSelector.SelectedIndex;
        if (_isSynchronizingControls
            || !BackdropPolicy.TryFromSelectionIndex(
                selectedIndex,
                out BackdropKind requestedBackdrop))
        {
            return;
        }

        await _appearanceChangeRouter.ChangeBackdropAsync(
            requestedBackdrop);
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

        PreparedBackupRestore? prepared = null;
        bool isCommitRequested = false;
        try
        {
            prepared = await ViewModel.PreviewRestoreAsync(
                selected.Path);
            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "バックアップを復元しますか？",
                PrimaryButtonText = "現在データを置き換える",
                PrimaryButtonStyle = (Style)Microsoft.UI.Xaml.Application
                    .Current.Resources["RestoreDialogPrimaryButtonStyle"],
                CloseButtonText = "キャンセル",
                CloseButtonStyle = (Style)Microsoft.UI.Xaml.Application
                    .Current.Resources["RestoreDialogCancelButtonStyle"],
                DefaultButton = ContentDialogButton.Close,
                Content = CreateRestorePreview(prepared.Preview),
            };
            AutomationProperties.SetAutomationId(
                confirmation,
                "RestoreBackupDialog");
            AutomationProperties.SetName(
                confirmation,
                "バックアップの復元確認");
            ContentDialogResult result = await confirmation.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                await ViewModel.CancelPreparedRestoreAsync(
                    prepared.SessionId);
                prepared = null;
                return;
            }

            isCommitRequested = true;
            await ExecuteSettingChangeAsync(() =>
                ViewModel.RestoreBackupAsync(
                    prepared.SessionId,
                    isReplacementConfirmed: true));
            prepared = null;
        }
        catch (Exception exception)
        {
            if (prepared is not null && !isCommitRequested)
            {
                try
                {
                    await ViewModel.CancelPreparedRestoreAsync(
                        prepared.SessionId);
                }
                catch (Exception cleanupException)
                {
                    Debug.WriteLine(
                        "Prepared restore cleanup failed: "
                        + cleanupException.GetType().Name);
                }
            }

            Debug.WriteLine(
                "Backup restore preparation failed: "
                + exception.GetType().Name);
            ViewModel.ReportBackupImportFailure();
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
            $"テーマ: {ViewModel.Theme} → {FormatTheme(preview.Theme)}",
            $"背景: {ViewModel.SelectedBackdrop} → {FormatBackdrop(preview.Backdrop)}",
            $"通知: {FormatEnabled(ViewModel.AreNotificationsEnabled)}"
                + $" → {FormatEnabled(preview.NotificationsEnabled)}",
            $"閉じる動作: {ViewModel.CloseBehavior} → "
                + FormatCloseBehavior(preview.CloseBehavior),
            $"Acrylic の色調不透明度: {preview.AcrylicTintOpacityPercent}%",
            $"言語: {FormatLanguage(preview.Language)}",
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

    private static string FormatTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => "ライト",
        AppTheme.Dark => "ダーク",
        _ => theme.ToString(),
    };

    private static string FormatBackdrop(BackdropKind backdrop) => backdrop switch
    {
        BackdropKind.Mica => "Mica",
        BackdropKind.Acrylic => "Acrylic",
        BackdropKind.Solid => "単色",
        BackdropKind.Blur => "Blur",
        BackdropKind.Transparent => "Transparent",
        _ => backdrop.ToString(),
    };

    private static string FormatCloseBehavior(CloseBehavior closeBehavior) =>
        closeBehavior == CloseBehavior.Exit ? "終了" : "最小化";

    private static string FormatLanguage(AppLanguage language) => language ==
        AppLanguage.English ? "English" : "日本語";

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
