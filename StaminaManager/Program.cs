using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using StaminaManager.Application;
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

        AppInstance currentInstance = AppInstance.GetCurrent();
        AppActivationArguments activationArguments =
            currentInstance.GetActivatedEventArgs();
        AppInstance mainInstance = AppInstance.FindOrRegisterForKey(
            MainInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            RedirectActivation(activationArguments, mainInstance);
            return 0;
        }

        _mainInstance = mainInstance;
        _mainInstance.Activated += OnActivated;
        Microsoft.UI.Xaml.Application.Start(initializationParameters =>
        {
            _ = initializationParameters;
            DispatcherQueue dispatcherQueue =
                DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcherQueue));
            new App();
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
