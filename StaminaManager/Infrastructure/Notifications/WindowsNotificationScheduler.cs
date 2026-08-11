using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using System.Diagnostics;
using System.Globalization;
using Windows.ApplicationModel;
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
    private readonly Func<string, string?> _resolveResource;
    private readonly Lazy<AppNotificationManager> _manager = new(
        static () => AppNotificationManager.Default,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<ToastNotifier> _notifier = new(
        static () => ToastNotificationManager.CreateToastNotifier(
            AppInfo.Current.AppUserModelId),
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal WindowsNotificationPlatformAdapter()
        : this(resourceId => LateBoundResourceText.Resolve(
            resourceId,
            "Notification"))
    {
    }

    internal WindowsNotificationPlatformAdapter(
        Func<string, string?> resolveResource)
    {
        ArgumentNullException.ThrowIfNull(resolveResource);
        _resolveResource = resolveResource;
    }

    public event EventHandler<string>? ActivationReceived;

    public void Register()
    {
        Manager.NotificationInvoked += OnNotificationInvoked;
        try
        {
            Manager.Register();
        }
        catch
        {
            Manager.NotificationInvoked -= OnNotificationInvoked;
            throw;
        }
    }

    public void Unregister()
    {
        Manager.NotificationInvoked -= OnNotificationInvoked;
        Manager.Unregister();
    }

    public IReadOnlyList<WindowsScheduledNotification> GetScheduled() =>
        Notifier.GetScheduledToastNotifications()
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
        Notifier.AddToSchedule(notification);
    }

    public bool Show(NotificationRequest request)
    {
        AppNotification notification = BuildNotification(request);
        notification.Tag = request.GameId.ToString("N");
        notification.Group = WindowsNotificationScheduler.NotificationGroup;
        Manager.Show(notification);
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

        Notifier.RemoveFromSchedule(native);
    }

    private AppNotificationManager Manager => _manager.Value;

    private ToastNotifier Notifier => _notifier.Value;

    internal AppNotification BuildNotification(
        NotificationRequest request)
    {
        AppNotificationBuilder builder = new AppNotificationBuilder()
            .AddArgument("gameId", request.GameId.ToString("N"))
            .AddText(request.GameName);
        string? detail = TryFormatDetail(
            request.FullAtUtc,
            _resolveResource);
        if (detail is not null)
        {
            builder.AddText(detail);
        }

        return builder.BuildNotification();
    }

    internal static string? TryFormatDetail(
        DateTimeOffset fullAtUtc,
        Func<string, string?> resolveResource)
        => TryFormatDetail(
            fullAtUtc,
            resolveResource,
            LateBoundResourceText.GetEffectiveLanguage());

    internal static string? TryFormatDetail(
        DateTimeOffset fullAtUtc,
        Func<string, string?> resolveResource,
        AppLanguage fallbackLanguage)
    {
        ArgumentNullException.ThrowIfNull(resolveResource);
        string? detailFormat = LateBoundResourceText.TryGet(
            "StaminaNotificationDetailFormat",
            "Notification",
            resolveResource);
        if (TryFormatDetail(
            fullAtUtc,
            detailFormat,
            out string? detail))
        {
            return detail;
        }

        string fallbackFormat = LateBoundResourceText.GetFallback(
            "StaminaNotificationDetailFormat",
            fallbackLanguage);
        return string.Format(
            CultureInfo.CurrentCulture,
            fallbackFormat,
            fullAtUtc.ToLocalTime());
    }

    private static bool TryFormatDetail(
        DateTimeOffset fullAtUtc,
        string? detailFormat,
        out string? detail)
    {
        detail = null;
        if (string.IsNullOrWhiteSpace(detailFormat))
        {
            return false;
        }

        try
        {
            detail = string.Format(
                CultureInfo.CurrentCulture,
                detailFormat,
                fullAtUtc.ToLocalTime());
            return !string.IsNullOrWhiteSpace(detail);
        }
        catch (FormatException exception)
        {
            Debug.WriteLine(
                "Notification resource formatting failed: "
                + exception.GetType().Name);
            return false;
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args) =>
        ActivationReceived?.Invoke(this, args.Argument);
}
