using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// Checks the account an operation acts for against the stored account, inside the operation. The
/// services check the role at the start of an operation; this check repeats it where the write happens,
/// so an admin demoted or deleted in between changes nothing.
/// </summary>
internal static class Actors
{
    /// <summary>
    /// Throws <see cref="ActorRevokedException"/> unless account <paramref name="actorId"/> exists and,
    /// with <paramref name="admin"/>, has the admin role. Called inside the caller's transaction before
    /// anything is written. On SQL Server the account row stays share-locked until that transaction
    /// ends (HOLDLOCK, a locking read also under read committed snapshot), so a concurrent demotion or
    /// deletion of the actor waits for the operation to finish instead of slipping in between the check
    /// and the write.
    /// </summary>
    public static async Task RequireAsync(AracDbContext db, int actorId, bool admin, CancellationToken cancellationToken)
    {
        bool valid;
        if (db.Database.IsSqlServer())
        {
            var needsAdmin = admin ? 1 : 0;
            var adminRole = SchemaLimits.AdminRoleValue;
            valid = await db.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM dbo.Kullanicilar WITH (HOLDLOCK) WHERE KullaniciID = {actorId} AND ({needsAdmin} = 0 OR Rol = {adminRole})")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false) == 1;
        }
        else
        {
            valid = await db.Users
                .AnyAsync(u => u.Id == actorId && (!admin || u.Role == UserRole.Admin), cancellationToken)
                .ConfigureAwait(false);
        }

        if (!valid)
        {
            throw new ActorRevokedException();
        }
    }
}
