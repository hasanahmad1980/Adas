using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// C# port of kayle2203/dlssnr-signature-repair. It never downloads a DLL:
/// the source must be a game the user lawfully owns, and the payload is accepted
/// only when its version, SHA-256 hash, NVIDIA signer, and Windows trust result all
/// match the upstream repair contract.
/// </summary>
public sealed class DlssNrRepairService
{
    public const string KnownGoodVersion = "310.8.0.0";
    public const string KnownGoodSha256 = "E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E";
    public const string DllName = "nvngx_dlssnr.dll";
    public const string AddonName = Renodx5AddonService.AddonFileName;

    private readonly ICrashReporter _crashReporter;

    public DlssNrRepairService(ICrashReporter crashReporter)
        => _crashReporter = crashReporter;

    public DlssNrFileState Inspect(string path)
    {
        if (!File.Exists(path))
            return new(path, DlssNrClassification.Missing, null, null, null, false);

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = info.FileVersion?.Trim();
            var hash = FileHelper.ComputeSha256(path);
            var signatureValid = AuthenticodeVerifier.IsTrusted(path);
            string? signer = null;
            try
            {
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                signer = certificate.Subject;
            }
            catch { }

            var isNvidia = signer?.Contains("NVIDIA Corporation", StringComparison.OrdinalIgnoreCase) == true;
            var descriptionLooksRight = (info.FileDescription?.Contains("DLSS", StringComparison.OrdinalIgnoreCase) == true
                || info.ProductName?.Contains("DLSS", StringComparison.OrdinalIgnoreCase) == true
                || info.OriginalFilename?.Equals(DllName, StringComparison.OrdinalIgnoreCase) == true);
            var trustedNvidiaDlssNr = signatureValid && isNvidia && descriptionLooksRight;
            var exact = trustedNvidiaDlssNr
                && string.Equals(version, KnownGoodVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(hash, KnownGoodSha256, StringComparison.OrdinalIgnoreCase);

            var classification = exact
                ? DlssNrClassification.KnownGood
                : trustedNvidiaDlssNr
                    ? DlssNrClassification.TrustedNvidiaOtherVersion
                    : DlssNrClassification.InvalidOrUntrusted;

            return new(path, classification, version, hash, signer, signatureValid);
        }
        catch (Exception ex)
        {
            return new(path, DlssNrClassification.InvalidOrUntrusted, null, null, null, false, ex.Message);
        }
    }

    public string FindKnownGoodPayload(string sourcePath, bool recurse = true)
    {
        if (File.Exists(sourcePath))
        {
            var state = Inspect(sourcePath);
            if (state.Classification == DlssNrClassification.KnownGood) return sourcePath;
            throw new InvalidOperationException(BuildSourceFailure(state));
        }

        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Source folder was not found: {sourcePath}");

        var files = EnumerateCandidateFiles(sourcePath, recurse);

        foreach (var candidate in files.Where(file => Path.GetFileName(file).Equals(DllName, StringComparison.OrdinalIgnoreCase)))
        {
            var state = Inspect(candidate);
            if (state.Classification == DlssNrClassification.KnownGood)
                return candidate;
        }

        throw new InvalidOperationException(
            $"The source did not contain the exact verified DLSSNR {KnownGoodVersion} build. No files were changed.");
    }

