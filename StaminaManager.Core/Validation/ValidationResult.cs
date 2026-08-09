using System.Collections.Immutable;

namespace StaminaManager.Core.Validation;

public sealed class ValidationResult
{
    internal ValidationResult(
        ImmutableDictionary<
            string,
            ImmutableArray<ValidationErrorCode>> errors)
    {
        Errors = errors;
    }

    public bool IsValid => Errors.IsEmpty;

    public ImmutableDictionary<
        string,
        ImmutableArray<ValidationErrorCode>> Errors
    {
        get;
    }
}
