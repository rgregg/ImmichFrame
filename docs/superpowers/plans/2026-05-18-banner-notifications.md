# Banner Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `FrameEventMode.Banner` — a top-of-screen passive notification that coexists with `PopupText`, doesn't pause the slideshow, and is dismissable by tap or auto-timeout. End-to-end across server queue, REST API, client polling, and UI.

**Architecture:** Add a third value to the existing `FrameEventMode` enum. Extend `IFrameEventQueue.PeekNextAsync` with an optional `mode` filter; the in-memory queue tracks an active event per mode so popups and banners don't shadow each other. The frontend splits its single `activeEvent` store into `activePopupEvent` and `activeBannerEvent`, polls both modes each tick, and renders a new `BannerOverlay` alongside the existing `PopupTextOverlay`. Only popups pause the slideshow.

**Tech Stack:** C# / .NET 8 (WebApi backend), NUnit + Moq (tests), Svelte 5 with runes (frontend), Tailwind CSS, fetch-based polling.

**Commit hygiene:** No Claude/Anthropic attribution in commit messages.

**Reference spec:** `docs/superpowers/specs/2026-05-18-banner-notifications-design.md`

---

## Task 1: Add `Banner` value to `FrameEventMode` enum

**Files:**
- Modify: `ImmichFrame.Core/Events/FrameEventMode.cs`

- [ ] **Step 1: Add the enum value**

Open `ImmichFrame.Core/Events/FrameEventMode.cs` and update it to:

```csharp
using System.Text.Json.Serialization;

namespace ImmichFrame.Core.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FrameEventMode
{
    PopupText,
    Close,
    Banner
}
```

- [ ] **Step 2: Build to confirm no compile errors**

Run: `dotnet build ImmichFrame.Core/ImmichFrame.Core.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add ImmichFrame.Core/Events/FrameEventMode.cs
git commit -m "feat(events): add Banner value to FrameEventMode"
```

---

## Task 2: Extend `IFrameEventQueue.PeekNextAsync` with optional mode filter

**Files:**
- Modify: `ImmichFrame.Core/Interfaces/IFrameEventQueue.cs`

- [ ] **Step 1: Update the interface**

Replace the `PeekNextAsync` signature so callers can optionally request a single mode. The default-parameter form keeps existing callers compiling unchanged.

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImmichFrame.Core.Events;

namespace ImmichFrame.Core.Interfaces;

public interface IFrameEventQueue
{
    Task<bool> EnqueueAsync(FrameEvent frameEvent, CancellationToken cancellationToken = default);
    Task<FrameEvent?> PeekNextAsync(string deviceId, FrameEventMode? mode = null, CancellationToken cancellationToken = default);
    Task<bool> AckAsync(string deviceId, string eventId, FrameEventAckStatus status, CancellationToken cancellationToken = default);
    Task<int> RemoveByCategoryAsync(string deviceId, string category, CancellationToken cancellationToken = default);
    IReadOnlyList<(FrameEvent Event, FrameEventAckStatus? LastAckStatus)> GetDeviceSnapshot(string deviceId);
}
```

- [ ] **Step 2: Build (will fail in the queue impl — expected for next task)**

Run: `dotnet build ImmichFrame.Core/ImmichFrame.Core.csproj`
Expected: Compile error in `InMemoryFrameEventQueue.cs` because its `PeekNextAsync` signature no longer matches the interface. We fix that in Task 3.

- [ ] **Step 3: Do NOT commit yet** — the tree won't build. Move on to Task 3, then commit together.

---

## Task 3: Per-mode active tracking in `InMemoryFrameEventQueue` (TDD)

**Files:**
- Test: `ImmichFrame.Core.Tests/Events/InMemoryFrameEventQueueTests.cs`
- Modify: `ImmichFrame.Core/Services/InMemoryFrameEventQueue.cs`

- [ ] **Step 1: Write the failing tests**

Open `ImmichFrame.Core.Tests/Events/InMemoryFrameEventQueueTests.cs`. The file already defines a `MakeEvent` helper that defaults `Mode = FrameEventMode.PopupText`. Add an overload that takes a mode, and add the new tests below.

Add this helper near the existing `MakeEvent` method:

```csharp
private static FrameEvent MakeEvent(FrameEventMode mode, string id, string deviceId = "device-1", int priority = 0, string? category = null, int? timeoutMs = null)
{
    return new FrameEvent
    {
        Id = id,
        DeviceId = deviceId,
        Type = "frame.ui.v1",
        Mode = mode,
        Message = $"Message for {id}",
        Priority = priority,
        Category = category,
        TimeoutMs = timeoutMs,
        PostedAt = DateTime.UtcNow
    };
}
```

Add these three tests at the bottom of the test class:

```csharp
[Test]
public async Task PeekNext_WithModeFilter_ReturnsOnlyMatchingMode()
{
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.PopupText, "popup-1"));
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.Banner, "banner-1"));

    var popup = await _queue.PeekNextAsync("device-1", FrameEventMode.PopupText);
    var banner = await _queue.PeekNextAsync("device-1", FrameEventMode.Banner);

    Assert.That(popup, Is.Not.Null);
    Assert.That(popup!.Id, Is.EqualTo("popup-1"));
    Assert.That(banner, Is.Not.Null);
    Assert.That(banner!.Id, Is.EqualTo("banner-1"));
}

