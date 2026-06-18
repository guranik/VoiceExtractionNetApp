namespace Common.Http.Utils;

public static class ExponentialBackoff
{
    public static TimeSpan Calculate(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, double factor = 2.0)
    {
        var delay = TimeSpan.FromTicks((long)(baseDelay.Ticks * Math.Pow(factor, attempt)));
        return delay > maxDelay ? maxDelay : delay;
    }

    public static async Task WaitAsync(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, CancellationToken ct, double factor = 2.0)
    {
        var delay = Calculate(attempt, baseDelay, maxDelay, factor);
        await Task.Delay(delay, ct);
    }
}
