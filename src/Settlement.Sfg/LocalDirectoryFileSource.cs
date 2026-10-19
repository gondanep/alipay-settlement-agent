using Settlement.Core.Configuration;

namespace Settlement.Sfg;

public sealed class LocalDirectoryFileSource : IFileSource
{
    private readonly SfgOptions _options;

    public LocalDirectoryFileSource(SfgOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.InboundPath);
        Directory.CreateDirectory(options.ProcessedPath);
        Directory.CreateDirectory(options.ErrorPath);
    }

    public IReadOnlyList<RemoteFile> ListPending() =>
        new DirectoryInfo(_options.InboundPath).GetFiles()
            .Where(f => GlobMatcher.IsMatch(f.Name, _options.FilePattern))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new RemoteFile(f.Name, f.FullName, f.Length))
            .ToList();

    public byte[] Download(RemoteFile file) => File.ReadAllBytes(file.FullPath);

    public void MoveToProcessed(RemoteFile file) => Move(file, _options.ProcessedPath);

    public void MoveToError(RemoteFile file) => Move(file, _options.ErrorPath);

    private static void Move(RemoteFile file, string target)
    {
        var destination = Path.Combine(target, file.Name);
        if (File.Exists(destination))
        {
            destination = Path.Combine(target, GlobMatcher.Unique(file.Name));
        }

        File.Move(file.FullPath, destination);
    }

    public void Dispose()
    {
    }
}
