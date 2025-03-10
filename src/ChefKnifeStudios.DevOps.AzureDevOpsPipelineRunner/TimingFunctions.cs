using System;
using System.Threading.Tasks;
using System.Threading;
using Godot;

public static class TimingFunctions
{
    public static void SetTimeout(Action action, int delay)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        Task.Delay(delay).ContinueWith(async (t) =>
        {
            action();
        }, cancellationToken);
    }

    // intervalTokenSource.Cancel(); will stop the interval.
    public static CancellationTokenSource SetInterval(Action action, int interval)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    action();
                    await Task.Delay(interval, cancellationToken);
                }
                catch (TaskCanceledException ex)
                {
                    // Task was canceled
                    GD.PrintErr($"SetInterval still running after cancellation. {ex.Message}");
                    break;
                }
            }
        }, cancellationToken);

        return cancellationTokenSource;
    }

    public static Action Debounce(Action action, int delay)
    {
        CancellationTokenSource cancellationTokenSource = null;

        return () =>
        {
            // Cancel any previously scheduled action
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        action();
                    }
                }
                catch (TaskCanceledException)
                {
                    // Task was canceled, no action needed
                }
            }, cancellationToken);
        };
    }
}