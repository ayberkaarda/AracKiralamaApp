using AracKiralama.Business;
using AracKiralama.Data.Persistence;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;

namespace AracKiralama.UI.Tests;

public sealed class ErrorReporterTests
{
    [Fact]
    public void FormatForLog_KeepsTypesAndMethodsButNoMessages()
    {
        Exception error;
        try
        {
            throw new InvalidOperationException("Server=gizli-sunucu;User=ornek;Password=Gizli.Sifre1", new TimeoutException("ornek@example.com"));
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }

        var log = ErrorReporter.FormatForLog(error);

        Assert.Contains("System.InvalidOperationException (HResult 0x", log, StringComparison.Ordinal);
        Assert.Contains("--- inner exception ---", log, StringComparison.Ordinal);
        Assert.Contains("System.TimeoutException", log, StringComparison.Ordinal);
        Assert.Contains(nameof(FormatForLog_KeepsTypesAndMethodsButNoMessages), log, StringComparison.Ordinal);
        Assert.DoesNotContain("gizli-sunucu", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Gizli.Sifre1", log, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", log, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_GivesFixedTurkishMessages()
    {
        Assert.Equal(ErrorReporter.DatabaseInitializationMessage, ErrorReporter.Describe(new DatabaseInitializationException(new InvalidOperationException("x"))));
        Assert.Equal(ErrorReporter.BusyMessage, ErrorReporter.Describe(new AggregateException(new AppLockNotGrantedException("x"))));
        Assert.Equal(ErrorReporter.UpgradeBusyMessage, ErrorReporter.Describe(new DatabaseInitializationException(new AppLockNotGrantedException("x"))));
        Assert.Equal(ErrorReporter.TimeoutMessage, ErrorReporter.Describe(new TimeoutException("x")));
        Assert.Equal(ErrorReporter.UnexpectedErrorMessage, ErrorReporter.Describe(new InvalidOperationException("Server=gizli")));
    }

    [Fact]
    public void Show_RuleMessageIsAWarningAndOtherErrorsNeverShowTheirText()
    {
        ErrorReporter.Show(new BusinessRuleException("Araç seçtiğiniz tarihlerin bir kısmında başka bir kiralamada."));
        ErrorReporter.Show(new InvalidOperationException("Server=gizli-sunucu"));

        Assert.Contains("Uyarı: Araç seçtiğiniz tarihlerin bir kısmında başka bir kiralamada.", NoDialogs.Requested);
        Assert.Contains(AppText.Title + ": " + ErrorReporter.UnexpectedErrorMessage, NoDialogs.Requested);
        Assert.DoesNotContain(NoDialogs.Requested, r => r.Contains("gizli-sunucu", StringComparison.Ordinal));
    }
}
