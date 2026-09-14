using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Phase 2 — "latest assessment wins". The setup pane runs each probe in the background and applies
/// its result only while the generation is still current, so a slow probe for a superseded selection
/// cannot overwrite the newest one. Ordering here is forced with <see cref="TaskCompletionSource"/>,
/// never sleeps, so the race outcome is deterministic.
/// </summary>
public sealed class GenerationGateTests
{
    [Fact]
    public void Current_IsZero_BeforeAnyRequest()
        => Assert.Equal(0, new GenerationGate().Current);

    [Fact]
    public void Begin_SupersedesEarlierRequests()
    {
        var gate = new GenerationGate();
        var first = gate.Begin();
        Assert.True(first.IsCurrent);

        var second = gate.Begin();
        Assert.False(first.IsCurrent);   // superseded
        Assert.True(second.IsCurrent);
        Assert.True(second.Generation > first.Generation);
        Assert.Equal(second.Generation, gate.Current);
    }

    [Fact]
    public async Task StaleProbeCompletingLast_DoesNotOverwriteLatestResult()
    {
        var gate = new GenerationGate();
        string? applied = null;

        // Two background probes, whose completion order we control deterministically.
        var slowStale = new TaskCompletionSource();
        var fastLatest = new TaskCompletionSource();

        // Probe A (stale) begins first, then probe B (latest) begins and supersedes it.
        var tokenA = gate.Begin();
        var tokenB = gate.Begin();

        async Task Run(GenerationToken token, string label, Task gate2)
        {
            await gate2;                       // wait until the test releases this probe
            if (token.IsCurrent) applied = label;
        }

        var a = Run(tokenA, "A-stale", slowStale.Task);
        var b = Run(tokenB, "B-latest", fastLatest.Task);

        // The newest probe finishes first and applies.
        fastLatest.SetResult();
        await b;
        Assert.Equal("B-latest", applied);

        // The stale probe finishes LAST but must NOT overwrite the newer result.
        slowStale.SetResult();
        await a;
        Assert.Equal("B-latest", applied);
    }

    [Fact]
    public void ReselectingSameGame_StartsAFreshGeneration()
    {
        // Re-selecting/refreshing the same card must bump the generation so an earlier in-flight
        // probe for that card is superseded too — ReferenceEquals alone would let it win.
        var gate = new GenerationGate();
        var firstRefresh = gate.Begin();
        var secondRefresh = gate.Begin();

        Assert.False(firstRefresh.IsCurrent);
        Assert.True(secondRefresh.IsCurrent);
    }
}
