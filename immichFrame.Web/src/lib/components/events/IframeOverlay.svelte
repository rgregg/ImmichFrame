<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';

	interface ResizePayload {
		kind: string;
		height?: number;
	}

	interface TelemetryPayload {
		kind: string;
		event?: string;
		properties?: Record<string, unknown>;
	}

	export let event: FrameEvent;
	export let onDismiss: (status: FrameEventAckStatus) => void | Promise<void>;
	export let deviceId: string;
	export let theme: string | undefined;
	export let locale: string | undefined;

	const HANDSHAKE_TIMEOUT_MS = 3000;
	const ERROR_AUTO_DISMISS_MS = 5000;

	let iframeEl: HTMLIFrameElement | null = null;
	let iframeHeight = event.mode === 'Popup' ? 420 : undefined;
	let handshakeTimer: ReturnType<typeof setTimeout> | null = null;
	let dismissTimer: ReturnType<typeof setTimeout> | null = null;
	let isDismissed = false;
	let hasError = false;
	let errorMessage = '';
	let handshakeAcknowledged = false;
	let contextSent = false;

	const sandboxAttributes = (event.security?.sandbox ?? []).join(' ');
	const targetOrigin = event.security?.origin ?? '*';

	onMount(() => {
		window.addEventListener('message', handleMessage);
		return () => {
			window.removeEventListener('message', handleMessage);
		};
	});

	onDestroy(() => {
		cleanupTimers();
	});

	function cleanupTimers() {
		if (handshakeTimer) {
			clearTimeout(handshakeTimer);
			handshakeTimer = null;
		}
		if (dismissTimer) {
			clearTimeout(dismissTimer);
			dismissTimer = null;
		}
	}

	function sendHelloAndContext() {
		if (contextSent) {
			return;
		}
		contextSent = true;
		postToApp({ kind: 'frame:hello', version: '1' });
		postToApp({
			kind: 'frame:context',
			deviceId,
			theme: theme ?? 'default',
			locale: locale ?? 'en'
		});
	}

	function startHandshake() {
		sendHelloAndContext();

		if (handshakeAcknowledged) {
			return;
		}

		cleanupHandshakeTimer();
		handshakeTimer = setTimeout(() => {
			setError('The experience did not respond in time.');
		}, HANDSHAKE_TIMEOUT_MS);
	}

	function cleanupHandshakeTimer() {
		if (handshakeTimer) {
			clearTimeout(handshakeTimer);
			handshakeTimer = null;
		}
	}

	function postToApp(message: unknown) {
		if (!iframeEl?.contentWindow) {
			return;
		}
		iframeEl.contentWindow.postMessage(message, targetOrigin === '*' ? '*' : targetOrigin);
	}

	async function dismiss(status: FrameEventAckStatus) {
		if (isDismissed) {
			return;
		}
		isDismissed = true;
		cleanupTimers();
		await onDismiss(status);
	}

	function scheduleAutoDismiss(status: FrameEventAckStatus, delay: number) {
		if (dismissTimer) {
			return;
		}
		dismissTimer = setTimeout(() => {
			void dismiss(status);
		}, delay);
	}

	function setError(message: string) {
		hasError = true;
		errorMessage = message;
		cleanupHandshakeTimer();
		scheduleAutoDismiss('Error', ERROR_AUTO_DISMISS_MS);
	}

	function handleIframeLoad() {
		if (hasError) {
			return;
		}
		startHandshake();
	}

	function handleMessage(messageEvent: MessageEvent) {
		if (!iframeEl?.contentWindow || messageEvent.source !== iframeEl.contentWindow) {
			return;
		}

		// Ensure origin matches when specified
		if (event.security?.origin && messageEvent.origin !== event.security.origin) {
			return;
		}

		const data = messageEvent.data;
		if (!data || typeof data !== 'object') {
			return;
		}

		switch ((data as { kind?: string }).kind) {
			case 'app:ready': {
				handshakeAcknowledged = true;
				cleanupHandshakeTimer();
				break;
			}
			case 'app:requestClose': {
				void dismiss('Closed');
				break;
			}
			case 'app:resize': {
				if (event.mode === 'Popup') {
					const resizePayload = data as ResizePayload;
					if (typeof resizePayload.height === 'number' && resizePayload.height > 0) {
						const maxHeight = Math.max(window.innerHeight - 160, 240);
						iframeHeight = Math.min(Math.max(resizePayload.height, 240), maxHeight);
					}
				}
				break;
			}
			case 'app:telemetry': {
				// Telemetry events are currently logged for debugging only.
				const telemetry = data as TelemetryPayload;
				console.debug('Overlay telemetry', telemetry.event, telemetry.properties ?? {});
				break;
			}
			default:
				break;
		}
	}

	$: if (event.mode !== 'Popup') {
		iframeHeight = undefined;
	}
</script>

{#if event.mode === 'Cover'}
	<div class="absolute inset-0 z-[150] bg-black">
		{#if hasError}
			<div class="flex h-full w-full items-center justify-center">
				<div class="max-w-md rounded-xl bg-neutral-900/95 p-6 text-center text-white shadow-2xl ring-1 ring-white/10">
					<h2 class="mb-4 text-xl font-semibold">Unable to load experience</h2>
					<p class="mb-4 text-sm text-white/80">{errorMessage}</p>
					<button
						type="button"
						class="rounded-full bg-white px-4 py-2 text-sm font-semibold text-black shadow transition hover:bg-neutral-200 focus:outline-none focus:ring-2 focus:ring-white/60 focus:ring-offset-2 focus:ring-offset-neutral-900"
						on:click={() => dismiss('Error')}
					>
						Dismiss
					</button>
				</div>
			</div>
		{:else}
			<iframe
				bind:this={iframeEl}
				src={event.url ?? ''}
				class="h-full w-full border-0"
				sandbox={sandboxAttributes}
				allow="autoplay; fullscreen"
				title={event.title ?? 'ImmichFrame overlay'}
				on:load={handleIframeLoad}
			></iframe>
		{/if}
	</div>
{:else}
	<div class="absolute inset-0 z-[150] flex items-center justify-center bg-black/60 p-6">
		<div class="w-full max-w-5xl rounded-2xl bg-neutral-950/95 p-4 shadow-2xl ring-1 ring-white/10">
			{#if hasError}
				<div class="flex flex-col items-center gap-4 px-6 py-12 text-center text-white">
					<h2 class="text-xl font-semibold">Unable to load experience</h2>
					<p class="text-sm text-white/80">{errorMessage}</p>
					<button
						type="button"
						class="rounded-full bg-white px-4 py-2 text-sm font-semibold text-black shadow transition hover:bg-neutral-200 focus:outline-none focus:ring-2 focus:ring-white/60 focus:ring-offset-2 focus:ring-offset-neutral-900"
						on:click={() => dismiss('Error')}
					>
						Dismiss
					</button>
				</div>
			{:else}
				<div class="overflow-hidden rounded-xl bg-black">
					<iframe
						bind:this={iframeEl}
						src={event.url ?? ''}
						class="h-full w-full border-0"
						style={iframeHeight ? `height: ${iframeHeight}px;` : ''}
						sandbox={sandboxAttributes}
						allow="autoplay"
						title={event.title ?? 'ImmichFrame overlay'}
						on:load={handleIframeLoad}
					></iframe>
				</div>
			{/if}
		</div>
	</div>
{/if}
