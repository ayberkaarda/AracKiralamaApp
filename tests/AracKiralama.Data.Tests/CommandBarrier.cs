using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AracKiralama.Data.Tests;

/// <summary>
/// A barrier for races on SQL Server: every command that matches waits, before it runs, until the test
/// releases all of them at once. The test can wait until a number of commands have arrived, so it knows
/// that every racing request has read what it decides on before any of them writes.
/// </summary>
internal sealed class CommandBarrier(Func<DbCommand, bool> matches) : DbCommandInterceptor
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    /// <summary>Stops every command whose text contains all of <paramref name="parts"/>.</summary>
    public static CommandBarrier BeforeCommandsWith(params string[] parts) =>
        new(command => parts.All(part => command.CommandText.Contains(part, StringComparison.Ordinal)));

    /// <summary>Waits until <paramref name="count"/> matching commands wait at the barrier, failing the test after 30 seconds.</summary>
    public async Task WaitForArrivalsAsync(int count, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref _arrived) < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Fewer than {count} commands reached the barrier.");
            await Task.Delay(10, ct);
        }
    }

    /// <summary>Lets every waiting command, and every later one, run.</summary>
    public void Release() => _released.TrySetResult();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await WaitIfMatchingAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await WaitIfMatchingAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await WaitIfMatchingAsync(command, cancellationToken);
        return result;
    }

    private async Task WaitIfMatchingAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (!_released.Task.IsCompleted && matches(command))
        {
            Interlocked.Increment(ref _arrived);
            await _released.Task.WaitAsync(cancellationToken);
        }
    }
}
