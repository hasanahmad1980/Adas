using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class FileSystemAccessServiceTests
{
    [Fact]
    public void CanWriteToDirectory_VerifiesRealWriteAndCleansProbeFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-write-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            Assert.True(FileSystemAccessService.CanWriteToDirectory(root, out var error), error);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
