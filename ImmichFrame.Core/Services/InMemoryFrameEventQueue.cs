using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImmichFrame.Core.Events;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Services;

public class InMemoryFrameEventQueue : IFrameEventQueue
{
    private class EventRecord
    {
        public FrameEvent Event { get; }
        public DateTime? ExpiresAt { get; }

        public EventRecord(FrameEvent frameEvent)
        {
            Event = frameEvent;
            if (frameEvent.TimeoutMs is int timeout && timeout > 0)
            {
                ExpiresAt = frameEvent.PostedAt.AddMilliseconds(timeout);
            }
        }
    }

    private class DeviceQueue
    {
        private readonly Dictionary<string, EventRecord> _eventsById = new();
        private readonly Dictionary<string, string> _categoryIndex = new();
        private readonly SortedSet<EventRecord> _priorityQueue = new(new EventComparer());
        private readonly Dictionary<string, FrameEventAckStatus> _ackStatusById = new();

        public string? ActiveEventId { get; set; }

        private sealed class EventComparer : IComparer<EventRecord>
        {
            public int Compare(EventRecord? x, EventRecord? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                var priorityComparison = x.Event.Priority.CompareTo(y.Event.Priority);
                if (priorityComparison != 0) return priorityComparison;

                var postedComparison = x.Event.PostedAt.CompareTo(y.Event.PostedAt);
                if (postedComparison != 0) return postedComparison;

                return string.CompareOrdinal(x.Event.Id, y.Event.Id);
            }
        }

        public bool ContainsEvent(string eventId) => _eventsById.ContainsKey(eventId);

        public void Add(EventRecord record)
        {
            _eventsById[record.Event.Id] = record;
            _priorityQueue.Add(record);

            if (!string.IsNullOrWhiteSpace(record.Event.Category))
            {
                _categoryIndex[record.Event.Category!] = record.Event.Id;
            }

            _ackStatusById[record.Event.Id] = FrameEventAckStatus.Shown; // default state placeholder
        }

        public IEnumerable<EventRecord> RemoveByCategory(string category)
        {
            if (!_categoryIndex.TryGetValue(category, out var eventId)) yield break;

            if (_eventsById.TryGetValue(eventId, out var record))
            {
                RemoveRecord(record);
                yield return record;
            }
        }

        public IEnumerable<EventRecord> RemoveAll()
        {
            foreach (var record in _priorityQueue.ToList())
            {
                RemoveRecord(record);
                yield return record;
            }
        }

        public bool TryGetById(string eventId, out EventRecord record) => _eventsById.TryGetValue(eventId, out record!);

        public EventRecord? Peek()
        {
            RemoveExpired();
            return _priorityQueue.FirstOrDefault();
        }

        public bool UpdateStatus(string eventId, FrameEventAckStatus status)
        {
            if (!_eventsById.TryGetValue(eventId, out var record))
            {
                return false;
            }

            if (status == FrameEventAckStatus.Shown)
            {
                _ackStatusById[eventId] = status;
                return true;
            }

            RemoveRecord(record);
            return true;
        }

        public int RemoveExpired()
        {
            var now = DateTime.UtcNow;
            var expired = _priorityQueue.Where(r => r.ExpiresAt is not null && r.ExpiresAt <= now).ToList();
            foreach (var record in expired)
            {
                RemoveRecord(record);
            }

            return expired.Count;
        }

        private void RemoveRecord(EventRecord record)
        {
            _eventsById.Remove(record.Event.Id);
            _priorityQueue.Remove(record);

            if (!string.IsNullOrWhiteSpace(record.Event.Category))
            {
                if (_categoryIndex.TryGetValue(record.Event.Category!, out var mappedId) && mappedId == record.Event.Id)
                {
                    _categoryIndex.Remove(record.Event.Category!);
                }
            }

            _ackStatusById.Remove(record.Event.Id);
            if (ActiveEventId == record.Event.Id)
            {
                ActiveEventId = null;
            }
        }
    }

    private readonly ConcurrentDictionary<string, DeviceQueue> _queues = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> EnqueueAsync(FrameEvent frameEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queue = _queues.GetOrAdd(frameEvent.DeviceId, _ => new DeviceQueue());

        lock (queue)
        {
            if (queue.ContainsEvent(frameEvent.Id))
            {
                return Task.FromResult(false);
            }

            if (frameEvent.Mode == FrameEventMode.Close)
            {
                if (!string.IsNullOrWhiteSpace(frameEvent.Category))
                {
                    queue.RemoveByCategory(frameEvent.Category).ToList();
                }
                else
                {
                    queue.RemoveAll().ToList();
                }

                return Task.FromResult(true);
            }

            if (!string.IsNullOrWhiteSpace(frameEvent.Category))
            {
                queue.RemoveByCategory(frameEvent.Category).ToList();
            }

            queue.RemoveExpired();
            queue.Add(new EventRecord(frameEvent));
        }

        return Task.FromResult(true);
    }

    public Task<FrameEvent?> PeekNextAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_queues.TryGetValue(deviceId, out var queue))
        {
            return Task.FromResult<FrameEvent?>(null);
        }

        lock (queue)
        {
            queue.RemoveExpired();
            var record = queue.Peek();
            if (record is null)
            {
                return Task.FromResult<FrameEvent?>(null);
            }

            queue.ActiveEventId = record.Event.Id;
            return Task.FromResult<FrameEvent?>(record.Event);
        }
    }

    public Task<bool> AckAsync(string deviceId, string eventId, FrameEventAckStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_queues.TryGetValue(deviceId, out var queue))
        {
            return Task.FromResult(false);
        }

        lock (queue)
        {
            queue.RemoveExpired();
            return Task.FromResult(queue.UpdateStatus(eventId, status));
        }
    }

    public Task<int> RemoveByCategoryAsync(string deviceId, string category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_queues.TryGetValue(deviceId, out var queue))
        {
            return Task.FromResult(0);
        }

        lock (queue)
        {
            return Task.FromResult(queue.RemoveByCategory(category).Count());
        }
    }
}
