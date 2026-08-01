using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Validation;

[TestClass]
public sealed class BackdropPolicyTests
{
    [TestMethod]
    [DataRow(0, BackdropKind.Mica)]
    [DataRow(1, BackdropKind.Acrylic)]
    [DataRow(2, BackdropKind.Solid)]
    public void TryFromSelectionIndex_選択可能な3背景だけを返す(
        int index,
        BackdropKind expected)
    {
        bool found = BackdropPolicy.TryFromSelectionIndex(
            index,
            out BackdropKind actual);

        Assert.IsTrue(found);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(3)]
    [DataRow(int.MaxValue)]
    public void TryFromSelectionIndex_範囲外を拒否する(int index)
    {
        bool found = BackdropPolicy.TryFromSelectionIndex(
            index,
            out BackdropKind actual);

        Assert.IsFalse(found);
        Assert.AreEqual(default, actual);
    }

    [TestMethod]
    [DataRow(BackdropKind.Mica, 0)]
    [DataRow(BackdropKind.Acrylic, 1)]
    [DataRow(BackdropKind.Solid, 2)]
    [DataRow(BackdropKind.Blur, 1)]
    [DataRow(BackdropKind.Transparent, 1)]
    public void ToSelectionIndex_旧背景をAcrylicへ同期する(
        BackdropKind backdrop,
        int expected)
    {
        Assert.AreEqual(
            expected,
            BackdropPolicy.ToSelectionIndex(backdrop));
    }

    [TestMethod]
    [DataRow(BackdropKind.Blur)]
    [DataRow(BackdropKind.Transparent)]
    public void NormalizeLegacy_旧背景だけをAcrylicへ変換する(
        BackdropKind backdrop)
    {
        Assert.AreEqual(
            BackdropKind.Acrylic,
            BackdropPolicy.NormalizeLegacy(backdrop));
    }

    [TestMethod]
    public void NormalizeLegacy_未知値を拒否する()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => BackdropPolicy.NormalizeLegacy((BackdropKind)999));
    }
}
