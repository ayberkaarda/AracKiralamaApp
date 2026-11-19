using AracKiralama.Business.Auditing;
using AracKiralama.Business.Exports;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Services;

/// <summary>Printable rental contracts.</summary>
public interface IContractService
{
    /// <summary>
    /// The contract of a rental as a printable HTML file. A customer gets only the contracts of their own
    /// rentals; another account's rental gets the same answer as one that does not exist. Admins get any.
    /// </summary>
    Task<ExportFile> CreateAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default);
}

internal sealed class ContractService(
    IRentalRepository rentals,
    AccessGuard access,
    TimeProvider timeProvider,
    IDatabaseClock clock,
    IAuditLog audit) : IContractService
{
    internal const string NotFound = "Kiralama bulunamadı.";

    public async Task<ExportFile> CreateAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        // The owner filter comes from the role stored now: a demoted admin gets only their own contracts.
        var owner = await access.OwnerFilterAsync(session, cancellationToken).ConfigureAwait(false);
        var data = await rentals.GetContractDataAsync(rentalId, owner, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(NotFound);
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), timeProvider.LocalTimeZone);

        audit.Record(AuditEvent.ContractCreated, owner is null ? AuditRole.Admin : AuditRole.User);
        return RentalContractDocument.ToHtml(new RentalContract(data, local));
    }
}
