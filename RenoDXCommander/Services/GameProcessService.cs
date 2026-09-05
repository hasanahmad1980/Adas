using System.Diagnostics;

namespace RenoDXCommander.Services;

internal sealed record RunningGameProcess(int Id, string Name, string ExecutablePath);

internal static class GameProcessService
{
    internal static IReadOnlyList<RunningGameProcess> FindRunningProcesses(string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return Array.Empty<RunningGameProcess>();

        var matches = new List<RunningGameProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var executable = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executable)
                    && IsExecutableInsideFolder(gameFolder, executable))
                    matches.Add(new RunningGameProcess(process.Id, process.ProcessName, executable));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                           or InvalidOperationException
                                           or NotSupportedException)
            {
                // Protected and already-exited processes are expected while enumerating.
            }
            finally
            {
                process.Dispose();
            }
        }

        return matches;
    }

    internal static bool IsExecutableInsideFolder(string folder, string executablePath)
    {
        try
        {
            var normalizedFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedExecutable = Path.GetFullPath(executablePath);
            var relative = Path.GetRelativePath(normalizedFolder, normalizedExecutable);
            return !Path.IsPathRooted(relative)
                && !string.Equals(relative, "..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    internal static async Task<IReadOnlyList<string>> StopProcessesAsync(
        IReadOnlyList<RunningGameProcess> processes,
        TimeSpan? gracefulTimeout = null)
    {
        var errors = new List<string>();
        var timeout = gracefulTimeout ?? TimeSpan.FromSeconds(3);

        foreach (var running in processes)
        {
            try
            {
                using var process = Process.GetProcessById(running.Id);
                if (process.HasExited) continue;

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                    using var gracefulCts = new CancellationTokenSource(timeout);
                    try { await process.WaitForExitAsync(gracefulCts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            catch (ArgumentException)
            {
                // The process exited between discovery and shutdown.
            }
            catch (Exception ex)
            {
                errors.Add($"{running.Name}: {ex.Message}");
            }
        }

        return errors;
    }
}
