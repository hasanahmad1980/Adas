using System.Diagnostics;
using System.Xml.Linq;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

internal static class Dlss5LaunchRecoveryService
{
    internal const int StatusStackOverflow = unchecked((int)0xC00000FD);

    internal static bool ShouldActivateDx9Fallback(
        Dlss5DeploymentMode mode,
        GraphicsApiType detectedApi,
        int exitCode)
        => detectedApi == GraphicsApiType.DirectX9
           && IsRecoverableFeederMode(mode)
           && exitCode == StatusStackOverflow;

    internal static bool RecordExit(string? deploymentPath, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(deploymentPath) || !Directory.Exists(deploymentPath)) return false;

        try
        {
            var record = Dlss5ComponentService.LoadRecord(deploymentPath);
            if (record == null) return false;
            var detectedApi = GraphicsEnvironmentService.Detect(deploymentPath).Api;
            if (!ShouldActivateDx9Fallback(record.Mode, detectedApi, exitCode)) return false;
            if (!HasManagedDirectX9ReShadeChain(deploymentPath, record)) return false;

            record.PreferDxvkForDirectX9 = true;
            record.Dx9FallbackDetectedAtUtc = DateTime.UtcNow;
            Dlss5ComponentService.SaveRecord(deploymentPath, record);
            CrashReporter.Log($"[DLSS launch recovery] Confirmed STATUS_STACK_OVERFLOW in the managed DX9 dgVoodoo/ReShade route at '{deploymentPath}'. DXVK/Vulkan recovery will be selected for this game only.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            CrashReporter.Log($"[DLSS launch recovery] Could not record DX9 fallback: {ex.Message}");
            return false;
        }
    }

    internal static bool TryRecordRecentWindowsCrash(
        string deploymentPath,
        Dlss5InstallRecord record,
        GraphicsApiType detectedApi)
    {
        if (record.PreferDxvkForDirectX9)
            return record.PreferDxvkForDirectX9;
        if (detectedApi != GraphicsApiType.DirectX9 || !IsRecoverableFeederMode(record.Mode)) return false;
        if (!HasManagedDirectX9ReShadeChain(deploymentPath, record)) return false;

        try
        {
            var executable = Path.Combine(Environment.SystemDirectory, "wevtutil.exe");
            if (!File.Exists(executable)) return false;
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("qe");
            start.ArgumentList.Add("Application");
            start.ArgumentList.Add("/q:*[System[Provider[@Name='Application Error'] and (EventID=1000) and TimeCreated[timediff(@SystemTime) <= 604800000]]]");
            start.ArgumentList.Add("/rd:true");
            start.ArgumentList.Add("/f:xml");
            start.ArgumentList.Add("/c:100");
            using var process = Process.Start(start);
            if (process == null) return false;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2500))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
            var xml = output.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !HasMatchingWindowsCrash(xml, deploymentPath, record.InstalledAtUtc.AddMinutes(-1)))
                return false;

            record.PreferDxvkForDirectX9 = true;
            record.Dx9FallbackDetectedAtUtc = DateTime.UtcNow;
            Dlss5ComponentService.SaveRecord(deploymentPath, record);
            CrashReporter.Log($"[DLSS launch recovery] Found a recent Windows Application Error proving the local DX9 ReShade chain stack-overflowed at '{deploymentPath}'.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashReporter.Log($"[DLSS launch recovery] Windows crash history was unavailable: {ex.Message}");
            return false;
        }
    }

    internal static bool HasMatchingWindowsCrash(string xml, string deploymentPath, DateTime installedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            // wevtutil emits adjacent <Event> elements rather than one XML document.
            // A neutral root accepts that stream and also accepts its optional <Events> wrapper.
            var document = XDocument.Parse("<Root>" + xml + "</Root>", LoadOptions.None);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var expectedModule = Path.GetFullPath(Path.Combine(deploymentPath, "dxgi.dll"));
            foreach (var item in document.Descendants(ns + "Event"))
            {
                var timestampText = item.Descendants(ns + "TimeCreated").FirstOrDefault()?.Attribute("SystemTime")?.Value;
                if (!DateTime.TryParse(timestampText, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var timestamp)
                    || timestamp.ToUniversalTime() < installedAtUtc.ToUniversalTime()) continue;
                var values = item.Descendants(ns + "Data")
                    .Where(value => value.Attribute("Name") != null)
                    .GroupBy(value => value.Attribute("Name")!.Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Value.Trim(), StringComparer.OrdinalIgnoreCase);
                var exception = Value(values, "ExceptionCode");
                var appPath = Value(values, "AppPath", "FaultingApplicationPath");
                var modulePath = Value(values, "ModulePath", "FaultingModulePath");
                if (exception.TrimStart('0', 'x').Equals("c00000fd", StringComparison.OrdinalIgnoreCase)
                    && GameProcessService.IsExecutableInsideFolder(deploymentPath, appPath)
                    && Path.GetFullPath(modulePath).Equals(expectedModule, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or ArgumentException or IOException or NotSupportedException) { }
        return false;
    }

    private static string Value(IReadOnlyDictionary<string, string> values, params string[] names)
        => names.Select(name => values.TryGetValue(name, out var value) ? value : "")
            .FirstOrDefault(value => value.Length > 0) ?? "";

    private static bool HasManagedDirectX9ReShadeChain(string deploymentPath, Dlss5InstallRecord record)
    {
        var wrapper = Path.Combine(deploymentPath, "d3d9.dll");
        var reshade = Path.Combine(deploymentPath, "dxgi.dll");
        var hasManagedAddon = record.InstalledHashes.Keys.Any(path => Path.GetFileName(path).Equals(
                Dlss5ComponentService.FeederAddon32, StringComparison.OrdinalIgnoreCase))
            && File.Exists(reshade)
            && AuxInstallService.IsReShadeFileStrict(reshade);
        if (!hasManagedAddon) return false;

        // A correctly-labelled DX9 install uses dgVoodoo. A stale/manual DX11
        // record may instead put ReShade directly in dxgi.dll even though launch
        // evidence proves the game created a D3D9 device. Both chains crash in
        // the same local ReShade module and must recover to the DXVK route.
        return record.Mode != Dlss5DeploymentMode.Dx9Feeder
               || (File.Exists(wrapper)
                   && (FileVersionInfo.GetVersionInfo(wrapper).FileDescription?.Contains(
                       "dgVoodoo", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static bool IsRecoverableFeederMode(Dlss5DeploymentMode mode)
        => Dlss5CompatibilityService.IsFeederMode(mode)
           && mode != Dlss5DeploymentMode.Dx9ViaDxvkFeeder;
}