    public DlssNrRepairPlan CreatePlan(
        string sourcePath,
        string targetPath,
        bool recurse = true,
        bool deployIfAddonPresent = true)
    {
        var payload = FindKnownGoodPayload(sourcePath, recurse);
        var sourceState = Inspect(payload);
        if (sourceState.Classification != DlssNrClassification.KnownGood)
            throw new InvalidOperationException(BuildSourceFailure(sourceState));
        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException($"Target folder was not found: {targetPath}");

        var targetFiles = (recurse
                ? Dlss5CompatibilityService.EnumerateFilesSafe(targetPath, maxDepth: 12)
                : Directory.EnumerateFiles(targetPath))
            .ToArray();

        var nrPaths = DiscoverTargetPaths(targetPath, targetFiles, deployIfAddonPresent);

        var actions = new List<DlssNrRepairAction>();
        var unchanged = new List<DlssNrFileState>();

        foreach (var nrPath in nrPaths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var state = Inspect(nrPath);
            switch (state.Classification)
            {
                case DlssNrClassification.InvalidOrUntrusted:
                    actions.Add(new(nrPath, DlssNrRepairActionKind.ReplaceInvalid, state,
                        "Signature, signer, description, version, or hash did not satisfy the trusted NVIDIA contract."));
                    break;
                case DlssNrClassification.Missing when deployIfAddonPresent:
                    actions.Add(new(nrPath, DlssNrRepairActionKind.DeployMissing, state,
                        $"{AddonName} is present for this ReShade deployment, whose runtime root is missing {DllName}."));
                    break;
                default:
                    unchanged.Add(state);
                    break;
            }
        }

        return new(payload, sourceState, actions, unchanged);
    }

