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
 * The browser's own API is used rather than a server-side engine because it is
 * the only option that works where this app is actually read — a plain-HTTP
 * Tailscale address, which is not a secure context. Unlike the clipboard and the
 * service worker, `speechSynthesis` carries no secure-context requirement, so it
 * is available where the alternatives are not. The trade-off is that playback
 * stops when the screen locks: this is not an <audio> element, so there is no
 * MediaSession and no background audio.
 */

export type SpeechStatus = 'idle' | 'speaking' | 'paused' | 'ended';

export interface SpeechSnapshot {
  status: SpeechStatus;
  /** Index of the utterance being spoken, or -1 when idle. */
  index: number;
  total: number;
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

  get supported(): boolean {
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
      index: this.index,
      total: this.chunks.length,
      rate: this.rate,
      voiceUri: this.voice?.voiceURI ?? null,
      turkishVoices: this.voices.filter(isTurkish),
      ready: this.ready,
      supported: this.supported,
      title: this.title,
      owner: this.owner,
    };

    return this.snapshot;
  }

  /** Server render has no engine; the hook needs a stable object anyway. */
  getServerSnapshot(): SpeechSnapshot {
    return SERVER_SNAPSHOT;
  }

  setRate(rate: number): void {
    const next = Math.min(MAX_RATE, Math.max(MIN_RATE, rate));
    if (next === this.rate) return;

    this.rate = next;
    remember(RATE_KEY, String(next));
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
    options: { title?: string; owner?: string; onFinished?: () => void } = {},
  ): void {
    if (!this.supported || chunks.length === 0) return;

    this.loadVoices();
    this.chunks = chunks;
    this.title = options.title ?? null;
    this.owner = options.owner ?? null;
    this.onFinished = options.onFinished ?? null;
    this.speakFrom(0);
  }

  resume(): void {
    if (!this.supported) return;

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
    if (!this.supported || this.status !== 'speaking') return;

    window.speechSynthesis.pause();
    this.status = 'paused';
    this.stopKeepAlive();
    this.emit();
  }

  stop(): void {
    if (!this.supported) return;

    this.generation++;
    window.speechSynthesis.cancel();
    this.stopKeepAlive();

    this.status = 'idle';
    this.index = -1;
    this.chunks = [];
    this.title = null;
    this.owner = null;
    this.onFinished = null;
    this.emit();
  }

  skip(offset: number): void {
    if (this.chunks.length === 0) return;

    const target = this.index + offset;
    if (target < 0 || target >= this.chunks.length) return;

    this.speakFrom(target);
  }

  private speakFrom(start: number): void {
    if (!this.supported) return;

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
    if (!this.supported) return;

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
  index: -1,
  total: 0,
  rate: 1,
  voiceUri: null,
  turkishVoices: [],
  ready: false,
  supported: false,
  title: null,
  owner: null,
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
