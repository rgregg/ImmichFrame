<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import PopupTextOverlay from './PopupTextOverlay.svelte';

	export let event: FrameEvent | null = null;
	export let dismiss: (status: FrameEventAckStatus) => void | Promise<void>;

	const unsupportedCopy =
		'Interactive overlays are coming soon. For now this placeholder lets you acknowledge the event.';
</script>

{#if event}
	{#if event.mode === 'PopupText'}
		<PopupTextOverlay {event} onDismiss={dismiss} />
	{:else}
		<div class="absolute inset-0 z-[140] flex items-center justify-center bg-black/50 p-6">
			<div class="max-w-xl rounded-xl bg-neutral-900/95 p-6 text-white shadow-xl ring-1 ring-white/10">
				<h2 class="mb-2 text-xl font-semibold">{event.title ?? 'Overlay experience'}</h2>
				<p class="mb-4 text-sm text-white/80">
					{unsupportedCopy}
				</p>
				<div class="flex flex-wrap gap-3">
					<button
						type="button"
						class="rounded-full bg-white px-4 py-2 text-sm font-semibold text-black shadow transition hover:bg-neutral-200 focus:outline-none focus:ring-2 focus:ring-white/60 focus:ring-offset-2 focus:ring-offset-neutral-900"
						on:click={() => dismiss('Closed')}
					>
						Dismiss
					</button>
				</div>
			</div>
		</div>
	{/if}
{/if}
