import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { apiErrorMessage } from './api-error';
import { MeetingPhase, MeetingSummary } from './meeting.models';
import { MeetingService } from './meeting.service';
import { WorkspaceLayout } from './workspace-layout';

@Component({
  selector: 'app-meetings-page',
  imports: [DatePipe, NgTemplateOutlet, RouterLink, WorkspaceLayout],
  templateUrl: './meetings-page.html',
  styleUrl: './meetings-page.scss',
})
export class MeetingsPage {
  private readonly meetingsService = inject(MeetingService);
  readonly meetings = signal<MeetingSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly archive = signal<MeetingSummary[]>([]);
  readonly archiveLoading = signal(false);
  readonly archiveError = signal<string | null>(null);
  readonly meetingGroups = computed(() => {
    const meetings = this.meetings();
    return [
      { title: 'Активные', meetings: meetings.filter((meeting) => meeting.phase !== 'Closed') },
      { title: 'Завершённые', meetings: meetings.filter((meeting) => meeting.phase === 'Closed') },
    ].filter((group) => group.meetings.length > 0);
  });

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

  loadArchive(expanded: boolean): void {
    if (!expanded || this.archiveLoading()) {
      return;
    }

    this.archiveLoading.set(true);
    this.archiveError.set(null);
    this.meetingsService.listArchived().subscribe({
      next: (meetings) => {
        this.archive.set(meetings);
        this.archiveLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.archiveError.set(apiErrorMessage(error));
        this.archiveLoading.set(false);
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
}