[Test]
public async Task PeekNext_WithModeFilter_ReturnsNullWhenNoMatch()
{
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.PopupText, "popup-1"));

    var banner = await _queue.PeekNextAsync("device-1", FrameEventMode.Banner);

    Assert.That(banner, Is.Null);
}

[Test]
public async Task PeekNext_AfterAckingPopup_BannerStillReturned()
{
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.PopupText, "popup-1"));
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.Banner, "banner-1"));

    await _queue.AckAsync("device-1", "popup-1", FrameEventAckStatus.Closed);

    var banner = await _queue.PeekNextAsync("device-1", FrameEventMode.Banner);
    Assert.That(banner, Is.Not.Null);
    Assert.That(banner!.Id, Is.EqualTo("banner-1"));
}

[Test]
public async Task PeekNext_NoModeFilter_ReturnsHighestPriorityRegardlessOfMode()
{
    // Higher Priority value sorts first per existing EventEntryComparer.
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.PopupText, "popup-low", priority: 0));
    await _queue.EnqueueAsync(MakeEvent(FrameEventMode.Banner, "banner-high", priority: 10));

    var top = await _queue.PeekNextAsync("device-1");

    Assert.That(top, Is.Not.Null);
    Assert.That(top!.Id, Is.EqualTo("banner-high"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ImmichFrame.Core.Tests/ImmichFrame.Core.Tests.csproj --filter "FullyQualifiedName~InMemoryFrameEventQueueTests"`
Expected: The four new tests fail (most likely with a compile error because the new `PeekNextAsync` signature exists on the interface but not on the impl yet — that's the expected red state).

- [ ] **Step 3: Update `InMemoryFrameEventQueue` to support per-mode active tracking**

In `ImmichFrame.Core/Services/InMemoryFrameEventQueue.cs`, change the outer `PeekNextAsync` signature and the inner `DeviceQueue.PeekNext` to accept a mode filter, and switch `_activeEventId` from a single value to a per-mode dictionary.

Replace the outer `PeekNextAsync` method:

```csharp
public Task<FrameEvent?> PeekNextAsync(string deviceId, FrameEventMode? mode = null, CancellationToken cancellationToken = default)
{
    if (!_queues.TryGetValue(deviceId, out var queue))
        return Task.FromResult<FrameEvent?>(null);

    return Task.FromResult(queue.PeekNext(mode));
}
```

Inside the `DeviceQueue` class:

1. Replace the field `private string? _activeEventId;` with:

```csharp
private readonly Dictionary<FrameEventMode, string> _activeEventIdByMode = new();
```

2. Replace the `PeekNext` method:

```csharp
public FrameEvent? PeekNext(FrameEventMode? mode = null)
{
    lock (_lock)
    {
        RemoveExpired();

        if (_entries.Count == 0)
        {
            _activeEventIdByMode.Clear();
            return null;
        }

        EventEntry? selected;
        if (mode is null)
        {
            selected = _entries.Min;
        }
        else
        {
            selected = _entries.FirstOrDefault(e => e.Event.Mode == mode.Value);
        }

        if (selected is null)
            return null;

        _activeEventIdByMode[selected.Event.Mode] = selected.Event.Id;
        return selected.Event;
    }
}
```

3. Update `Ack` to clear the per-mode slot for the acked event:

```csharp
public bool Ack(string eventId, FrameEventAckStatus status)
{
    lock (_lock)
    {
        if (!_byId.TryGetValue(eventId, out var entry))
            return false;

        entry.LastAckStatus = status;

        if (status != FrameEventAckStatus.Shown)
        {
            var mode = entry.Event.Mode;
            Remove(entry);
            if (_activeEventIdByMode.TryGetValue(mode, out var activeId) && activeId == eventId)
                _activeEventIdByMode.Remove(mode);
        }

        return true;
    }
}
```

4. Update the `Clear` method:

```csharp
private void Clear()
{
    _entries.Clear();
    _byId.Clear();
    _byCategory.Clear();
    _activeEventIdByMode.Clear();
}
```

Note: `_entries` is a `SortedSet<EventEntry>` sorted by `EventEntryComparer` (priority desc, then PostedAt asc, then Id). `FirstOrDefault` walks in sort order, so the filtered branch correctly picks the highest-priority entry of that mode.

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test ImmichFrame.Core.Tests/ImmichFrame.Core.Tests.csproj --filter "FullyQualifiedName~InMemoryFrameEventQueueTests"`
Expected: All tests pass (existing + 4 new).

- [ ] **Step 6: Commit**

```bash
git add ImmichFrame.Core/Interfaces/IFrameEventQueue.cs \
        ImmichFrame.Core/Services/InMemoryFrameEventQueue.cs \
        ImmichFrame.Core.Tests/Events/InMemoryFrameEventQueueTests.cs
git commit -m "feat(events): track active event per mode in queue"
```

---

## Task 4: Accept `Banner` mode in `FrameEventValidator` (TDD)

**Files:**
- Test: `ImmichFrame.WebApi.Tests/Events/FrameEventValidatorTests.cs`
- Modify: `ImmichFrame.WebApi/Services/FrameEventValidator.cs`

- [ ] **Step 1: Write failing tests**

Open `ImmichFrame.WebApi.Tests/Events/FrameEventValidatorTests.cs`. Reuse the existing `MakeValidPopupText` shape; add a Banner helper and two tests.

Add a helper near the existing `MakeValidPopupText`:

```csharp
private static FrameEventRequestDto MakeValidBanner()
{
    return new FrameEventRequestDto
    {
        DeviceId = "device-1",
        Id = "evt-banner-1",
        Type = "frame.ui.banner",
        Mode = FrameEventMode.Banner,
        Message = "banner text"
    };
}
```

Add these two tests at the bottom of the test class:

```csharp
[Test]
public void Validate_BannerWithMessage_Succeeds()
{
    var dto = MakeValidBanner();

    var domain = _validator.Validate(dto);

    Assert.That(domain.Mode, Is.EqualTo(FrameEventMode.Banner));
    Assert.That(domain.Message, Is.EqualTo("banner text"));
}

[Test]
public void Validate_BannerWithoutMessage_Throws()
{
    var dto = MakeValidBanner();
    dto.Message = null;

    Assert.Throws<ValidationException>(() => _validator.Validate(dto));
}

[Test]
public void Validate_BannerWithTitleAndActions_StillValidates()
{
    var dto = MakeValidBanner();
    dto.Title = "banner title";
    dto.Actions = new List<FrameEventActionDto>
    {
        new() { Id = "ack", Label = "OK", Kind = "primary" }
    };

    var domain = _validator.Validate(dto);

    // Validator accepts them; the client chooses to ignore Title/Actions for Banner mode.
    Assert.That(domain.Title, Is.EqualTo("banner title"));
    Assert.That(domain.Actions, Has.Count.EqualTo(1));
}
```

Add `using System.Collections.Generic;` to the test file's `using` block if not already present.

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test ImmichFrame.WebApi.Tests/ImmichFrame.WebApi.Tests.csproj --filter "FullyQualifiedName~FrameEventValidatorTests"`
Expected: `Validate_BannerWithMessage_Succeeds` fails with `ValidationException: mode 'Banner' is not supported` (the current default branch rejects it). `Validate_BannerWithoutMessage_Throws` may pass accidentally for the same reason — that's fine, it's still verifying the "no message → throws" property.

- [ ] **Step 3: Add the `Banner` case to the validator**

In `ImmichFrame.WebApi/Services/FrameEventValidator.cs`, change the `switch (dto.Mode)` block:

```csharp
switch (dto.Mode)
{
    case FrameEventMode.PopupText:
        if (string.IsNullOrWhiteSpace(dto.Message))
            throw new ValidationException("message is required for PopupText mode");
        break;

    case FrameEventMode.Banner:
        if (string.IsNullOrWhiteSpace(dto.Message))
            throw new ValidationException("message is required for Banner mode");
        break;

    case FrameEventMode.Close:
        break;

    default:
        throw new ValidationException($"mode '{dto.Mode}' is not supported");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ImmichFrame.WebApi.Tests/ImmichFrame.WebApi.Tests.csproj --filter "FullyQualifiedName~FrameEventValidatorTests"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add ImmichFrame.WebApi/Services/FrameEventValidator.cs \
        ImmichFrame.WebApi.Tests/Events/FrameEventValidatorTests.cs
git commit -m "feat(events): accept Banner mode in validator"
```

---

## Task 5: Pass `?mode=` query param to queue in `EventsController`

**Files:**
- Modify: `ImmichFrame.WebApi/Controllers/EventsController.cs`

- [ ] **Step 1: Update the `GetNext` action to accept and forward `mode`**

Replace the existing `GetNext` method in `ImmichFrame.WebApi/Controllers/EventsController.cs`:

```csharp
[HttpGet("next")]
public async Task<IActionResult> GetNext([FromQuery] string deviceId, [FromQuery] FrameEventMode? mode, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(deviceId))
        return BadRequest(new { message = "deviceId is required" });

    if (!_settings.EventHostEnabled)
        return NotFound(new { message = "Event host is disabled" });

    var frameEvent = await _queue.PeekNextAsync(deviceId, mode, cancellationToken);

    if (frameEvent is null)
        return NoContent();

    return Ok(FrameEventResponseDto.FromDomain(frameEvent));
}
```

Note: `FrameEventMode` is a string-serialised enum (`[JsonStringEnumConverter]`), and ASP.NET model-binding handles `?mode=PopupText` and `?mode=Banner` case-insensitively out of the box. Invalid values return 400 automatically via `[ApiController]` model validation.

The `using ImmichFrame.Core.Events;` directive is already present at the top of `EventsController.cs` (via `IFrameEventQueue` types). No additional `using` needed.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Smoke-test the API locally**

Start the API: `dotnet run --project ImmichFrame.WebApi/ImmichFrame.WebApi.csproj` (or use your existing dev workflow).

In another shell:

```bash
# No mode filter — backwards compatible
curl -s -o /dev/null -w "no-filter: HTTP %{http_code}\n" \
  "http://localhost:8080/api/events/next?deviceId=test"

# Banner filter
curl -s -o /dev/null -w "banner: HTTP %{http_code}\n" \
  "http://localhost:8080/api/events/next?deviceId=test&mode=Banner"

# Bad filter — should be 400
curl -s -o /dev/null -w "bad: HTTP %{http_code}\n" \
  "http://localhost:8080/api/events/next?deviceId=test&mode=Bogus"
```

Expected: `no-filter: HTTP 204`, `banner: HTTP 204`, `bad: HTTP 400`.

- [ ] **Step 4: Commit**

```bash
git add ImmichFrame.WebApi/Controllers/EventsController.cs
git commit -m "feat(events): support mode filter on GET /api/events/next"
```

---

## Task 6: Add `Banner` to client `FrameEventMode` union and split active stores

**Files:**
- Modify: `immichFrame.Web/src/lib/events/event-service.ts`

- [ ] **Step 1: Update the type union and replace the single store with two**

Replace the top portion of `immichFrame.Web/src/lib/events/event-service.ts` (lines 1 through approximately 55, up to but not including the `pollLoop` function). Final state of that region:

```ts
import { get, writable } from 'svelte/store';
import { configStore } from '$lib/stores/config.store';

export type FrameEventMode = 'PopupText' | 'Close' | 'Banner';

export type FrameEventAckStatus = 'Shown' | 'Closed' | 'Timeout' | 'Error' | 'Dismissed';

export interface FrameEventAction {
  id: string;
  label: string;
  kind?: string | null;
}

export interface FrameEventInput {
  allowTouchDismiss: boolean;
  allowKeyboardDismiss: boolean;
}

export interface FrameEvent {
  id: string;
  type: string;
  mode: FrameEventMode;
  message?: string | null;
  timeoutMs?: number | null;
  priority: number;
  category?: string | null;
  title?: string | null;
  meta?: Record<string, unknown> | null;
  actions: FrameEventAction[];
  input: FrameEventInput;
  postedAt: string;
}

const activePopupStore = writable<FrameEvent | null>(null);
const activeBannerStore = writable<FrameEvent | null>(null);

export const activePopupEvent = {
  subscribe: activePopupStore.subscribe
};

export const activeBannerEvent = {
  subscribe: activeBannerStore.subscribe
};

export function clearActivePopupEvent() {
  activePopupStore.set(null);
}

export function clearActiveBannerEvent() {
  activeBannerStore.set(null);
}

let pollingController: AbortController | null = null;

export function startEventPolling(deviceId: string) {
  stopEventPolling();
  pollingController = new AbortController();
  void pollLoop(deviceId, pollingController);
}

export function stopEventPolling() {
  pollingController?.abort();
  pollingController = null;
}
```

Notes:
- `Dismissed` is added to `FrameEventAckStatus` — the banner uses this to differentiate tap dismissal from popup `Closed`.
- The old `activeEvent` / `clearActiveEvent` exports are gone. Consumers (`home-page.svelte`) get updated in Task 9.

- [ ] **Step 2: Replace `pollLoop` to poll both modes**

Replace the existing `pollLoop` function with:

```ts
async function pollLoop(deviceId: string, controller: AbortController) {
  const settings = get(configStore);
  const intervalMs = Math.max(500, (settings.eventPollingIntervalSeconds ?? 2) * 1000);

  while (!controller.signal.aborted) {
    if (!get(configStore).eventHostEnabled) {
      activePopupStore.set(null);
      activeBannerStore.set(null);
      await delay(intervalMs, controller.signal);
      continue;
    }

    await Promise.all([
      pollOne(deviceId, 'PopupText', activePopupStore, controller.signal),
      pollOne(deviceId, 'Banner', activeBannerStore, controller.signal)
    ]);

    await delay(intervalMs, controller.signal);
  }
}

async function pollOne(
  deviceId: string,
  mode: FrameEventMode,
  store: typeof activePopupStore,
  signal: AbortSignal
) {
  try {
    const url = `/api/events/next?deviceId=${encodeURIComponent(deviceId)}&mode=${mode}`;
    const response = await fetch(url, { method: 'GET', signal });

    if (response.status === 200) {
      const payload = (await response.json()) as FrameEvent;
      store.set(payload);
    } else if (response.status === 204) {
      store.set(null);
    }
  } catch (error) {
    if ((error as Error).name !== 'AbortError') {
      console.error(`event poll failed (mode=${mode})`, error);
    }
  }
}
```

Leave `acknowledgeEvent` and `delay` exactly as they are.

- [ ] **Step 3: Type-check the frontend**

Run: `cd immichFrame.Web && npm run check`
Expected: Errors in `home-page.svelte` and `EventOverlayHost.svelte` referencing the removed `activeEvent` / `clearActiveEvent`. That's expected — fixed in Tasks 8 and 9.

- [ ] **Step 4: Do NOT commit yet** — frontend still won't type-check until Tasks 7, 8, 9 land. Continue.

---

## Task 7: Create `BannerOverlay.svelte`

**Files:**
- Create: `immichFrame.Web/src/lib/components/events/BannerOverlay.svelte`

- [ ] **Step 1: Write the component**

Create `immichFrame.Web/src/lib/components/events/BannerOverlay.svelte` with this content:

```svelte
<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import { onMount } from 'svelte';

	let { event, onDismiss }: {
		event: FrameEvent;
		onDismiss: (status: FrameEventAckStatus) => void | Promise<void>;
	} = $props();

	let dismissing = false;

	const message = event.message ?? '';
	const timeoutMs = event.timeoutMs ?? 0;
	const allowTouchDismiss = event.input?.allowTouchDismiss ?? true;

	let progressPercent = $state(100);

	onMount(() => {
		if (timeoutMs <= 0) return;

		// Defer the transition start one frame so the initial 100% paints first.
		requestAnimationFrame(() => {
			progressPercent = 0;
		});
	});

	async function dismiss(status: FrameEventAckStatus = 'Dismissed') {
		if (dismissing) return;
		dismissing = true;
		await onDismiss(status);
		dismissing = false;
	}

	function handleClick() {
		if (!allowTouchDismiss) return;
		dismiss('Dismissed');
	}
</script>

<div
	class="pointer-events-none fixed inset-x-0 top-0 z-[160] flex justify-center p-4"
	role="presentation"
>
	<button
		type="button"
		class="pointer-events-auto w-full max-w-[min(48rem,90vw)] cursor-pointer rounded-xl bg-neutral-900/90 px-5 py-3 text-left text-white shadow-2xl ring-1 ring-white/10 backdrop-blur transition hover:bg-neutral-800/90 motion-safe:animate-[bannerin_200ms_ease-out]"
		onclick={handleClick}
		aria-label="Notification: {message}"
	>
		<p class="whitespace-pre-line text-base leading-snug">{message}</p>
		{#if timeoutMs > 0}
			<div class="mt-2 h-[2px] w-full overflow-hidden rounded-full bg-white/10">
				<div
					class="h-full bg-white/60"
					style="width: {progressPercent}%; transition: width {timeoutMs}ms linear;"
				></div>
			</div>
		{/if}
	</button>
</div>

<style>
	@keyframes bannerin {
		from {
			transform: translateY(-100%);
			opacity: 0;
		}
		to {
			transform: translateY(0);
			opacity: 1;
		}
	}
</style>
```

Notes:
- Uses Tailwind utility classes consistent with `PopupTextOverlay.svelte`.
- `z-[160]` sits above `PopupTextOverlay`'s `z-[150]` — visually banners overlay even atop popups (rare overlap; expected).
- `pointer-events-none` on the wrapper and `pointer-events-auto` on the inner button ensures the banner doesn't block clicks on the rest of the page when allowTouchDismiss is true (the only interactive zone is the banner itself).
- Uses a `<button>` element so keyboard focus / Enter activation work and to satisfy a11y for a clickable surface. Default styles are reset by the Tailwind classes.

- [ ] **Step 2: Type-check**

Run: `cd immichFrame.Web && npm run check`
Expected: Same pre-existing errors from Task 6's incomplete state. No new errors from this file.

- [ ] **Step 3: Do NOT commit yet.** Continue to Task 8.

---

## Task 8: Update `EventOverlayHost` to render both overlays

**Files:**
- Modify: `immichFrame.Web/src/lib/components/events/EventOverlayHost.svelte`

- [ ] **Step 1: Replace the host component**

Replace the entire contents of `immichFrame.Web/src/lib/components/events/EventOverlayHost.svelte`:

```svelte
<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import PopupTextOverlay from './PopupTextOverlay.svelte';
	import BannerOverlay from './BannerOverlay.svelte';

	let {
		popupEvent = null,
		bannerEvent = null,
		dismissPopup,
		dismissBanner
	}: {
		popupEvent: FrameEvent | null;
		bannerEvent: FrameEvent | null;
		dismissPopup: (status: FrameEventAckStatus) => void | Promise<void>;
		dismissBanner: (status: FrameEventAckStatus) => void | Promise<void>;
	} = $props();
</script>

{#if popupEvent}
	{#key popupEvent.id}
		{#if popupEvent.mode === 'PopupText'}
			<PopupTextOverlay event={popupEvent} onDismiss={dismissPopup} />
		{/if}
	{/key}
{/if}

{#if bannerEvent}
	{#key bannerEvent.id}
		{#if bannerEvent.mode === 'Banner'}
			<BannerOverlay event={bannerEvent} onDismiss={dismissBanner} />
		{/if}
	{/key}
{/if}
```

- [ ] **Step 2: Type-check**

Run: `cd immichFrame.Web && npm run check`
Expected: Remaining errors are now confined to `home-page.svelte` — fixed in Task 9.

- [ ] **Step 3: Do NOT commit yet.** Continue to Task 9.

---

## Task 9: Wire `home-page.svelte` to split popup/banner handling

**Files:**
- Modify: `immichFrame.Web/src/lib/components/home-page/home-page.svelte`

- [ ] **Step 1: Update the import block**

Find the import at the top (around line 18-26) and replace with:

```svelte
import {
    activePopupEvent,
    activeBannerEvent,
    acknowledgeEvent,
    clearActivePopupEvent,
    clearActiveBannerEvent,
    startEventPolling,
    stopEventPolling
} from '$lib/events/event-service';
import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
import EventOverlayHost from '$lib/components/events/EventOverlayHost.svelte';
```

- [ ] **Step 2: Update the event-related state declarations**

Find the existing event-state declarations (around line 79-84):

```svelte
let currentEvent: FrameEvent | null = $state(null);
let lastEventId: string | null = $state(null);
let eventTimeoutHandle: ReturnType<typeof setTimeout> | null = null;
let eventPausedSlideshow = $state(false);
let eventShownAcked = $state(false);
let unsubscribeActiveEvent: (() => void) | undefined;
```

Replace with:

```svelte
let currentPopup: FrameEvent | null = $state(null);
let currentBanner: FrameEvent | null = $state(null);
let lastPopupId: string | null = $state(null);
let lastBannerId: string | null = $state(null);
let popupTimeoutHandle: ReturnType<typeof setTimeout> | null = null;
let bannerTimeoutHandle: ReturnType<typeof setTimeout> | null = null;
let popupPausedSlideshow = $state(false);
let popupShownAcked = $state(false);
let bannerShownAcked = $state(false);
let unsubscribePopupEvent: (() => void) | undefined;
let unsubscribeBannerEvent: (() => void) | undefined;
```

- [ ] **Step 3: Replace the helper functions for popup and add banner counterparts**

Find the existing helper functions in this order:
- `clearEventTimer`
- `markEventShownOnce`
- `dismissEvent`
- `handleActiveEvent`

Replace all four with the following block:

```svelte
function clearPopupTimer() {
    if (popupTimeoutHandle) {
        clearTimeout(popupTimeoutHandle);
        popupTimeoutHandle = null;
    }
}

function clearBannerTimer() {
    if (bannerTimeoutHandle) {
        clearTimeout(bannerTimeoutHandle);
        bannerTimeoutHandle = null;
    }
}

function markPopupShownOnce() {
    if (!($configStore.eventHostEnabled ?? false)) return;
    if (!currentPopup || popupShownAcked || !deviceId) return;
    popupShownAcked = true;
    void acknowledgeEvent(deviceId, currentPopup.id, 'Shown');
}

function markBannerShownOnce() {
    if (!($configStore.eventHostEnabled ?? false)) return;
    if (!currentBanner || bannerShownAcked || !deviceId) return;
    bannerShownAcked = true;
    void acknowledgeEvent(deviceId, currentBanner.id, 'Shown');
}

async function dismissPopup(status: FrameEventAckStatus, explicitEvent: FrameEvent | null = null) {
    if (!($configStore.eventHostEnabled ?? false)) return;
    const target = explicitEvent ?? currentPopup;
    if (!target) return;

    clearPopupTimer();
    clearActivePopupEvent();
    if (!deviceId) return;

    try {
        await acknowledgeEvent(deviceId, target.id, status);
    } catch (error) {
        console.error('failed to acknowledge popup event', error);
    }
}

async function dismissBanner(status: FrameEventAckStatus, explicitEvent: FrameEvent | null = null) {
    if (!($configStore.eventHostEnabled ?? false)) return;
    const target = explicitEvent ?? currentBanner;
    if (!target) return;

    clearBannerTimer();
    clearActiveBannerEvent();
    if (!deviceId) return;

    try {
        await acknowledgeEvent(deviceId, target.id, status);
    } catch (error) {
        console.error('failed to acknowledge banner event', error);
    }
}

function handlePopupEvent(event: FrameEvent | null) {
    if (!($configStore.eventHostEnabled ?? false)) {
        currentPopup = null;
        return;
    }
    clearPopupTimer();

    if (!event) {
        currentPopup = null;
        popupShownAcked = false;
        lastPopupId = null;
        if (popupPausedSlideshow && progressBar) {
            void progressBar.play();
        }
        popupPausedSlideshow = false;
        return;
    }

    if (event.mode === 'Close') {
        void dismissPopup('Closed', event);
        return;
    }

    currentPopup = event;
    const isNewEvent = event.id !== lastPopupId;
    if (isNewEvent) {
        lastPopupId = event.id;
        popupShownAcked = false;
        if (progressBar && progressBarStatus !== ProgressBarStatus.Paused) {
            void progressBar.pause();
            popupPausedSlideshow = true;
        } else if (progressBarStatus === ProgressBarStatus.Paused) {
            popupPausedSlideshow = false;
        }
    }

    markPopupShownOnce();

    const fallbackTimeout = $configStore.eventDefaultTimeoutMs ?? 0;
    const timeoutMs = event.timeoutMs ?? fallbackTimeout;
    if (timeoutMs && timeoutMs > 0) {
        popupTimeoutHandle = setTimeout(() => {
            void dismissPopup('Timeout');
        }, timeoutMs);
    }
}

function handleBannerEvent(event: FrameEvent | null) {
    if (!($configStore.eventHostEnabled ?? false)) {
        currentBanner = null;
        return;
    }
    clearBannerTimer();

    if (!event) {
        currentBanner = null;
        bannerShownAcked = false;
        lastBannerId = null;
        return;
    }

    if (event.mode === 'Close') {
        void dismissBanner('Closed', event);
        return;
    }

    currentBanner = event;
    const isNewEvent = event.id !== lastBannerId;
    if (isNewEvent) {
        lastBannerId = event.id;
        bannerShownAcked = false;
        // Banners never pause the slideshow.
    }

    markBannerShownOnce();

    const fallbackTimeout = $configStore.eventDefaultTimeoutMs ?? 0;
    const timeoutMs = event.timeoutMs ?? fallbackTimeout;
    if (timeoutMs && timeoutMs > 0) {
        bannerTimeoutHandle = setTimeout(() => {
            void dismissBanner('Timeout');
        }, timeoutMs);
    }
}
```

- [ ] **Step 4: Update the subscription wiring in `onMount`**

Find the line subscribing to `activeEvent` (around line 503):

```svelte
unsubscribeActiveEvent = activeEvent.subscribe(handleActiveEvent);
```

Replace with:

```svelte
unsubscribePopupEvent = activePopupEvent.subscribe(handlePopupEvent);
unsubscribeBannerEvent = activeBannerEvent.subscribe(handleBannerEvent);
```

- [ ] **Step 5: Update the cleanup blocks**

There are two cleanup blocks that reference `unsubscribeActiveEvent`:

1. The `onMount` return cleanup (around line 552). Replace this region:

```svelte
if (unsubscribeActiveEvent) {
    unsubscribeActiveEvent();
    unsubscribeActiveEvent = undefined;
}
clearEventTimer();
stopEventPolling();
hasMounted = false;
```

with:

```svelte
if (unsubscribePopupEvent) {
    unsubscribePopupEvent();
    unsubscribePopupEvent = undefined;
}
if (unsubscribeBannerEvent) {
    unsubscribeBannerEvent();
    unsubscribeBannerEvent = undefined;
}
clearPopupTimer();
clearBannerTimer();
stopEventPolling();
hasMounted = false;
```

2. The `onDestroy` cleanup (around line 571). Replace:

```svelte
if (unsubscribeActiveEvent) {
    unsubscribeActiveEvent();
    unsubscribeActiveEvent = undefined;
}
```

with:

```svelte
if (unsubscribePopupEvent) {
    unsubscribePopupEvent();
    unsubscribePopupEvent = undefined;
}
if (unsubscribeBannerEvent) {
    unsubscribeBannerEvent();
    unsubscribeBannerEvent = undefined;
}
```

- [ ] **Step 6: Update the `EventOverlayHost` invocation in the template**

Find the existing template block (around line 629):

```svelte
{#if $configStore.eventHostEnabled}
    <EventOverlayHost
        event={currentEvent}
        dismiss={dismissEvent}
    />
{/if}
```

Replace with:

```svelte
{#if $configStore.eventHostEnabled}
    <EventOverlayHost
        popupEvent={currentPopup}
        bannerEvent={currentBanner}
        dismissPopup={dismissPopup}
        dismissBanner={dismissBanner}
    />
{/if}
```

- [ ] **Step 7: Type-check and build**

Run: `cd immichFrame.Web && npm run check`
Expected: No errors.

Run: `cd immichFrame.Web && npm run build`
Expected: Build succeeds.

- [ ] **Step 8: Commit the frontend changes together**

```bash
git add immichFrame.Web/src/lib/events/event-service.ts \
        immichFrame.Web/src/lib/components/events/BannerOverlay.svelte \
        immichFrame.Web/src/lib/components/events/EventOverlayHost.svelte \
        immichFrame.Web/src/lib/components/home-page/home-page.svelte
git commit -m "feat(events): add Banner overlay and split active stores"
```

---

## Task 10: Document the banner mode

**Files:**
- Modify: `docs/docs/getting-started/configuration.md`

- [ ] **Step 1: Locate the event-host section**

Find the section in `docs/docs/getting-started/configuration.md` that documents `EventHostEnabled` / `EventPollingIntervalSeconds` / `EventDefaultTimeoutMs`. (If there isn't one yet on this branch, add it under the existing settings reference table — the surrounding structure of `configuration.md` should make the right insertion point obvious.)

- [ ] **Step 2: Append a "Posting events" example with both modes**

Add this subsection at the end of the event-host section:

````markdown
### Posting frame events

ImmichFrame's event host accepts two notification modes:

- **PopupText** — full-screen modal that pauses the slideshow until dismissed (or its `timeoutMs` elapses).
- **Banner** — passive top-of-screen notification that does *not* pause the slideshow. Tapping the banner dismisses it. Newer banners with the same `category` replace the older one.

Example: post a banner via `curl`:

```bash
curl -X POST http://<your-host>:8080/api/events \
  -H 'Content-Type: application/json' \
  -d '{
    "deviceId": "<device-id>",
    "id": "calendar-reminder-2026-05-18T14",
    "type": "frame.ui.banner",
    "mode": "Banner",
    "message": "Coffee with Sam in 15 minutes",
    "category": "banner.calendar",
    "timeoutMs": 8000
  }'
```

Both modes share the same `POST /api/events` schema; only `mode` differs. See the spec at `docs/superpowers/specs/2026-05-18-banner-notifications-design.md` for the full design rationale.
````

- [ ] **Step 3: Commit**

```bash
git add docs/docs/getting-started/configuration.md
git commit -m "docs(events): document Banner mode with curl example"
```

---

## Task 11: End-to-end smoke test

**Files:** (no edits — verification only)

- [ ] **Step 1: Run the full backend test suite**

Run: `dotnet test`
Expected: All tests pass (existing + new).

- [ ] **Step 2: Rebuild the kiosk docker image and push**

Run from repo root:

```bash
git rev-parse --short HEAD
# Note the SHA — use it for VERSION below. Convert to numeric form
# (e.g. 2026.5.18.2) because dotnet AssemblyVersion rejects non-numeric.

docker buildx build --builder multiarch --platform linux/arm64 \
  --build-arg VERSION=2026.5.18.2 \
  -t ghcr.io/rgregg/immichframe:notification-ux \
  --push .
```

Expected: build succeeds, push completes.

- [ ] **Step 3: Pull and restart on the kiosk host**

```bash
ssh <kiosk-host> 'sudo -n docker pull ghcr.io/rgregg/immichframe:notification-ux && \
  cd <compose-dir> && sudo -n docker compose up -d --force-recreate immichframe'
```

Expected: container recreates, comes up healthy.

- [ ] **Step 4: Verify the kiosk picks up the new bundle**

Reload the kiosk page so it fetches the new frontend bundle:

```bash
ssh <kiosk-host> 'systemctl --user restart immich-frame'
```

(If the kiosk isn't a transient systemd-run unit yet, fall back to `pkill chromium` + `DISPLAY=:0 XAUTHORITY=~/.Xauthority setsid -f ~/.local/bin/frame.sh`.)

- [ ] **Step 5: Send a test banner and a test popup**

Get the kiosk's deviceId from devtools (port 9222 → query localStorage), then:

```bash
ssh <kiosk-host> 'DEV=<device-id>
curl -sS -X POST http://localhost:8080/api/events \
  -H "Content-Type: application/json" \
  -d "{\"deviceId\":\"$DEV\",\"id\":\"banner-test-$(date +%s)\",\"type\":\"frame.ui.banner\",\"mode\":\"Banner\",\"message\":\"Banner test — should NOT pause slideshow\",\"category\":\"banner\",\"timeoutMs\":10000}"
echo
curl -sS -X POST http://localhost:8080/api/events \
  -H "Content-Type: application/json" \
  -d "{\"deviceId\":\"$DEV\",\"id\":\"popup-test-$(date +%s)\",\"type\":\"frame.ui.popup\",\"mode\":\"PopupText\",\"title\":\"Popup test\",\"message\":\"Should pause slideshow\",\"timeoutMs\":15000}"'
```

Verify visually on the frame:
- Banner appears at the top, slideshow keeps advancing.
- A second banner sent with `"category":"banner"` replaces the first one immediately.
- The popup appears centered, slideshow pauses.
- Tapping the banner dismisses it; auto-dismiss after timeout otherwise.

- [ ] **Step 6: (No commit)** End of plan. If any tweaks were needed during smoke test, make them in a follow-up commit.

---

## Plan summary

| Task | What it produces | Commit |
|---|---|---|
| 1 | `Banner` in `FrameEventMode` enum | `feat(events): add Banner value to FrameEventMode` |
| 2+3 | Interface + queue per-mode tracking + 4 new tests | `feat(events): track active event per mode in queue` |
| 4 | Validator accepts Banner + 2 new tests | `feat(events): accept Banner mode in validator` |
| 5 | Controller `?mode=` filter | `feat(events): support mode filter on GET /api/events/next` |
| 6+7+8+9 | Frontend dual stores, BannerOverlay, host & page wiring | `feat(events): add Banner overlay and split active stores` |
| 10 | Docs example | `docs(events): document Banner mode with curl example` |
| 11 | E2E smoke test | (no commit unless tweaks) |
