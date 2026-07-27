'use client';

import { useSyncExternalStore } from 'react';
import type { SpeechChunk } from './types';

/**
 * The speech engine, wrapped.
 *
 * `speechSynthesis` is a single global device resource, so this is a module
 * singleton: two components each holding their own queue would interleave into
 * one voice reading two things. Everything that wants to speak goes through here.
 *
 * There are two engines behind this, and the reader is never asked to choose.
 *
 * The first is a file rendered on the server and played through an <audio>
 * element. That is what makes the lock screen work — the operating system will
 * keep an element playing and put controls on the lock screen, and it will do
 * neither for `speechSynthesis`, which it does not consider media at all.
 *
 * The second is the device's own voice, which is what runs when there is no
 * rendering to play: no key configured, the day's allowance spent, a story whose
 * synthesis failed. It needs no key, no quota and no network, and it carries no
 * secure-context requirement — which matters, because this app is read over a
 * plain-HTTP Tailscale address where the clipboard and the service worker are
 * both unavailable.
 *
 * Falling back is automatic and silent by design. A reader whose quota ran out
 * mid-morning should notice the voice change, not find a button that does nothing.
 */

export type SpeechStatus = 'idle' | 'speaking' | 'paused' | 'ended';

/**
 * Which engine is actually producing the sound.
 *
 * `audio` is a file rendered on the server: it sounds like a person, it obeys
 * playbackRate without restarting, and because it is an <audio> element the
 * operating system will keep it playing with the screen locked and put it on the
 * lock screen. `device` is the browser's own speech synthesis, which does none of
 * those things but needs no key, no quota and no network.
 *
 * The reader is never asked to choose. Server audio is tried first and the device
 * voice is what happens when it is unavailable — no key configured, the day's
 * allowance spent, or a story whose rendering failed.
 */
export type SpeechEngine = 'audio' | 'device';

export interface SpeechSnapshot {
  status: SpeechStatus;
  /** Which engine is producing the sound. */
  engine: SpeechEngine;
  /** Index of the utterance being spoken, or -1 when idle. Device engine only. */
  index: number;
  total: number;
  /** Seconds played and total seconds. Both 0 unless the engine is `audio`. */
  position: number;
  duration: number;
  /**
   * How far through, 0 to 1, whichever engine is running.
   *
   * The two engines measure progress in different units — one knows sentences and
   * the other knows seconds — and every caller that draws a bar wants the same
   * number. Computed here so no component has to know which engine it is watching.
   */
  progress: number;
  rate: number;
  voiceUri: string | null;
  /** Turkish voices the device actually has. Empty means the feature is unusable. */
  turkishVoices: SpeechSynthesisVoice[];
  /** False until the voice list has resolved — Safari returns it asynchronously. */
  ready: boolean;
  supported: boolean;
  /** What is being read, so the caller can label the player. */
  title: string | null;
  /**
   * Where this story sits in the queue that was started, 1-based, and how long
   * that queue is. Both 0 for a single story.
   *
   * Held here rather than in the player because the dock outlives it: the queue
   * keeps playing while the reader browses, and by then the component that knows
   * about the list has been unmounted by the router.
   */
  queueIndex: number;
  queueTotal: number;
  /**
   * Which player started this. There is one engine and more than one player on
   * screen — the story's own and the day queue — so each has to be able to ask
   * "is that me speaking" before it renders itself as playing.
   */
  owner: string | null;
}

export const SPEECH_RATES = [0.75, 1, 1.25, 1.5, 2] as const;

const MIN_RATE = 0.5;
const MAX_RATE = 2.5;

/** Chrome stops speaking after roughly fifteen seconds unless it is nudged. */
const KEEP_ALIVE_MS = 10_000;

/**
 * Speed and voice are remembered on the device, not on the account.
 *
 * They describe the hardware you are listening on, not who you are: the rate
 * that works in headphones is not the rate that works through a laptop speaker,
 * and the voice list differs per device anyway. Syncing them through the profile
 * would mean a phone preference silently changing what the desktop does.
 */
const RATE_KEY = 'focusai.speech.rate';
const VOICE_KEY = 'focusai.speech.voice';

