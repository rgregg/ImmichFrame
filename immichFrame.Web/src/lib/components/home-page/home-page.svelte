<script lang="ts">
	import * as api from '$lib/index';
	import ProgressBar, {
		ProgressBarLocation,
		ProgressBarStatus
	} from '$lib/components/elements/progress-bar.svelte';
	import { slideshowStore } from '$lib/stores/slideshow.store';
	import { clientIdentifierStore, authSecretStore } from '$lib/stores/persist.store';
	import { onDestroy, onMount, setContext } from 'svelte';
	import OverlayControls from '../elements/overlay-controls.svelte';
	import ImageComponent from '../elements/image-component.svelte';
	import { configStore } from '$lib/stores/config.store';
	import ErrorElement from '../elements/error-element.svelte';
	import Clock from '../elements/clock.svelte';
	import Appointments from '../elements/appointments.svelte';
	import LoadingElement from '../elements/LoadingElement.svelte';
	import { page } from '$app/state';
	import {
		activeEvent,
		acknowledgeEvent,
		clearActiveEvent,
		startEventPolling,
		stopEventPolling
	} from '$lib/events/event-service';
	import type { FrameEvent, FrameEventAckStatus } from '$lib/events/event-service';
	import EventOverlayHost from '$lib/components/events/EventOverlayHost.svelte';

	interface ImagesState {
		images: [string, api.AssetResponseDto, api.AlbumResponseDto[]][];
		error: boolean;
		loaded: boolean;
		split: boolean;
		hasBday: boolean;
	}

	api.init();

	// TODO: make this configurable?
	const PRELOAD_IMAGES = 5;

	let assetHistory: api.AssetResponseDto[] = [];
	let assetBacklog: api.AssetResponseDto[] = [];

	let displayingAssets: api.AssetResponseDto[] = $state() as api.AssetResponseDto[];

	const { restartProgress, stopProgress, instantTransition } = slideshowStore;

	let progressBarStatus: ProgressBarStatus = $state(ProgressBarStatus.Playing);
	let progressBar: ProgressBar = $state() as ProgressBar;

	let error: boolean = $state(false);
	let infoVisible: boolean = $state(false);
	let authError: boolean = $state(false);
	let errorMessage: string = $state() as string;
	let imagesState: ImagesState = $state({
		images: [],
		error: false,
		loaded: false,
		split: false,
		hasBday: false
	});
	let imagePromisesDict: Record<
		string,
		Promise<[string, api.AssetResponseDto, api.AlbumResponseDto[]]>
	> = {};

	let unsubscribeRestart: () => void;
	let unsubscribeStop: () => void;

	let cursorVisible = $state(true);
	let timeoutId: ReturnType<typeof setTimeout>;
	const deviceId = $derived.by(() => getCurrentDeviceId());
	let pollingDeviceId: string | null = $state(null);
	let hasMounted = $state(false);
	let currentEvent: FrameEvent | null = $state(null);
	let lastEventId: string | null = $state(null);
	let eventTimeoutHandle: ReturnType<typeof setTimeout> | null = null;
	let eventPausedSlideshow = $state(false);
	let eventShownAcked = $state(false);
	let unsubscribeActiveEvent: (() => void) | undefined;

	const clientIdentifier = page.url.searchParams.get('client');
	const authsecret = page.url.searchParams.get('authsecret');

	if (clientIdentifier && clientIdentifier != $clientIdentifierStore) {
		clientIdentifierStore.set(clientIdentifier);
	}

	if (authsecret && authsecret != $authSecretStore) {
		authSecretStore.set(authsecret);
		api.init();
	}

	function getCurrentDeviceId(): string {
		const storeId = typeof $clientIdentifierStore === 'string' ? $clientIdentifierStore.trim() : '';
		const queryId = typeof clientIdentifier === 'string' ? clientIdentifier.trim() : '';
		return storeId || queryId || 'default';
	}

	$effect(() => {
		if (!hasMounted) {
			return;
		}

		const id = deviceId;
		if (id && id !== pollingDeviceId) {
			startEventPolling(id);
			pollingDeviceId = id;
		} else if (!id && pollingDeviceId) {
			stopEventPolling();
			pollingDeviceId = null;
		}
	});

	const hideCursor = () => {
		cursorVisible = false;
	};

	setContext('close', provideClose);

	async function provideClose() {
		infoVisible = false;
		await progressBar.play();
	}

	const showCursor = () => {
		cursorVisible = true;
		clearTimeout(timeoutId);
		timeoutId = setTimeout(hideCursor, 2000);
	};

	function clearEventTimer() {
		if (eventTimeoutHandle) {
			clearTimeout(eventTimeoutHandle);
			eventTimeoutHandle = null;
		}
	}

	function markEventShownOnce() {
		if (!currentEvent || eventShownAcked || !deviceId) {
			return;
		}
		eventShownAcked = true;
		void acknowledgeEvent(deviceId, currentEvent.id, 'Shown');
	}

	async function dismissEvent(status: FrameEventAckStatus, explicitEvent: FrameEvent | null = null) {
		const target = explicitEvent ?? currentEvent;
		if (!target) {
			return;
		}

		clearEventTimer();
		clearActiveEvent();
		if (!deviceId) {
			return;
		}

		try {
			await acknowledgeEvent(deviceId, target.id, status);
		} catch (error) {
			console.error('failed to acknowledge frame event', error);
		}
	}

	function handleActiveEvent(event: FrameEvent | null) {
		clearEventTimer();

		if (!event) {
			currentEvent = null;
			eventShownAcked = false;
			lastEventId = null;
			if (eventPausedSlideshow && progressBar) {
				void progressBar.play();
			}
			eventPausedSlideshow = false;
			return;
		}

		if (event.mode === 'Close') {
			void dismissEvent('Closed', event);
			return;
		}

		currentEvent = event;
		const isNewEvent = event.id !== lastEventId;
		if (isNewEvent) {
			lastEventId = event.id;
			eventShownAcked = false;
			if (progressBar && progressBarStatus !== ProgressBarStatus.Paused) {
				void progressBar.pause();
				eventPausedSlideshow = true;
			} else if (progressBarStatus === ProgressBarStatus.Paused) {
				eventPausedSlideshow = false;
			}
		}

		markEventShownOnce();

		const fallbackTimeout = $configStore.eventDefaultTimeoutMs ?? 0;
		const timeoutMs = event.timeoutMs ?? fallbackTimeout;
		if (timeoutMs && timeoutMs > 0) {
			eventTimeoutHandle = setTimeout(() => {
				void dismissEvent('Timeout');
			}, timeoutMs);
		}
	}

	async function updateImagePromises() {
		for (let asset of displayingAssets) {
			if (!(asset.id in imagePromisesDict)) {
				imagePromisesDict[asset.id] = loadImage(asset);
			}
		}
		for (let i = 0; i < PRELOAD_IMAGES; i++) {
			if (i >= assetBacklog.length) {
				break;
			}
			if (!(assetBacklog[i].id in imagePromisesDict)) {
				imagePromisesDict[assetBacklog[i].id] = loadImage(assetBacklog[i]);
			}
		}
		// originally just deleted displayingAssets after they were no longer needed
		// but this is more bulletproof to edge cases I think
		for (let key in imagePromisesDict) {
			if (
				!(
					displayingAssets.find((item) => item.id == key) ||
					assetBacklog.find((item) => item.id == key)
				)
			) {
				delete imagePromisesDict[key];
			}
		}
	}

	async function loadAssets() {
		try {
			let assetRequest = await api.getAsset();

			if (assetRequest.status != 200) {
				if (assetRequest.status == 401) {
					authError = true;
				}
				error = true;
				return;
			}

			error = false;
			assetBacklog = assetRequest.data;
		} catch {
			error = true;
		}
	}

	const handleDone = async (previous: boolean = false, instant: boolean = false) => {
		progressBar.restart(false);
		$instantTransition = instant;
		if (previous) await getPreviousAssets();
		else await getNextAssets();
		progressBar.play();
	};

	async function getNextAssets() {
		if (!assetBacklog || assetBacklog.length < 1) {
			await loadAssets();
		}

		if (!error && assetBacklog.length == 0) {
			error = true;
			errorMessage = 'No assets were found! Check your configuration.';
			return;
		}

		let next: api.AssetResponseDto[];
		if (
			$configStore.layout?.trim().toLowerCase() == 'splitview' &&
			assetBacklog.length > 1 &&
			isHorizontal(assetBacklog[0]) &&
			isHorizontal(assetBacklog[1])
		) {
			next = assetBacklog.splice(0, 2);
		} else {
			next = assetBacklog.splice(0, 1);
		}
		assetBacklog = [...assetBacklog];

		if (displayingAssets) {
			// Push to History
			assetHistory.push(...displayingAssets);
		}

		// History max 250 Items
		if (assetHistory.length > 250) {
			assetHistory = assetHistory.splice(assetHistory.length - 250, 250);
		}

		displayingAssets = next;
		updateImagePromises();
		imagesState = await loadImages(next);
	}

	async function getPreviousAssets() {
		if (!assetHistory || assetHistory.length < 1) {
			return;
		}

		let next: api.AssetResponseDto[];
		if (
			$configStore.layout?.trim().toLowerCase() == 'splitview' &&
			assetHistory.length > 1 &&
			isHorizontal(assetHistory[assetHistory.length - 1]) &&
			isHorizontal(assetHistory[assetHistory.length - 2])
		) {
			next = assetHistory.splice(assetHistory.length - 2, 2);
		} else {
			next = assetHistory.splice(assetHistory.length - 1, 1);
		}

		assetHistory = [...assetHistory];

		// Unshift to Backlog
		if (displayingAssets) {
			assetBacklog.unshift(...displayingAssets);
		}
		displayingAssets = next;
		updateImagePromises();
		imagesState = await loadImages(next);
	}

	function isHorizontal(asset: api.AssetResponseDto) {
		const isFlipped = (orientation: number) => [5, 6, 7, 8].includes(orientation);
		let imageHeight = asset.exifInfo?.exifImageHeight ?? 0;
		let imageWidth = asset.exifInfo?.exifImageWidth ?? 0;
		if (isFlipped(Number(asset.exifInfo?.orientation ?? 0))) {
			[imageHeight, imageWidth] = [imageWidth, imageHeight];
		}
		return imageHeight > imageWidth; // or imageHeight > imageWidth * 1.25;
	}

	function hasBirthday(assets: api.AssetResponseDto[]) {
		let today = new Date();
		let hasBday: boolean = false;

		for (let asset of assets) {
			for (let person of asset.people ?? new Array()) {
				let birthdate = new Date(person.birthDate ?? '');
				if (birthdate.getDate() === today.getDate() && birthdate.getMonth() === today.getMonth()) {
					hasBday = true;
					break;
				}
			}
			if (hasBday) break;
		}

		return hasBday;
	}

	async function loadImages(assets: api.AssetResponseDto[]) {
		let newImages = [];
		try {
			for (let asset of assets) {
				let img = await imagePromisesDict[asset.id];
				newImages.push(img);
			}
			return {
				images: newImages,
				error: false,
				loaded: true,
				split: assets.length == 2,
				hasBday: hasBirthday(assets)
			};
		} catch {
			return {
				images: [],
				error: true,
				loaded: false,
				split: false,
				hasBday: false
			};
		}
	}

	async function loadImage(assetResponse: api.AssetResponseDto) {
		let req = await api.getImage(assetResponse.id, { clientIdentifier: $clientIdentifierStore });
		let album: api.AlbumResponseDto[] | null = null;
		if ($configStore.showAlbumName) {
			let albumReq = await api.getAlbumInfo(assetResponse.id, {
				clientIdentifier: $clientIdentifierStore
			});
			album = albumReq.data;
		}

		if (req.status != 200 || ($configStore.showAlbumName && album == null)) {
			return ['', assetResponse, []] as [string, api.AssetResponseDto, api.AlbumResponseDto[]];
		}

		// if the people array is already populated, there is no need to call the API again
		if ($configStore.showPeopleDesc && (assetResponse.people ?? []).length == 0) {
			let assetInfoRequest = await api.getAssetInfo(assetResponse.id, {
				clientIdentifier: $clientIdentifierStore
			});
			assetResponse.people = assetInfoRequest.data.people;
			// assetResponse.exifInfo = assetInfoRequest.data.exifInfo;
		}

		return [getImageUrl(req.data), assetResponse, album] as [
			string,
			api.AssetResponseDto,
			api.AlbumResponseDto[]
		];
	}

	function getImageUrl(image: Blob) {
		return URL.createObjectURL(image);
	}

	onMount(() => {
		hasMounted = true;
		unsubscribeActiveEvent = activeEvent.subscribe(handleActiveEvent);
		window.addEventListener('mousemove', showCursor);
		window.addEventListener('click', showCursor);
		if ($configStore.primaryColor) {
			document.documentElement.style.setProperty('--primary-color', $configStore.primaryColor);
		}

		if ($configStore.secondaryColor) {
			document.documentElement.style.setProperty('--secondary-color', $configStore.secondaryColor);
		}

		if ($configStore.baseFontSize) {
			document.documentElement.style.fontSize = $configStore.baseFontSize;
		}

		unsubscribeRestart = restartProgress.subscribe((value) => {
			if (value) {
				progressBar.restart(value);
			}
		});

		unsubscribeStop = stopProgress.subscribe((value) => {
			if (value) {
				progressBar.restart(false);
			}
		});

		getNextAssets();

		return () => {
			window.removeEventListener('mousemove', showCursor);
			window.removeEventListener('click', showCursor);
			if (unsubscribeActiveEvent) {
				unsubscribeActiveEvent();
				unsubscribeActiveEvent = undefined;
			}
			clearEventTimer();
			stopEventPolling();
			hasMounted = false;
		};
	});

	onDestroy(() => {
		if (unsubscribeRestart) {
			unsubscribeRestart();
		}

		if (unsubscribeStop) {
			unsubscribeStop();
		}
		if (unsubscribeActiveEvent) {
			unsubscribeActiveEvent();
			unsubscribeActiveEvent = undefined;
		}
		clearEventTimer();
		stopEventPolling();
		hasMounted = false;
	});
