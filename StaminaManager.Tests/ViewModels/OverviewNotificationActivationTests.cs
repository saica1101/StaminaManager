using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class OverviewNotificationActivationTests
{
    [TestMethod]
    public async Task ShowNotificationTargetMissingAsync_ExplainsDeletedGame()
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light);
        GameManager manager = new(
            new InMemoryDataStore(),
            new FakeClock(DateTimeOffset.UtcNow),
            settings);
        await manager.InitializeAsync(
            new DataEnvelope(
                DataEnvelope.CurrentSchemaVersion,
                ImmutableArray<GameEntry>.Empty,
                settings),
            CancellationToken.None);
        OverviewViewModel viewModel = new(
            manager,
            new FakeClock(DateTimeOffset.UtcNow),
            new RecordingUiDispatcher(),
            new AppResourceService(resourceId => resourceId));

        await viewModel.ShowNotificationTargetMissingAsync();

        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual(
            "OverviewNotificationTargetMissingError",
            viewModel.ErrorMessage);
    }

    private sealed class InMemoryDataStore : ILocalDataStore
    {
        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => throw new
                NotSupportedException();

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) => throw new
                NotSupportedException();
    }
}
