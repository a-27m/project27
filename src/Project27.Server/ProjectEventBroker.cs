using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Project27.Server;

public sealed record ProjectEvent(string Kind, string Data);

/// <summary>
/// In-memory per-project SSE fan-out (single-node scope; see
/// docs/spec/06-server.md). Slow subscribers drop oldest events instead of
/// blocking publishers.
/// </summary>
public sealed class ProjectEventBroker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ProjectEvent>>> _subscribers = new();

    public void Publish(Guid projectId, string kind, object payload)
    {
        if (!_subscribers.TryGetValue(projectId, out var channels))
        {
            return;
        }

        var @event = new ProjectEvent(kind, JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
        foreach (var channel in channels.Values)
        {
            channel.Writer.TryWrite(@event);
        }
    }

    public async IAsyncEnumerable<ProjectEvent> Subscribe(
        Guid projectId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ProjectEvent>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        var channels = _subscribers.GetOrAdd(projectId, _ => new ConcurrentDictionary<Guid, Channel<ProjectEvent>>());
        channels[id] = channel;
        try
        {
            await foreach (var @event in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return @event;
            }
        }
        finally
        {
            channels.TryRemove(id, out _);
        }
    }
}
