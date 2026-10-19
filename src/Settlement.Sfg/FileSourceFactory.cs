using Settlement.Core.Configuration;

namespace Settlement.Sfg;

public static class FileSourceFactory
{
    public static IFileSource Create(SfgOptions options) => options.Mode switch
    {
        SfgMode.Sftp => new SftpFileSource(options),
        _ => new LocalDirectoryFileSource(options),
    };
}
