import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TuiButton } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { MeetingForm } from './meeting-form';
import {
  defaultColumns,
  defaultVoteSettings,
  MeetingCard,
  MeetingPhase,
  MeetingSettingsPayload,
  MeetingState,
  MeetingTemplate,
} from './meeting.models';
import { MeetingService } from './meeting.service';
import { MeetingSyncService } from './meeting-sync.service';
import { TemplateService } from './template.service';

@Component({
  selector: 'app-meeting-settings-page',
  imports: [FormsModule, MeetingForm, RouterLink, TuiButton],
  providers: [MeetingSyncService],
  templateUrl: './meeting-settings-page.html',
  styleUrl: './meeting-settings-page.scss',
})
export class MeetingSettingsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly meetings = inject(MeetingService);
  private readonly templatesService = inject(TemplateService);
  private readonly meetingId = this.route.snapshot.paramMap.get('id')!;
  readonly sync = inject(MeetingSyncService);

  readonly state = this.sync.snapshot;
  readonly revoked = this.sync.revoked;
  readonly templates = signal<MeetingTemplate[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly commandsBlocked = computed(() => this.busy() || !this.sync.canModify());
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly selectedTemplate = signal('');
  readonly selectedMember = signal('');
  readonly invite = signal<{ url: string; version: number } | null>(null);
  readonly copied = signal(false);
  readonly newCardTexts = signal<Record<string, string>>({});
  readonly editingCardId = signal<string | null>(null);
  readonly editingText = signal('');
  readonly otherParticipants = computed(() =>
    this.state()?.participants.filter((participant) =>
      participant.status === 'Active' && participant.id !== this.state()?.facilitatorId) ?? []);

  readonly name = signal('');
  readonly columns = signal(defaultColumns());
  readonly anonymousCards = signal(true);
  readonly privateCollection = signal(true);
  readonly anonymousVotes = signal(true);
  readonly voteSettings = signal(defaultVoteSettings());
  private formVersion = 0;

  constructor() {
    effect(() => {
      const incoming = this.sync.snapshot();
      if (!incoming) {
        return;
      }
      this.loading.set(false);
      if (this.formVersion === 0 || incoming.version > this.formVersion) {
        this.formVersion = incoming.version;
        this.applyState(incoming);
      }
    });
    this.templatesService.list().subscribe({
      next: (templates) => this.templates.set(templates),
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
    void this.connect();
  }

  save(): void {
    const current = this.state();
    if (!current || this.commandsBlocked()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.update(this.meetingId, current.version, this.payload()).subscribe({
      next: (state) => {
        this.sync.replaceSnapshot(state);
        this.busy.set(false);
        this.notice.set('Настройки сохранены.');
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
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

  excludeMember(): void {
    const participant = this.otherParticipants().find((item) => item.id === this.selectedMember());
    if (!participant || !window.confirm(`Исключить участника «${participant.displayName}»?`)) {
      return;
    }

    this.meetings.exclude(this.meetingId, participant.id).subscribe({
      next: () => {
        this.selectedMember.set('');
        this.notice.set('Участник исключён.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  transferRole(): void {
    const participant = this.otherParticipants().find((item) => item.id === this.selectedMember());
    if (!participant || !window.confirm(`Передать роль участнику «${participant.displayName}»?`)) {
      return;
    }

    this.meetings.transfer(this.meetingId, participant.id).subscribe({
      next: () => {
        this.selectedMember.set('');
        this.notice.set('Роль фасилитатора передана.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  newCardText(columnId: string): string {
    return this.newCardTexts()[columnId] ?? '';
  }

  setNewCardText(columnId: string, text: string): void {
    this.newCardTexts.update((texts) => ({ ...texts, [columnId]: text }));
  }

  createCard(columnId: string): void {
    if (this.commandsBlocked()) {
      return;
    }
    const text = this.newCardText(columnId);
    if (!text.trim()) {
      this.error.set('Введите текст карточки.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.createCard(this.meetingId, columnId, text).subscribe({
      next: () => {
        this.setNewCardText(columnId, '');
        this.busy.set(false);
        this.notice.set('Карточка добавлена.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  startEdit(card: MeetingCard): void {
    this.editingCardId.set(card.id);
    this.editingText.set(card.text ?? '');
    this.error.set(null);
  }

  cancelEdit(): void {
    this.editingCardId.set(null);
    this.editingText.set('');
  }

  saveCard(card: MeetingCard): void {
    if (this.commandsBlocked()) {
      return;
    }
    const text = this.editingText();
    if (!text.trim()) {
      this.error.set('Введите текст карточки.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.meetings.updateCard(this.meetingId, card.id, text, card.version).subscribe({
      next: () => {
        this.cancelEdit();
        this.busy.set(false);
        this.notice.set('Карточка обновлена.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  deleteCard(card: MeetingCard): void {
    if (this.commandsBlocked() || !window.confirm('Удалить эту карточку?')) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.meetings.deleteCard(this.meetingId, card.id, card.version).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set('Карточка удалена.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  setReady(ready: boolean): void {
    const current = this.state();
    if (!current || this.commandsBlocked()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.meetings.setReady(this.meetingId, current.phase, ready).subscribe({
      next: () => {
        this.busy.set(false);
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => {
        this.commandFailed(error);
        void this.sync.refresh();
      },
    });
  }

  advance(): void {
    const current = this.state();
    if (!current || this.commandsBlocked()) {
      return;
    }
    if (current.phase === 'Discuss' && !window.confirm('Завершить ретро?')) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.advance(this.meetingId, current.phase, current.version).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set('Встреча переведена на следующую фазу.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  cardsForColumn(columnId: string): MeetingCard[] {
    return this.state()?.cards.filter((card) => card.columnId === columnId) ?? [];
  }

  readinessText(): string {
    const readiness = this.state()?.readiness;
    if (!readiness) {
      return '';
    }
    const count = `${readiness.readyCount}/${readiness.totalCount}`;
    return readiness.readyCount === readiness.totalCount && readiness.totalCount > 0
      ? `Все готовы (${count})`
      : `Готово ${count}`;
  }

  phaseLabel(phase: MeetingPhase): string {
    const labels: Record<MeetingPhase, string> = {
      Draft: 'Подготовка',
      Collect: 'Сбор карточек',
      Group: 'Группировка',
      Vote: 'Голосование',
      Discuss: 'Обсуждение',
      Closed: 'Завершена',
    };
    return labels[phase];
  }

  advanceLabel(phase: MeetingPhase): string {
    const labels: Record<MeetingPhase, string> = {
      Draft: 'Начать сбор',
      Collect: 'Перейти к группировке',
      Group: 'Открыть голосование',
      Vote: 'Перейти к обсуждению',
      Discuss: 'Завершить ретро',
      Closed: 'Встреча завершена',
    };
    return labels[phase];
  }

  connectionText(): string {
    const states = {
      connected: 'Соединение активно',
      reconnecting: 'Восстанавливаем соединение',
      disconnected: 'Нет соединения',
    };
    return states[this.sync.connectionState()];
  }

  private async connect(): Promise<void> {
    try {
      await this.sync.connect(this.meetingId);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Не удалось подключиться к встрече.');
      this.loading.set(false);
    }
  }

  private commandFailed(error: HttpErrorResponse): void {
    this.error.set(apiErrorMessage(error));
    this.busy.set(false);
  }

  private applyState(state: MeetingState): void {
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
