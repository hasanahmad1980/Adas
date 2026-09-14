using System.Threading;

namespace RenoDXCommander.Services;

/// <summary>
/// Monotonic "latest wins" gate for background work whose result must be discarded once a newer
/// request has started. Each <see cref="Begin"/> increments the generation and hands back a
/// <see cref="GenerationToken"/>; a completed background task applies its result only while
/// <see cref="GenerationToken.IsCurrent"/> is still true. This is the deterministic core of the
/// setup pane's assessment guard — a slow probe for a superseded selection can no longer win a race
/// against the newest one. Thread-safe.
/// </summary>
internal sealed class GenerationGate
{
    private long _current;

    /// <summary>The generation of the most recently started request (0 before the first <see cref="Begin"/>).</summary>
    public long Current => Interlocked.Read(ref _current);

    /// <summary>Starts a new request, superseding any earlier one, and returns its token.</summary>
    public GenerationToken Begin()
    {
        var generation = Interlocked.Increment(ref _current);
        return new GenerationToken(this, generation);
    }

    internal bool IsCurrent(long generation) => Interlocked.Read(ref _current) == generation;
}

/// <summary>A single request's place in a <see cref="GenerationGate"/>.</summary>
internal readonly struct GenerationToken
{
    private readonly GenerationGate _gate;

    internal GenerationToken(GenerationGate gate, long generation)
    {
        _gate = gate;
        Generation = generation;
    }

    /// <summary>This request's generation number; increases with each new request on the gate.</summary>
    public long Generation { get; }

    /// <summary>True while no newer request has begun on the owning gate — i.e. this result is still the latest.</summary>
    public bool IsCurrent => _gate.IsCurrent(Generation);
}
