using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// Exclusive SQL Server application locks (sp_getapplock) owned by the current transaction and released
/// when it ends. sp_getapplock reports a refused lock through its return code instead of an error, so
/// the code is always checked and the caller stops before it reads or writes anything.
/// </summary>
internal static class AppLock
{
    /// <summary>
    /// Room the client leaves the server beyond a lock's own wait before it gives up on the statement, so
    /// a busy lock always ends with the server's "not granted" (shown as a busy message) and never with a
    /// client side command timeout that would look like a broken connection.
    /// </summary>
    internal const int CommandTimeoutMarginSeconds = 30;

    // SqlClient's default when the context sets no command timeout.
    private const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>
    /// Takes the lock on SQL Server. Other providers (the tests' SQLite) serialize writing transactions
    /// themselves and need none. The caller must have begun a transaction. While the lock is requested
    /// the statement's command timeout is at least the lock's wait plus
    /// <see cref="CommandTimeoutMarginSeconds"/>.
    /// </summary>
    public static async Task AcquireAsync(AracDbContext db, string resource, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var name = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource };
        var timeout = new SqlParameter("@timeout", SqlDbType.Int) { Value = timeoutMilliseconds };
        var configured = db.Database.GetCommandTimeout();
        var raised = CommandTimeoutFor(configured, timeoutMilliseconds);
        if (raised is { } seconds)
        {
            db.Database.SetCommandTimeout(seconds);
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
                [result, name, timeout],
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (raised is not null)
            {
                db.Database.SetCommandTimeout(configured);
            }
        }

        EnsureGranted(result.Value is int code ? code : null, resource);
    }

    /// <summary>
    /// The command timeout, in seconds, the lock request needs when <paramref name="configuredSeconds"/>
    /// (<see langword="null"/>: SqlClient's default of 30 seconds, 0: no limit) is too short for a wait of
    /// <paramref name="lockTimeoutMilliseconds"/>; <see langword="null"/> when it is long enough.
    /// </summary>
    internal static int? CommandTimeoutFor(int? configuredSeconds, int lockTimeoutMilliseconds)
    {
        var effective = configuredSeconds ?? DefaultCommandTimeoutSeconds;
        var needed = ((lockTimeoutMilliseconds + 999) / 1000) + CommandTimeoutMarginSeconds;
        return effective == 0 || effective >= needed ? null : needed;
    }

    /// <summary>Throws when an sp_getapplock return code means the lock was not granted (0 and 1 mean granted).</summary>
    internal static void EnsureGranted(int? returnCode, string resource)
    {
        if (returnCode is >= 0)
        {
            return;
        }

        var reason = returnCode switch
        {
            -1 => "the request timed out because another operation still holds it",
            -2 => "the request was canceled",
            -3 => "the request was chosen as a deadlock victim",
            -999 => "the lock request was invalid",
            null => "the lock procedure returned no result",
            _ => "the lock procedure failed",
        };

        throw new AppLockNotGrantedException(
            $"The application lock '{resource}' could not be acquired ({reason}, sp_getapplock returned "
            + $"{returnCode?.ToString(CultureInfo.InvariantCulture) ?? "null"}). No changes were made; try again later.");
    }
}

/// <summary>Raised when an application lock was refused; the operation made no change.</summary>
public sealed class AppLockNotGrantedException : Exception
{
    public AppLockNotGrantedException()
    {
    }

    public AppLockNotGrantedException(string message)
        : base(message)
    {
    }

    public AppLockNotGrantedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Named locks of the application.</summary>
internal static class Locks
{
    private const int TimeoutMilliseconds = 30_000;

    /// <summary>
    /// Serializes the operations on one car's calendar (book, end a rental, change or delete the car):
    /// each takes this lock first inside its transaction and only then reads the rentals it decides on,
    /// so no other such operation on the same car can book an overlapping range before it commits.
    /// </summary>
    public static Task CarAsync(AracDbContext db, int carId, CancellationToken cancellationToken) =>
        AppLock.AcquireAsync(db, "AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture), TimeoutMilliseconds, cancellationToken);

    /// <summary>Serializes changes of the admin role, so two concurrent demotions cannot remove the last admin.</summary>
    public static Task AdminRolesAsync(AracDbContext db, CancellationToken cancellationToken) =>
        AppLock.AcquireAsync(db, "AracKiralama.AdminRoles", TimeoutMilliseconds, cancellationToken);
}
