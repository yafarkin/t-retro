import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorMessage } from './api-error';
import { MeetingForm } from './meeting-form';
import { MeetingService } from './meeting.service';
import { defaultColumns, defaultVoteSettings, MeetingSettingsPayload } from './meeting.models';

@Component({
  selector: 'app-create-meeting-page',
  imports: [MeetingForm, RouterLink],
  templateUrl: './create-meeting-page.html',
  styleUrl: './create-meeting-page.scss',
})
export class CreateMeetingPage {
  private readonly meetings = inject(MeetingService);
  private readonly router = inject(Router);

  readonly name = signal('Ретро команды');
  readonly columns = signal(defaultColumns());
  readonly anonymousCards = signal(true);
  readonly privateCollection = signal(true);
  readonly anonymousVotes = signal(true);
  readonly voteSettings = signal(defaultVoteSettings());
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  create(): void {
    this.busy.set(true);
    this.error.set(null);
    this.meetings.create(this.payload()).subscribe({
      next: (result) => this.router.navigateByUrl(`/meetings/${result.id}`),
      error: (error: HttpErrorResponse) => {
        this.error.set(apiErrorMessage(error));
        this.busy.set(false);
      },
    });
  }

  private payload(): MeetingSettingsPayload {
    return {
      name: this.name(),
      columns: this.columns(),
      anonymousCards: this.anonymousCards(),
      privateCollection: this.privateCollection(),
      anonymousVotes: this.anonymousVotes(),
      voteSettings: this.voteSettings(),
    };
  }
}
