<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import PopupTextOverlay from './PopupTextOverlay.svelte';

	export let event: FrameEvent | null = null;
	export let dismiss: (status: FrameEventAckStatus) => void | Promise<void>;
</script>

{#if event}
	{#key event.id}
		{#if event.mode === 'PopupText'}
			<PopupTextOverlay {event} onDismiss={dismiss} />
		{/if}
	{/key}
{/if}