function readStoredRate(): number {
  if (typeof localStorage === 'undefined') return 1;

  const raw = Number(localStorage.getItem(RATE_KEY));
  return Number.isFinite(raw) && raw >= MIN_RATE && raw <= MAX_RATE ? raw : 1;
}

function remember(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Private mode, or storage full. A forgotten preference is not worth an error.
  }
}

function isTurkish(voice: SpeechSynthesisVoice): boolean {
  return voice.lang.toLowerCase().replace('_', '-').startsWith('tr');
}

class SpeechController {
  private chunks: SpeechChunk[] = [];
  private title: string | null = null;
  private owner: string | null = null;
  private queueIndex = 0;
  private queueTotal = 0;
  private engine: SpeechEngine = 'device';

  /**
   * The one audio element, reused for every story.
   *
   * Created on the first play rather than at module load, and never replaced: iOS
   * only lets an element play if its *first* play() came from a user gesture, and
   * a fresh element per story would need a fresh gesture per story. Reusing this
   * one is what lets the day's queue advance on its own.
   */
  private audio: HTMLAudioElement | null = null;
  private index = -1;
  private status: SpeechStatus = 'idle';
  private rate = 1;
  private restored = false;
  private voice: SpeechSynthesisVoice | null = null;
  private voices: SpeechSynthesisVoice[] = [];
  private ready = false;

  private readonly listeners = new Set<() => void>();
  private keepAlive: ReturnType<typeof setInterval> | null = null;
  private snapshot: SpeechSnapshot | null = null;

  /**
   * Bumped on every cancel. `cancel()` fires `onend` for the utterance it killed
   * on several browsers, and without a generation guard that stale callback
   * advances the queue — the reader presses stop and the next sentence starts.
   */
  private generation = 0;

  private onFinished: (() => void) | null = null;

  /**
   * Whether reading aloud is possible at all.
   *
   * Broader than it used to be, and it has to be: this once meant "the browser has
   * speechSynthesis", which was the only engine there was. Now the main engine is
   * an audio file, and every browser can play one — so gating the feature on the
   * device's speech support would hide the better path on exactly the devices that
   * need it, which is what iOS looked like before the server started rendering.
   */
  get supported(): boolean {
    return typeof window !== 'undefined';
  }

  /** Whether the device can read to us itself, which is the fallback path. */
  get deviceSupported(): boolean {
    return typeof window !== 'undefined' && 'speechSynthesis' in window;
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);

    // Restored here rather than in the field initialiser: the controller is
    // constructed during the server bundle's module evaluation too, where there
    // is no localStorage to read.
    if (!this.restored && typeof localStorage !== 'undefined') {
      this.restored = true;
      this.rate = readStoredRate();
    }

    // First subscriber wakes the voice list; Safari hands it back asynchronously
    // and reports an empty array until it does.
    this.loadVoices();

