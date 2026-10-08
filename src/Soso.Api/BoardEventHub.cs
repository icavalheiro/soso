using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Soso.Api;

public sealed record BoardChangedEvent(string? BoardId);

public sealed class BoardEventHub
{
    public sealed class Subscription(string userId) : IDisposable
    {
        private readonly object gate = new();
        private readonly HashSet<string> pendingBoards = new(StringComparer.Ordinal);
        private bool invalidateAll;
        private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        public string UserId { get; } = userId;

        public void Publish(string boardId)
        {
            lock (gate)
            {
                if (!invalidateAll)
                {
                    pendingBoards.Add(boardId);
                    if (pendingBoards.Count > 64)
                    {
                        pendingBoards.Clear();
                        invalidateAll = true;
                    }
                }
            }
            signals.Writer.TryWrite(true);
        }

        public IAsyncEnumerable<BoardChangedEvent> ReadAllAsync(CancellationToken cancellationToken = default) => ReadCoreAsync(cancellationToken);

        private async IAsyncEnumerable<BoardChangedEvent> ReadCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await signals.Reader.WaitToReadAsync(cancellationToken))
            {
                while (signals.Reader.TryRead(out _)) { }
                BoardChangedEvent[] events;
                lock (gate)
                {
                    events = invalidateAll
                        ? [new BoardChangedEvent(null)]
                        : pendingBoards.Select(id => new BoardChangedEvent(id)).ToArray();
                    pendingBoards.Clear();
                    invalidateAll = false;
                }
                foreach (var item in events)
                {
                    yield return item;
                }
            }
        }

        public void Dispose() => signals.Writer.TryComplete();
    }

    private readonly ConcurrentDictionary<Guid, Subscription> subscriptions = new();

    public IDisposable Subscribe(string userId, out Subscription subscription)
    {
        var id = Guid.NewGuid();
        subscription = new Subscription(userId);
        var subscribed = subscription;
        subscriptions[id] = subscribed;
        return new Unsubscriber(() =>
        {
            subscriptions.TryRemove(id, out _);
            subscribed.Dispose();
        });
    }

    public void Publish(string boardId, IEnumerable<string> userIds)
    {
        var recipients = userIds.ToHashSet(StringComparer.Ordinal);
        foreach (var subscription in subscriptions.Values)
        {
            if (recipients.Contains(subscription.UserId))
            {
                subscription.Publish(boardId);
            }
        }
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
