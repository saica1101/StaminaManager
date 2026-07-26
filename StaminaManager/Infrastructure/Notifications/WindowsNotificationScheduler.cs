using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Core.Abstractions;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace StaminaManager.Infrastructure.Notifications;

public sealed class WindowsNotificationScheduler
    : INotificationScheduler
{
    internal const string NotificationGroup = "stamina";
    private const string GameIdArgumentName = "gameId";
    private readonly IWindowsNotificationPlatformAdapter _adapter;
    private readonly Lock _activationGate = new();
    private readonly Queue<NotificationActivationEventArgs>
        _pendingActivations = new();
    private EventHandler<NotificationActivationEventArgs>?
        _activationRequested;
    private bool _isInitialized;
    private bool _isDisposed;

    public WindowsNotificationScheduler()
        : this(new WindowsNotificationPlatformAdapter())
    {
    }

    internal WindowsNotificationScheduler(
        IWindowsNotificationPlatformAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
    }

    public event EventHandler<NotificationActivationEventArgs>?
        ActivationRequested
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            NotificationActivationEventArgs[] pending;
            lock (_activationGate)
            {
                _activationRequested += value;
                pending = [.. _pendingActivations];
                _pendingActivations.Clear();
            }

            foreach (NotificationActivationEventArgs activation in pending)
            {
                value(this, activation);
            }
        }
        remove
        {
            lock (_activationGate)
            {
                _activationRequested -= value;
            }
        }
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isInitialized)
        {
            return;
        }

        _adapter.ActivationReceived += OnActivationReceived;
        try
        {
            _adapter.Register();
            _isInitialized = true;
        }
        catch
        {
            _adapter.ActivationReceived -= OnActivationReceived;
            throw;
        }
    }

    public Task<IReadOnlySet<Guid>> GetScheduledGameIdsAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<Guid> gameIds = [];
        foreach (WindowsScheduledNotification notification in
            _adapter.GetScheduled())
        {
            if (IsOwned(notification)
                && Guid.TryParseExact(
                    notification.Tag,
                    "N",
                    out Guid gameId))
            {
                gameIds.Add(gameId);
            }
        }

        return Task.FromResult((IReadOnlySet<Guid>)gameIds);
    }

    public Task ScheduleAsync(
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RemoveScheduled(request.GameId);
        _adapter.AddToSchedule(request);
        return Task.CompletedTask;
    }

    public Task ShowImmediateAsync(
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_adapter.Show(request))
        {
            throw new InvalidOperationException(
                "Windowsが通知を受理しませんでした。");
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        RemoveScheduled(gameId);
        return Task.CompletedTask;
    }

    public Task CancelAllAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (WindowsScheduledNotification notification in
            _adapter.GetScheduled().Where(IsOwned).ToArray())
        {
            _adapter.Remove(notification);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isInitialized)
        {
            try
            {
                _adapter.ActivationReceived -= OnActivationReceived;
                _adapter.Unregister();
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Notification unregistration failed: "
                    + exception.GetType().Name);
            }
        }
    }

    private void RemoveScheduled(Guid gameId)
    {
        string tag = gameId.ToString("N");
        foreach (WindowsScheduledNotification notification in
            _adapter.GetScheduled().Where(notification =>
                IsOwned(notification)
                && string.Equals(
                    notification.Tag,
                    tag,
                    StringComparison.Ordinal)).ToArray())
        {
            _adapter.Remove(notification);
        }
    }

    private void OnActivationReceived(object? sender, string argument)
    {
        if (!TryParseGameId(argument, out Guid gameId))
        {
            return;
        }

        NotificationActivationEventArgs activation = new(gameId);
        EventHandler<NotificationActivationEventArgs>? handler;
        lock (_activationGate)
        {
            handler = _activationRequested;
            if (handler is null)
            {
                _pendingActivations.Enqueue(activation);
                return;
            }
        }

        handler(this, activation);
    }

    internal static bool TryParseGameId(
        string argument,
        out Guid gameId)
    {
        gameId = Guid.Empty;
        string prefix = GameIdArgumentName + "=";
        if (!argument.StartsWith(prefix, StringComparison.Ordinal)
            || argument.Length == prefix.Length
            || argument.Contains('&', StringComparison.Ordinal))
        {
            return false;
        }

        string encoded = argument[prefix.Length..];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(encoded.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return false;
        }

        return Guid.TryParseExact(decoded, "N", out gameId);
    }

    private static bool IsOwned(
        WindowsScheduledNotification notification) => string.Equals(
            notification.Group,
            NotificationGroup,
            StringComparison.Ordinal);

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}

