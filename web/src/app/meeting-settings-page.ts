import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TuiButton } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { MeetingForm } from './meeting-form';
import {
  defaultColumns,
  defaultVoteSettings,
  MeetingSettingsPayload,
  MeetingState,
  MeetingTemplate,
} from './meeting.models';
import { MeetingService } from './meeting.service';
import { TemplateService } from './template.service';

@Component({
  selector: 'app-meeting-settings-page',
  imports: [FormsModule, MeetingForm, RouterLink, TuiButton],
  templateUrl: './meeting-settings-page.html',
  styleUrl: './meeting-settings-page.scss',
})
export class MeetingSettingsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly meetings = inject(MeetingService);
  private readonly templatesService = inject(TemplateService);
  private readonly meetingId = this.route.snapshot.paramMap.get('id')!;

  readonly state = signal<MeetingState | null>(null);
  readonly templates = signal<MeetingTemplate[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly selectedTemplate = signal('');
  readonly invite = signal<{ url: string; version: number } | null>(null);
  readonly copied = signal(false);

  readonly name = signal('');
  readonly columns = signal(defaultColumns());
  readonly anonymousCards = signal(true);
  readonly privateCollection = signal(true);
  readonly anonymousVotes = signal(true);
  readonly voteSettings = signal(defaultVoteSettings());

  constructor() {
    this.load();
    this.templatesService.list().subscribe({
      next: (templates) => this.templates.set(templates),
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  save(): void {
    const current = this.state();
    if (!current) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.update(this.meetingId, current.version, this.payload()).subscribe({
      next: (state) => {
        this.applyState(state);
        this.busy.set(false);
        this.notice.set('Настройки сохранены.');
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(apiErrorMessage(error));
        this.busy.set(false);
      },
    });
  }

  applyTemplate(): void {
    const template = this.templates().find((item) => item.id === this.selectedTemplate());
    if (!template) {
      return;
    }
    if (!window.confirm('Заменить текущие колонки колонками шаблона?')) {
      return;
    }

    this.columns.set(template.columns.map((column) => ({ name: column.name, color: column.color })));
    this.selectedTemplate.set('');
    this.notice.set('Колонки шаблона применены к черновику. Сохраните настройки.');
  }

  saveAsTemplate(): void {
    const name = window.prompt('Название шаблона', this.name());
    if (!name) {
      return;
    }

    this.templatesService.create({
      name,
      visibility: 'personal',
      columns: this.columns(),
    }).subscribe({
      next: () => this.notice.set('Текущие колонки сохранены в личный шаблон.'),
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  loadInvite(): void {
    this.error.set(null);
    this.meetings.invite(this.meetingId).subscribe({
      next: (invite) => this.invite.set(invite),
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  rotateInvite(): void {
    if (!window.confirm('Отозвать прежнюю ссылку и создать новую?')) {
      return;
    }

    this.meetings.rotateInvite(this.meetingId).subscribe({
      next: (invite) => {
        this.invite.set(invite);
        this.copied.set(false);
      },
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  copyInvite(): void {
    const invite = this.invite();
    if (!invite) {
      return;
    }

    navigator.clipboard.writeText(invite.url).then(() => this.copied.set(true));
  }

  private load(): void {
    this.meetings.state(this.meetingId).subscribe({
      next: (state) => {
        this.applyState(state);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(apiErrorMessage(error));
        this.loading.set(false);
      },
    });
  }

  private applyState(state: MeetingState): void {
    this.state.set(state);
    this.name.set(state.name);
    this.columns.set(state.columns.map((column) => ({ name: column.name, color: column.color })));
    this.anonymousCards.set(state.anonymousCards);
    this.privateCollection.set(state.privateCollection);
    this.anonymousVotes.set(state.anonymousVotes);
    this.voteSettings.set({ ...state.voteSettings });
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
