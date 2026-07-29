using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Notifications;
using StaminaManager.Infrastructure.Persistence;
using System.Text;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class NotificationLedgerStoreTests
{
    private string _rootPath = null!;
    private NotificationLedgerStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _rootPath = Path.Combine(
            Path.GetTempPath(),
            $"StaminaManagerNotificationTests-{Guid.NewGuid():N}");
        _store = new NotificationLedgerStore(
            new TestAppDataPathProvider(_rootPath));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_RoundTripsEntriesAtomically()
    {
        NotificationLedgerEntry scheduled = CreateEntry(
            NotificationState.Scheduled);
        NotificationLedgerEntry consumed = CreateEntry(
            NotificationState.Consumed,
            leadMinutes: 20);

        await _store.SaveAsync(
            [scheduled, consumed],
            CancellationToken.None);
        IReadOnlyList<NotificationLedgerEntry> loaded =
            await _store.LoadAsync(CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[] { scheduled, consumed },
            loaded.ToArray());
        Assert.IsTrue(File.Exists(LedgerPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
        Assert.IsFalse(File.Exists(Path.Combine(_rootPath, "data.json")));
    }

    [TestMethod]
    public async Task LoadAsync_MissingFileReturnsEmptyLedger()
    {
        IReadOnlyList<NotificationLedgerEntry> loaded =
            await _store.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(loaded);
    }

    [TestMethod]
    public async Task LoadAsync_MalformedLedgerIsRejectedWithoutMutation()
    {
        Directory.CreateDirectory(_rootPath);
        byte[] malformed = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"entries\":[{\"key\":\"bad\"}]}");
        await File.WriteAllBytesAsync(LedgerPath, malformed);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => _store.LoadAsync(CancellationToken.None));

        CollectionAssert.AreEqual(
            malformed,
            await File.ReadAllBytesAsync(LedgerPath));
    }

    [TestMethod]
    public async Task LoadAsync_DuplicateCycleKeyIsRejected()
    {
        NotificationLedgerEntry entry = CreateEntry(
            NotificationState.Consumed);
        await _store.SaveAsync([entry], CancellationToken.None);
        string json = await File.ReadAllTextAsync(LedgerPath);
        string duplicate = json.Replace(
            $"[{SerializeEntry(json)}]",
            $"[{SerializeEntry(json)},{SerializeEntry(json)}]",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(LedgerPath, duplicate);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => _store.LoadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_InvalidKeyIsRejectedBeforeWriting()
    {
        NotificationLedgerEntry invalid = CreateEntry(
            NotificationState.Scheduled) with
        {
            Key = "different",
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => _store.SaveAsync([invalid], CancellationToken.None));

        Assert.IsFalse(File.Exists(LedgerPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
    }

    private string LedgerPath => Path.Combine(
        _rootPath,
        "notification-state.json");

    private string TemporaryPath => Path.Combine(
        _rootPath,
        "notification-state.json.tmp");

    private static NotificationLedgerEntry CreateEntry(
        NotificationState state,
        int leadMinutes = 15)
    {
        GameEntry game = new(
            Guid.Parse("EAF52F97-591F-468B-A17B-153551A2053E"),
            "Test game",
            BaseStamina: 90,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: new DateTimeOffset(
                2026,
                7,
                25,
                0,
                0,
                0,
                TimeSpan.Zero),
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);
        return NotificationLedgerEntry.Create(
            game,
            game.RecordedAtUtc.AddMinutes(50),
            leadMinutes,
            state);
    }

    private static string SerializeEntry(string ledgerJson)
    {
        const string prefix = "\"entries\":[";
        int start = ledgerJson.IndexOf(
            prefix,
            StringComparison.Ordinal) + prefix.Length;
        int end = ledgerJson.LastIndexOf(']');
        return ledgerJson[start..end];
    }

    private sealed class TestAppDataPathProvider(string rootPath)
        : IAppDataPathProvider
    {
        public string DataRootPath { get; } = rootPath;
    }
}
