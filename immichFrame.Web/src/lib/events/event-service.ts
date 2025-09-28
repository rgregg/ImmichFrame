import { get, writable } from 'svelte/store';
import type { ClientSettingsWithUx } from '$lib/stores/config.store';
import { configStore } from '$lib/stores/config.store';

export type FrameEventMode = 'Popup' | 'PopupText' | 'Cover' | 'Close';

export type FrameEventAckStatus = 'Shown' | 'Closed' | 'Timeout' | 'Error';

export interface FrameEventAction {
  id: string;
  label: string;
  kind?: string | null;
}

export interface FrameEventInput {
  allowTouchDismiss: boolean;
  allowKeyboardDismiss: boolean;
}

export interface FrameEventSecurity {
  origin?: string | null;
  sandbox?: string[];
  signature?: string | null;
}

export interface FrameEvent {
  id: string;
  type: string;
  mode: FrameEventMode;
  url?: string | null;
  message?: string | null;
  timeoutMs?: number | null;
  priority: number;
  category?: string | null;
  title?: string | null;
  meta?: Record<string, unknown> | null;
  actions: FrameEventAction[];
  input: FrameEventInput;
  security: FrameEventSecurity;
  postedAt: string;
}

const activeEventStore = writable<FrameEvent | null>(null);

export const activeEvent = {
  subscribe: activeEventStore.subscribe
};

export function clearActiveEvent() {
  activeEventStore.set(null);
}

let pollingController: AbortController | null = null;

export function startEventPolling(deviceId: string) {
  const settings = get(configStore) as ClientSettingsWithUx;
  if (!settings.eventHostEnabled) {
    activeEventStore.set(null);
    return;
  }
  stopEventPolling();
  pollingController = new AbortController();
  void pollLoop(deviceId, pollingController);
}

export function stopEventPolling() {
  pollingController?.abort();
  pollingController = null;
}

async function pollLoop(deviceId: string, controller: AbortController) {
  const settings = get(configStore) as ClientSettingsWithUx;
  const intervalMs = Math.max(500, (settings.eventPollingIntervalSeconds ?? 2) * 1000);

  while (!controller.signal.aborted) {
    if (!get(configStore).eventHostEnabled) {
      activeEventStore.set(null);
      await delay(intervalMs, controller.signal);
      continue;
    }

    try {
      const response = await fetch(`/api/events/next?deviceId=${encodeURIComponent(deviceId)}`, {
        method: 'GET',
        signal: controller.signal
      });

      if (response.status === 200) {
        const payload = (await response.json()) as FrameEvent;
        activeEventStore.set(payload);
      } else if (response.status === 204) {
        activeEventStore.set(null);
      }
    } catch (error) {
      if ((error as Error).name !== 'AbortError') {
        // For now we simply log to console; later we can surface via telemetry store
        console.error('event poll failed', error);
      }
    }

    await delay(intervalMs, controller.signal);
  }
}

export async function acknowledgeEvent(deviceId: string, eventId: string, status: FrameEventAckStatus) {
  if (!get(configStore).eventHostEnabled) {
    return;
  }
  try {
    await fetch(`/api/events/${encodeURIComponent(eventId)}/ack?deviceId=${encodeURIComponent(deviceId)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status })
    });
  } catch (error) {
    console.error('failed to acknowledge event', error);
  }
}

async function delay(durationMs: number, signal: AbortSignal) {
  return new Promise<void>((resolve) => {
    if (signal.aborted) {
      resolve();
      return;
    }

    const timeout = setTimeout(() => {
      resolve();
    }, durationMs);

    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(timeout);
        resolve();
      },
      { once: true }
    );
  });
}
