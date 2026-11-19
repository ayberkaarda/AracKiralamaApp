using AracKiralama.Domain.Entities;

namespace AracKiralama.Business.Auditing;

/// <summary>
/// Security relevant events. An event carries only its kind and the role of the account involved: no
/// account id, user name, e-mail, phone, password, car, plate or amount, so an audit entry cannot identify a
/// person. The entries can only be counted.
/// </summary>
public enum AuditEvent
{
    /// <summary>A sign-in succeeded.</summary>
    SignInSucceeded = 1,

    /// <summary>
    /// A sign-in failed: unknown account, wrong password, a locked account or an account changed while
    /// it signed in. All cases share one event and are recorded without a role, so the log cannot tell
    /// which accounts exist or are locked.
    /// </summary>
    SignInFailed = 2,

    /// <summary>A plain text or weaker stored password was replaced with a current hash.</summary>
    PasswordUpgraded = 3,

    /// <summary>A new account registered itself.</summary>
    AccountRegistered = 4,

    /// <summary>An account changed its own password.</summary>
    PasswordChanged = 5,

    /// <summary>A password change was refused because the current password was wrong.</summary>
    PasswordChangeRefused = 6,

    /// <summary>The first admin account was created by the first-run setup.</summary>
    FirstAdminCreated = 7,

    /// <summary>A setup key was refused.</summary>
    SetupKeyRefused = 8,

    /// <summary>An account asked for an operation its role does not allow.</summary>
    AccessDenied = 9,

    /// <summary>A car was added, changed or deleted.</summary>
    CarChanged = 10,

    /// <summary>A car was booked.</summary>
    RentalCreated = 11,

    /// <summary>A booking was refused because the car is taken on some of the days.</summary>
    RentalOverlapRefused = 12,

    /// <summary>A rental was ended: cancelled before its start or returned.</summary>
    RentalEnded = 13,

    /// <summary>An admin added, changed or deleted an account.</summary>
    AccountChanged = 14,

    /// <summary>A sign-up used a user name, e-mail address or phone number that is taken; no account was created.</summary>
    RegistrationNotCompleted = 15,

    /// <summary>An admin added, changed or cancelled a maintenance or damage record.</summary>
    MaintenanceChanged = 16,

    /// <summary>A maintenance write was refused because a rental holds some of the days or the record changed meanwhile.</summary>
    MaintenanceRefused = 17,

    /// <summary>An admin produced a revenue report.</summary>
    RevenueReportCreated = 18,

    /// <summary>A rental contract was produced.</summary>
    ContractCreated = 19,
}

/// <summary>Role of the account an <see cref="AuditEvent"/> concerns.</summary>
public enum AuditRole
{
    Unknown = 0,
    User = 1,
    Admin = 2,
}

/// <summary>Receives <see cref="AuditEvent"/>s.</summary>
public interface IAuditLog
{
    void Record(AuditEvent auditEvent, AuditRole role);
}

public static class AuditCodes
{
    /// <summary>Stable, lower case code of <paramref name="auditEvent"/> as it is written to the log.</summary>
    public static string Of(AuditEvent auditEvent) => auditEvent switch
    {
        AuditEvent.SignInSucceeded => "auth.sign_in.succeeded",
        AuditEvent.SignInFailed => "auth.sign_in.failed",
        AuditEvent.PasswordUpgraded => "auth.password.upgraded",
        AuditEvent.AccountRegistered => "auth.account.registered",
        AuditEvent.PasswordChanged => "auth.password.changed",
        AuditEvent.PasswordChangeRefused => "auth.password.change_refused",
        AuditEvent.FirstAdminCreated => "setup.first_admin.created",
        AuditEvent.SetupKeyRefused => "setup.key.refused",
        AuditEvent.AccessDenied => "access.denied",
        AuditEvent.CarChanged => "car.changed",
        AuditEvent.RentalCreated => "rental.created",
        AuditEvent.RentalOverlapRefused => "rental.overlap_refused",
        AuditEvent.RentalEnded => "rental.ended",
        AuditEvent.AccountChanged => "account.changed",
        AuditEvent.RegistrationNotCompleted => "auth.account.registration_not_completed",
        AuditEvent.MaintenanceChanged => "maintenance.changed",
        AuditEvent.MaintenanceRefused => "maintenance.refused",
        AuditEvent.RevenueReportCreated => "report.revenue.created",
        AuditEvent.ContractCreated => "rental.contract.created",
        _ => throw new ArgumentOutOfRangeException(nameof(auditEvent), auditEvent, "Unknown audit event."),
    };

    /// <summary>
    /// The code written to a log file the local Windows user can read. It is <see cref="Of(AuditEvent)"/>
    /// except for the two outcomes of a sign-up: a new account and a taken name, e-mail address or phone
    /// number share one neutral code, because whoever made the attempt knows its time and could
    /// otherwise tell from the file whether someone else's values are registered.
    /// </summary>
    public static string ForPublicLog(AuditEvent auditEvent) => auditEvent switch
    {
        AuditEvent.AccountRegistered or AuditEvent.RegistrationNotCompleted => RegistrationSubmitted,
        _ => Of(auditEvent),
    };

    /// <summary>The neutral code of <see cref="ForPublicLog"/> for both sign-up outcomes.</summary>
    public const string RegistrationSubmitted = "auth.account.registration_submitted";

    /// <summary>Lower case role name as it is written to the log.</summary>
    public static string Of(AuditRole role) => role switch
    {
        AuditRole.User => "user",
        AuditRole.Admin => "admin",
        _ => "unknown",
    };

    public static AuditRole Of(UserRole role) => role == UserRole.Admin ? AuditRole.Admin : AuditRole.User;
}

/// <summary>Discards every event; used when no audit sink is registered, for example in tests.</summary>
internal sealed class NullAuditLog : IAuditLog
{
    public static readonly NullAuditLog Instance = new();

    public void Record(AuditEvent auditEvent, AuditRole role)
    {
    }
}
