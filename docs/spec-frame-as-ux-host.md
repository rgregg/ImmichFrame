# ImmichFrame Event-Driven UI Overlay Specification  

**Version:** Draft v1.0  
**Date:** 2025-09-27  

---

## 1. Purpose  
Extend the ImmichFrame single-page app (SPA) beyond clock/weather/photo 
display into a **home hub** capable of receiving events from external 
services. These events will temporarily alter the UI (e.g., 
notifications, alerts, full-screen apps) without requiring updates to 
the SPA codebase.

For the MVP implementation (HTTP-only + text popups) see §14.

## 2. Objectives
1. **Dynamic UX injection** – Load arbitrary experiences from URLs at runtime.  
2. **Modes of presentation** – Popup overlay, full-screen cover, close, or host-rendered text popup.  
3. **Event-driven** – Triggered via push events (MQTT/WebSocket/SSE).  
4. **Timeout & dismissal** – Automatic or manual dismissal supported.  
5. **Security** – Only approved origins may load; experiences sandboxed.  
6. **Extensibility** – Schema and lifecycle designed to evolve without breaking changes.

## 3. Event Transport
- **Primary:** MQTT over WebSocket  
  - Topic: `frame/{deviceId}/event` (events inbound)  
  - Topic: `frame/{deviceId}/ack` (acknowledgements outbound)  
- **Alternative:** WebSocket/SSE endpoint `/events`  

The base topic 'frame' should be configurable in the configuration file.

## 4. Event Data Contract

### 4.1 Schema (JSON)
```json
{
  "id": "string-uuid",
  "type": "frame.ui.v1",
  "mode": "popup | popupText | cover | close",
  "url": "https://ux.example.com/path",
  "message": "optional short text",
  "timeoutMs": 15000,
  "priority": 0,
  "category": "optional-string",
  "title": "optional short label",
  "meta": { "any": "serializable" },
  "actions": [
    {"id":"close","label":"Dismiss","kind":"primary"}
  ],
  "input": {
    "allowTouchDismiss": true,
    "allowKeyboardDismiss": true
  },
  "security": {
    "origin": "https://ux.example.com",
    "sandbox": ["allow-scripts","allow-same-origin"],
    "signature": "optional-JWS"
  },
  "postedAt": "2025-09-27T09:00:00Z"
}
```

### 4.2 Field semantics
- **id**: Unique identifier for deduplication. Recommended to be a UUID.
- **mode**:  
  - `popup`: Overlay window on top of photos.  
  - `popupText`: Lightweight host-rendered modal that displays `message`. No iframe is created.  
  - `cover`: Full takeover of the screen.  
  - `close`: Dismiss current UI (or specific `category`).  
- **url**: Experience endpoint, loaded inside iframe. Required for `popup` and `cover`, optional otherwise.  
- **message**: Optional plaintext string rendered by the host when `mode = popupText`. Ignored for iframe-based modes.  
- **timeoutMs**: Duration before auto-dismiss. If absent → sticky.  
- **priority**: Lower values win if multiple events compete.
- **category**: Allows new events to replace older ones of the same category.
- **security.origin**: Must match iframe origin; otherwise, reject.  
- **sandbox**: HTML iframe sandbox attributes.  
- **actions**: Host-rendered buttons (e.g., “Dismiss”).  
- **input**: User interaction policy (touch/keyboard dismissal).  

## 5. UI Behavior

### 5.1 Popup
- Centered modal card over photo/clock/weather.  
- Rounded corners, shadow.  
- Optional translucent backdrop.  

### 5.2 Cover
- Full-screen iframe takeover.  
- Default frame hidden until dismissal.  

### 5.3 Close
- Tear down any active popup/cover.  
- If `category` provided → only dismiss that category.  
- Host-rendered text popups ignore messenger handshake and are dismissed via the same rules (timeout/user/close).  


## 6. State Machine
- **idle** → **showing(popup|cover)** → **idle**  
- Events placed into a **priority queue**:  
  - Sort by `priority` ascending, then `postedAt`.
  - If `category` matches existing → replace.
- Idempotency: ignore duplicate `id`.  


## 7. Iframe Integration

### 7.1 Load
- Iframe created with attributes:  
  - `sandbox="allow-scripts allow-same-origin"` (configurable based on the event parameters).  
  - `src = event.url`.  
- Timeout guard: fail if no `ready` handshake within 3s.  

### 7.2 Messaging API (postMessage)

This allows for communication between the host (ImmichFrame) and the 
app being loaded in the IFrame.

**Host → App**
```ts
{kind:"frame:hello", version:"1"}
{kind:"frame:context", deviceId:"frame-01", theme:"dark", locale:"en-US"}
{kind:"frame:close"}
```

**App → Host**
```ts
{kind:"app:ready"}
{kind:"app:requestClose"}
{kind:"app:resize", height:420}
{kind:"app:telemetry", event:"button-clicked", properties:{...}}
```

- Host validates `event.origin` against `security.origin`.  
- App may request close/resize, but host has authority.  

## 8. Dismissal Rules
- **Timeout**: Close after `timeoutMs`.  
- **User**: Tap outside (if `allowTouchDismiss`).  
- **Keyboard**: ESC key (if `allowKeyboardDismiss`).  
- **Event**: Incoming `mode:"close"` event.  


## 9. Acknowledgements
SPA should publish acknowledgements back to MQTT.
```json
{"id":"<eventId>","status":"shown|closed|timeout|error"}
```

