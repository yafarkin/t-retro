import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, effect, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { TuiButton } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { MeetingForm } from './meeting-form';
import { MeetingInvite } from './meeting-invite';
import { MeetingTimerControls } from './meeting-timer-controls';
import {
  ActionItem,
  copyEditableColumns,
  defaultColumns,
  defaultVoteSettings,
  DiscussionStatus,
  GroupProposal,
  MeetingCard,
  MeetingGroup,
  MeetingPhase,
  MeetingSettingsPayload,
  MeetingState,
  MeetingTemplate,
  MeetingTopic,
  TopicComment,
  TopicDecision,
} from './meeting.models';
import { MeetingService } from './meeting.service';
import { MeetingSyncService } from './meeting-sync.service';
import { TemplateService } from './template.service';

@Component({
  selector: 'app-meeting-settings-page',
  imports: [FormsModule, MeetingForm, MeetingInvite, MeetingTimerControls, RouterLink, TuiButton],
  providers: [MeetingSyncService],
  templateUrl: './meeting-settings-page.html',
  styleUrls: ['./meeting-settings-page.scss', './meeting-board.scss'],
})
export class MeetingSettingsPage {
  private readonly injector = inject(Injector);
  private readonly newCardInput = viewChild<ElementRef<HTMLTextAreaElement>>('newCardInput');
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
  readonly cardComposerColumn = signal<string | null>(null);
  readonly newCardTexts = signal<Record<string, string>>({});
  readonly newCardAnonymity = signal<Record<string, boolean>>({});
  readonly editingCardId = signal<string | null>(null);
  readonly editingText = signal('');
  readonly proposal = signal<GroupProposal | null>(null);
  readonly moveTargets = signal<Record<string, string>>({});
  readonly groupNames = signal<Record<string, string>>({});
  readonly newCommentText = signal('');
  readonly newDecisionText = signal('');
  readonly newActionText = signal('');
  readonly newActionAssignee = signal('');
  readonly editingEntryKind = signal<'comments' | 'decisions' | 'actions' | null>(null);
  readonly editingEntryId = signal<string | null>(null);
  readonly editingEntryText = signal('');
  readonly editingActionAssignee = signal('');
  readonly activeParticipants = computed(() =>
    this.state()?.participants.filter((participant) => participant.status === 'Active') ?? []);
  readonly otherParticipants = computed(() =>
    this.activeParticipants().filter((participant) => participant.id !== this.state()?.facilitatorId));

  readonly name = signal('');
  readonly columns = signal(defaultColumns());
  readonly anonymousCards = signal(true);
  readonly privateCollection = signal(true);
  readonly anonymousVotes = signal(true);
  readonly voteSettings = signal(defaultVoteSettings());
  readonly votingEnabled = signal(true);
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

    this.columns.set(copyEditableColumns(template.columns));
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

  openCardComposer(columnId: string): void {
    this.cardComposerColumn.set(columnId);
    afterNextRender(() => this.newCardInput()?.nativeElement.focus(), { injector: this.injector });
  }

  cancelNewCard(columnId: string): void {
    this.setNewCardText(columnId, '');
    this.setNewCardAnonymity(columnId, false);
    this.cardComposerColumn.set(null);
  }

  newCardText(columnId: string): string {
    return this.newCardTexts()[columnId] ?? '';
  }

  setNewCardText(columnId: string, text: string): void {
    this.newCardTexts.update((texts) => ({ ...texts, [columnId]: text }));
  }

