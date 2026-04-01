<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import { onMount } from 'svelte';

	let { event, onDismiss }: {
		event: FrameEvent;
		onDismiss: (status: FrameEventAckStatus) => void | Promise<void>;
	} = $props();

	let dismissing = false;

	const allowTouchDismiss = event.input?.allowTouchDismiss ?? true;
	const allowKeyboardDismiss = event.input?.allowKeyboardDismiss ?? true;

	const actions = event.actions?.length
		? event.actions
		: [{ id: 'close', label: 'Dismiss', kind: 'primary' }];

	const message = event.message ?? '';
	const timeoutMs = event.timeoutMs ?? 0;

	let secondsRemaining = $state(timeoutMs > 0 ? Math.ceil(timeoutMs / 1000) : 0);

	onMount(() => {
		if (timeoutMs <= 0) return;

		const interval = setInterval(() => {
			secondsRemaining = Math.max(0, secondsRemaining - 1);
		}, 1000);

		return () => clearInterval(interval);
	});

	async function dismiss(status: FrameEventAckStatus = 'Closed') {
		if (dismissing) return;
		dismissing = true;
		await onDismiss(status);
		dismissing = false;
	}

	function handleBackdropPointerDown(e: PointerEvent) {
		if (!allowTouchDismiss) return;
		if (e.target === e.currentTarget) {
			dismiss('Closed');
		}
	}

	function handleKey(e: KeyboardEvent) {
		if (!allowKeyboardDismiss) return;
		if (e.key === 'Escape') {
			e.preventDefault();
			dismiss('Closed');
		}
	}
</script>

<svelte:window onkeydown={handleKey} />

<div
	class="absolute inset-0 z-[150] flex items-center justify-center bg-black/60 p-6"
	role="presentation"
	onpointerdown={handleBackdropPointerDown}
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
		<div class="flex flex-wrap items-center gap-3">
			{#each actions as action}
				<button
					type="button"
					class={`rounded-full px-4 py-2 text-sm font-semibold transition focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-neutral-900 ${
						action.kind === 'primary'
							? 'bg-white text-black hover:bg-neutral-200 focus:ring-white/60'
							: 'bg-white/10 text-white hover:bg-white/20 focus:ring-white/50'
					}`}
					onclick={() => dismiss('Closed')}
				>
					{action.label}
				</button>
			{/each}
			{#if secondsRemaining > 0}
				<span class="ml-auto text-sm text-white/50">{secondsRemaining}s</span>
			{/if}
		</div>
	</div>
</div>
