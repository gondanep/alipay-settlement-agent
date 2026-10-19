using Microsoft.Extensions.Configuration;
using Settlement.Core.Configuration;
using Settlement.Core.Model;

namespace Settlement.Tests;

public static class TestSupport
{
    public static string RepoRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AlipaySettlement.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    public static string Sample(params string[] parts) => Path.Combine([RepoRoot, "samples", .. parts]);

    /// <summary>Loads the shipped appsettings.json so tests exercise the real profiles.</summary>
    public static SettlementOptions LoadOptions()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot, "src", "Settlement.Agent", "appsettings.json"))
            .Build();
        var options = config.GetSection(SettlementOptions.SectionName).Get<SettlementOptions>()!;
        options.InternalSource.Path = Sample("internal", "transactions.csv");
        return options;
    }

    public static AdviceProfile SimpleProfile() => new()
    {
        Provider = "Test",
        Fields =
        [
            new FieldMapping { Name = CanonicalFields.TransactionId, Source = "id", Required = true },
            new FieldMapping { Name = CanonicalFields.OriginalTransactionId, Source = "orig" },
            new FieldMapping { Name = CanonicalFields.TransactionType, Source = "type", Required = true, Transform = FieldTransform.Upper },
            new FieldMapping { Name = CanonicalFields.Amount, Source = "amount", Type = FieldDataType.Decimal, Required = true },
            new FieldMapping { Name = CanonicalFields.Fee, Source = "fee", Type = FieldDataType.Decimal, Default = "0" },
            new FieldMapping { Name = CanonicalFields.Currency, Source = "ccy", Required = true },
            new FieldMapping { Name = CanonicalFields.TransactionTime, Source = "time", Type = FieldDataType.DateTime, Format = "yyyy-MM-dd HH:mm:ss" },
            new FieldMapping { Name = CanonicalFields.SettlementTime, Source = "settled", Type = FieldDataType.DateTime, Format = "yyyy-MM-dd HH:mm:ss" },
        ],
        TransactionTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["P"] = new() { Type = TransactionTypes.Payment },
            ["R"] = new() { Type = TransactionTypes.Refund },
            ["C"] = new() { Type = TransactionTypes.Cancel },
            ["CB"] = new() { Type = TransactionTypes.Chargeback },
            ["CBR"] = new() { Type = TransactionTypes.ChargebackReversal },
            ["F"] = new() { Type = TransactionTypes.Fee },
            ["A"] = new() { Type = TransactionTypes.Adjustment },
        },
    };

    public const string Header = "id,orig,type,amount,fee,ccy,time,settled";

    public static InternalTransaction Txn(string id, string type, decimal amount, string status = "SUCCESS", string? orig = null, string time = "2026-10-17 10:00:00") =>
        new(id, type, amount, "USD", status, DateTimeOffset.Parse(time + "Z"), orig);
}
