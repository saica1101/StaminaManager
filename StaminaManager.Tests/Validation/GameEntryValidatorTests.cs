using StaminaManager.Core.Validation;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Validation;

[TestClass]
public sealed class GameEntryValidatorTests
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
    public void Limits_AreExposedAsNamedConstants()
    {
        AssertPublicConstant(
            nameof(GameEntryValidator.MaxStaminaValue),
            1_000_000);
        AssertPublicConstant(
            nameof(GameEntryValidator.MaxRecoveryMinutes),
            525_600);
        AssertPublicConstant(
            nameof(GameEntryValidator.MaxGameCount),
            100);
    }

    [TestMethod]
    public void RecoveryIntervalField_IsExposedAsNamedConstant()
    {
        AssertPublicConstant(
            nameof(GameEntryValidator.RecoveryIntervalField),
            "RecoveryInterval");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void Validate_RejectsBlankName(string name)
    {
        GameDraft draft = CreateValidDraft() with { Name = name };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(
            result.Errors.Keys.ToArray(),
            nameof(GameDraft.Name));
        AssertFieldError(
            result,
            nameof(GameDraft.Name),
            ValidationErrorCode.NameRequired);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1_000_001)]
    public void Validate_RejectsCurrentStaminaOutsideAllowedRange(
        int currentStamina)
    {
        GameDraft draft = CreateValidDraft() with
        {
            CurrentStamina = currentStamina,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        AssertFieldError(
            result,
            nameof(GameDraft.CurrentStamina),
            ValidationErrorCode.CurrentStaminaOutOfRange);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1_000_001)]
    public void Validate_RejectsMaxStaminaOutsideAllowedRange(
        int maxStamina)
    {
        GameDraft draft = CreateValidDraft() with
        {
            MaxStamina = maxStamina,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        AssertFieldError(
            result,
            nameof(GameDraft.MaxStamina),
            ValidationErrorCode.MaxStaminaOutOfRange);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(525_601)]
    public void Validate_RejectsRecoveryMinutesOutsideAllowedRange(
        int recoveryMinutes)
    {
        GameDraft draft = CreateValidDraft() with
        {
            RecoveryMinutes = recoveryMinutes,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        AssertFieldError(
            result,
            nameof(GameDraft.RecoveryMinutes),
            ValidationErrorCode.RecoveryMinutesOutOfRange);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(525_600, 1)]
    public void Validate_RejectsRecoveryIntervalOutsideAllowedRange(
        int recoveryMinutes,
        int recoverySeconds)
    {
        GameDraft draft = CreateValidDraft() with
        {
            RecoveryMinutes = recoveryMinutes,
            RecoverySeconds = recoverySeconds,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        AssertFieldError(
            result,
            GameEntryValidator.RecoveryIntervalField,
            ValidationErrorCode.RecoveryIntervalOutOfRange);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(60)]
    public void Validate_RejectsRecoverySecondsOutsideAllowedRange(
        int recoverySeconds)
    {
        GameDraft draft = CreateValidDraft() with
        {
            RecoverySeconds = recoverySeconds,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        AssertFieldError(
            result,
            nameof(GameDraft.RecoverySeconds),
            ValidationErrorCode.RecoverySecondsOutOfRange);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(0, 59)]
    [DataRow(1, 0)]
    [DataRow(525_600, 0)]
    public void Validate_AcceptsRecoveryIntervalBoundaryValues(
        int recoveryMinutes,
        int recoverySeconds)
    {
        GameDraft draft = CreateValidDraft() with
        {
            RecoveryMinutes = recoveryMinutes,
            RecoverySeconds = recoverySeconds,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    public void Validate_AcceptsCurrentStaminaAboveMaximum()
    {
        GameDraft draft = CreateValidDraft() with
        {
            CurrentStamina = 101,
            MaxStamina = 100,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow(0, 1, 1)]
    [DataRow(1_000_000, 1_000_000, 525_600)]
    public void Validate_AcceptsNumericBoundaryValues(
        int currentStamina,
        int maxStamina,
        int recoveryMinutes)
    {
        GameDraft draft = CreateValidDraft() with
        {
            CurrentStamina = currentStamina,
            MaxStamina = maxStamina,
            RecoveryMinutes = recoveryMinutes,
        };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    public void Validate_ReturnsFieldErrorWhenFullTimeIsOutOfRange()
    {
        GameDraft draft = CreateValidDraft() with
        {
            CurrentStamina = 0,
            MaxStamina = 2,
            RecoveryMinutes = 1,
        };
        DateTimeOffset proposedRecordedAtUtc =
            DateTimeOffset.MaxValue.AddMinutes(-1);

        ValidationResult result =
            GameEntryValidator.Validate(draft, proposedRecordedAtUtc);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(
            result.Errors.Keys.ToArray(),
            nameof(GameDraft.MaxStamina));
        AssertFieldError(
            result,
            nameof(GameDraft.MaxStamina),
            ValidationErrorCode.FullTimeOutOfRange);
    }

    [TestMethod]
    public void Validate_DoesNotCalculateFullTimeWhenBasicFieldsAreInvalid()
    {
        GameDraft draft = new(
            Name: " ",
            CurrentStamina: -1,
            MaxStamina: 0,
            RecoveryMinutes: 0,
            ImageAssetId: null,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);

        ValidationResult result = GameEntryValidator.Validate(
            draft,
            DateTimeOffset.MaxValue);

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(4, result.Errors);
    }

    [TestMethod]
    public void Validate_ThrowsWhenDraftIsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => GameEntryValidator.Validate(null!, RecordedAtUtc));
    }

    [TestMethod]
    public void Validate_ExposesErrorsAsImmutableCollections()
    {
        GameDraft draft = CreateValidDraft() with { Name = string.Empty };

        ValidationResult result =
            GameEntryValidator.Validate(draft, RecordedAtUtc);

        Assert.IsInstanceOfType<
            ImmutableDictionary<
                string,
                ImmutableArray<ValidationErrorCode>>>(
                result.Errors);
    }

    private static GameDraft CreateValidDraft() => new(
        Name: "Test game",
        CurrentStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        ImageAssetId: null,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private static void AssertPublicConstant(string name, object expected)
    {
        System.Reflection.FieldInfo? field =
            typeof(GameEntryValidator).GetField(name);

        Assert.IsNotNull(field);
        Assert.IsTrue(field.IsPublic);
        Assert.IsTrue(field.IsLiteral);
        Assert.AreEqual(expected, field.GetRawConstantValue());
    }

    private static void AssertFieldError(
        ValidationResult result,
        string fieldKey,
        ValidationErrorCode expectedCode)
    {
        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Errors.Keys.ToArray(), fieldKey);
        Assert.AreEqual(
            expectedCode,
            result.Errors[fieldKey].Single());
    }
}