    internal static HashSet<string> DiscoverTargetPaths(
        string gameRoot,
        IReadOnlyCollection<string> targetFiles,
        bool deployIfAddonPresent)
    {
        var nrPaths = new HashSet<string>(
            targetFiles.Where(file => Path.GetFileName(file).Equals(DllName, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);

        if (!deployIfAddonPresent)
            return nrPaths;

        var addonPaths = targetFiles
            .Where(file => Path.GetFileName(file).Equals(AddonName, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AddRuntimeTargetForAddonOwner(gameRoot, addonPaths, nrPaths);

        var hostRoot = Path.Combine(gameRoot, "host64");
        if (Directory.Exists(hostRoot))
            AddRuntimeTargetForAddonOwner(hostRoot, addonPaths, nrPaths);

        return nrPaths;
    }

    private static void AddRuntimeTargetForAddonOwner(
        string ownerRoot,
        IReadOnlySet<string> addonPaths,
        HashSet<string> nrPaths)
    {
        var rootAddon = Path.GetFullPath(Path.Combine(ownerRoot, AddonName));
        var configuredAddon = Path.GetFullPath(Path.Combine(ModInstallService.GetAddonDeployPath(ownerRoot), AddonName));
        if (addonPaths.Contains(rootAddon)
            || addonPaths.Contains(configuredAddon)
            || File.Exists(rootAddon)
            || File.Exists(configuredAddon))
            nrPaths.Add(Path.GetFullPath(Path.Combine(ownerRoot, DllName)));
    }

    internal static IEnumerable<string> EnumerateCandidateFiles(string sourcePath, bool recurse)
        => recurse
            ? Dlss5CompatibilityService.EnumerateFilesSafe(sourcePath, maxDepth: 12)
            : Directory.EnumerateFiles(sourcePath);

    public IReadOnlyList<DlssNrRepairResult> Execute(DlssNrRepairPlan plan)
    {
        if (plan.SourceState.Classification != DlssNrClassification.KnownGood
            || Inspect(plan.SourcePath).Classification != DlssNrClassification.KnownGood)
            throw new InvalidOperationException("The verified source payload changed after preview. No files were changed.");

        var results = new List<DlssNrRepairResult>();
        foreach (var action in plan.Actions.Where(action => action.Kind != DlssNrRepairActionKind.None))
            results.Add(InstallKnownGood(action, plan.SourcePath));

        var succeeded = results.Count(result => result.Succeeded);
        _crashReporter.Log($"[DlssNrRepairService] Completed: {succeeded}/{results.Count} changed successfully; source='{plan.SourcePath}'");
        return results;
    }

    private DlssNrRepairResult InstallKnownGood(DlssNrRepairAction action, string payloadPath)
    {
        var target = action.TargetPath;
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);

        string? backup = null;
        var temp = Path.Combine(directory, $".{DllName}.{Guid.NewGuid():N}.adas-staging");

        try
        {
            var currentState = Inspect(target);
            if (!MatchesPreview(currentState, action.CurrentState))
                throw new InvalidOperationException("The target DLL changed after preview. Create a new repair preview before applying changes.");

            if (File.Exists(target))
            {
                using var lockProbe = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }

            File.Copy(payloadPath, temp, overwrite: false);
            if (Inspect(temp).Classification != DlssNrClassification.KnownGood)
                throw new InvalidOperationException("Staged payload failed post-copy verification.");

            if (File.Exists(target))
            {
                backup = BuildBackupPath(target);
                File.Replace(temp, target, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, target);
            }
            if (Inspect(target).Classification != DlssNrClassification.KnownGood)
                throw new InvalidOperationException("Installed payload failed post-install verification.");

            _crashReporter.Log($"[DlssNrRepairService] {action.Kind}: '{target}', backup='{backup ?? "(none)"}'");
            return new(target, action.Kind, true, backup, "Verified NVIDIA DLSSNR installed successfully.");
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (backup != null && File.Exists(backup))
                {
                    if (File.Exists(target))
                        File.Replace(backup, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    else
                        File.Move(backup, target);
                }
            }
            catch (Exception rollbackError)
            {
                ex = new AggregateException(ex, new InvalidOperationException($"Rollback failed: {rollbackError.Message}", rollbackError));
            }

            _crashReporter.Log($"[DlssNrRepairService] Failed '{target}' — {ex.Message}");
            return new(target, action.Kind, false, backup, ex.Message);
        }
    }

    private static string BuildBackupPath(string target)
    {
        var directory = Path.GetDirectoryName(target)!;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(directory, $"{DllName}.adas-backup-{stamp}");
        for (var suffix = 1; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{DllName}.adas-backup-{stamp}-{suffix}");
        return candidate;
    }

    private static string BuildSourceFailure(DlssNrFileState state)
        => $"Source verification failed. Expected DLSSNR {KnownGoodVersion}, SHA-256 {KnownGoodSha256}, and a valid NVIDIA signature; "
         + $"found version '{state.Version ?? "unknown"}', hash '{state.Sha256 ?? "unknown"}', signer '{state.Signer ?? "unknown"}', signature valid={state.SignatureValid}.";

    private static bool MatchesPreview(DlssNrFileState current, DlssNrFileState preview)
        => current.Classification == preview.Classification
           && string.Equals(current.Sha256, preview.Sha256, StringComparison.OrdinalIgnoreCase)
           && string.Equals(current.Version, preview.Version, StringComparison.OrdinalIgnoreCase)
           && string.Equals(current.Signer, preview.Signer, StringComparison.OrdinalIgnoreCase)
           && current.SignatureValid == preview.SignatureValid;

    private static class AuthenticodeVerifier
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        public static bool IsTrusted(string filePath)
        {
            var fileInfo = new WinTrustFileInfo(filePath);
            var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var data = new WinTrustData(fileInfoPointer);
                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeHGlobal(fileInfoPointer);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true, SetLastError = true)]
        private static extern int WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
            ref WinTrustData trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;

            public WinTrustFileInfo(string filePath)
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
                FilePath = filePath;
                FileHandle = IntPtr.Zero;
                KnownSubject = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;

            public WinTrustData(IntPtr fileInfo)
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>();
                PolicyCallbackData = IntPtr.Zero;
                SipClientData = IntPtr.Zero;
                UiChoice = 2; // WTD_UI_NONE
                RevocationChecks = 0;
                UnionChoice = 1; // WTD_CHOICE_FILE
                FileInfo = fileInfo;
                StateAction = 0;
                StateData = IntPtr.Zero;
                UrlReference = IntPtr.Zero;
                ProviderFlags = 0x00001000; // WTD_CACHE_ONLY_URL_RETRIEVAL: no network
                UiContext = 0;
            }
        }
    }
}
