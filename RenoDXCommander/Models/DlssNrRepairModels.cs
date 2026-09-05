namespace RenoDXCommander.Models;

public enum DlssNrClassification
{
    Missing,
    KnownGood,
    TrustedNvidiaOtherVersion,
    InvalidOrUntrusted,
}

public sealed record DlssNrFileState(
    string Path,
    DlssNrClassification Classification,
    string? Version,
    string? Sha256,
    string? Signer,
    bool SignatureValid,
    string? Error = null);

public enum DlssNrRepairActionKind
{
    None,
    ReplaceInvalid,
    DeployMissing,
}

public sealed record DlssNrRepairAction(
    string TargetPath,
    DlssNrRepairActionKind Kind,
    DlssNrFileState CurrentState,
    string Reason);

public sealed record DlssNrRepairPlan(
    string SourcePath,
    DlssNrFileState SourceState,
    IReadOnlyList<DlssNrRepairAction> Actions,
    IReadOnlyList<DlssNrFileState> UnchangedFiles)
{
    public int ChangeCount => Actions.Count(action => action.Kind != DlssNrRepairActionKind.None);
}

public sealed record DlssNrRepairResult(
    string TargetPath,
    DlssNrRepairActionKind Action,
    bool Succeeded,
    string? BackupPath,
    string Message);
