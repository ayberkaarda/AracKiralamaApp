using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AracKiralama.Data.Tests;

/// <summary>
/// A deterministic interleaving point for races on SQL Server: the first command of a context that
/// matches waits, before it runs and with every lock its transaction has taken so far, until the test
/// releases it. Other commands run as usual.
/// </summary>
internal sealed class CommandGate(Func<DbCommand, bool> matches) : DbCommandInterceptor
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _fired;

    /// <summary>Completes when the matching command is about to run.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Stops before the application lock <paramref name="resource"/> is requested.</summary>
    public static CommandGate BeforeAppLock(string resource) =>
        new(command => command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(p => p.ParameterName == "@resource" && Equals(p.Value, resource)));

    /// <summary>Stops before the first command whose text contains all of <paramref name="parts"/>.</summary>
    public static CommandGate BeforeCommandWith(params string[] parts) =>
        new(command => parts.All(part => command.CommandText.Contains(part, StringComparison.Ordinal)));

    public void Release() => _released.TrySetResult();

    /// <summary>Waits until the gate is reached, failing the test after 30 seconds.</summary>
    public Task WaitUntilReachedAsync(CancellationToken ct) => Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await PauseIfMatchingAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await PauseIfMatchingAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await PauseIfMatchingAsync(command, cancellationToken);
        return result;
    }

    private async Task PauseIfMatchingAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _fired) == 0 && matches(command) && Interlocked.Exchange(ref _fired, 1) == 0)
        {
            _reached.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }
    }
}