## 10. Security
- Allowlist of permitted origins in the configuration file.
- CSP headers to enforce iframe content restrictions.
- No camera/mic access by default.

## 11. Error Handling
- If iframe fails to load → show fallback message, auto-dismiss after 5 seconds. Make sure to log the error.
- If event invalid → log + ignore.  
- If conflicting events → apply queue rules (priority + timestamp).  

## 12. Example Events

**Doorbell Popup**
```json
{
  "id":"evt-123",
  "type":"frame.ui.v1",
  "mode":"popup",
  "title":"Doorbell",
  "url":"https://ux.example.com/doorbell?camera=front",
  "timeoutMs":15000,
  "priority":5,
  "category":"doorbell",
  "input":{"allowTouchDismiss":true},
  "security":{"origin":"https://ux.example.com","sandbox":["allow-scripts","allow-same-origin"]}
}
```

**Alarm Cover**
```json
{
  "id":"evt-456",
  "type":"frame.ui.v1",
  "mode":"cover",
  "title":"Alarm Triggered",
  "url":"https://ux.example.com/alarm",
  "priority":10,
  "actions":[{"id":"close","label":"Dismiss","kind":"primary"}],
  "security":{"origin":"https://ux.example.com","sandbox":["allow-scripts","allow-same-origin"]}
}
```

**Close Event**
```json
{"id":"evt-789","type":"frame.ui.v1","mode":"close","category":"doorbell"}
```

## 13. Deliverables for Engineering
1. **Event validator** – schema validation (e.g., Zod/JSON Schema).  
2. **Event queue** – handles priority, category, timeouts.  
3. **UI host component** – iframe injection, messaging, dismissal.  
4. **MQTT/WebSocket client** – listens for events, publishes acks.  
5. **Security guard** – enforce origin allowlist + sandbox rules.  
6. **Demo apps** – Example popup/cover HTML experiences.  

## 14. MVP Scope (HTTP Events + Host Text Popups)

To accelerate initial integration we will land a reduced-scope MVP that
avoids MQTT and iframe authoring overhead:

- **Transport**: HTTP-only. Expose `/api/events` for POSTing events,
  `/api/events/next` for long-poll consumption, and `/api/events/{id}/ack`
  for acknowledgements. The SPA will poll the “next” endpoint.
- **Queue**: Same priority/category/timeout semantics as §6, but stored
  in-memory per device.
- **Security**: Configurable origin allowlist is still enforced for
  iframe modes. Text popups (`popupText`) bypass iframe origin checks.
- **Host-rendered text**: `popupText` mode must render the `message`
  field in a styled modal with optional actions; no iframe/postMessage
  handshake is required.
- **Configuration changes**: Extend the existing config with fields for
  allowed origins, default sandbox flags, and polling interval so the SPA
  can connect to the new endpoints.
- **Opt-in flag**: The general setting `EventHostEnabled` defaults to `false`.
  When disabled the API returns `404` for event endpoints and the SPA skips
  polling/overlays.
- **Future work**: MQTT/WebSocket delivery, iframe demo apps, telemetry,
  and digital signature verification remain post-MVP.

### 14.1 Diagnostics & Tooling

- **API endpoints**  
  - `POST /api/events` – enqueue popup/cover/close events.  
  - `GET /api/events/next?deviceId=<id>` – long-poll the highest-priority
    event for a frame (host-side).  
  - `POST /api/events/{eventId}/ack?deviceId=<id>` – report
    `Shown|Closed|Timeout|Error`. Response echoes the status for logging.  
  - `GET /api/events/pending?deviceId=<id>` – returns a diagnostics view of
    pending events and their last acknowledged status.
- All endpoints are disabled (HTTP 404) when `EventHostEnabled=false`.
- **Logging**: Every acknowledgement is logged at Information level with the
  device id, event id, and status.
- **Demo iframe**: `static/demo/cover-demo.html` demonstrates the handshake
  (`app:ready`, `frame:context`, `app:requestClose`, `app:resize`) and can be
  loaded via `url="/demo/cover-demo.html"` for local testing.

## 15. Proposed Post-MVP Enhancements

These items represent the next tranche of work once the HTTP + overlay MVP is
validated. Prioritize/adjust based on field feedback:

1. **Realtime transport** – add MQTT/WebSocket delivery alongside HTTP polling,
   including outbound acknowledgements and presence heartbeats.
2. **Persistent storage** – optional backing store so queued events survive
   frame/app restarts rather than living purely in-memory.
3. **Telemetry pipeline** – forward `app:telemetry` events to Application
   Insights/Prometheus for diagnostics, with opt-in anonymisation controls.
4. **UI polish** – theming hooks for overlays (custom CSS, configurable
   backdrops), animated transitions, and on-frame controls for dismiss/retry.
5. **Security hardening** – signature validation for events (`security.signature`),
   CSP headers per event origin, and optional per-event token requirements.
6. **Scenario templates** – out-of-the-box iframe apps (doorbell, weather alert,
   calendar overview) plus developer scripts for local scaffolding.
7. **Admin tooling** – dashboard for viewing event history, resend/force-close
   controls, and device status (online/offline, last ack).
8. **Testing & CI** – automated integration tests that spin up the API + SPA,
   trigger events, and verify overlay behaviour via Playwright or Cypress.
9. **Accessibility** – ensure postMessage-enabled apps can request focus,
   surface keyboard shortcuts, and communicate screen-reader friendly metadata.
10. **Multi-frame routing** – rules engine for broadcasting events to groups of
    frames, with per-device overrides and rate limiting.
