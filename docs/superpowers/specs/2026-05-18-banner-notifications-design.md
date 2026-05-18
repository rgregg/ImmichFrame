# Banner Notifications — Design

Date: 2026-05-18
Branch: `rgregg-notification-ux` (extends the in-flight PopupText event work)

## Summary

Add a second notification mode, `Banner`, to ImmichFrame's frame-event system. Banners are passive top-of-screen messages that do not pause the slideshow, can coexist with a `PopupText` popup, and auto-dismiss after the standard event timeout. They share the same event API, validator, queue, and ack flow — distinguished only by `FrameEventMode` and renderered by a new overlay component.

## Goals / non-goals

**Goals**

- New `FrameEventMode.Banner` accepted by `POST /api/events` with the same validation envelope as `PopupText`.
- Banner and PopupText can be active simultaneously on the same device, each in its own region.
- Banner renders at the top of the frame, message-only, with a thin draining progress bar.
- Tap-anywhere-on-banner dismisses (`Ack: Dismissed`).
- A newer banner immediately replaces an older one (via existing category mechanism).
- Slideshow runs uninterrupted while a banner is showing.

**Non-goals**

- Banner stacking / queue-and-replay. Replacement only.
- A separate `EventDefaultBannerTimeoutMs` setting. Reuses `EventDefaultTimeoutMs`.
- Frontend test scaffolding (none on this branch).
- New ack statuses or input semantics — reuse existing.

## API changes

### `FrameEventMode` enum

Add `Banner` value:

```csharp
public enum FrameEventMode { PopupText, Close, Banner }
```

### `GET /api/events/next`

Add optional `mode` query parameter:

- `GET /api/events/next?deviceId=…` — unchanged. Returns the highest-priority active event across all modes (backwards-compatible default).
- `GET /api/events/next?deviceId=…&mode=PopupText` — returns the active event for that mode, or 204.
- `GET /api/events/next?deviceId=…&mode=Banner` — same, scoped to banner mode.

Filter is applied by the queue, not by post-filtering the unfiltered response (so per-mode `_activeEventId` tracking works correctly — see "Queue changes").

### Validator

`FrameEventValidator.Validate`:

- `Banner` events MUST have a non-empty `Message` (existing rule applies to all events, but stated explicitly for clarity).
- `Title`, `Actions`, and `Input` on Banner events are accepted by the validator but the client ignores `Title` and `Actions`. Keep the schema uniform; do not reject them — that would surprise senders writing generic clients.

### `POST /api/events` and ack

No shape changes. `mode: "Banner"` is the only delta on the request side.

## Queue changes (`InMemoryFrameEventQueue`)

Current behavior: each `DeviceQueue` tracks a single `_activeEventId`. `PeekNext()` returns the highest-priority entry and records it as active.

New behavior: track active per mode.

```csharp
private readonly Dictionary<FrameEventMode, string?> _activeEventIdByMode = new();

public FrameEvent? PeekNext(FrameEventMode? modeFilter = null)
{
    // RemoveExpired() unchanged.
    // If modeFilter is null, return highest-priority across all entries (current behavior).
    // If modeFilter is set, return highest-priority entry where Event.Mode == modeFilter.
    // Update _activeEventIdByMode[returnedEvent.Mode] = returnedEvent.Id.
}
```

`Ack()` clears the appropriate slot in `_activeEventIdByMode` when an event is removed.

`RemoveByCategory()` and `RemoveExpired()` continue to operate across all entries regardless of mode — they're concerned with the queue contents, not the active-slot bookkeeping.

The interface change:

```csharp
Task<FrameEvent?> PeekNextAsync(string deviceId, FrameEventMode? mode = null, CancellationToken ct = default);
```

Existing callers passing no `mode` argument keep working unchanged.

## Client changes

### Stores (`config.store.ts`, `event-service.ts`)

Split `activeEvent` into two derived stores:

