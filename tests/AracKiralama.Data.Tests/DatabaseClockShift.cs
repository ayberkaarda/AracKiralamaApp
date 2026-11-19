using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Moves the database server's clock as one application instance reads it: every SYSUTCDATETIME() in a
/// command of a context with this interceptor reads the server's clock plus <see cref="Offset"/>. The
/// offset can be changed at any moment, for example while a request waits for a lock, to let the clock
/// pass midnight between the request and its write.
/// </summary>
internal sealed class DatabaseClockShift : DbCommandInterceptor
{
    private const string ServerClock = "SYSUTCDATETIME()";
    private long _seconds;

    /// <summary>How far the shifted clock is ahead of the server's (whole seconds).</summary>
    public TimeSpan Offset
    {
        get => TimeSpan.FromSeconds(Interlocked.Read(ref _seconds));
        set => Interlocked.Exchange(ref _seconds, (long)value.TotalSeconds);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Shift(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Shift(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Shift(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Shift(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Shift(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Shift(command);
        return result;
    }

    private void Shift(DbCommand command)
    {
        if (command.CommandText.Contains(ServerClock, StringComparison.OrdinalIgnoreCase))
        {
            var seconds = Interlocked.Read(ref _seconds).ToString(CultureInfo.InvariantCulture);
            command.CommandText = command.CommandText.Replace(ServerClock, "DATEADD(SECOND, " + seconds + ", SYSUTCDATETIME())", StringComparison.OrdinalIgnoreCase);
        }
    }
}
