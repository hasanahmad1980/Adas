using System;

namespace RenoDXCommander.Services;

/// <summary>
/// The engine rolled back a half-finished profile switch it found on disk; the game's files and
/// settings are consistent again and the caller should re-assess before applying another profile.
/// Replaces the shell's fragile <c>ex.Message.StartsWith("Recovered the previous interrupted switch")</c>
/// routing with a type the caller can catch precisely.
/// </summary>
public sealed class Dlss5RecoveredInterruptedSwitchException : InvalidOperationException
{
    public Dlss5RecoveredInterruptedSwitchException(string message) : base(message) { }
}

/// <summary>
/// A different DLSS rendering pipeline is already installed for this game and Adas will not stack
/// two of them. The caller may offer to remove the existing pipeline (restoring originals) and retry.
/// Replaces the shell's <c>ex.Message.StartsWith("Remove the ")</c> routing. The human-readable
/// message is preserved verbatim so the confirmation dialog can still show exactly what conflicts.
/// </summary>
public sealed class Dlss5ConflictingPipelineException : InvalidOperationException
{
    public Dlss5ConflictingPipelineException(string message) : base(message) { }
}
