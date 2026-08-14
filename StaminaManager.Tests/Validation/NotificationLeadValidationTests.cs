using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Validation;

[TestClass]
public sealed class NotificationLeadValidationTests
{
    private static readonly DateTimeOffset RecordedAtUtc = new(
        2026,
        8,
        14,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    [DataRow(-1)]
    [DataRow(525_601)]
    public void Validate_RejectsNotificationLeadOverrideOutsideAllowedRange(
        int leadMinutes)
    {
        GameDraft draft = CreateValidDraft() with
        {
            NotificationLeadMinutesOverride = leadMinutes,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(
            ValidationErrorCode.NotificationLeadMinutesOverrideOutOfRange,
            result.Errors[nameof(GameDraft.NotificationLeadMinutesOverride)]
                .Single());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(525_600)]
    public void Validate_AcceptsNotificationLeadOverrideBoundaryValues(
        int leadMinutes)
    {
        GameDraft draft = CreateValidDraft() with
        {
            NotificationLeadMinutesOverride = leadMinutes,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    public void Validate_AcceptsNullNotificationLeadOverride()
    {
        ValidationResult result = GameEntryValidator.Validate(
            CreateValidDraft() with
            {
                NotificationLeadMinutesOverride = null,
            },
            RecordedAtUtc);

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
    }

    private static GameDraft CreateValidDraft() => new(
        Name: "Test game",
        CurrentStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        ImageAssetId: null,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);
}
