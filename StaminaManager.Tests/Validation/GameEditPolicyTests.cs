using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Validation;

[TestClass]
public sealed class GameEditPolicyTests
{
    private static readonly DateTimeOffset RecordedAtUtc = new(
        2026,
        7,
        25,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public void Apply_PreservesBaseAndRecordedTimeForMetadataOnlyEdit()
    {
        GameEntry original = CreateOriginal();
        GameDraft initialDraft = CreateInitialDraft();
        GameDraft editedDraft = initialDraft with
        {
            Name = "Edited game",
            ImageAssetId = "edited-image",
        };

        GameEntry result = GameEditPolicy.Apply(
            original,
            initialDraft,
            editedDraft,
            RecordedAtUtc.AddHours(1));

        Assert.AreEqual(original.Id, result.Id);
        Assert.AreEqual(original.SortOrder, result.SortOrder);
        Assert.AreEqual("Edited game", result.Name);
        Assert.AreEqual("edited-image", result.ImageAssetId);
        Assert.AreEqual(original.BaseStamina, result.BaseStamina);
        Assert.AreEqual(original.RecordedAtUtc, result.RecordedAtUtc);
        Assert.AreEqual(100, result.MaxStamina);
        Assert.AreEqual(5, result.RecoveryMinutes);
    }

    [TestMethod]
    public void Apply_ResetsBaseAndRecordedTimeWhenCurrentStaminaChanges()
    {
        GameDraft initialDraft = CreateInitialDraft();
        GameDraft editedDraft = initialDraft with
        {
            CurrentStamina = 60,
        };
        DateTimeOffset savedAt = RecordedAtUtc
            .AddHours(10)
            .ToOffset(TimeSpan.FromHours(9));

        GameEntry result = GameEditPolicy.Apply(
            CreateOriginal(),
            initialDraft,
            editedDraft,
            savedAt);

        Assert.AreEqual(60, result.BaseStamina);
        Assert.AreEqual(savedAt.ToUniversalTime(), result.RecordedAtUtc);
    }

    [TestMethod]
    public void Apply_ResetsBaseAndRecordedTimeWhenMaximumChanges()
    {
        GameDraft initialDraft = CreateInitialDraft();
        GameDraft editedDraft = initialDraft with { MaxStamina = 120 };
        DateTimeOffset savedAt = RecordedAtUtc.AddHours(1);

        GameEntry result = GameEditPolicy.Apply(
            CreateOriginal(),
            initialDraft,
            editedDraft,
            savedAt);

        Assert.AreEqual(50, result.BaseStamina);
        Assert.AreEqual(120, result.MaxStamina);
        Assert.AreEqual(savedAt, result.RecordedAtUtc);
    }

    [TestMethod]
    public void Apply_ResetsBaseAndRecordedTimeWhenRecoveryChanges()
    {
        GameDraft initialDraft = CreateInitialDraft();
        GameDraft editedDraft = initialDraft with { RecoveryMinutes = 10 };
        DateTimeOffset savedAt = RecordedAtUtc.AddHours(1);

        GameEntry result = GameEditPolicy.Apply(
            CreateOriginal(),
            initialDraft,
            editedDraft,
            savedAt);

        Assert.AreEqual(50, result.BaseStamina);
        Assert.AreEqual(10, result.RecoveryMinutes);
        Assert.AreEqual(savedAt, result.RecordedAtUtc);
    }

    [TestMethod]
    public void Apply_UsesEditedStaminaSettingsForMetadataOnlyEdit()
    {
        GameEntry original = CreateOriginal() with
        {
            MaxStamina = 80,
            RecoveryMinutes = 3,
        };
        GameDraft initialDraft = CreateInitialDraft();
        GameDraft editedDraft = initialDraft with { Name = "Renamed" };

        GameEntry result = GameEditPolicy.Apply(
            original,
            initialDraft,
            editedDraft,
            RecordedAtUtc.AddHours(1));

        Assert.AreEqual(initialDraft.MaxStamina, result.MaxStamina);
        Assert.AreEqual(
            initialDraft.RecoveryMinutes,
            result.RecoveryMinutes);
    }

    [TestMethod]
    public void Apply_ThrowsWhenAnyRequiredArgumentIsNull()
    {
        GameEntry original = CreateOriginal();
        GameDraft draft = CreateInitialDraft();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => GameEditPolicy.Apply(
                null!, draft, draft, RecordedAtUtc));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => GameEditPolicy.Apply(
                original, null!, draft, RecordedAtUtc));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => GameEditPolicy.Apply(
                original, draft, null!, RecordedAtUtc));
    }

    private static GameEntry CreateOriginal() => new(
        Id: Guid.Parse("ed0a7230-e5f6-4e07-8e24-92fbf06e08d1"),
        Name: "Original game",
        BaseStamina: 20,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc: RecordedAtUtc,
        ImageAssetId: "original-image",
        SortOrder: 7);

    private static GameDraft CreateInitialDraft() => new(
        Name: "Original game",
        CurrentStamina: 50,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        ImageAssetId: "original-image");
}
