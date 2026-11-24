export type BeepTone = 'ok' | 'error' | 'rework';

interface BeepProfile {
  frequency: number;
  duration: number;
  gain: number;
  sweepTo?: number;
}

const PROFILES: Record<BeepTone, BeepProfile> = {
  ok: { frequency: 980, duration: 0.09, gain: 0.08 },
  error: { frequency: 240, duration: 0.26, gain: 0.12, sweepTo: 140 },
  rework: { frequency: 640, duration: 0.16, gain: 0.09, sweepTo: 820 },
};

let audioContext: AudioContext | null = null;

function resolveContext(): AudioContext | null {
  if (typeof window === 'undefined') return null;
  if (audioContext) return audioContext;
  const legacyWindow = window as unknown as { webkitAudioContext?: typeof AudioContext };
  const AudioContextCtor = window.AudioContext ?? legacyWindow.webkitAudioContext;
  if (!AudioContextCtor) return null;
  audioContext = new AudioContextCtor();
  return audioContext;
}

export function beep(tone: BeepTone = 'ok'): void {
  const context = resolveContext();
  if (!context) return;
  if (context.state === 'suspended') void context.resume();

  const profile = PROFILES[tone];
  const oscillator = context.createOscillator();
  const gain = context.createGain();
  const now = context.currentTime;

  oscillator.connect(gain);
  gain.connect(context.destination);
  oscillator.type = tone === 'error' ? 'square' : 'sine';
  oscillator.frequency.setValueAtTime(profile.frequency, now);
  if (profile.sweepTo) {
    oscillator.frequency.linearRampToValueAtTime(profile.sweepTo, now + profile.duration);
  }
  gain.gain.setValueAtTime(profile.gain, now);
  gain.gain.exponentialRampToValueAtTime(0.0001, now + profile.duration);

  oscillator.start(now);
  oscillator.stop(now + profile.duration + 0.02);
}
