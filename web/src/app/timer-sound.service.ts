import { Injectable, OnDestroy, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TimerSoundService implements OnDestroy {
  private audioContext: AudioContext | null = null;
  readonly enabled = signal(false);
  readonly error = signal(false);

  async enableAndTest(): Promise<void> {
    try {
      this.audioContext ??= new AudioContext();
      await this.audioContext.resume();
      this.enabled.set(true);
      this.error.set(false);
      this.playTone();
    } catch {
      this.enabled.set(false);
      this.error.set(true);
    }
  }

  async playExpired(meetingId: string, runId: string): Promise<void> {
    if (!this.enabled()) {
      return;
    }
    const key = `t-retro.timer-expired.${meetingId}.${runId}`;
    if (navigator.locks) {
      await navigator.locks.request(key, { ifAvailable: true }, async (lock) => {
        if (lock) {
          this.playOnce(key);
        }
      });
    } else {
      this.playOnce(key);
    }
  }

  async ngOnDestroy(): Promise<void> {
    if (this.audioContext) {
      await this.audioContext.close();
    }
  }

  private playOnce(key: string): void {
    if (localStorage.getItem(key) === 'played') {
      return;
    }
    localStorage.setItem(key, 'played');
    this.playTone();
  }

  private playTone(): void {
    if (!this.audioContext) {
      return;
    }
    const oscillator = this.audioContext.createOscillator();
    oscillator.type = 'sine';
    oscillator.frequency.value = 880;
    oscillator.connect(this.audioContext.destination);
    oscillator.start();
    oscillator.stop(this.audioContext.currentTime + 0.25);
  }
}
