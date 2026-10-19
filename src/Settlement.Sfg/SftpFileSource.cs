using Renci.SshNet;
using Settlement.Core.Configuration;

namespace Settlement.Sfg;

/// <summary>Reads advice files from a Sterling File Gateway SFTP mailbox.</summary>
public sealed class SftpFileSource : IFileSource
{
    private readonly SfgOptions _options;
    private readonly SftpClient _client;

    public SftpFileSource(SfgOptions options)
    {
        _options = options;
        _client = new SftpClient(BuildConnectionInfo(options));
        _client.Connect();
        EnsureDirectory(options.ProcessedPath);
        EnsureDirectory(options.ErrorPath);
    }

    private static ConnectionInfo BuildConnectionInfo(SfgOptions o)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrEmpty(o.PrivateKeyPath))
        {
            var key = string.IsNullOrEmpty(o.PrivateKeyPassphrase)
                ? new PrivateKeyFile(o.PrivateKeyPath)
                : new PrivateKeyFile(o.PrivateKeyPath, o.PrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(o.Username, key));
        }

        if (!string.IsNullOrEmpty(o.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(o.Username, o.Password));
        }

        if (methods.Count == 0)
        {
            throw new InvalidOperationException("SFG SFTP mode needs Settlement:Sfg:Password or Settlement:Sfg:PrivateKeyPath");
        }

        return new ConnectionInfo(o.Host, o.Port, o.Username, methods.ToArray());
    }

    public IReadOnlyList<RemoteFile> ListPending() =>
        _client.ListDirectory(_options.InboundPath)
            .Where(f => f.IsRegularFile && GlobMatcher.IsMatch(f.Name, _options.FilePattern))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new RemoteFile(f.Name, f.FullName, f.Length))
            .ToList();

    public byte[] Download(RemoteFile file)
    {
        using var ms = new MemoryStream();
        _client.DownloadFile(file.FullPath, ms);
        return ms.ToArray();
    }

    public void MoveToProcessed(RemoteFile file) => Move(file, _options.ProcessedPath);

    public void MoveToError(RemoteFile file) => Move(file, _options.ErrorPath);

    private void Move(RemoteFile file, string target)
    {
        var destination = $"{target.TrimEnd('/')}/{file.Name}";
        if (_client.Exists(destination))
        {
            destination = $"{target.TrimEnd('/')}/{GlobMatcher.Unique(file.Name)}";
        }

        _client.RenameFile(file.FullPath, destination);
    }

    private void EnsureDirectory(string path)
    {
        if (!_client.Exists(path))
        {
            _client.CreateDirectory(path);
        }
    }

    public void Dispose()
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        _client.Dispose();
    }
}
