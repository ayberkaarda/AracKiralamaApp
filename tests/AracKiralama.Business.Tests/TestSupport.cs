using AracKiralama.Business.Auditing;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Tests;

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession OtherUser { get; } = new(5, "baska", UserRole.User);

    public static UserSession Admin { get; } = new(1, "yonetici", UserRole.Admin);
}

/// <summary>The validators the services get from the container.</summary>
internal static class TestValidators
{
    public static SignInInputValidator SignIn { get; } = new();

    public static SignUpInputValidator SignUp { get; } = new();

    public static PasswordChangeInputValidator PasswordChange { get; } = new();

    public static FirstAdminInputValidator FirstAdmin { get; } = new();

    public static NewAccountInputValidator NewAccount { get; } = new();

    public static AccountChangeInputValidator AccountChange { get; } = new();

    public static CarFieldsValidator Car { get; } = new();

    public static RentalQuoteInputValidator Quote(RentalPolicy policy) => new(policy);
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
