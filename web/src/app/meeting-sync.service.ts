import { computed, HostListener, inject, Injectable, isDevMode, OnDestroy, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { MeetingState } from './meeting.models';
import { MeetingService } from './meeting.service';

@Injectable()
export class MeetingSyncService implements OnDestroy {
  private connection: HubConnection | null = null;
  private meetingId = '';
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private clockTimer: ReturnType<typeof setInterval> | null = null;
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshing = false;
  private refreshAgain = false;
  private clockOffset = 0;
  private connectionGeneration = 0;
  private subscribed = false;

  readonly snapshot = signal<MeetingState | null>(null);
  readonly revoked = signal(false);
  readonly error = signal<string | null>(null);
  readonly expired = signal<{ runId: string; phase: string } | null>(null);
  readonly now = signal(Date.now());
  readonly connectionState = signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected');
  readonly canModify = computed(() => this.connectionState() === 'connected' && !this.revoked());
  readonly timerRemaining = computed(() => {
    const timer = this.snapshot()?.timer;
    if (!timer) {
      return null;
    }
    return Math.max(0, Math.ceil((Date.parse(timer.endsAtUtc) - this.now() - this.clockOffset) / 1000));
  });

  constructor(private readonly meetings: MeetingService) {}

  async connect(meetingId: string): Promise<void> {
    this.meetingId = meetingId;
    this.revoked.set(false);
    const hubUrl = isDevMode()
      ? `${window.location.protocol}//${window.location.hostname}:5166/hubs/retro`
      : '/hubs/retro';
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();
    this.connection = connection;

    connection.on('StateChanged', (id: string) => this.scheduleRefresh(id));
    connection.on('BallotChanged', (id: string) => this.scheduleRefresh(id));
    connection.on('PresenceChanged', (id: string) => this.scheduleRefresh(id));
    connection.on('AccessRevoked', (id: string) => {
      if (id === this.meetingId) {
        this.revoked.set(true);
        this.scheduleRefresh(id);
      }
    });
    connection.on('TimerExpired', (id: string, runId: string, phase: string) => {
      if (id === this.meetingId && this.snapshot()?.phase === phase) {
        this.expired.set({ runId, phase });
      }
    });
    connection.onreconnecting(() => this.invalidateConnection('reconnecting'));
    connection.onreconnected(async () => {
      try {
        await this.subscribe(connection);
      } catch {
        // Keep editing locked if the subscription could not be restored.
      }
    });
    connection.onclose(() => this.invalidateConnection('disconnected'));

    await connection.start();
    await this.subscribe(connection);
    this.pollTimer = setInterval(() => void this.refresh(), 15_000);
    this.clockTimer = setInterval(() => this.now.set(Date.now()), 1000);
  }

  replaceSnapshot(snapshot: MeetingState): boolean {
    const current = this.snapshot();
    if (!current || snapshot.version >= current.version) {
      const receivedAt = Date.now();
      this.clockOffset = Date.parse(snapshot.serverNowUtc) - receivedAt;
      this.now.set(receivedAt);
      const expired = this.expired();
      if (expired && (expired.phase !== snapshot.phase || (snapshot.timer && expired.runId !== snapshot.timer.runId))) {
        this.expired.set(null);
      }
      this.snapshot.set(snapshot);
      return true;
    }

    return false;
  }

  dismissExpired(): void {
    this.expired.set(null);
  }

  async refresh(): Promise<void> {
    if (this.refreshing) {
      this.refreshAgain = true;
      return;
    }
    if (!this.meetingId || this.revoked()) {
      return;
    }

    const generation = this.connectionGeneration;
    const subscribed = this.subscribed;
    this.refreshing = true;
    try {
      const snapshot = await firstValueFrom(this.meetings.state(this.meetingId));
      if (generation !== this.connectionGeneration || this.revoked()) {
        return;
      }
      const accepted = this.replaceSnapshot(snapshot);
      if (accepted && subscribed) {
        this.connectionState.set('connected');
      }
      this.error.set(null);
    } catch (error) {
      if (generation !== this.connectionGeneration) {
        return;
      }
      const status = (error as { status?: number }).status;
      if (status === 403) {
        this.revoked.set(true);
      } else {
        this.error.set('Не удалось обновить состояние встречи.');
      }
    } finally {
      this.refreshing = false;
      if (this.refreshAgain) {
        this.refreshAgain = false;
        this.scheduleRefresh(this.meetingId);
      }
    }
  }

  @HostListener('window:pagehide')
  onPageHide(): void {
    const connection = this.connection;
    this.connection = null;
    if (connection) {
      void connection.stop();
    }
  }

  async ngOnDestroy(): Promise<void> {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
    }
    if (this.clockTimer) {
      clearInterval(this.clockTimer);
    }
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    if (this.connection) {
      await this.connection.stop();
    }
  }

  private async subscribe(connection: HubConnection): Promise<void> {
    const generation = this.connectionGeneration;
    await connection.invoke('Subscribe', this.meetingId);
    if (connection !== this.connection || generation !== this.connectionGeneration) {
      return;
    }

    this.subscribed = true;
    await this.refresh();
  }

  private invalidateConnection(state: 'reconnecting' | 'disconnected'): void {
    this.connectionGeneration++;
    this.subscribed = false;
    this.connectionState.set(state);
  }

  private scheduleRefresh(meetingId: string): void {
    if (meetingId !== this.meetingId) {
      return;
    }
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    this.debounceTimer = setTimeout(() => void this.refresh(), 100);
  }
}