```ts
export const activePopupEvent: Readable<FrameEvent | null>;
export const activeBannerEvent: Readable<FrameEvent | null>;
```

`startEventPolling` polls twice per tick (or once with both modes — implementation decides; two parallel `fetch` calls is simpler and the overhead is negligible). Each response updates its corresponding store.

`acknowledgeEvent(deviceId, eventId, status)` is unchanged and serves both modes.

### Components

New file: `immichFrame.Web/src/lib/components/events/BannerOverlay.svelte`

- Container: `position: fixed; top: 1rem; left: 50%; transform: translateX(-50%); max-width: min(800px, 90vw);` — top-centered, slightly inset.
- Content: a single `<p>` rendering `event.message`. No title.
- Below the message: a 2px-tall progress bar element that animates its `width` from 100% to 0% over `event.timeoutMs ?? configStore.eventDefaultTimeoutMs` using CSS transition.
- Enter: `transform: translateY(-100%)` → `translateY(0)` over ~200ms.
- Exit: `opacity 1 → 0` over ~150ms before unmount.
- Click handler on container: `dismissEvent('Dismissed', event)`.
- Pointer cursor.

Updated file: `EventOverlayHost.svelte` — render both `PopupTextOverlay` (bound to `activePopupEvent`) and `BannerOverlay` (bound to `activeBannerEvent`). They have different fixed positions so no stacking-context coordination needed.

Updated file: `home-page.svelte` — the `progressBar.pause()` / `progressBar.play()` slideshow pausing logic remains tied to `activePopupEvent` only. The `handleActiveEvent` function is split into two: `handlePopupEvent` (current logic, minus the mode === 'Close' branch which still applies, but only when received via the popup poll) and `handleBannerEvent` (clears timer, sets currentBanner, no slideshow side-effects).

### Replace semantics

Replacement is sender-driven: when an event POST includes a `category` matching a currently-queued event, the queue replaces the older entry. For "newer banner replaces older banner," senders should pass a consistent `category` value (the docs/example recommends `"banner"` for the generic case, but specific categories like `"banner.calendar-reminder"` are fine too — only events sharing a category replace each other).

No new server- or client-side logic is needed beyond the existing category mechanism; this section documents the convention senders should follow to achieve the intended UX.

## Settings

No new `Settings.yml` keys. The reused setting:

- `EventDefaultTimeoutMs` (existing, defaults to 15000) — applied to banners when `timeoutMs` is not specified on the request.

## Tests

`ImmichFrame.Core.Tests/Events/InMemoryFrameEventQueueTests.cs` — add:

- Enqueueing a `Banner` and a `PopupText` for the same device, calling `PeekNext(modeFilter: PopupText)` and `PeekNext(modeFilter: Banner)` returns each respectively.
- After acking the popup, the banner remains active.
- A new banner with the same category replaces the prior banner.

`ImmichFrame.WebApi.Tests/Events/FrameEventValidatorTests.cs` — add:

- Banner with a `Message` validates.
- Banner without `Message` is rejected with the existing "message is required" error.
- Banner with `Title` and `Actions` populated still validates (no extra rejection).

No frontend tests — none exist on this branch and adding test infrastructure is out of scope.

## Documentation

`docs/docs/getting-started/configuration.md` — add a short subsection under the event section showing a `curl` example for posting a banner. Existing `EventHostEnabled` / `EventDefaultTimeoutMs` / `EventPollingIntervalSeconds` docs already cover the relevant settings.

## Migration / backwards compatibility

- The `Banner` enum value is additive. Existing clients/servers continue to work.
- The `?mode=` query parameter is optional. Existing clients that don't pass it see unchanged behavior.
- `FrameEventResponseDto` already serializes `mode` as a string (via `JsonStringEnumConverter`), so banners flow through the existing response shape.
- No DB or persisted-state changes.

## Build / commit hygiene

Per project preference: commits in this work must NOT include Claude/Anthropic attribution trailers. Plain commit messages only.