</script>

<section class="fixed grid h-dvh-safe w-screen bg-black" class:cursor-none={!cursorVisible}>
	{#if error}
		<ErrorElement {authError} message={errorMessage} />
	{:else if displayingAssets}
		<div class="absolute h-screen w-screen">
			<ImageComponent
				showLocation={$configStore.showImageLocation}
				interval={$configStore.interval}
				showPhotoDate={$configStore.showPhotoDate}
				showImageDesc={$configStore.showImageDesc}
				showPeopleDesc={$configStore.showPeopleDesc}
				showAlbumName={$configStore.showAlbumName}
				{...imagesState}
				imageFill={$configStore.imageFill}
				imageZoom={$configStore.imageZoom}
				imagePan={$configStore.imagePan}
				bind:showInfo={infoVisible}
			/>
		</div>

		{#if $configStore.showClock}
			<Clock />
		{/if}

		<Appointments />

			<EventOverlayHost
				event={currentEvent}
				dismiss={dismissEvent}
				deviceId={deviceId}
				theme={$configStore.style ?? 'default'}
				locale={$configStore.language ?? 'en'}
			/>

		<OverlayControls
			next={async () => {
				await handleDone(false, true);
				infoVisible = false;
			}}
			back={async () => {
				await handleDone(true, true);
				infoVisible = false;
			}}
			pause={async () => {
				infoVisible = false;
				if (progressBarStatus == ProgressBarStatus.Paused) {
					await progressBar.play();
				} else {
					await progressBar.pause();
				}
			}}
			showInfo={async () => {
				if (infoVisible) {
					infoVisible = false;
					await progressBar.play();
				} else {
					infoVisible = true;
					await progressBar.pause();
				}
			}}
			bind:status={progressBarStatus}
			bind:infoVisible
			overlayVisible={cursorVisible && !currentEvent}
		/>

		<ProgressBar
			autoplay
			duration={$configStore.interval}
			hidden={!$configStore.showProgressBar}
			location={ProgressBarLocation.Bottom}
			bind:this={progressBar}
			bind:status={progressBarStatus}
			onDone={handleDone}
		/>
	{:else}
		<LoadingElement />
	{/if}
</section>