    return () => {
      this.listeners.delete(listener);
    };
  }

  getSnapshot(): SpeechSnapshot {
    // useSyncExternalStore compares by identity, so the object is rebuilt only
    // when something actually changed.
    this.snapshot ??= {
      status: this.status,
      engine: this.engine,
      index: this.index,
      total: this.chunks.length,
      position: this.audioPosition,
      duration: this.audioDuration,
      progress: this.computeProgress(),
      rate: this.rate,
      voiceUri: this.voice?.voiceURI ?? null,
      turkishVoices: this.voices.filter(isTurkish),
      ready: this.ready,
      supported: this.supported,
      title: this.title,
      owner: this.owner,
      queueIndex: this.queueIndex,
      queueTotal: this.queueTotal,
    };

    return this.snapshot;
  }

  /** Server render has no engine; the hook needs a stable object anyway. */
  getServerSnapshot(): SpeechSnapshot {
    return SERVER_SNAPSHOT;
  }

  private get audioPosition(): number {
    return this.engine === 'audio' && this.audio ? this.audio.currentTime : 0;
  }

  private get audioDuration(): number {
    // Infinity and NaN both appear before metadata has loaded, and both would
    // render as a bar of unknown width or a time of "NaN:aN".
    const duration = this.engine === 'audio' && this.audio ? this.audio.duration : 0;
    return Number.isFinite(duration) ? duration : 0;
  }

  private computeProgress(): number {
    if (this.engine === 'audio') {
      return this.audioDuration > 0 ? Math.min(1, this.audioPosition / this.audioDuration) : 0;
    }

    return this.chunks.length > 0 ? (this.index + 1) / this.chunks.length : 0;
  }

  setRate(rate: number): void {
    const next = Math.min(MAX_RATE, Math.max(MIN_RATE, rate));
    if (next === this.rate) return;

    this.rate = next;
    remember(RATE_KEY, String(next));

    // A file can simply be played faster, with no gap and nothing repeated.
    if (this.engine === 'audio' && this.audio) {
      this.audio.playbackRate = next;
      this.emit();
      return;
    }

    this.emit();

    // Rate is fixed when an utterance starts and cannot be changed mid-flight, so
    // the current sentence is re-spoken at the new speed. Restarting the sentence
    // is audible; restarting the whole article would be unforgivable.
    if (this.status === 'speaking') {
      this.speakFrom(this.index);
    }
  }

  setVoice(voiceUri: string): void {
    const found = this.voices.find((voice) => voice.voiceURI === voiceUri);
    if (!found || found === this.voice) return;

    this.voice = found;
    remember(VOICE_KEY, found.voiceURI);
    this.emit();

    if (this.status === 'speaking') {
      this.speakFrom(this.index);
    }
  }

  /**
   * Loads a script and starts reading. Must be called from a user gesture — iOS
   * refuses to start speech otherwise, and silently.
   */
  play(
    chunks: SpeechChunk[],
    options: {
      title?: string;
      owner?: string;
      onFinished?: () => void;
      queue?: { index: number; total: number };
      /**
       * Where the server's rendering of this story lives, when there is one.
       * Tried first; the chunks above are what happens if it will not play.
       */
      audioUrl?: string;
    } = {},
  ): void {
    if (chunks.length === 0) return;

    this.loadVoices();
    this.chunks = chunks;
    this.title = options.title ?? null;
    this.owner = options.owner ?? null;
    this.onFinished = options.onFinished ?? null;
    this.queueIndex = options.queue?.index ?? 0;
    this.queueTotal = options.queue?.total ?? 0;

    if (options.audioUrl) {
      this.playAudio(options.audioUrl);
      return;
    }

    if (!this.deviceSupported) return;
    this.engine = 'device';
    this.speakFrom(0);
  }

  /**
   * Plays the server's rendering, falling back to the device voice if it will not.
   *
   * The fallback is the whole reason the script is fetched even when audio is
   * expected: a 404 from a story whose rendering failed, or a day whose allowance
   * ran out, has to become a different voice rather than a dead button — and by
   * then the gesture that authorised playback is long gone, so there is no chance
   * to go and fetch anything.
   */
  private playAudio(url: string): void {
    this.generation++;
    const generation = this.generation;

    window.speechSynthesis?.cancel();
    this.stopKeepAlive();

    const audio = this.ensureAudio();

    this.engine = 'audio';
    this.index = -1;
    this.status = 'speaking';
    this.emit();

    audio.src = url;
    audio.playbackRate = this.rate;
    audio.currentTime = 0;

    void audio.play().catch(() => {
      if (generation !== this.generation) return;
      this.fallBackToDevice();
    });
  }

  private fallBackToDevice(): void {
    if (!this.deviceSupported || this.chunks.length === 0) {
      this.status = 'idle';
      this.emit();
      return;
    }

    this.engine = 'device';
    this.speakFrom(0);
  }

  private ensureAudio(): HTMLAudioElement {
    if (this.audio) return this.audio;

    const audio = new Audio();
    audio.preload = 'auto';

    audio.addEventListener('timeupdate', () => this.emit());
    audio.addEventListener('loadedmetadata', () => {
      this.emit();
      this.publishMediaSession();
    });
    audio.addEventListener('ended', () => {
      if (this.engine !== 'audio') return;
      this.status = 'ended';
      this.emit();
      this.onFinished?.();
    });
    audio.addEventListener('error', () => {
      // A rendering that will not load is the expected shape of "no quota today".
      if (this.engine !== 'audio' || this.status === 'idle') return;
      this.fallBackToDevice();
    });

    this.audio = audio;
    return audio;
  }

  /**
   * Hands the lock screen a title and working buttons.
   *
   * Only ever set for the audio engine. speechSynthesis is not media playback as
   * far as the operating system is concerned — there is no element to attach to —
   * so a MediaSession registered for it would show a card whose controls do
   * nothing.
   */
  private publishMediaSession(): void {
    if (typeof navigator === 'undefined' || !('mediaSession' in navigator)) return;
    if (this.engine !== 'audio') return;

    const session = navigator.mediaSession;

    session.metadata = new MediaMetadata({
      title: this.title ?? 'Focus AI',
      artist: this.queueTotal > 1 ? `${this.queueIndex}/${this.queueTotal} haber` : 'Focus AI',
      album: 'Focus AI',
    });

    session.setActionHandler('play', () => this.resume());
    session.setActionHandler('pause', () => this.pause());
    session.setActionHandler('stop', () => this.stop());
    session.setActionHandler('seekbackward', () => this.seekBy(-15));
    session.setActionHandler('seekforward', () => this.seekBy(15));
  }

  /** Nudges the playhead. Audio engine only; the device voice cannot seek. */
  seekBy(seconds: number): void {
    if (this.engine !== 'audio' || !this.audio) return;

    const duration = this.audioDuration;
    const target = this.audio.currentTime + seconds;
    this.audio.currentTime = Math.max(0, duration > 0 ? Math.min(duration, target) : target);
    this.emit();
  }

  resume(): void {
    if (this.engine === 'audio' && this.audio) {
      // A finished reading starts over; a paused one carries on.
      if (this.status === 'ended') this.audio.currentTime = 0;

      this.status = 'speaking';
      this.emit();
      void this.audio.play().catch(() => this.fallBackToDevice());
      return;
    }

    if (!this.deviceSupported) return;

    if (this.status === 'paused') {
      window.speechSynthesis.resume();
      this.status = 'speaking';
      this.startKeepAlive();
      this.emit();
      return;
    }

    // Finished, or stopped and asked to go again.
    if (this.chunks.length > 0) {
      this.speakFrom(this.status === 'ended' ? 0 : Math.max(0, this.index));
    }
  }

  pause(): void {
    if (this.status !== 'speaking') return;

    if (this.engine === 'audio' && this.audio) {
      this.audio.pause();
      this.status = 'paused';
      this.emit();
      return;
    }

    if (!this.deviceSupported) return;

    window.speechSynthesis.pause();
    this.status = 'paused';
    this.stopKeepAlive();
    this.emit();
  }

  stop(): void {
    this.generation++;

    if (this.audio) {
      this.audio.pause();
      // Released so the browser stops holding the file and the lock-screen card
      // goes away; the element itself is kept, because its gesture permission is.
      this.audio.removeAttribute('src');
      this.audio.load();
    }

    if (typeof navigator !== 'undefined' && 'mediaSession' in navigator) {
      navigator.mediaSession.metadata = null;
      navigator.mediaSession.playbackState = 'none';
    }

    if (this.deviceSupported) {
      window.speechSynthesis.cancel();
    }

    this.stopKeepAlive();

    this.engine = 'device';
    this.status = 'idle';
    this.index = -1;
    this.chunks = [];
    this.title = null;
    this.owner = null;
    this.onFinished = null;
    this.queueIndex = 0;
    this.queueTotal = 0;
    this.emit();
  }

  skip(offset: number): void {
    // The audio engine has no sentences to step through; fifteen seconds is the
    // nearest honest equivalent, and it is what the lock screen offers too.
    if (this.engine === 'audio') {
      this.seekBy(offset * 15);
      return;
    }

    if (this.chunks.length === 0) return;

    const target = this.index + offset;
    if (target < 0 || target >= this.chunks.length) return;

    this.speakFrom(target);
  }

  private speakFrom(start: number): void {
    if (!this.deviceSupported) return;

    this.generation++;
    const generation = this.generation;

    window.speechSynthesis.cancel();

    this.index = start;
    this.status = 'speaking';
    this.emit();

    const speakAt = (position: number) => {
      if (generation !== this.generation) return;

      if (position >= this.chunks.length) {
        this.status = 'ended';
        this.stopKeepAlive();
        this.emit();
        this.onFinished?.();
        return;
      }

      this.index = position;
      this.emit();

      const utterance = new SpeechSynthesisUtterance(this.chunks[position].text);
      utterance.rate = this.rate;
      utterance.lang = 'tr-TR';

      if (this.voice) {
        utterance.voice = this.voice;
      }

      utterance.onend = () => speakAt(position + 1);

      // A failed utterance must not stall the queue. "interrupted" and "canceled"
      // are our own cancel() landing, and the generation guard already handles
      // those; anything else is a real failure worth stepping over.
      utterance.onerror = (event) => {
        if (event.error === 'interrupted' || event.error === 'canceled') return;
        speakAt(position + 1);
      };

      window.speechSynthesis.speak(utterance);
    };

    speakAt(start);
    this.startKeepAlive();
  }

  private startKeepAlive(): void {
    this.stopKeepAlive();

    // Desktop Chrome cuts speech off after about fifteen seconds. A pause
    // immediately followed by a resume resets its timer without dropping audio.
    this.keepAlive = setInterval(() => {
      if (this.status !== 'speaking') return;

      window.speechSynthesis.pause();
      window.speechSynthesis.resume();
    }, KEEP_ALIVE_MS);
  }

  private stopKeepAlive(): void {
    if (this.keepAlive === null) return;

    clearInterval(this.keepAlive);
    this.keepAlive = null;
  }

  /**
   * Re-asks the device for its voices. A voice installed while the page was open
   * does not announce itself, so the reader who just downloaded Turkish needs a
   * way to say "look again" that is not a page reload.
   */
  refreshVoices(): void {
    this.ready = false;
    this.emit();
    this.loadVoices();
  }

  private loadVoices(): void {
    if (!this.deviceSupported) return;

    const apply = () => {
      const voices = window.speechSynthesis.getVoices();
      if (voices.length === 0) return false;

      this.voices = voices;
      this.ready = true;

      // The remembered voice first, then any Turkish one. A stored voice that is
      // no longer installed simply falls through rather than leaving it unset.
      const preferred = typeof localStorage === 'undefined' ? null : localStorage.getItem(VOICE_KEY);

      this.voice ??=
        (preferred ? voices.find((voice) => voice.voiceURI === preferred) : undefined) ??
        voices.find(isTurkish) ??
        null;

      this.emit();
      return true;
    };

    if (apply()) return;

    window.speechSynthesis.addEventListener('voiceschanged', apply, { once: true });

    // iOS does not always fire voiceschanged. A few short polls cost nothing and
    // are the difference between the feature working and appearing unsupported.
    let attempts = 0;
    const poll = setInterval(() => {
      if (apply() || ++attempts >= 10) {
        clearInterval(poll);

        // Out of attempts with nothing to show: stop claiming to be loading, so
        // the UI can say the device has no voices rather than spin forever.
        if (!this.ready) {
          this.ready = true;
          this.emit();
        }
      }
    }, 250);
  }

  private emit(): void {
    this.snapshot = null;

    for (const listener of this.listeners) {
      listener();
    }
  }
}

const SERVER_SNAPSHOT: SpeechSnapshot = {
  status: 'idle',
  engine: 'device',
  index: -1,
  total: 0,
  position: 0,
  duration: 0,
  progress: 0,
  rate: 1,
  voiceUri: null,
  turkishVoices: [],
  ready: false,
  supported: false,
  title: null,
  owner: null,
  queueIndex: 0,
  queueTotal: 0,
};

export const speech = new SpeechController();

const subscribe = (listener: () => void) => speech.subscribe(listener);
const getSnapshot = () => speech.getSnapshot();
const getServerSnapshot = () => speech.getServerSnapshot();

/** Subscribes a component to the one engine. */
export function useSpeech(): SpeechSnapshot {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}

if (typeof window !== 'undefined') {
  // Navigating away must silence it. Without this the voice reads on over the
  // next page, and on iOS it survives the tab going to the background.
  window.addEventListener('pagehide', () => speech.stop());
  window.addEventListener('beforeunload', () => speech.stop());
}