  setNewCardAnonymity(columnId: string, anonymous: boolean): void {
    this.newCardAnonymity.update((choices) => ({ ...choices, [columnId]: anonymous }));
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
    this.meetings.createCard(this.meetingId, columnId, text, this.newCardAnonymity()[columnId] ?? false).subscribe({
      next: () => {
        this.cancelNewCard(columnId);
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
    return this.state()?.cards.filter((card) => card.columnId === columnId && card.groupId === null) ?? [];
  }

  groupsForColumn(columnId: string): MeetingGroup[] {
    return this.state()?.groups.filter((group) => group.columnId === columnId) ?? [];
  }

  cardsForGroup(groupId: string): MeetingCard[] {
    return this.state()?.cards.filter((card) => card.groupId === groupId) ?? [];
  }

  moveOptions(sourceKind: string, sourceId: string): { value: string; label: string }[] {
    const meeting = this.state();
    if (!meeting) {
      return [];
    }
    const columns = meeting.columns
      .map((column) => ({ value: `column:${column.id}`, label: `Колонка: ${column.name}` }));
    if (sourceKind === 'group') {
      return columns;
    }
    const cards = meeting.cards
      .filter((card) => card.id !== sourceId)
      .map((card) => ({
        value: `card:${card.id}`,
        label: `Карточка: ${(card.text ?? '').slice(0, 60)}`,
      }));
    const groups = meeting.groups
      .map((group) => ({ value: `group:${group.id}`, label: `Группа: ${group.name}` }));
    return [...columns, ...cards, ...groups];
  }

  selectedMoveTarget(sourceKind: string, sourceId: string): string {
    return this.moveTargets()[`${sourceKind}:${sourceId}`] ?? '';
  }

  setMoveTarget(sourceKind: string, sourceId: string, target: string): void {
    this.moveTargets.update((targets) => ({ ...targets, [`${sourceKind}:${sourceId}`]: target }));
  }

  move(sourceKind: string, sourceId: string): void {
    const target = this.selectedMoveTarget(sourceKind, sourceId);
    if (!target) {
      return;
    }
    const separator = target.indexOf(':');
    this.executeMove(sourceKind, sourceId, target.slice(0, separator), target.slice(separator + 1));
  }

  startDrag(event: DragEvent, sourceKind: string, sourceId: string): void {
    event.stopPropagation();
    if (this.commandsBlocked()) {
      event.preventDefault();
      return;
    }
    event.dataTransfer?.setData('text/plain', `${sourceKind}:${sourceId}`);
  }

  allowDrop(event: DragEvent): void {
    event.preventDefault();
  }

  drop(event: DragEvent, targetKind: string, targetId: string): void {
    event.preventDefault();
    event.stopPropagation();
    const source = event.dataTransfer?.getData('text/plain') ?? '';
    const separator = source.indexOf(':');
    if (separator < 0) {
      return;
    }
    this.executeMove(source.slice(0, separator), source.slice(separator + 1), targetKind, targetId);
  }

  renameGroup(group: MeetingGroup): void {
    const current = this.state();
    const name = this.groupNames()[group.id] ?? group.name;
    if (!current || this.commandsBlocked() || !name.trim()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.meetings.renameGroup(this.meetingId, group.id, name, current.boardVersion).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set('Название группы изменено.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  setGroupName(groupId: string, name: string): void {
    this.groupNames.update((names) => ({ ...names, [groupId]: name }));
  }

  previewGroups(): void {
    if (this.commandsBlocked()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.meetings.previewGroups(this.meetingId).subscribe({
      next: (proposal) => {
        this.proposal.set(proposal);
        this.busy.set(false);
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  applyProposal(): void {
    const current = this.state();
    const proposal = this.proposal();
    if (!current || !proposal || this.commandsBlocked()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.meetings.applyGroups(this.meetingId, proposal.id, current.boardVersion).subscribe({
      next: () => {
        this.proposal.set(null);
        this.busy.set(false);
        this.notice.set('Предложение применено.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  cancelProposal(): void {
    this.proposal.set(null);
  }

  proposalCards(cardIds: string[]): MeetingCard[] {
    const ids = new Set(cardIds);
    return this.state()?.cards.filter((card) => ids.has(card.id)) ?? [];
  }

  isTimerPhase(phase: MeetingPhase): boolean {
    return phase === 'Collect' || phase === 'Group' || phase === 'Vote' || phase === 'Discuss';
  }

  startTimer(seconds: number): void {
    const current = this.state();
    if (!current || seconds <= 0 || this.commandsBlocked()) {
      return;
    }
    this.executeTimerCommand(
      this.meetings.startTimer(this.meetingId, seconds),
      'Таймер запущен.',
    );
  }

  addTimer(seconds: number): void {
    const current = this.state();
    if (!current?.timer || seconds <= 0 || this.commandsBlocked()) {
      return;
    }
    this.executeTimerCommand(
      this.meetings.addTimer(this.meetingId, current.timer.runId, seconds),
      'Время добавлено.',
    );
  }

  stopTimer(): void {
    const current = this.state();
    if (!current?.timer || this.commandsBlocked()) {
      return;
    }
    this.executeTimerCommand(
      this.meetings.stopTimer(this.meetingId, current.timer.runId),
      'Таймер остановлен.',
    );
  }

  private executeTimerCommand(command: Observable<unknown>, success: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    command.subscribe({
      next: () => {
        this.notice.set(success);
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.commandFailed(error);
        void this.sync.refresh();
      },
    });
  }

  currentTopic(): MeetingTopic | undefined {
    const current = this.state();
    return current?.topics.find((topic) => topic.id === current.discussion?.currentTopicId);
  }

  discussionTopic(topicId: string): MeetingTopic | undefined {
    return this.state()?.topics.find((topic) => topic.id === topicId);
  }

  currentTopicCards(): MeetingCard[] {
    const current = this.state();
    const topic = this.currentTopic();
    if (!current || !topic) {
      return [];
    }
    return topic.kind === 'group'
      ? current.cards.filter((card) => card.groupId === topic.sourceId)
      : current.cards.filter((card) => card.id === topic.sourceId);
  }

  selectDiscussionTopic(topicId: string): void {
    const current = this.state();
    if (!current || this.commandsBlocked()) {
      return;
    }
    this.executeDiscussionCommand(
      this.meetings.selectCurrentTopic(this.meetingId, topicId, current.version),
      'Тема выбрана.',
    );
  }

  setDiscussionStatus(status: DiscussionStatus, advance: boolean): void {
    const current = this.state();
    const topic = this.currentTopic();
    if (!current || !topic || this.commandsBlocked()) {
      return;
    }
    this.executeDiscussionCommand(
      this.meetings.setTopicStatus(this.meetingId, topic.id, status, advance, current.version),
      advance ? 'Открыта следующая тема.' : 'Статус темы изменён.',
    );
  }

  discussionStatusLabel(status: DiscussionStatus): string {
    const labels: Record<DiscussionStatus, string> = {
      pending: 'Ожидает',
      discussed: 'Обсуждена',
      skipped: 'Пропущена',
    };
    return labels[status];
  }

  createComment(): void {
    const current = this.state();
    const topic = this.currentTopic();
    const text = this.newCommentText().trim();
    if (!current || !topic || !text || this.commandsBlocked()) {
      return;
    }
    this.executeDiscussionCommand(
      this.meetings.createComment(this.meetingId, topic.id, text, current.version),
      'Комментарий добавлен.',
      () => this.newCommentText.set(''),
    );
  }

  createDecision(): void {
    const current = this.state();
    const topic = this.currentTopic();
    const text = this.newDecisionText().trim();
    if (!current || !topic || !text || this.commandsBlocked()) {
      return;
    }
    this.executeDiscussionCommand(
      this.meetings.createDecision(this.meetingId, topic.id, text, current.version),
      'Решение добавлено.',
      () => this.newDecisionText.set(''),
    );
  }

  createAction(): void {
    const current = this.state();
    const topic = this.currentTopic();
    const text = this.newActionText().trim();
    if (!current || !topic || !text || this.commandsBlocked()) {
      return;
    }
    this.executeDiscussionCommand(
      this.meetings.createAction(
        this.meetingId,
        topic.id,
        text,
        this.newActionAssignee() || null,
        current.version,
      ),
      'Задача добавлена.',
      () => {
        this.newActionText.set('');
        this.newActionAssignee.set('');
      },
    );
  }

  currentComments(): TopicComment[] {
    const current = this.state();
    const topic = this.currentTopic();
    return current?.discussion?.comments.filter((comment) => comment.topicId === topic?.id) ?? [];
  }

  currentDecisions(): TopicDecision[] {
    const current = this.state();
    const topic = this.currentTopic();
    return current?.discussion?.decisions.filter((decision) => decision.topicId === topic?.id) ?? [];
  }

  currentActions(): ActionItem[] {
    const current = this.state();
    const topic = this.currentTopic();
    return current?.discussion?.actions.filter((action) => action.topicId === topic?.id) ?? [];
  }

  commentsForTopic(topicId: string): TopicComment[] {
    return this.state()?.discussion?.comments.filter((comment) => comment.topicId === topicId) ?? [];
  }

  decisionsForTopic(topicId: string): TopicDecision[] {
    return this.state()?.discussion?.decisions.filter((decision) => decision.topicId === topicId) ?? [];
  }

  actionsForTopic(topicId: string): ActionItem[] {
    return this.state()?.discussion?.actions.filter((action) => action.topicId === topicId) ?? [];
  }

  formatClosedAt(value: string): string {
    return new Date(value).toLocaleString('ru-RU', {
      dateStyle: 'short',
      timeStyle: 'short',
      timeZone: 'UTC',
    });
  }

  startEntryEdit(
    kind: 'comments' | 'decisions' | 'actions',
    entry: TopicComment | TopicDecision | ActionItem,
  ): void {
    this.editingEntryKind.set(kind);
    this.editingEntryId.set(entry.id);
    this.editingEntryText.set(entry.text);
    this.editingActionAssignee.set('assigneeId' in entry ? entry.assigneeId ?? '' : '');
  }

  cancelEntryEdit(): void {
    this.editingEntryKind.set(null);
    this.editingEntryId.set(null);
    this.editingEntryText.set('');
    this.editingActionAssignee.set('');
  }

  isEntryEditing(kind: string, entryId: string): boolean {
    return this.editingEntryKind() === kind && this.editingEntryId() === entryId;
  }

  saveEntryEdit(): void {
    const current = this.state();
    const kind = this.editingEntryKind();
    const entryId = this.editingEntryId();
    const text = this.editingEntryText().trim();
    if (!current || !kind || !entryId || !text || this.commandsBlocked()) {
      return;
    }
    const command = kind === 'comments'
      ? this.meetings.updateComment(this.meetingId, entryId, text, current.version)
      : kind === 'decisions'
        ? this.meetings.updateDecision(this.meetingId, entryId, text, current.version)
        : this.meetings.updateAction(
          this.meetingId,
          entryId,
          text,
          this.editingActionAssignee() || null,
          current.version,
        );
    this.executeDiscussionCommand(command, 'Изменения сохранены.', () => this.cancelEntryEdit());
  }

  deleteEntry(kind: 'comments' | 'decisions' | 'actions', entryId: string): void {
    const current = this.state();
    if (!current || this.commandsBlocked() || !window.confirm('Удалить запись?')) {
      return;
    }
    const command = kind === 'comments'
      ? this.meetings.deleteComment(this.meetingId, entryId, current.version)
      : kind === 'decisions'
        ? this.meetings.deleteDecision(this.meetingId, entryId, current.version)
        : this.meetings.deleteAction(this.meetingId, entryId, current.version);
    this.executeDiscussionCommand(command, 'Запись удалена.');
  }

  availableAssignees(currentAssigneeId: string | null = null) {
    return this.state()?.participants.filter((participant) =>
      participant.status === 'Active' || participant.id === currentAssigneeId) ?? [];
  }

  actionAssigneeName(assigneeId: string | null): string {
    if (!assigneeId) {
      return 'Без исполнителя';
    }
    return this.state()?.participants.find((participant) => participant.id === assigneeId)?.displayName
      ?? 'Участник недоступен';
  }

  isActionLocked(action: ActionItem): boolean {
    return action.exportStatus === 'pending'
      || action.exportStatus === 'created'
      || action.exportStatus === 'unknown';
  }

  canExport(action: ActionItem): boolean {
    const current = this.state();
    return !!current?.canManage && current.integrations.jira && action.exportStatus === 'notExported';
  }

  exportAction(action: ActionItem): void {
    if (!this.canExport(action) || this.commandsBlocked()) {
      return;
    }
    this.executeDiscussionCommand(
      this.meetings.exportAction(this.meetingId, action.id),
      'Mock Jira обработал запрос экспорта.',
    );
  }

  exportStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      notExported: 'Jira: не экспортировано',
      pending: 'Mock Jira: отправка…',
      created: 'Mock Jira: создано',
      rejected: 'Mock Jira: подтверждён отказ',
      unknown: 'Mock Jira: требуется сверка',
      unavailable: 'Mock Jira: недоступен',
    };
    return labels[status] ?? status;
  }

  private executeDiscussionCommand(
    command: ReturnType<MeetingService['updateComment']>,
    success: string,
    after?: () => void,
  ): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    command.subscribe({
      next: () => {
        after?.();
        this.notice.set(success);
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.commandFailed(error);
        void this.sync.refresh();
      },
    });
  }

  saveVoteSettings(): void {
    const current = this.state();
    if (!current || this.commandsBlocked()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.updateVoteSettings(this.meetingId, current.version, this.voteSettings()).subscribe({
      next: () => {
        this.notice.set('Бюджет голосов обновлён.');
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.commandFailed(error);
        void this.sync.refresh();
      },
    });
  }

  changeVotes(topic: MeetingState['topics'][number], change: number): void {
    const current = this.state();
    if (!current?.voting || this.commandsBlocked()) {
      return;
    }
    const count = topic.myVotes + change;
    if (count < 0 || count - topic.myVotes > current.voting.remaining) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.setMyVotes(this.meetingId, topic.id, count, current.voting.ballotVersion).subscribe({
      next: () => {
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.commandFailed(error);
        void this.sync.refresh();
      },
    });
  }

  topicTitle(topic: MeetingState['topics'][number]): string {
    const current = this.state();
    if (topic.kind === 'group') {
      return current?.groups.find((group) => group.id === topic.sourceId)?.name ?? 'Группа';
    }
    return current?.cards.find((card) => card.id === topic.sourceId)?.text ?? 'Карточка';
  }

  topicColumnName(topic: MeetingState['topics'][number]): string {
    const current = this.state();
    const columnId = topic.kind === 'group'
      ? current?.groups.find((group) => group.id === topic.sourceId)?.columnId
      : current?.cards.find((card) => card.id === topic.sourceId)?.columnId;
    return columnId ? this.columnName(columnId) : '';
  }

  topicCardCount(topic: MeetingState['topics'][number]): number {
    const current = this.state();
    return topic.kind === 'group'
      ? current?.cards.filter((card) => card.groupId === topic.sourceId).length ?? 0
      : 1;
  }

  budgetFormula(): string {
    const current = this.state();
    if (!current?.voting) {
      return '';
    }
    if (current.voteSettings.mode === 'fixed') {
      return `Фиксированный бюджет B = ${current.voting.budget}.`;
    }
    const voting = current.voting;
    return `B = max(${current.voteSettings.minimum}, ceil(${voting.originalCardCount} × ${current.voteSettings.percent} / 100)) = ${voting.budget}.`;
  }

  columnName(columnId: string): string {
    return this.state()?.columns.find((column) => column.id === columnId)?.name ?? '';
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

  advanceLabel(phase: MeetingPhase, votingEnabled: boolean): string {
    const labels: Record<MeetingPhase, string> = {
      Draft: 'Начать сбор',
      Collect: 'Перейти к группировке',
      Group: votingEnabled ? 'Открыть голосование' : 'Перейти к обсуждению',
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

  private executeMove(sourceKind: string, sourceId: string, targetKind: string, targetId: string): void {
    const current = this.state();
    if (!current || this.commandsBlocked()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.meetings.move(this.meetingId, sourceKind, sourceId, targetKind, targetId, current.boardVersion).subscribe({
      next: () => {
        this.setMoveTarget(sourceKind, sourceId, '');
        this.busy.set(false);
        this.notice.set('Перенос выполнен.');
        void this.sync.refresh();
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
  }

  private commandFailed(error: HttpErrorResponse): void {
    this.error.set(apiErrorMessage(error));
    this.busy.set(false);
  }

  private applyState(state: MeetingState): void {
    this.name.set(state.name);
    this.columns.set(copyEditableColumns(state.columns));
    this.anonymousCards.set(state.anonymousCards);
    this.privateCollection.set(state.privateCollection);
    this.anonymousVotes.set(state.anonymousVotes);
    this.voteSettings.set({ ...state.voteSettings });
    this.votingEnabled.set(state.votingEnabled !== false);
    this.groupNames.set(Object.fromEntries(state.groups.map((group) => [group.id, group.name])));
  }

  private payload(): MeetingSettingsPayload {
    return {
      name: this.name(),
      columns: this.columns(),
      anonymousCards: this.anonymousCards(),
      privateCollection: this.privateCollection(),
      anonymousVotes: this.anonymousVotes(),
      voteSettings: this.voteSettings(),
      votingEnabled: this.votingEnabled(),
    };
  }
}
