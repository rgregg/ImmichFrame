<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import PopupTextOverlay from './PopupTextOverlay.svelte';

	let { event = null, dismiss }: {
		event: FrameEvent | null;
		dismiss: (status: FrameEventAckStatus) => void | Promise<void>;
	} = $props();
</script>

{#if event}
	{#key event.id}
		{#if event.mode === 'PopupText'}
			<PopupTextOverlay {event} onDismiss={dismiss} />
		{/if}
	{/key}
{/if}
