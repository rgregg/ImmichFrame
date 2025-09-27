using ImmichFrame.Core.Events;
using ImmichFrame.Core.Services;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Events;

[TestFixture]
public class InMemoryFrameEventQueueTests
{
    private static FrameEvent CreateEvent(
        string deviceId,
        string id,
        FrameEventMode mode = FrameEventMode.Popup,
        int priority = 0,
        string? category = null,
        int? timeoutMs = null,
        DateTime? postedAt = null)
    {
        return new FrameEvent
        {
            DeviceId = deviceId,
            Id = id,
            Type = "frame.ui.v1",
            Mode = mode,
            Url = "https://example.com",
            Priority = priority,
            Category = category,
            TimeoutMs = timeoutMs,
            PostedAt = (postedAt ?? DateTime.UtcNow).ToUniversalTime(),
            Actions = Array.Empty<FrameEventAction>(),
            Input = new FrameEventInput(),
            Security = new FrameEventSecurity()
        };
    }

    [Test]
    public async Task PeekNextAsync_ReturnsNull_WhenQueueEmpty()
    {
        var queue = new InMemoryFrameEventQueue();

        var result = await queue.PeekNextAsync("device-1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task PeekNextAsync_ReturnsHighestPriority()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-low", priority: 5));
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-high", priority: 1));

        var result = await queue.PeekNextAsync("device-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("evt-high"));
    }

    [Test]
    public async Task EnqueueAsync_ReplacesCategory()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-1", category: "doorbell"));
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-2", category: "doorbell"));

        var result = await queue.PeekNextAsync("device-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("evt-2"));
    }

    [Test]
    public async Task EnqueueAsync_CloseMode_RemovesMatchingCategory()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-1", category: "doorbell"));
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-2", category: "weather"));

        await queue.EnqueueAsync(CreateEvent("device-1", "close-doorbell", FrameEventMode.Close, category: "doorbell"));

        var result = await queue.PeekNextAsync("device-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("evt-2"));
    }

    [Test]
    public async Task EnqueueAsync_CloseModeWithoutCategory_RemovesAll()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-1"));
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-2"));

        await queue.EnqueueAsync(CreateEvent("device-1", "close-all", FrameEventMode.Close));

        var result = await queue.PeekNextAsync("device-1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveExpired_SkipsExpiredEvents()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-expired", timeoutMs: 1, postedAt: DateTime.UtcNow.AddMilliseconds(-100))); // already expired
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-active", timeoutMs: 10000));

        var result = await queue.PeekNextAsync("device-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("evt-active"));
    }

    [Test]
    public async Task AckAsync_DoesNotRemove_OnShown()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-1"));

        var peeked = await queue.PeekNextAsync("device-1");
        Assert.That(peeked, Is.Not.Null);

        var ackResult = await queue.AckAsync("device-1", "evt-1", FrameEventAckStatus.Shown);
        Assert.That(ackResult, Is.True);

        var stillPresent = await queue.PeekNextAsync("device-1");
        Assert.That(stillPresent, Is.Not.Null);
        Assert.That(stillPresent!.Id, Is.EqualTo("evt-1"));
    }

    [Test]
    public async Task AckAsync_Removes_OnClosed()
    {
        var queue = new InMemoryFrameEventQueue();
        await queue.EnqueueAsync(CreateEvent("device-1", "evt-1"));

        await queue.PeekNextAsync("device-1");
        var ackResult = await queue.AckAsync("device-1", "evt-1", FrameEventAckStatus.Closed);

        Assert.That(ackResult, Is.True);

        var result = await queue.PeekNextAsync("device-1");
        Assert.That(result, Is.Null);
    }
}
