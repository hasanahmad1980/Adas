namespace RenoDXCommander.Services;

internal static class FileSystemAccessService
{
    internal static bool CanWriteToDirectory(string directory, out string? error)
    {
        var probePath = Path.Combine(directory, $".adas-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); }
            catch { }
        }
    }
}
