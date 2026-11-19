using AracKiralama.Business.Auditing;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using FluentValidation;

namespace AracKiralama.Business.Services;

/// <summary>A maintenance or damage record as the admin's form enters it; the cost as typed.</summary>
public sealed record MaintenanceInput(MaintenanceKind Kind, string? Description, DateOnly StartDate, DateOnly EndDate, string? Cost);

/// <summary>The maintenance and damage log of the cars (admin only).</summary>
public interface IMaintenanceService
{
    /// <summary>Every record of the car, newest start first, cancelled ones included.</summary>
    Task<IReadOnlyList<MaintenanceRecord>> GetForCarAsync(UserSession session, int carId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs maintenance or a damage repair: the car cannot be rented on its days. Refused when an active
    /// rental of the car holds any of them. Returns the stored record's id.
    /// </summary>
    Task<int> AddAsync(UserSession session, int carId, MaintenanceInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an active record. <paramref name="expectedVersion"/> is the version the form loaded; when
    /// the record changed since, nothing is written and the form has to load it again.
    /// </summary>
    Task UpdateAsync(UserSession session, int recordId, int expectedVersion, MaintenanceInput input, CancellationToken cancellationToken = default);

    /// <summary>Cancels an active record, which frees its days; the same version rule as <see cref="UpdateAsync"/> applies.</summary>
    Task CancelAsync(UserSession session, int recordId, int expectedVersion, CancellationToken cancellationToken = default);
}

/// <remarks>
/// The day limits are checked against today by the database's clock, and the repository stamps the records
/// with that clock inside the write, after the car's lock was granted, so computers with wrong clocks neither
/// accept other ranges nor write other times, and a write that waited for the lock is stamped when it happens.
/// </remarks>
internal sealed class MaintenanceService(
    IMaintenanceRepository maintenance,
    AccessGuard access,
    TimeProvider timeProvider,
    IDatabaseClock clock,
    IAuditLog audit,
    IValidator<MaintenanceFields> validator) : IMaintenanceService
{
    internal const string CarNotFound = "Araç bulunamadı.";
    internal const string NotFound = "Bakım kaydı bulunamadı veya iptal edilmiş.";
    internal const string ChangedElsewhere = "Bakım kaydı siz düzenlerken başka biri tarafından değiştirildi. Güncel hali yüklendi; lütfen değişikliğinizi yeniden yapın.";
    internal const string OverlapsRental = "Araç seçtiğiniz tarihlerin bir kısmında kiralanmış. Önce kiralamayı sonlandırın veya başka tarihler seçin.";
    internal const string OverlapsMaintenance = "Araç seçtiğiniz tarihlerin bir kısmında zaten bakımda. Mevcut kaydı güncelleyin veya başka tarihler seçin.";

    public async Task<IReadOnlyList<MaintenanceRecord>> GetForCarAsync(UserSession session, int carId, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        return await maintenance.GetForCarAsync(carId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(UserSession session, int carId, MaintenanceInput input, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var values = Validate(input, Today(now));
        var result = await access.WriteAsActorAsync(session, () => maintenance.AddAsync(session.Actor, carId, values, cancellationToken)).ConfigureAwait(false);
        Check(result.Outcome);
        return result.RecordId;
    }

    public async Task UpdateAsync(UserSession session, int recordId, int expectedVersion, MaintenanceInput input, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var values = Validate(input, Today(now));
        var result = await access.WriteAsActorAsync(
            session,
            () => maintenance.UpdateAsync(session.Actor, recordId, expectedVersion, values, cancellationToken)).ConfigureAwait(false);
        Check(result.Outcome);
    }

    public async Task CancelAsync(UserSession session, int recordId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var result = await access.WriteAsActorAsync(
            session,
            () => maintenance.CancelAsync(session.Actor, recordId, expectedVersion, cancellationToken)).ConfigureAwait(false);
        Check(result.Outcome);
    }

    private DateOnly Today(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), timeProvider.LocalTimeZone));

    private MaintenanceValues Validate(MaintenanceInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var fields = new MaintenanceFields(input, today);
        validator.ValidateOrThrow(fields);
        var cost = string.IsNullOrWhiteSpace(input.Cost) ? 0m : InputRules.TryParseMoney(input.Cost, out var parsed) ? parsed : 0m;
        return new MaintenanceValues(input.Kind, input.Description!.Trim(), input.StartDate, input.EndDate, cost);
    }

    /// <summary>Records the outcome and turns a refusal into its fixed message.</summary>
    private void Check(MaintenanceOutcome outcome)
    {
        if (outcome == MaintenanceOutcome.Saved)
        {
            audit.Record(AuditEvent.MaintenanceChanged, AuditRole.Admin);
            return;
        }

        audit.Record(AuditEvent.MaintenanceRefused, AuditRole.Admin);

        // A record changed or cancelled meanwhile makes the form stale: it has to load the record again.
        // Any other refusal keeps what the admin typed.
        throw outcome switch
        {
            MaintenanceOutcome.CarNotFound => new BusinessRuleException(CarNotFound),
            MaintenanceOutcome.OverlapsRental => new BusinessRuleException(OverlapsRental),
            MaintenanceOutcome.OverlapsMaintenance => new BusinessRuleException(OverlapsMaintenance),
            MaintenanceOutcome.ChangedElsewhere => BusinessRuleException.Stale(ChangedElsewhere),
            _ => BusinessRuleException.Stale(NotFound),
        };
    }
}
