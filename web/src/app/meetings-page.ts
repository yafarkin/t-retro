import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TuiButton } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { AuthService } from './auth.service';
import { MeetingPhase, MeetingSummary } from './meeting.models';
import { MeetingService } from './meeting.service';

@Component({
  selector: 'app-meetings-page',
  imports: [DatePipe, RouterLink, TuiButton],
  templateUrl: './meetings-page.html',
  styleUrl: './meetings-page.scss',
})
export class MeetingsPage {
  private readonly meetingsService = inject(MeetingService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly meetings = signal<MeetingSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly active = computed(() => this.meetings().filter((meeting) => meeting.phase !== 'Closed'));
  readonly closed = computed(() => this.meetings().filter((meeting) => meeting.phase === 'Closed'));

  constructor() {
    this.meetingsService.list().subscribe({
      next: (meetings) => {
        this.meetings.set(meetings);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(apiErrorMessage(error));
        this.loading.set(false);
      },
    });
  }

  phaseName(phase: MeetingPhase): string {
    const names: Record<MeetingPhase, string> = {
      Draft: 'Подготовка',
      Collect: 'Сбор',
      Group: 'Группировка',
      Vote: 'Голосование',
      Discuss: 'Обсуждение',
      Closed: 'Завершена',
    };
    return names[phase];
  }

  logout(): void {
    this.auth.logout().subscribe(() => this.router.navigateByUrl('/login'));
  }
}
