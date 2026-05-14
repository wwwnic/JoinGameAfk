using System.Windows.Threading;

namespace JoinGameAfk.Services
{
    internal static class DispatcherExtensions
    {
        public static bool TryInvoke(this Dispatcher dispatcher, Action action)
        {
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return false;

            try
            {
                if (dispatcher.CheckAccess())
                    action();
                else
                    dispatcher.Invoke(action);

                return true;
            }
            catch (TaskCanceledException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }
            catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }
        }

        public static bool TryInvokeAsync(
            this Dispatcher dispatcher,
            Action action,
            DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return false;

            try
            {
                dispatcher.InvokeAsync(action, priority);
                return true;
            }
            catch (TaskCanceledException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }
            catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }
        }
    }
}
