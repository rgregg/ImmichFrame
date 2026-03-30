<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';

	export let event: FrameEvent;
	export let onDismiss: (status: FrameEventAckStatus) => void | Promise<void>;

	let dismissing = false;

	const allowTouchDismiss = event.input?.allowTouchDismiss ?? true;
	const allowKeyboardDismiss = event.input?.allowKeyboardDismiss ?? true;

	const actions = event.actions?.length
		? event.actions
		: [{ id: 'close', label: 'Dismiss', kind: 'primary' }];

	const message = event.message ?? '';

	async function dismiss(status: FrameEventAckStatus = 'Closed') {
		if (dismissing) return;
		dismissing = true;
		await onDismiss(status);
		dismissing = false;
	}

	function handleBackdropPointerDown(event: PointerEvent) {
		if (!allowTouchDismiss) return;
		if (event.target === event.currentTarget) {
			dismiss('Closed');
		}
	}

	function handleKey(event: KeyboardEvent) {
		if (!allowKeyboardDismiss) return;
		if (event.key === 'Escape') {
			event.preventDefault();
			dismiss('Closed');
		}
	}
</script>

<svelte:window on:keydown={handleKey} />

<div
	class="absolute inset-0 z-[150] flex items-center justify-center bg-black/60 p-6"
	role="presentation"
	on:pointerdown={handleBackdropPointerDown}
>
	<div
		class="max-w-[min(28rem,90vw)] rounded-xl bg-neutral-900/95 p-6 text-white shadow-2xl ring-1 ring-white/10"
		role="dialog"
		aria-modal="true"
		aria-labelledby={event.title ? 'popup-text-title' : undefined}
	>
		{#if event.title}
			<h2 id="popup-text-title" class="mb-2 text-2xl font-semibold">{event.title}</h2>
		{/if}
		<p class="mb-6 whitespace-pre-line text-lg leading-relaxed">{message}</p>
		<div class="flex flex-wrap gap-3">
			{#each actions as action}
				<button
					type="button"
					class={`rounded-full px-4 py-2 text-sm font-semibold transition focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-neutral-900 ${
						action.kind === 'primary'
							? 'bg-white text-black hover:bg-neutral-200 focus:ring-white/60'
							: 'bg-white/10 text-white hover:bg-white/20 focus:ring-white/50'
					}`}
					on:click={() => dismiss('Closed')}
				>
					{action.label}
				</button>
			{/each}
		</div>
	</div>
</div>
