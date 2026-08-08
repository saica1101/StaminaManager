using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Validation;

[TestClass]
public sealed class LanguagePolicyTests
{
    [TestMethod]
    public void SelectionIndex_RoundTripsTheExplicitLanguageMapping()
    {
        Assert.AreEqual(0, LanguagePolicy.ToSelectionIndex(
            AppLanguage.Japanese));
        Assert.AreEqual(1, LanguagePolicy.ToSelectionIndex(
            AppLanguage.English));

        Assert.IsTrue(LanguagePolicy.TryFromSelectionIndex(
            0,
            out AppLanguage japanese));
        Assert.AreEqual(AppLanguage.Japanese, japanese);
        Assert.IsTrue(LanguagePolicy.TryFromSelectionIndex(
            1,
            out AppLanguage english));
        Assert.AreEqual(AppLanguage.English, english);
    }

    [TestMethod]
    public void SelectionIndex_InvalidValueIsRejected()
    {
        Assert.IsFalse(LanguagePolicy.TryFromSelectionIndex(
            -1,
            out _));
        Assert.IsFalse(LanguagePolicy.TryFromSelectionIndex(
            2,
            out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LanguagePolicy.ToSelectionIndex((AppLanguage)999));
    }
}
