import { computed, Injectable, OnDestroy, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { MeetingState } from './meeting.models';
import { MeetingService } from './meeting.service';

@Injectable()
export class MeetingSyncService implements OnDestroy {
  private connection: HubConnection | null = null;
  private meetingId = '';
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshing = false;
  private refreshAgain = false;

  readonly snapshot = signal<MeetingState | null>(null);
  readonly revoked = signal(false);
  readonly error = signal<string | null>(null);
  readonly connectionState = signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected');
  readonly canModify = computed(() => this.connectionState() === 'connected' && !this.revoked());

  constructor(private readonly meetings: MeetingService) {}

  async connect(meetingId: string): Promise<void> {
    this.meetingId = meetingId;
    this.revoked.set(false);
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/retro')
      .withAutomaticReconnect()
      .build();
    this.connection = connection;

    connection.on('StateChanged', (id: string) => this.scheduleRefresh(id));
    connection.on('PresenceChanged', (id: string) => this.scheduleRefresh(id));
    connection.on('AccessRevoked', (id: string) => {
      if (id === this.meetingId) {
        this.revoked.set(true);
        this.scheduleRefresh(id);
      }
    });
    connection.onreconnecting(() => this.connectionState.set('reconnecting'));
    connection.onreconnected(async () => {
      try {
        await connection.invoke('Subscribe', this.meetingId);
        this.connectionState.set('connected');
        await this.refresh();
      } catch {
        this.connectionState.set('reconnecting');
      }
    });
    connection.onclose(() => this.connectionState.set('disconnected'));

    await connection.start();
    await connection.invoke('Subscribe', meetingId);
    this.connectionState.set('connected');
    await this.refresh();
    this.pollTimer = setInterval(() => void this.refresh(), 15_000);
  }

  replaceSnapshot(snapshot: MeetingState): void {
    const current = this.snapshot();
    if (!current || snapshot.version >= current.version) {
      this.snapshot.set(snapshot);
    }
  }

  async refresh(): Promise<void> {
    if (this.refreshing) {
      this.refreshAgain = true;
      return;
    }
    if (!this.meetingId || this.revoked()) {
      return;
    }

    this.refreshing = true;
    try {
      const snapshot = await firstValueFrom(this.meetings.state(this.meetingId));
      this.replaceSnapshot(snapshot);
      this.error.set(null);
    } catch (error) {
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

  async ngOnDestroy(): Promise<void> {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
    }
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
    }
    if (this.connection) {
      await this.connection.stop();
    }
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
