using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Quartz.Tests.AspNetCore.Dashboard.Support;

/// <summary>
/// Minimal in-memory <see cref="IFileProvider"/> for testing endpoints that serve files
/// from the web root without touching the file system.
/// </summary>
public sealed class TestFileProvider(Dictionary<string, byte[]> files) : IFileProvider
{
    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IFileInfo GetFileInfo(string subpath)
    {
        string normalized = subpath.Replace('\\', '/').TrimStart('/');
        return files.TryGetValue(normalized, out byte[]? content)
            ? new TestFileInfo(normalized, content)
            : new NotFoundFileInfo(subpath);
    }

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class TestFileInfo(string name, byte[] content) : IFileInfo
    {
        public bool Exists => true;
        public long Length => content.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(content);
    }
}
