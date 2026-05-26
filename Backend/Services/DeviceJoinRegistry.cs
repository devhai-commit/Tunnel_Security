using System.Collections.Concurrent;

namespace Backend.Services;

/// <summary>
/// Singleton registry — maps pending join request IDs to their awaiting TaskCompletionSource.
/// The WebSocket handler registers a TCS; the REST controller resolves it when the operator decides.
/// </summary>
public class DeviceJoinRegistry
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JoinDecision>> _pending = new();

    public TaskCompletionSource<JoinDecision> Register(int requestId)
    {
        var tcs = new TaskCompletionSource<JoinDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        return tcs;
    }

    /// <summary>Returns true if the requestId was found and the decision was applied.</summary>
    public bool TryDecide(int requestId, JoinDecision decision)
    {
        if (_pending.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetResult(decision);
            return true;
        }
        return false;
    }

    public void CancelAll()
    {
        foreach (var key in _pending.Keys.ToArray())
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
    }
}

public sealed record JoinDecision(bool Accepted, byte AssignedNodeByteId);
