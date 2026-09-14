using System;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Phase 3 — the shell routes install retries on typed engine exceptions instead of matching message
/// prefixes. These lock in the contract that route depends on: the types stay
/// <see cref="InvalidOperationException"/> (so existing broad catches still hold) and preserve the
/// human-readable message the confirmation dialog shows.
/// </summary>
public sealed class Dlss5OperationExceptionTests
{
    [Fact]
    public void RecoveredInterruptedSwitch_IsInvalidOperation_AndKeepsMessage()
    {
        var ex = new Dlss5RecoveredInterruptedSwitchException("Recovered the previous interrupted switch.");
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Equal("Recovered the previous interrupted switch.", ex.Message);
    }

    [Fact]
    public void ConflictingPipeline_IsInvalidOperation_AndKeepsMessage()
    {
        const string message = "Remove the current DLSS suite with its × button before switching rendering pipelines.";
        var ex = new Dlss5ConflictingPipelineException(message);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void TheTwoOutcomes_AreDistinctTypes()
    {
        // The install retry loop must be able to tell "recovered a switch, just retry" apart from
        // "a conflicting pipeline exists, offer to replace it" — so they cannot share a type.
        Assert.NotEqual(
            typeof(Dlss5RecoveredInterruptedSwitchException),
            typeof(Dlss5ConflictingPipelineException));
    }
}
