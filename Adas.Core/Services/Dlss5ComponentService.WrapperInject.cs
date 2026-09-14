using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Launches a DLSS 5 tool that hosts nvngx_dlssnr.dll in its own process and, on RTX 20/30/40
/// cards, injects adas-arch-shim.dll so the tool can create DLSS 5 Neural Rendering (feature 18).
/// The shim spoofs NvAPI_GPU_GetArchInfo to Blackwell inside the tool's process only — the same
/// technique NeuralScreen's worker uses on itself. Nothing on disk is modified; on a Blackwell
/// card the shim installs nothing and the launch is identical to a plain start.
/// </summary>
public sealed partial class Dlss5ComponentService
{
    /// <summary>Location of the bundled arch-spoof shim (copied to Assets\DLSS5 with the other payloads).</summary>
    public static string ArchSpoofShimPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "DLSS5", "adas-arch-shim.dll");

    /// <summary>
    /// True when the shim is needed for this GPU: RTX 40/30/20 (or unknown) rather than RTX 50.
    /// A Blackwell card can create feature 18 unaided, so no injection is attempted there.
    /// </summary>
    public static bool ArchSpoofNeeded(string? gpuName)
        => !string.IsNullOrWhiteSpace(gpuName)
           && !System.Text.RegularExpressions.Regex.IsMatch(gpuName, @"RTX\s*50\d\d",
               System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Starts <paramref name="executable"/>. When <paramref name="injectShim"/> is set and the shim
    /// exists, the process is created suspended, the shim is injected, its patch is awaited, and only
    /// then is the main thread resumed — so the spoof is in place before the tool touches NGX.
    /// Any failure in the injection path falls back to a normal resumed start (the tool then reports
    /// its own "Unsupported GPU architecture", which is no worse than launching without the shim).
    /// </summary>
    internal int StartWithOptionalArchSpoof(string executable, string workingDirectory, bool injectShim)
    {
        if (!injectShim || !File.Exists(ArchSpoofShimPath))
        {
            using var plain = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
            });
            return plain?.Id ?? 0;
        }
        return LaunchSuspendedAndInject(executable, workingDirectory, ArchSpoofShimPath);
    }

    private int LaunchSuspendedAndInject(string executable, string workingDirectory, string shimPath)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var commandLine = "\"" + executable + "\"";
        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_SUSPENDED, IntPtr.Zero, workingDirectory, ref si, out var pi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the wrapper suspended.");

        EventWaitHandle? patched = null;
        try
        {
            // Named event the shim signals from inside the target once its patch is installed.
            patched = new EventWaitHandle(false, EventResetMode.ManualReset, $"Local\\AdasArchShim-{pi.dwProcessId}");
            InjectLibrary(pi.hProcess, shimPath);
            // Give the in-process patch time to cache the GPU list and rewrite the prologue.
            if (!patched.WaitOne(TimeSpan.FromSeconds(10)))
                _crashReporter.Log("[FullScreenWrapper] arch-spoof shim did not report back within 10s; resuming anyway.");
            else
                _crashReporter.Log("[FullScreenWrapper] arch-spoof shim reported its patch is installed.");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[FullScreenWrapper] arch-spoof injection failed ({ex.Message}); resuming without it.");
        }
        finally
        {
            ResumeThread(pi.hThread);
            patched?.Dispose();
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
        return (int)pi.dwProcessId;
    }

    private static void InjectLibrary(IntPtr process, string dllPath)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
        var remote = VirtualAllocEx(process, IntPtr.Zero, (uint)bytes.Length, MEM_COMMIT_RESERVE, PAGE_READWRITE);
        if (remote == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed.");
        try
        {
            if (!WriteProcessMemory(process, remote, bytes, (uint)bytes.Length, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed.");

            var kernel32 = GetModuleHandle("kernel32.dll");
            // kernel32 loads at the same base in every process on a given boot, so LoadLibraryW's
            // address here is valid in the target.
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryW not found.");

            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
            if (thread == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed.");
            try { WaitForSingleObject(thread, 10000); }
            finally { CloseHandle(thread); }
        }
        finally
        {
            VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        }
    }

    // ── Win32 ────────────────────────────────────────────────────────────────────────────────
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint MEM_COMMIT_RESERVE = 0x1000 | 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
