namespace Settlement.Sfg;

public sealed record RemoteFile(string Name, string FullPath, long Size);

/// <summary>Mailbox abstraction over Sterling File Gateway (SFTP) or a local folder.</summary>
public interface IFileSource : IDisposable
{
    IReadOnlyList<RemoteFile> ListPending();
    byte[] Download(RemoteFile file);
    void MoveToProcessed(RemoteFile file);
    void MoveToError(RemoteFile file);
}
