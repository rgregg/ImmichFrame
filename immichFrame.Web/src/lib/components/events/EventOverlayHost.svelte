<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import PopupTextOverlay from './PopupTextOverlay.svelte';
	import IframeOverlay from './IframeOverlay.svelte';

	export let event: FrameEvent | null = null;
	export let dismiss: (status: FrameEventAckStatus) => void | Promise<void>;
	export let deviceId: string;
	export let theme: string | undefined;
	export let locale: string | undefined;
</script>

{#if event}
	{#key event.id}
		{#if event.mode === 'PopupText'}
			<PopupTextOverlay {event} onDismiss={dismiss} />
		{:else if event.mode === 'Popup' || event.mode === 'Cover'}
			<IframeOverlay {event} {deviceId} {theme} {locale} onDismiss={dismiss} />
		{/if}
	{/key}
{/if}
