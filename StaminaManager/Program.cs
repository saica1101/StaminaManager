using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Infrastructure.Notifications;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace StaminaManager;

public static class Program
{
    private const string MainInstanceKey = "StaminaManager.Main";
    private const uint Infinite = 0xFFFFFFFF;
    private static AppInstance? _mainInstance;

    [STAThread]
    public static int Main(string[] args)
    {
        _ = args;
        WinRT.ComWrappersSupport.InitializeComWrappers();

        INotificationScheduler notificationScheduler =
            new WindowsNotificationScheduler();
        (AppInstance Current, AppActivationArguments Activation)
            activationContext;
        try
        {
            activationContext = InitializeNotificationsBeforeActivation(
                notificationScheduler,
                () =>
                {
                    AppInstance current = AppInstance.GetCurrent();
                    return (current, current.GetActivatedEventArgs());
                });
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Early notification registration failed: "
                + exception.GetType().Name);
            notificationScheduler.Dispose();
            return 1;
        }

        AppInstance currentInstance = activationContext.Current;
        AppActivationArguments activationArguments =
            activationContext.Activation;
        AppInstance mainInstance = AppInstance.FindOrRegisterForKey(
            MainInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            try
            {
                RedirectActivation(activationArguments, mainInstance);
            }
            finally
            {
                notificationScheduler.Dispose();
            }

            return 0;
        }

        _mainInstance = mainInstance;
        _mainInstance.Activated += OnActivated;
        if (activationArguments.Kind
            == ExtendedActivationKind.AppNotification)
        {
            ActivationRouter.Enqueue(activationArguments);
        }

        Microsoft.UI.Xaml.Application.Start(initializationParameters =>
        {
            _ = initializationParameters;
            DispatcherQueue dispatcherQueue =
                DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcherQueue));
            new App(notificationScheduler);
        });
        return 0;
    }

    private static void RedirectActivation(
        AppActivationArguments activationArguments,
        AppInstance mainInstance)
    {
        using EventWaitHandle completed = new(
            initialState: false,
            EventResetMode.ManualReset);
        ExceptionDispatchInfo? failure = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await mainInstance.RedirectActivationToAsync(
                    activationArguments);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                _ = completed.Set();
            }
        });

        int result = CoWaitForMultipleObjects(
            flags: 0,
            milliseconds: Infinite,
            handleCount: 1,
            [completed.SafeWaitHandle.DangerousGetHandle()],
            out _);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        failure?.Throw();
    }

    private static void OnActivated(
        object? sender,
        AppActivationArguments activationArguments) =>
        ActivationRouter.Enqueue(activationArguments);

    internal static T InitializeNotificationsBeforeActivation<T>(
        INotificationScheduler notificationScheduler,
        Func<T> readActivation)
    {
        ArgumentNullException.ThrowIfNull(notificationScheduler);
        ArgumentNullException.ThrowIfNull(readActivation);
        // 通知COM activationでは、activation引数を読む前の登録が必須。
        notificationScheduler.Initialize();
        return readActivation();
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoWaitForMultipleObjects(
        uint flags,
        uint milliseconds,
        uint handleCount,
        [In] nint[] handles,
        out uint index);
}

internal static class ActivationRouter
{
    private static readonly ActivationQueue<AppActivationArguments> Queue =
        new();

    public static void Enqueue(AppActivationArguments activationArguments) =>
        Queue.Enqueue(activationArguments);

    public static void Attach(
        Action<AppActivationArguments> handler) => Queue.Attach(handler);
}
