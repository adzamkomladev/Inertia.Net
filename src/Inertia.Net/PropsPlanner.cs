using System.Runtime.ExceptionServices;

namespace Inertia.Net;

/// <summary>
/// Starts the loaders of <see cref="InertiaProp.Parallel"/> props before the props walk reaches them, at most
/// <see cref="InertiaPropsOptions.MaxConcurrency"/> at a time. The walk still writes values and collects metadata in protocol order;
/// it takes each started task instead of calling the loader. One instance per render that has parallel props.
/// </summary>
internal sealed class PropsPlanner : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly InertiaPageWriter _owner;
    private readonly SemaphoreSlim _gate;
    private readonly List<Planned> _planned = [];
    private readonly Dictionary<InertiaProp, Planned> _pending = new(ReferenceEqualityComparer.Instance);
    private volatile bool _failed;

    public PropsPlanner(InertiaPageWriter owner, CancellationToken requestAborted)
    {
        _owner = owner;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        _gate = new SemaphoreSlim(owner.Options.Props.MaxConcurrency, owner.Options.Props.MaxConcurrency);
    }

    /// <summary>A started loader failed without being rescued; the others were cancelled and the render fails.</summary>
    public bool Failed => _failed;

    /// <summary>
    /// Plans a prop the walk will resolve, in protocol order. An instance added twice is not planned: it resolves at each path. A cached
    /// prop's pending <paramref name="lookup"/> is awaited first: a hit is the task's result, and the loader does not run.
    /// </summary>
    public void Add(InertiaProp prop, Task<CachedProp?>? lookup = null)
    {
        if (_pending.TryGetValue(prop, out var existing))
        {
            existing.Duplicate = true;
            return;
        }

        var planned = new Planned(prop) { Lookup = lookup };
        _pending.Add(prop, planned);
        _planned.Add(planned);
    }

    /// <summary>Starts every planned loader; the ones beyond the concurrency limit wait for a slot in order.</summary>
    public void Start()
    {
        foreach (var planned in _planned)
        {
            if (planned.Duplicate)
            {
                _pending.Remove(planned.Prop);
                continue;
            }

            planned.Task = RunAsync(planned);
        }
    }

    /// <summary>Whether <paramref name="prop"/> has a started task the walk has not taken yet.</summary>
    public bool IsPlanned(InertiaProp prop) => _pending.ContainsKey(prop);

    /// <summary>The started loader of <paramref name="prop"/>, handed out once.</summary>
    public bool TryTake(InertiaProp prop, out Task<object?> task)
    {
        if (_pending.Remove(prop, out var planned) && planned.Task is { } started)
        {
            task = started;
            return true;
        }

        task = null!;
        return false;
    }

    /// <summary>
    /// Waits for every started loader, so none outlives the render (and the request's services). With <paramref name="cancel"/> the
    /// loaders still running are cancelled first.
    /// </summary>
    public async ValueTask DrainAsync(bool cancel)
    {
        if (cancel)
        {
            Cancel();
        }

        foreach (var planned in _planned)
        {
            Task? task = planned.Task; // awaited as a Task: Task<T> rejects SuppressThrowing at runtime (CA2261)
            if (task is not null)
            {
                await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                _ = task.Exception; // observed: the walk reports failures itself
            }
        }
    }

    /// <summary>The failure of the first planned prop, in protocol order, that failed without being rescued.</summary>
    public ExceptionDispatchInfo? FirstFailure()
    {
        foreach (var planned in _planned)
        {
            if (planned.Failure is { } failure)
            {
                return failure;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _cancellation.Dispose();
        _gate.Dispose();
    }

    private async Task<object?> RunAsync(Planned planned)
    {
        var cancellationToken = _cancellation.Token;
        if (planned.Lookup is { } lookup && await lookup is { } hit)
        {
            return hit;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await (planned.Prop.Caching is null
                ? planned.Prop.ResolveAsync(cancellationToken)
                : _owner.LoadCachedPropAsync(planned.Prop, cancellationToken));
        }
        catch (Exception ex) when (!planned.Prop.ShouldRescue && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // The render fails either way: stop the other loaders instead of waiting for them.
            planned.Failure = ExceptionDispatchInfo.Capture(ex);
            _failed = true;
            Cancel();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A loader's cancellation callback threw; the loaders are still awaited and their own failures reported.
        }
    }

    private sealed class Planned(InertiaProp prop)
    {
        public InertiaProp Prop { get; } = prop;

        public Task<object?>? Task { get; set; }

        public Task<CachedProp?>? Lookup { get; init; }

        public ExceptionDispatchInfo? Failure { get; set; }

        public bool Duplicate { get; set; }
    }
}
