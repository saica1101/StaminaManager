using StaminaManager.Core.Models;
using StaminaManager.ViewModels;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class GameCardViewModelTests
{
    private static readonly DateTimeOffset NowUtc = new(
        2026,
        8,
        14,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public void UpdateCurrentCommand_RaisesRequestForTheCardGame()
    {
        GameEntry entry = CreateEntry();
        Guid? requestedGameId = null;
        GameCardViewModel viewModel = new(
            entry,
            NowUtc,
            updateCurrentRequested: gameId => requestedGameId = gameId);

        viewModel.UpdateCurrentCommand.Execute(null);

        Assert.AreEqual(entry.Id, requestedGameId);
    }

    [TestMethod]
    public void EditCommand_RaisesRequestForTheCardGame()
    {
        GameEntry entry = CreateEntry();
        Guid? requestedGameId = null;
        GameCardViewModel viewModel = new(
            entry,
            NowUtc,
            editRequested: gameId => requestedGameId = gameId);

        viewModel.EditCommand.Execute(null);

        Assert.AreEqual(entry.Id, requestedGameId);
    }

    private static GameEntry CreateEntry() => new(
        Guid.NewGuid(),
        "Test game",
        BaseStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc: NowUtc,
        ImageAssetId: null,
        SortOrder: 0,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);
}