internal sealed record WindowsScheduledNotification(
    string Tag,
    string Group,
    object? NativeNotification = null);

internal interface IWindowsNotificationPlatformAdapter
{
    event EventHandler<string>? ActivationReceived;

    void Register();

    void Unregister();

    IReadOnlyList<WindowsScheduledNotification> GetScheduled();

    void AddToSchedule(NotificationRequest request);

    bool Show(NotificationRequest request);

    void Remove(WindowsScheduledNotification notification);
}

internal sealed class WindowsNotificationPlatformAdapter
    : IWindowsNotificationPlatformAdapter
{
    private readonly AppNotificationManager _manager =
        AppNotificationManager.Default;
    private readonly ToastNotifier _notifier =
        ToastNotificationManager.CreateToastNotifier();

    public event EventHandler<string>? ActivationReceived;

    public void Register()
    {
        _manager.NotificationInvoked += OnNotificationInvoked;
        try
        {
            _manager.Register();
        }
        catch
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            throw;
        }
    }

    public void Unregister()
    {
        _manager.NotificationInvoked -= OnNotificationInvoked;
        _manager.Unregister();
    }

    public IReadOnlyList<WindowsScheduledNotification> GetScheduled() =>
        _notifier.GetScheduledToastNotifications()
            .Select(notification => new WindowsScheduledNotification(
                notification.Tag,
                notification.Group,
                notification))
            .ToArray();

    public void AddToSchedule(NotificationRequest request)
    {
        XmlDocument document = new();
        document.LoadXml(BuildNotification(request).Payload);
        ScheduledToastNotification notification = new(
            document,
            request.NotificationAtUtc)
        {
            Tag = request.GameId.ToString("N"),
            Group = WindowsNotificationScheduler.NotificationGroup,
        };
        _notifier.AddToSchedule(notification);
    }

    public bool Show(NotificationRequest request)
    {
        AppNotification notification = BuildNotification(request);
        notification.Tag = request.GameId.ToString("N");
        notification.Group = WindowsNotificationScheduler.NotificationGroup;
        _manager.Show(notification);
        return notification.Id != 0;
    }

    public void Remove(WindowsScheduledNotification notification)
    {
        if (notification.NativeNotification
            is not ScheduledToastNotification native)
        {
            throw new ArgumentException(
                "Windows予約通知の参照がありません。",
                nameof(notification));
        }

        _notifier.RemoveFromSchedule(native);
    }

    private static AppNotification BuildNotification(
        NotificationRequest request)
    {
        string detailFormat = GetResourceText(
            "StaminaNotificationDetailFormat",
            "全回復予定: {0:g}");
        string detail = string.Format(
            CultureInfo.CurrentCulture,
            detailFormat,
            request.FullAtUtc.ToLocalTime());
        return new AppNotificationBuilder()
            .AddArgument("gameId", request.GameId.ToString("N"))
            .AddText(request.GameName)
            .AddText(detail)
            .BuildNotification();
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args) =>
        ActivationReceived?.Invoke(this, args.Argument);

    private static string GetResourceText(
        string resourceId,
        string fallback)
    {
        try
        {
            string value = new ResourceLoader().GetString(resourceId);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (Exception exception) when (
            exception is COMException
                or ArgumentException
                or InvalidOperationException)
        {
            Debug.WriteLine(
                "Notification resource resolution failed: "
                + exception.GetType().Name);
            return fallback;
        }
    }
}
