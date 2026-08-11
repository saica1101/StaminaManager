using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public async Task DynamicAccessibilityAndAvailabilityText_UsesResources()
    {
        Context context = await Context.CreateAsync();
        AppResourceService resources = CreateDynamicResources();
        SettingsViewModel viewModel = new(
            context.Manager,
            context.ThemeService,
            context.BackdropService,
            new PassThroughStartupService(),
            context.NotificationReconciler,
            new PassThroughPermissionService(),
            new PassThroughSettingsLauncher(),
            resources);
        viewModel.MarkReady();

        Assert.AreEqual("localized-error-title", viewModel.InfoBarTitle);
        Assert.AreEqual(
            $"opacity={AppSettings.DefaultAcrylicTintOpacityPercent}%",
            viewModel.AcrylicOpacityValueAutomationName);
        Assert.AreEqual(
            $"disabled-opacity={AppSettings.DefaultAcrylicTintOpacityPercent}%",
            viewModel.AcrylicOpacityHelpText);
        Assert.AreEqual(
            "localized-NotificationAvailabilityEnabled",
            viewModel.NotificationAvailabilityText);

        viewModel.SetWindowsNotificationAvailability(isAvailable: false);
        Assert.AreEqual(
            "localized-NotificationAvailabilityDisabledForApplication",
            viewModel.NotificationAvailabilityText);

        context.ThemeService.NextResult = new ThemeResult(
            AppTheme.Light,
            AppTheme.Light,
            IsApplied: false,
            ErrorMessage: null);
        Assert.IsFalse(await viewModel.SetThemeAsync(AppTheme.Dark));
        Assert.AreEqual("localized-error-title", viewModel.InfoBarTitle);
    }

    [TestMethod]
    [DataRow(BackdropKind.Acrylic, "enabled-opacity={0}%")]
    [DataRow(BackdropKind.Mica, "disabled-opacity={0}%")]
    public async Task AcrylicOpacityHelpText_UsesEnabledOrDisabledResource(
        BackdropKind backdrop,
        string expectedFormat)
    {
        Context context = await Context.CreateAsync(backdrop, 42);
        SettingsViewModel viewModel = context.CreateViewModel(
            CreateDynamicResources());

        Assert.AreEqual(
            string.Format(expectedFormat, 42),
            viewModel.AcrylicOpacityHelpText);
    }

    [TestMethod]
    [DataRow(
        NotificationPermissionState.Enabled,
        "NotificationAvailabilityEnabled")]
    [DataRow(
        NotificationPermissionState.DisabledForApplication,
        "NotificationAvailabilityDisabledForApplication")]
    [DataRow(
        NotificationPermissionState.DisabledForUser,
        "NotificationAvailabilityDisabledForUser")]
    [DataRow(
        NotificationPermissionState.DisabledByPolicy,
        "NotificationAvailabilityDisabledByPolicy")]
    [DataRow(
        NotificationPermissionState.DisabledByManifest,
        "NotificationAvailabilityDisabledByManifest")]
    [DataRow(
        NotificationPermissionState.Unsupported,
        "NotificationAvailabilityUnsupported")]
    public async Task NotificationAvailabilityText_UsesResourceForEachState(
        NotificationPermissionState state,
        string resourceId)
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel(
            CreateDynamicResources());

        SetViewModelProperty(viewModel, nameof(
            SettingsViewModel.WindowsNotificationState), state);

        Assert.AreEqual(
            $"localized-{resourceId}",
            viewModel.NotificationAvailabilityText);
    }

    [TestMethod]
    public async Task DynamicDisplayProperties_RaiseRequiredPropertyChanged()
    {
        Context context = await Context.CreateAsync(BackdropKind.Acrylic, 42);
        SettingsViewModel viewModel = context.CreateViewModel(
            CreateDynamicResources());
        List<string> changedProperties = [];
        viewModel.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName ?? string.Empty);

        SetViewModelProperty(
            viewModel,
            nameof(SettingsViewModel.AcrylicTintOpacityPercent),
            43);

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(SettingsViewModel.AcrylicTintOpacityPercent),
                nameof(SettingsViewModel.AcrylicOpacityValueText),
                nameof(SettingsViewModel.AcrylicOpacityValueAutomationName),
                nameof(SettingsViewModel.AcrylicOpacityHelpText),
            },
            changedProperties);

        changedProperties.Clear();
        SetViewModelProperty(
            viewModel,
            nameof(SettingsViewModel.WindowsNotificationState),
            NotificationPermissionState.DisabledForUser);

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(SettingsViewModel.WindowsNotificationState),
                nameof(SettingsViewModel.NotificationAvailabilityText),
            },
            changedProperties);
    }

    [TestMethod]
    public async Task SettingChanges_BeforeReadyAreRejectedWithoutSideEffects()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateNotReadyViewModel(
            SettingsEnglishResourceFixture.Create());

        bool[] results =
        [
            await viewModel.SetThemeAsync(AppTheme.Dark),
            await viewModel.SetBackdropAsync(BackdropKind.Acrylic),
            await viewModel.SetCloseBehaviorAsync(CloseBehavior.Exit),
            await viewModel.SetStartupEnabledAsync(isEnabled: true),
            await viewModel.SetNotificationsEnabledAsync(isEnabled: false),
            await viewModel.SetNotificationLeadMinutesAsync(30),
        ];

        CollectionAssert.AreEqual(
            new[] { false, false, false, false, false, false },
            results);
        Assert.IsFalse(viewModel.IsReady);
        Assert.IsEmpty(context.ThemeService.Requests);
        Assert.IsEmpty(context.BackdropService.Requests);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(
            "Settings are still loading. Try again when loading is complete.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task MarkReady_AfterManagerInitializationEnablesChanges()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateNotReadyViewModel();

        Assert.IsTrue(viewModel.IsLoading);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Visible,
            viewModel.LoadingVisibility);

        viewModel.MarkReady();
        bool saved = await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit);

        Assert.IsTrue(viewModel.IsReady);
        Assert.IsFalse(viewModel.IsLoading);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Collapsed,
            viewModel.LoadingVisibility);
        Assert.IsTrue(saved);
        Assert.AreEqual(1, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task MarkFailed_StopsLoadingAndKeepsChangesDisabled()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateNotReadyViewModel();

        viewModel.MarkFailed();
        bool saved = await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit);

        Assert.AreEqual(
            SettingsInitializationState.Failed,
            viewModel.InitializationState);
        Assert.IsFalse(viewModel.IsReady);
        Assert.IsFalse(viewModel.IsLoading);
        Assert.IsTrue(viewModel.IsFailed);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Collapsed,
            viewModel.LoadingVisibility);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Visible,
            viewModel.FailedVisibility);
        Assert.IsFalse(saved);
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task UnexpectedThemeServiceException_IsSafelyReported()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel(
            SettingsEnglishResourceFixture.Create());
        context.ThemeService.ApplyException =
            new InvalidOperationException("service implementation detail");
        int synchronizationCount = 0;

        await SettingsChangeExecutor.ExecuteAsync(
            async () =>
            {
                await viewModel.SetThemeAsync(AppTheme.Dark);
            },
            viewModel.ReportUnexpectedFailure,
            () => synchronizationCount++);

        Assert.AreEqual(1, synchronizationCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(
            "The setting could not be changed. Try again.",
            viewModel.InfoBarMessage);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "service implementation detail",
            StringComparison.Ordinal));
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public void CreateDefault_UsesResolvedWindowsThemeAndDocumentedDefaults()
    {
        RecordingThemeService themeService = new(AppTheme.Dark);

        AppSettings settings = AppSettings.CreateDefault(
            themeService.ResolveInitialTheme());

        Assert.AreEqual(AppTheme.Dark, settings.Theme);
        Assert.AreEqual(BackdropKind.Mica, settings.Backdrop);
        Assert.IsTrue(settings.NotificationsEnabled);
        Assert.AreEqual(15, settings.NotificationLeadMinutes);
        Assert.AreEqual(
            CloseBehavior.MinimizeToTray,
            settings.CloseBehavior);
        Assert.IsFalse(settings.StartupEnabled);
    }

    [TestMethod]
    public async Task SetBackdropAsync_FallbackKeepsSavedPreference()
    {
        Context context = await Context.CreateAsync();
        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Acrylic,
            BackdropKind.Solid,
            BackdropFallbackReason.HighContrast,
            "ハイ コントラストでは単色背景を使用します。");
        SettingsViewModel viewModel = context.CreateViewModel(
            SettingsEnglishResourceFixture.Create());

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(
            "The background cannot be used in high contrast. "
            + "Review contrast settings or keep a solid background.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task SetBackdropAsync_保存済みAcrylic色調不透明度を適用要求へ渡す()
    {
        Context context = await Context.CreateAsync();
        await context.Manager.UpdateSettingsAsync(
            settings => settings with { AcrylicTintOpacityPercent = 55 },
            CancellationToken.None);
        SettingsViewModel viewModel = context.CreateViewModel(
            SettingsEnglishResourceFixture.Create());

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsTrue(applied);
        Assert.AreEqual(
            new BackdropRequest(BackdropKind.Acrylic, 55),
            context.BackdropService.RequestModels.Single());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(50)]
    [DataRow(100)]
    public async Task PreviewAcrylicTintOpacityAsync_ValidValuesApplyWithoutSaving(
        int percent)
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.PreviewAcrylicTintOpacityAsync(percent);

        Assert.IsTrue(applied);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.AreEqual(
            new BackdropRequest(BackdropKind.Acrylic, percent),
            context.BackdropService.RequestModels.Single());
        Assert.AreEqual(
            percent,
            viewModel.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(101)]
    public async Task PreviewAcrylicTintOpacityAsync_OutOfRangeIsRejected(
        int percent)
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.PreviewAcrylicTintOpacityAsync(percent);

        Assert.IsFalse(applied);
        Assert.IsEmpty(context.BackdropService.Requests);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.AreEqual(80, viewModel.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    public async Task PreviewAcrylicTintOpacityAsync_NonAcrylicActualStateDoesNotApply()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.PreviewAcrylicTintOpacityAsync(50);

        Assert.IsFalse(applied);
        Assert.IsEmpty(context.BackdropService.Requests);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsFalse(viewModel.IsAcrylicOpacityEnabled);
    }

    [TestMethod]
    public async Task PreviewAcrylicTintOpacityAsync_FailureRestoresLastAppliedValue()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Acrylic,
            BackdropKind.Solid,
            BackdropFallbackReason.ApplyFailed,
            "preview failure");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.PreviewAcrylicTintOpacityAsync(50);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Acrylic },
            context.BackdropService.Requests);
        Assert.AreEqual(80, viewModel.AcrylicTintOpacityPercent);
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task CommitAcrylicTintOpacityAsync_SaveFailureRollsBackValueAndBackdrop()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();
        Assert.IsTrue(await viewModel.PreviewAcrylicTintOpacityAsync(50));
        context.Store.SaveException = new IOException("save failure");

        bool committed = await viewModel.CommitAcrylicTintOpacityAsync(50);

        Assert.IsFalse(committed);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Acrylic },
            context.BackdropService.Requests);
        Assert.AreEqual(80, viewModel.AcrylicTintOpacityPercent);
        Assert.AreEqual(80, context.Manager.CurrentData.Settings
            .AcrylicTintOpacityPercent);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task CommitAcrylicTintOpacityAsync_SavesLatestPreviewOnce()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();
        Assert.IsTrue(await viewModel.PreviewAcrylicTintOpacityAsync(50));

        bool committed = await viewModel.CommitAcrylicTintOpacityAsync(50);

        Assert.IsTrue(committed);
        Assert.AreEqual(1, context.Store.SaveCount);
        Assert.AreEqual(50, context.Manager.CurrentData.Settings
            .AcrylicTintOpacityPercent);
        Assert.AreEqual(50, viewModel.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    public async Task AcrylicOpacityTwentyPreviewChanges_SaveOnlyLatestValueOnce()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();

        for (int index = 0; index < 20; index++)
        {
            Assert.IsTrue(await viewModel.PreviewAcrylicTintOpacityAsync(index * 5));
        }

        Assert.IsTrue(await viewModel.CommitAcrylicTintOpacityAsync(95));

        Assert.AreEqual(1, context.Store.SaveCount);
        Assert.AreEqual(
            95,
            context.Manager.CurrentData.Settings.AcrylicTintOpacityPercent);
        Assert.AreEqual(
            95,
            context.Store.LastSaved.Settings.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    public async Task CommitAcrylicTintOpacityAsync_CancellationRollsBackAndRethrows()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();
        Assert.IsTrue(await viewModel.PreviewAcrylicTintOpacityAsync(50));
        context.Store.SaveException = new OperationCanceledException(
            "save cancellation");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.CommitAcrylicTintOpacityAsync(50));

        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Acrylic },
            context.BackdropService.Requests);
        Assert.AreEqual(80, viewModel.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    public async Task CommitAcrylicTintOpacityAsync_RollbackFailureUsesSolidAndRestartGuidance()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel(
            SettingsEnglishResourceFixture.Create());
        Assert.IsTrue(await viewModel.PreviewAcrylicTintOpacityAsync(50));
        context.Store.SaveException = new IOException("save failure");
        context.BackdropService.RollbackException =
            new InvalidOperationException("rollback failure");

        bool committed = await viewModel.CommitAcrylicTintOpacityAsync(50);

        Assert.IsFalse(committed);
        CollectionAssert.AreEqual(
            new[]
            {
                BackdropKind.Acrylic,
                BackdropKind.Acrylic,
                BackdropKind.Solid,
            },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(
            "Acrylic tint opacity could not be saved. "
            + "The background was switched to a safe solid background. "
            + "Restart the app.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task AcrylicOpacityEnabled_RequiresSelectedAndActualAcrylic()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            55);
        SettingsViewModel viewModel = context.CreateViewModel();

        Assert.IsTrue(viewModel.IsAcrylicOpacityEnabled);

        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Solid,
            BackdropKind.Solid,
            BackdropFallbackReason.None,
            null);
        Assert.IsTrue(await viewModel.SetBackdropAsync(BackdropKind.Solid));
        Assert.IsFalse(viewModel.IsAcrylicOpacityEnabled);
        Assert.AreEqual(55, viewModel.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    public async Task AcrylicOpacityEnabled_DisablesDuringBackupAndAppearanceMutation()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            55);
        SettingsViewModel viewModel = context.CreateViewModel();

        SetViewModelProperty(viewModel, "IsBackupBusy", true);
        Assert.IsFalse(viewModel.IsAcrylicOpacityEnabled);

        SetViewModelProperty(viewModel, "IsBackupBusy", false);
        SetViewModelProperty(viewModel, "IsAppearanceBusy", true);
        Assert.IsFalse(viewModel.IsAcrylicOpacityEnabled);
    }

    [TestMethod]
    public async Task SetBackdropAsync_RepeatedAcrylicMicaSolidUsesSavedOpacity()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Mica,
            55);
        SettingsViewModel viewModel = context.CreateViewModel();

        Assert.IsTrue(await viewModel.SetBackdropAsync(BackdropKind.Acrylic));
        Assert.IsTrue(await viewModel.SetBackdropAsync(BackdropKind.Mica));
        Assert.IsTrue(await viewModel.SetBackdropAsync(BackdropKind.Solid));
        Assert.IsTrue(await viewModel.SetBackdropAsync(BackdropKind.Acrylic));

        CollectionAssert.AreEqual(
            new[]
            {
                new BackdropRequest(BackdropKind.Acrylic, 55),
                new BackdropRequest(BackdropKind.Mica, 55),
                new BackdropRequest(BackdropKind.Solid, 55),
                new BackdropRequest(BackdropKind.Acrylic, 55),
            },
            context.BackdropService.RequestModels);
        Assert.AreEqual(55, context.Manager.CurrentData.Settings
            .AcrylicTintOpacityPercent);
    }

    private static void SetViewModelProperty(
        SettingsViewModel viewModel,
        string propertyName,
        object value)
    {
        PropertyInfo? property = typeof(SettingsViewModel).GetProperty(
            propertyName);
        Assert.IsNotNull(property);
        property!.SetValue(viewModel, value);
    }

    private static AppResourceService CreateDynamicResources() => new(
        resourceId => resourceId switch
        {
            "SettingsErrorTitle" => "localized-error-title",
            "AcrylicOpacityAutomationNameFormat" => "opacity={0}%",
            "AcrylicOpacityHelpTextEnabledFormat" =>
                "enabled-opacity={0}%",
            "AcrylicOpacityHelpTextDisabledFormat" =>
                "disabled-opacity={0}%",
            _ when resourceId.StartsWith(
                "NotificationAvailability",
                StringComparison.Ordinal) => $"localized-{resourceId}",
            _ => resourceId,
        });

    [TestMethod]
    [DataRow(
        BackdropFallbackReason.HighContrast,
        "SettingsBackdropFallbackHighContrast")]
    [DataRow(
        BackdropFallbackReason.TransparencyDisabled,
        "SettingsBackdropFallbackTransparencyDisabled")]
    [DataRow(
        BackdropFallbackReason.RemoteSession,
        "SettingsBackdropFallbackRemoteSession")]
    [DataRow(
        BackdropFallbackReason.Unsupported,
        "SettingsBackdropFallbackUnsupported")]
    [DataRow(
        BackdropFallbackReason.ApplyFailed,
        "SettingsBackdropFallbackApplyFailed")]
    public async Task SetBackdropAsync_Fallback理由別の次アクションを案内して保存設定を維持する(
        BackdropFallbackReason reason,
        string expectedResourceId)
    {
        Context context = await Context.CreateAsync();
        RecordingResourceService resources = new();
        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Acrylic,
            BackdropKind.Solid,
            reason,
            "選択した背景を適用できませんでした。");
        SettingsViewModel viewModel = context.CreateViewModel(resources);

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
            viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "SettingsBackdropFallbackTitle",
            viewModel.InfoBarTitle);
        Assert.AreEqual(expectedResourceId, viewModel.InfoBarMessage);
        CollectionAssert.Contains(
            resources.RequestedResourceIds,
            expectedResourceId);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "選択した背景を適用できませんでした。",
            StringComparison.Ordinal));
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task SetBackdropAsync_SolidFallbackFailureShowsErrorAndKeepsActualBackdrop()
    {
        Context context = await Context.CreateAsync();
        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Acrylic,
            BackdropKind.Mica,
            BackdropFallbackReason.SolidFallbackFailed,
            "solid fallback implementation detail");
        SettingsViewModel viewModel = context.CreateViewModel(
            SettingsEnglishResourceFixture.Create());

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "Could not complete the setting",
            viewModel.InfoBarTitle);
        Assert.AreEqual(
            "The solid background could not be applied. Restart the app.",
            viewModel.InfoBarMessage);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "solid fallback implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SynchronizeFromCurrentSettings_SolidFallbackFailureShowsError()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel(
            SettingsEnglishResourceFixture.Create());

        viewModel.SynchronizeFromCurrentSettings(
            backdropResult: new BackdropResult(
                BackdropKind.Acrylic,
                BackdropKind.Mica,
                BackdropFallbackReason.SolidFallbackFailed,
                "solid fallback implementation detail"));

        Assert.AreEqual(
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "Could not complete the setting",
            viewModel.InfoBarTitle);
        Assert.AreEqual(
            "The solid background could not be applied. Restart the app.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task SetBackdropAsync_SaveFailureRollsBackAppliedBackdrop()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new IOException("save failure");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Mica },
            context.BackdropService.Requests);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task SetBackdropAsync_SaveFailureAfterPreviewRestoresSavedAcrylicOpacity()
    {
        Context context = await Context.CreateAsync(
            BackdropKind.Acrylic,
            80);
        SettingsViewModel viewModel = context.CreateViewModel();
        Assert.IsTrue(await viewModel.PreviewAcrylicTintOpacityAsync(50));
        context.Store.SaveException = new IOException("save failure");

        Assert.IsFalse(await viewModel.SetBackdropAsync(BackdropKind.Mica));

        CollectionAssert.AreEqual(
            new[]
            {
                BackdropKind.Acrylic,
                BackdropKind.Mica,
                BackdropKind.Acrylic,
            },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Acrylic, viewModel.ActualBackdrop);
        Assert.AreEqual(80, viewModel.AcrylicTintOpacityPercent);
    }

    [TestMethod]
    public async Task SetBackdropAsync_SaveCancellationRollsBackThenRethrows()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new OperationCanceledException(
            "cancellation implementation detail");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.SetBackdropAsync(
                BackdropKind.Acrylic,
                CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Mica },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
    }

    [TestMethod]
    public async Task SetBackdropAsync_UnexpectedSaveFailureRollsBackAllState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Mica },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "persistence implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetBackdropAsync_RollbackExceptionPublishesSafeState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");
        context.BackdropService.RollbackException =
            new InvalidOperationException(
                "rollback implementation detail");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[]
            {
                BackdropKind.Acrylic,
                BackdropKind.Mica,
                BackdropKind.Solid,
            },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
        Assert.AreEqual(
            "SettingsBackdropRollbackSafeFallback",
            viewModel.InfoBarMessage);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "rollback implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetBackdropAsync_SolidFallbackExceptionKeepsLastKnownActualState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");
        context.BackdropService.RollbackException =
            new InvalidOperationException(
                "rollback implementation detail");
        context.BackdropService.SafeFallbackException =
            new InvalidOperationException(
                "fallback implementation detail");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[]
            {
                BackdropKind.Acrylic,
                BackdropKind.Mica,
                BackdropKind.Solid,
            },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Acrylic, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
        Assert.AreEqual(
            "SettingsBackdropRollbackUnknown",
            viewModel.InfoBarMessage);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetThemeAsync_ApplyFailureKeepsLastGoodTheme()
    {
        Context context = await Context.CreateAsync();
        context.ThemeService.NextResult = new ThemeResult(
            AppTheme.Dark,
            AppTheme.Light,
            IsApplied: false,
            "テーマを適用できませんでした。");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.SetThemeAsync(
            AppTheme.Dark,
            CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(
            AppTheme.Light,
            context.Manager.CurrentData.Settings.Theme);
        Assert.AreEqual(AppTheme.Light, viewModel.Theme);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task SetThemeAsync_UnexpectedSaveFailureRollsBackAllState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");

        bool applied = await viewModel.SetThemeAsync(
            AppTheme.Dark,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark, AppTheme.Light },
            context.ThemeService.Requests);
        Assert.AreEqual(AppTheme.Light, viewModel.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Manager.CurrentData.Settings.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Store.LastSaved.Settings.Theme);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "persistence implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetThemeAsync_SaveCancellationPreservesCancellationWhenRollbackThrows()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new OperationCanceledException(
            "cancellation implementation detail");
        context.ThemeService.RollbackException =
            new InvalidOperationException(
                "rollback implementation detail");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.SetThemeAsync(
                AppTheme.Dark,
                CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark, AppTheme.Light },
            context.ThemeService.Requests);
        Assert.AreEqual(AppTheme.Dark, viewModel.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Manager.CurrentData.Settings.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Store.LastSaved.Settings.Theme);
        Assert.AreEqual(
            "SettingsThemeRollbackUnknownAfterCancel",
            viewModel.InfoBarMessage);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "rollback implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(-1d)]
    [DataRow(0.5d)]
    [DataRow(525_601d)]
    public async Task SetNotificationLeadMinutesAsync_InvalidValueIsRejected(
        double value)
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        bool saved = await viewModel.SetNotificationLeadMinutesAsync(
            value,
            CancellationToken.None);

        Assert.IsFalse(saved);
        Assert.AreEqual(15, viewModel.NotificationLeadMinutes);
        Assert.AreEqual(15,
            context.Manager.CurrentData.Settings.NotificationLeadMinutes);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(525_600d)]
    public async Task SetNotificationLeadMinutesAsync_BoundaryIsSaved(
        double value)
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        bool saved = await viewModel.SetNotificationLeadMinutesAsync(
            value,
            CancellationToken.None);

        Assert.IsTrue(saved);
        Assert.AreEqual((int)value, viewModel.NotificationLeadMinutes);
        Assert.AreEqual(
            (int)value,
            context.Manager.CurrentData.Settings.NotificationLeadMinutes);
        Assert.AreEqual(1, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task GeneralAndNotificationChanges_PublishAfterSave()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        Assert.IsTrue(await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit,
            CancellationToken.None));
        Assert.IsTrue(await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None));
        Assert.IsTrue(await viewModel.SetNotificationsEnabledAsync(
            isEnabled: false,
            CancellationToken.None));

        Assert.AreEqual(CloseBehavior.Exit, viewModel.CloseBehavior);
        Assert.IsTrue(viewModel.IsStartupEnabled);
        Assert.IsFalse(viewModel.AreNotificationsEnabled);
        Assert.AreEqual(3, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task GeneralChange_SaveFailureDoesNotPublish()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new IOException("save failure");

        bool saved = await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit,
            CancellationToken.None);

        Assert.IsFalse(saved);
        Assert.AreEqual(
            CloseBehavior.MinimizeToTray,
            viewModel.CloseBehavior);
        Assert.AreEqual(
            CloseBehavior.MinimizeToTray,
            context.Manager.CurrentData.Settings.CloseBehavior);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task SetLanguageAsync_SavesOverridesThenReconcilesNotifications()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateLanguageViewModel(
            resources: SettingsEnglishResourceFixture.Create());

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.English,
            CancellationToken.None);

        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(
            new[] { "Save", "Override", "Notifications" },
            context.Store.Operations);
        Assert.AreEqual(AppLanguage.English, viewModel.Language);
        Assert.AreEqual(1, viewModel.SelectedLanguageIndex);
        Assert.IsTrue(viewModel.IsLanguageRestartRequired);
        Assert.AreEqual(
            AppLanguage.English,
            context.Manager.CurrentData.Settings.Language);
        Assert.AreEqual(
            AppLanguage.English,
            context.NotificationReconciler.LastSettings!.Language);
        Assert.AreEqual(
            "The language changed. Restart the app to apply it.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task SetLanguageAsync_SaveFailureDoesNotCallOverride()
    {
        Context context = await Context.CreateAsync();
        context.Store.SaveException = new IOException("save detail");
        SettingsViewModel viewModel = context.CreateLanguageViewModel();

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.English,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.IsEmpty(context.LanguageService.SetRequests);
        Assert.AreEqual(AppLanguage.Japanese, viewModel.Language);
        Assert.AreEqual(
            AppLanguage.Japanese,
            context.Manager.CurrentData.Settings.Language);
        Assert.AreEqual(0, context.NotificationReconciler.CallCount);
    }

    [TestMethod]
    public async Task SetLanguageAsync_OverrideFailureRollsBackSavedLanguage()
    {
        Context context = await Context.CreateAsync();
        context.LanguageService.NextResult = new LanguageChangeResult(
            AppLanguage.English,
            IsApplied: false,
            LanguageFailureReason.PlatformError);
        SettingsViewModel viewModel = context.CreateLanguageViewModel();

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.English,
            CancellationToken.None);

        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(
            new[] { "Save", "Override", "Save" },
            context.Store.Operations);
        Assert.AreEqual(AppLanguage.Japanese, viewModel.Language);
        Assert.AreEqual(
            AppLanguage.Japanese,
            context.Manager.CurrentData.Settings.Language);
        Assert.AreEqual(
            LanguageConsistencyState.Synchronized,
            viewModel.LanguageConsistencyState);
        Assert.AreEqual(0, context.NotificationReconciler.CallCount);
    }

    [TestMethod]
    public async Task SetLanguageAsync_RollbackFailureKeepsNewLanguageAsInconsistent()
    {
        Context context = await Context.CreateAsync();
        context.LanguageService.NextResult = new LanguageChangeResult(
            AppLanguage.English,
            IsApplied: false,
            LanguageFailureReason.PlatformError);
        context.Store.SaveExceptions.Enqueue(null);
        context.Store.SaveExceptions.Enqueue(new IOException("rollback detail"));
        SettingsViewModel viewModel = context.CreateLanguageViewModel(
            resources: SettingsEnglishResourceFixture.Create());

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.English,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.AreEqual(AppLanguage.English, viewModel.Language);
        Assert.AreEqual(
            AppLanguage.English,
            context.Manager.CurrentData.Settings.Language);
        Assert.AreEqual(
            LanguageConsistencyState.Inconsistent,
            viewModel.LanguageConsistencyState);
        Assert.IsTrue(viewModel.IsLanguageRestartRequired);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(
            "The language setting was saved, but Windows could not apply it. "
            + "It will be retried at the next startup.",
            viewModel.InfoBarMessage);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "rollback detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetLanguageAsync_SameSavedLanguageStillSynchronizes()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateLanguageViewModel();

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.Japanese,
            CancellationToken.None);

        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(
            new[] { "Override", "Notifications" },
            context.Store.Operations);
        Assert.AreEqual(0, context.Store.SaveCount);
        CollectionAssert.AreEqual(
            new[] { AppLanguage.Japanese },
            context.LanguageService.SetRequests);
        Assert.AreEqual(1, context.NotificationReconciler.CallCount);
    }

    [TestMethod]
    public async Task SetLanguageAsync_SameSavedLanguageOverrideFailureKeepsSavedValueInconsistent()
    {
        Context context = await Context.CreateAsync();
        context.LanguageService.NextResult = new LanguageChangeResult(
            AppLanguage.Japanese,
            IsApplied: false,
            LanguageFailureReason.PlatformError);
        SettingsViewModel viewModel = context.CreateLanguageViewModel();

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.Japanese,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.AreEqual(
            AppLanguage.Japanese,
            context.Store.LastSaved.Settings.Language);
        Assert.AreEqual(
            AppLanguage.Japanese,
            context.Manager.CurrentData.Settings.Language);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.AreEqual(AppLanguage.Japanese, viewModel.Language);
        Assert.AreEqual(
            LanguageConsistencyState.Inconsistent,
            viewModel.LanguageConsistencyState);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
            viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "SettingsLanguageApplyFailureSaved",
            viewModel.InfoBarMessage);
        Assert.AreEqual(0, context.NotificationReconciler.CallCount);
    }

    [TestMethod]
    public async Task SetLanguageAsync_NotificationFailureKeepsLanguageApplied()
    {
        Context context = await Context.CreateAsync();
        context.NotificationReconciler.Result = new NotificationReconcileResult(
            ImmutableArray.Create(new NotificationReconcileIssue(
                GameId: null,
                NotificationDecisionError.None,
                "notification detail")));
        SettingsViewModel viewModel = context.CreateLanguageViewModel();

        bool changed = await viewModel.SetLanguageAsync(
            AppLanguage.English,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.AreEqual(AppLanguage.English, viewModel.Language);
        Assert.AreEqual(
            AppLanguage.English,
            context.Manager.CurrentData.Settings.Language);
        Assert.AreEqual(1, context.NotificationReconciler.CallCount);
        Assert.AreEqual(
            "SettingsNotificationReconcileFailure",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task SetLanguageAsync_SessionLanguageDoesNotRequireRestartWhenMatching()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateLanguageViewModel(
            AppLanguage.English);

        Assert.IsTrue(await viewModel.SetLanguageAsync(
            AppLanguage.English,
            CancellationToken.None));

        Assert.IsFalse(viewModel.IsLanguageRestartRequired);
    }

    [TestMethod]
    public async Task SynchronizeFromCurrentSettings_LanguageFailureKeepsSavedValueAndWarnsForRetry()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateLanguageViewModel();
        await context.Manager.UpdateSettingsAsync(
            settings => settings with { Language = AppLanguage.English },
            CancellationToken.None);

        viewModel.SynchronizeFromCurrentSettings(
            languageResult: new LanguageChangeResult(
                AppLanguage.English,
                IsApplied: false,
                LanguageFailureReason.PlatformError),
            isLanguageSynchronized: false,
            languageConsistencyState: LanguageConsistencyState.Inconsistent);

        Assert.AreEqual(AppLanguage.English, viewModel.Language);
        Assert.AreEqual(
            LanguageConsistencyState.Inconsistent,
            viewModel.LanguageConsistencyState);
        Assert.AreEqual(
            "SettingsLanguageApplyFailureSaved",
            viewModel.InfoBarMessage);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "以前の設定に戻しました",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReservedActions_ReportPreparationWithoutClaimingSuccess()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        List<SettingsPreparationAction> requests = [];
        viewModel.PreparationRequested += requests.Add;

        viewModel.PrepareBackupExport();
        viewModel.PrepareBackupImport();
        viewModel.SetWindowsNotificationAvailability(isAvailable: false);
        viewModel.PrepareWindowsNotificationSettings();

        CollectionAssert.AreEqual(
            new[]
            {
                SettingsPreparationAction.ExportBackup,
                SettingsPreparationAction.ImportBackup,
                SettingsPreparationAction.OpenWindowsNotificationSettings,
            },
            requests);
        Assert.IsFalse(viewModel.AreWindowsNotificationsAvailable);
        Assert.IsTrue(viewModel.IsOpenWindowsNotificationSettingsVisible);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        StringAssert.Contains(viewModel.InfoBarMessage, "準備中");
    }

    [TestMethod]
    public void AsyncSave_PublishesPropertiesOnCallingSynchronizationContext()
    {
        Context context = Context.CreateAsync().GetAwaiter().GetResult();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.ShouldBlockSave = true;
        PumpSynchronizationContext synchronizationContext = new();
        SynchronizationContext? previousContext =
            SynchronizationContext.Current;
        bool wasPublishedOnCallingContext = false;

        try
        {
            SynchronizationContext.SetSynchronizationContext(
                synchronizationContext);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(
                    SettingsViewModel.CloseBehavior))
                {
                    wasPublishedOnCallingContext = ReferenceEquals(
                        SynchronizationContext.Current,
                        synchronizationContext);
                }
            };

            Task<bool> saveTask = viewModel.SetCloseBehaviorAsync(
                CloseBehavior.Exit,
                CancellationToken.None);
            context.Store.ReleaseSave();
            bool saved = synchronizationContext.RunUntil(saveTask);

            Assert.IsTrue(saved);
            Assert.IsTrue(wasPublishedOnCallingContext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed record Context(
        MemoryDataStore Store,
        GameManager Manager,
        RecordingThemeService ThemeService,
        RecordingBackdropService BackdropService,
        RecordingLanguageService LanguageService,
        RecordingNotificationReconciler NotificationReconciler)
    {
        public static async Task<Context> CreateAsync(
            BackdropKind backdrop = BackdropKind.Mica,
            int acrylicOpacity = AppSettings.DefaultAcrylicTintOpacityPercent)
        {
            AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
            {
                Backdrop = backdrop,
                AcrylicTintOpacityPercent = acrylicOpacity,
            };
            DataEnvelope envelope = new(
                DataEnvelope.CurrentSchemaVersion,
                ImmutableArray<GameEntry>.Empty,
                settings);
            MemoryDataStore store = new(envelope);
            List<string> operations = [];
            store.Operations = operations;
            RecordingLanguageService languageService = new(operations);
            RecordingNotificationReconciler notificationReconciler = new(
                operations);
            GameManager manager = new(
                store,
                new FakeClock(DateTimeOffset.UtcNow),
                settings);
            await manager.InitializeAsync(envelope, CancellationToken.None);
            return new Context(
                store,
                manager,
                new RecordingThemeService(AppTheme.Light),
                new RecordingBackdropService(),
                languageService,
                notificationReconciler);
        }

        public SettingsViewModel CreateViewModel(
            IAppResourceService? resources = null)
        {
            SettingsViewModel viewModel = CreateNotReadyViewModel(resources);
            viewModel.MarkReady();
            return viewModel;
        }

        public SettingsViewModel CreateNotReadyViewModel(
            IAppResourceService? resources = null) => new(
            Manager,
            ThemeService,
            BackdropService,
            resources ?? new RecordingResourceService());

        public SettingsViewModel CreateLanguageViewModel(
            AppLanguage sessionLanguage = AppLanguage.Japanese,
            IAppResourceService? resources = null)
        {
            SettingsViewModel viewModel = new(
                Manager,
                ThemeService,
                BackdropService,
                new PassThroughStartupService(),
                NotificationReconciler,
                new PassThroughPermissionService(),
                new PassThroughSettingsLauncher(),
                resources ?? new RecordingResourceService(),
                appLanguageService: LanguageService,
                sessionLanguage: sessionLanguage);
            viewModel.MarkReady();
            return viewModel;
        }
    }

    private sealed class RecordingThemeService(AppTheme initialTheme)
        : IThemeService
    {
        public List<AppTheme> Requests { get; } = [];

        public ThemeResult? NextResult { get; set; }

        public Exception? ApplyException { get; set; }

        public Exception? RollbackException { get; set; }

        public AppTheme ResolveInitialTheme() => initialTheme;

        public ThemeResult Apply(AppTheme requestedTheme)
        {
            Requests.Add(requestedTheme);
            if (Requests.Count == 2 && RollbackException is not null)
            {
                throw RollbackException;
            }

            if (ApplyException is not null)
            {
                throw ApplyException;
            }

            ThemeResult result = NextResult ?? new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
            NextResult = null;
            return result;
        }
    }

    private sealed class RecordingBackdropService : IBackdropService
    {
        public List<BackdropKind> Requests { get; } = [];

        public List<BackdropRequest> RequestModels { get; } = [];

        public BackdropResult? NextResult { get; set; }

        public Exception? RollbackException { get; set; }

        public Exception? SafeFallbackException { get; set; }

        public BackdropResult Apply(BackdropRequest request)
        {
            BackdropKind requestedBackdrop = request.Kind;
            Requests.Add(requestedBackdrop);
            RequestModels.Add(request);
            if (Requests.Count == 2 && RollbackException is not null)
            {
                throw RollbackException;
            }

            if (Requests.Count == 3 && SafeFallbackException is not null)
            {
                throw SafeFallbackException;
            }

            BackdropResult result = NextResult ?? new BackdropResult(
                requestedBackdrop,
                requestedBackdrop,
                BackdropFallbackReason.None,
                ErrorMessage: null,
                requestedBackdrop == BackdropKind.Acrylic
                    ? request.AcrylicTintOpacityPercent
                    : null);
            NextResult = null;
            return result;
        }
    }

    private sealed class RecordingLanguageService(
        List<string> operations) : IAppLanguageService
    {
        public AppLanguage EffectiveLanguage { get; set; } =
            AppLanguage.Japanese;

        public List<AppLanguage> SetRequests { get; } = [];

        public LanguageChangeResult? NextResult { get; set; }

        public AppLanguage GetEffectiveLanguage() => EffectiveLanguage;

        public LanguageChangeResult SetLanguage(AppLanguage language)
        {
            operations.Add("Override");
            SetRequests.Add(language);
            LanguageChangeResult result = NextResult
                ?? new LanguageChangeResult(
                    language,
                    IsApplied: true,
                    LanguageFailureReason.None);
            NextResult = null;
            if (result.IsApplied)
            {
                EffectiveLanguage = language;
            }

            return result;
        }
    }

    private sealed class RecordingNotificationReconciler(
        List<string> operations) : INotificationReconciler
    {
        public int CallCount { get; private set; }

        public AppSettings? LastSettings { get; private set; }

        public NotificationReconcileResult Result { get; set; } =
            NotificationReconcileResult.Success;

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            operations.Add("Notifications");
            CallCount++;
            LastSettings = settings;
            return Task.FromResult(Result);
        }
    }

    private sealed class PassThroughStartupService : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(StartupState.Disabled));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupChangeResult(
                    new StartupStatus(isEnabled
                        ? StartupState.Enabled
                        : StartupState.Disabled),
                    IsApplied: true,
                    StartupFailureReason.None));
    }

    private sealed class PassThroughPermissionService
        : INotificationPermissionService
    {
        public Task<NotificationPermissionStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new NotificationPermissionStatus(
                    NotificationPermissionState.Enabled));
    }

    private sealed class PassThroughSettingsLauncher : ISettingsLauncher
    {
        public Task<bool> OpenNotificationSettingsAsync(
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class MemoryDataStore : ILocalDataStore
    {
        private readonly TaskCompletionSource _continueSave = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly DataEnvelope _envelope;

        public MemoryDataStore(DataEnvelope envelope)
        {
            _envelope = envelope;
            LastSaved = envelope;
        }

        public Exception? SaveException { get; set; }

        public Queue<Exception?> SaveExceptions { get; } = [];

        public int SaveCount { get; private set; }

        public DataEnvelope LastSaved { get; private set; }

        public bool ShouldBlockSave { get; set; }

        public List<string> Operations { get; set; } = [];

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new DataLoadResult(
                    DataLoadStatus.Primary,
                    _envelope,
                    "primary",
                    "recovery"));

        public async Task SaveAsync(
            DataEnvelope value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("Save");
            SaveCount++;
            if (ShouldBlockSave)
            {
                await _continueSave.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (SaveExceptions.Count > 0)
            {
                Exception? queuedException = SaveExceptions.Dequeue();
                if (queuedException is not null)
                {
                    throw queuedException;
                }
            }
            else if (SaveException is not null)
            {
                throw SaveException;
            }

            LastSaved = value;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ReleaseSave() => _continueSave.TrySetResult();
    }

    private sealed class PumpSynchronizationContext
        : SynchronizationContext
    {
        private readonly ConcurrentQueue<(
            SendOrPostCallback Callback,
            object? State)> _callbacks = [];

        public override void Post(
            SendOrPostCallback callback,
            object? state) => _callbacks.Enqueue((callback, state));

        public T RunUntil<T>(Task<T> task)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted)
            {
                if (_callbacks.TryDequeue(out var work))
                {
                    work.Callback(work.State);
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        "UI continuation did not complete.");
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            while (_callbacks.TryDequeue(out var remaining))
            {
                remaining.Callback(remaining.State);
            }

            return task.GetAwaiter().GetResult();
        }
    }
}
