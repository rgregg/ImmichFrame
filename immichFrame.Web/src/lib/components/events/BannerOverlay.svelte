<script lang="ts">
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import { onMount } from 'svelte';

	let { event, onDismiss }: {
		event: FrameEvent;
		onDismiss: (status: FrameEventAckStatus) => void | Promise<void>;
	} = $props();

	let dismissing = false;

	const message = event.message ?? '';
	const timeoutMs = event.timeoutMs ?? 0;
	const allowTouchDismiss = event.input?.allowTouchDismiss ?? true;

	let progressPercent = $state(100);

	onMount(() => {
		if (timeoutMs <= 0) return;

		// Defer the transition start one frame so the initial 100% paints first.
		requestAnimationFrame(() => {
			progressPercent = 0;
		});
	});

	async function dismiss(status: FrameEventAckStatus = 'Dismissed') {
		if (dismissing) return;
		dismissing = true;
		await onDismiss(status);
		dismissing = false;
	}

	function handleClick() {
		if (!allowTouchDismiss) return;
		dismiss('Dismissed');
	}
</script>

<div
	class="pointer-events-none fixed inset-x-0 top-0 z-[160] flex justify-center p-4"
	role="presentation"
>
	<button
		type="button"
		class="pointer-events-auto w-full max-w-[min(48rem,90vw)] cursor-pointer rounded-xl bg-neutral-900/90 px-5 py-3 text-left text-white shadow-2xl ring-1 ring-white/10 backdrop-blur transition hover:bg-neutral-800/90 motion-safe:animate-[bannerin_200ms_ease-out]"
		onclick={handleClick}
		aria-label="Notification: {message}"
	>
		<p class="whitespace-pre-line text-base leading-snug">{message}</p>
		{#if timeoutMs > 0}
			<div class="mt-2 h-[2px] w-full overflow-hidden rounded-full bg-white/10">
				<div
					class="h-full bg-white/60"
					style="width: {progressPercent}%; transition: width {timeoutMs}ms linear;"
				></div>
			</div>
		{/if}
	</button>
</div>

<style>
	@keyframes bannerin {
		from {
			transform: translateY(-100%);
			opacity: 0;
		}
		to {
			transform: translateY(0);
			opacity: 1;
		}
	}
</style>
