using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace StaminaManager.Core.Models;

public sealed record NotificationLedgerEntry(
    string Key,
    Guid GameId,
    long FullAtUtcTicks,
    int LeadMinutes,
    long NotificationAtUtcTicks,
    NotificationState State,
    string ContentFingerprint)
{
    public static NotificationLedgerEntry Create(
        GameEntry game,
        DateTimeOffset fullAtUtc,
        int leadMinutes,
        NotificationState state)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!NotificationCycle.TryCreate(
            game,
            fullAtUtc,
            leadMinutes,
            state,
            out NotificationLedgerEntry? entry))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadMinutes),
                "通知時刻が DateTimeOffset の表現範囲外です。");
        }

        return entry!;
    }
}

internal static class NotificationCycle
{
    public static bool TryCreate(
        GameEntry game,
        DateTimeOffset fullAtUtc,
        int leadMinutes,
        NotificationState state,
        [NotNullWhen(true)] out NotificationLedgerEntry? entry)
    {
        entry = null;
        if (leadMinutes < 0)
        {
            return false;
        }

        fullAtUtc = fullAtUtc.ToUniversalTime();
        long notificationAtUtcTicks;
        try
        {
            long leadTicks = checked(
                (long)leadMinutes * TimeSpan.TicksPerMinute);
            notificationAtUtcTicks = checked(
                fullAtUtc.UtcTicks - leadTicks);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (notificationAtUtcTicks < DateTimeOffset.MinValue.UtcTicks)
        {
            return false;
        }

        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"{game.Id:N}/{fullAtUtc.UtcTicks}/{leadMinutes}");
        entry = new NotificationLedgerEntry(
            key,
            game.Id,
            fullAtUtc.UtcTicks,
            leadMinutes,
            notificationAtUtcTicks,
            state,
            CreateContentFingerprint(game.Name, game.ImageAssetId));
        return true;
    }

    private static string CreateContentFingerprint(
        string gameName,
        string? imageAssetId)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(gameName);
        byte[] imageBytes = imageAssetId is null
            ? []
            : Encoding.UTF8.GetBytes(imageAssetId);
        byte[] fingerprintInput = new byte[
            sizeof(int) + nameBytes.Length
            + sizeof(int) + imageBytes.Length];
        Span<byte> input = fingerprintInput;
        BinaryPrimitives.WriteInt32LittleEndian(
            input,
            nameBytes.Length);
        nameBytes.CopyTo(input[sizeof(int)..]);
        int imageLengthOffset = sizeof(int) + nameBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(
            input[imageLengthOffset..],
            imageAssetId is null ? -1 : imageBytes.Length);
        imageBytes.CopyTo(input[(imageLengthOffset + sizeof(int))..]);
        return Convert.ToHexString(SHA256.HashData(fingerprintInput));
    }
}
