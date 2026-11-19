using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Runs every command of a context as a database user without a login of its own, as the application
/// would under its own login. Impersonation stays on a connection, so the context must not pool them.
/// </summary>
internal sealed class ImpersonateUser(string user) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = Impersonate(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = Impersonate(connection);
        command.ExecuteNonQuery();
    }

    private DbCommand Impersonate(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "EXECUTE AS USER = @user";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@user";
        parameter.Value = user;
        command.Parameters.Add(parameter);
        return command;
    }
}
