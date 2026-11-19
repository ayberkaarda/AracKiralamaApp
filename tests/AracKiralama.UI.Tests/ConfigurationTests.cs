using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AracKiralama.UI.Tests;

public sealed class ConfigurationTests
{
    private static string ExamplePath => Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");

    [Fact]
    public void ExampleSettings_AreValidJsonWithALocalServerAndDemoOff()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ExamplePath));

        var configuration = new ConfigurationBuilder().AddSettingsFile(AppContext.BaseDirectory, "appsettings.example.json").Build();
        var connection = AppConfiguration.ResolveConnectionString(configuration);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.NotNull(connection);
        Assert.StartsWith("Server=localhost\\SQLEXPRESS;", connection, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", connection, StringComparison.OrdinalIgnoreCase);
        Assert.False(AppConfiguration.IsDemoRequested(configuration));

        var (policy, problems) = AppConfiguration.ReadRentalPolicy(configuration);
        Assert.Empty(problems);
        Assert.Equal(1, policy.MinRentalDays);
        Assert.Equal(90, policy.MaxRentalDays);
        Assert.Equal(365, policy.MaxAdvanceDays);
        Assert.Equal(0, policy.LongRentalDiscountPercent);
    }

    [Fact]
    public void ConnectionString_EnvironmentVariableWins()
    {
        var configuration = Settings(new()
        {
            ["ConnectionStrings:AracKiralama"] = "Server=dosya;",
            ["ARAC_CONNECTION"] = "Server=ortam;",
        });

        Assert.Equal("Server=ortam;", AppConfiguration.ResolveConnectionString(configuration));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("0", false)]
    [InlineData("evet", false)]
    [InlineData(null, false)]
    public void Demo_IsOnOnlyWhenAskedFor(string? value, bool expected)
    {
        Assert.Equal(expected, AppConfiguration.IsDemoRequested(Settings(new() { ["ARAC_DEMO"] = value })));
        Assert.Equal(expected, AppConfiguration.IsDemoRequested(Settings(new() { ["Seed:Demo"] = value })));
    }

    [Theory]
    [InlineData("Rental:MinRentalDays", "abc")]
    [InlineData("Rental:MinRentalDays", "0")]
    [InlineData("Rental:MaxRentalDays", "otuz")]
    [InlineData("Rental:MaxAdvanceDays", "-1")]
    [InlineData("Rental:LongRentalDiscountPercent", "95")]
    [InlineData("Rental:LongRentalDiscountPercent", "10.5")]
    public void RentalSettings_InvalidValues_AreReportedAndTheDefaultsKept(string key, string value)
    {
        var (policy, problems) = AppConfiguration.ReadRentalPolicy(Settings(new() { [key] = value }));

        Assert.NotEmpty(problems);
        Assert.Equal(90, policy.MaxRentalDays);
        Assert.Equal(0, policy.LongRentalDiscountPercent);
    }

    [Fact]
    public void RentalSettings_AreRead()
    {
        var (policy, problems) = AppConfiguration.ReadRentalPolicy(Settings(new()
        {
            ["Rental:MinRentalDays"] = "2",
            ["Rental:MaxRentalDays"] = "14",
            ["Rental:LongRentalDays"] = "7",
            ["Rental:LongRentalDiscountPercent"] = "10",
        }));

        Assert.Empty(problems);
        Assert.Equal(2, policy.MinRentalDays);
        Assert.Equal(14, policy.MaxRentalDays);
        Assert.Equal(7, policy.LongRentalDays);
        Assert.Equal(10, policy.LongRentalDiscountPercent);
    }

    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
