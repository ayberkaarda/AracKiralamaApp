using AracKiralama.Business.Auditing;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Tests;

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession OtherUser { get; } = new(5, "baska", UserRole.User);

    public static UserSession Admin { get; } = new(1, "yonetici", UserRole.Admin);
}

/// <summary>A database clock that reads <paramref name="time"/>, as on a database whose clock agrees with the computer's.</summary>
internal sealed class TestDatabaseClock(TimeProvider time) : IDatabaseClock
{
    public Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default) => Task.FromResult(time.GetUtcNow().UtcDateTime);
}

/// <summary>Collects audit events as code and role, as a log would show them.</summary>
internal sealed class RecordingAuditLog : IAuditLog
{
    public List<string> Entries { get; } = [];

    public void Record(AuditEvent auditEvent, AuditRole role) =>
        Entries.Add(AuditCodes.Of(auditEvent) + " " + AuditCodes.Of(role));
}
