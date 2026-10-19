using Microsoft.Extensions.Logging;
using Settlement.Core.Configuration;
using Settlement.Core.Internal;
using Settlement.Core.Pipeline;
using Settlement.Core.Reporting;
using Settlement.Sfg;

namespace Settlement.Agent;

public enum FileOutcome
{
    Processed,
    Duplicate,
    Failed,
}

public sealed record FileRunResult(string FileName, FileOutcome Outcome, SettlementSummary? Summary, string? ReportPath, string? Error);

/// <summary>Picks pending advice files from SFG, settles and reconciles each one, then archives it.</summary>
public sealed class SettlementRunner(
    SettlementOptions options,
    Func<IFileSource> fileSourceFactory,
    IInternalTransactionSource internalSource,
    ILogger<SettlementRunner> logger)
{
    private readonly SettlementPipeline _pipeline = new(options);
    private readonly ReportWriter _reports = new();

    public IReadOnlyList<FileRunResult> RunOnce()
    {
        using var source = fileSourceFactory();
        var pending = source.ListPending();
        logger.LogInformation("Found {Count} pending advice file(s) in {Path}", pending.Count, options.Sfg.InboundPath);
        if (pending.Count == 0)
        {
            return [];
        }

        var ledger = new ProcessedFileLedger(options.Sfg.LedgerPath);
        var internalTransactions = internalSource.Load();
        var results = new List<FileRunResult>();

        foreach (var file in pending)
        {
            byte[] content;
            try
            {
                content = source.Download(file);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not download {File}", file.Name);
                results.Add(new FileRunResult(file.Name, FileOutcome.Failed, null, null, ex.Message));
                continue;
            }

            var hash = ProcessedFileLedger.Hash(content);
            if (ledger.TryGet(hash, out var previous))
            {
                logger.LogWarning("Skipping {File}: same content already processed as {Previous} at {At}", file.Name, previous!.FileName, previous.ProcessedAt);
                source.MoveToError(file);
                results.Add(new FileRunResult(file.Name, FileOutcome.Duplicate, null, null, $"Duplicate of {previous.FileName}"));
                continue;
            }

            try
            {
                SaveWorkingCopy(file.Name, content);
                var result = _pipeline.Process(file.Name, new MemoryStream(content), internalTransactions);
                var reportPath = _reports.Write(result, options.Output.ReportsPath);
                source.MoveToProcessed(file);
                ledger.Record(file.Name, hash, FileOutcome.Processed.ToString());
                logger.LogInformation(
                    "Settled {File} with profile {Profile}: {Records} records, matched {Matched}/{Matchable}, net {Net}, {Breaks} break(s). Reports: {Reports}",
                    file.Name, result.Summary.Profile, result.Summary.Records, result.Summary.Matched, result.Summary.Matchable,
                    result.Summary.NetSettlement, result.Breaks.Count, reportPath);
                results.Add(new FileRunResult(file.Name, FileOutcome.Processed, result.Summary, reportPath, null));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to settle {File}; moving it to the error folder", file.Name);
                source.MoveToError(file);
                ledger.Record(file.Name, hash, FileOutcome.Failed.ToString());
                results.Add(new FileRunResult(file.Name, FileOutcome.Failed, null, null, ex.Message));
            }
        }

        return results;
    }

    public FileRunResult ProcessLocalFile(string path)
    {
        var internalTransactions = internalSource.Load();
        using var stream = File.OpenRead(path);
        var result = _pipeline.Process(Path.GetFileName(path), stream, internalTransactions);
        var reportPath = _reports.Write(result, options.Output.ReportsPath);
        return new FileRunResult(Path.GetFileName(path), FileOutcome.Processed, result.Summary, reportPath, null);
    }

    private void SaveWorkingCopy(string name, byte[] content)
    {
        Directory.CreateDirectory(options.Sfg.WorkingPath);
        File.WriteAllBytes(Path.Combine(options.Sfg.WorkingPath, name), content);
    }
}
