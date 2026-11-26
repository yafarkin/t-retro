import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, linkedSignal, signal, TemplateRef, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';
import { TuiButton } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { MeetingForm } from './meeting-form';
import { MeetingHeaderService } from './meeting-header.service';
import { MeetingInvite } from './meeting-invite';
import { MeetingTimerControls } from './meeting-timer-controls';
import { MeetingTopicVotes } from './meeting-topic-votes';
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

const PHASES: ReadonlyArray<{ id: MeetingPhase; label: string }> = [
  { id: 'Draft', label: 'Подготовка' },
  { id: 'Collect', label: 'Сбор карточек' },
  { id: 'Group', label: 'Группировка' },
  { id: 'Vote', label: 'Голосование' },
  { id: 'Discuss', label: 'Обсуждение' },
  { id: 'Closed', label: 'Итоги' },
];

interface BoardTarget {
  kind: 'column' | 'card' | 'group';
  id: string;
}

type DiscussionEntryKind = 'comments' | 'decisions' | 'actions';

type DiscussionEntry =
  | { kind: 'comments'; entry: TopicComment }
  | { kind: 'decisions'; entry: TopicDecision }
  | { kind: 'actions'; entry: ActionItem };

interface DiscussionDraft {
  kind: DiscussionEntryKind;
  text: string;
  description: string;
  assignee: string;
}

const EMPTY_DISCUSSION_DRAFT: DiscussionDraft = { kind: 'comments', text: '', description: '', assignee: '' };

@Component({
  selector: 'app-meeting-settings-page',
  host: { '(window:pagehide)': 'sync.onPageHide($event)' },
  imports: [FormsModule, NgTemplateOutlet, MeetingForm, MeetingInvite, MeetingTimerControls, MeetingTopicVotes, TuiButton],
  providers: [MeetingSyncService],
  templateUrl: './meeting-settings-page.html',
  styleUrls: [
    './meeting-settings-page.scss',
    './meeting-board.scss',
    './meeting-grouping.scss',
    './meeting-toolbar.scss',
    './meeting-voting.scss',
    './meeting-dialog.scss',
    './meeting-discussion.scss',
    './meeting-results.scss',
    './meeting-participants.scss',
  ],
})
export class MeetingSettingsPage {
  private readonly injector = inject(Injector);
  private readonly newCardInput = viewChild<ElementRef<HTMLTextAreaElement>>('newCardInput');
  private readonly groupNameInput = viewChild<ElementRef<HTMLInputElement>>('groupNameInput');
  private readonly headerInvite = viewChild<TemplateRef<unknown>>('headerInvite');
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
  readonly voteBudgetDraft = signal<number | null>(null);
  readonly voteBudgetError = signal<string | null>(null);
  readonly validVoteBudget = computed(() => {
    const budget = this.voteBudgetDraft();
    return budget !== null && Number.isInteger(budget) && budget >= 1 && budget <= 2147483647;
  });
  readonly topicsBySource = computed(() => new Map(this.state()?.topics.map((topic) => [topic.sourceId, topic])));
  readonly selectedTemplate = signal('');
  readonly selectedMember = signal('');
  readonly participantError = signal<string | null>(null);
  readonly cardComposerColumn = signal<string | null>(null);
  readonly newCardTexts = signal<Record<string, string>>({});
  readonly newCardAnonymity = signal<Record<string, boolean>>({});
  readonly editingCardId = signal<string | null>(null);
  readonly editingText = signal('');
  readonly proposal = signal<GroupProposal | null>(null);
  readonly expandedGroups = signal<ReadonlySet<string>>(new Set());
  readonly editingGroupId = signal<string | null>(null);
  readonly editingGroupName = signal('');
  readonly dragSource = signal<BoardTarget | null>(null);
  private readonly dropTarget = signal<BoardTarget | null>(null);
  readonly blockedDropColumn = computed(() => {
    const source = this.dragSource();
    const card = source?.kind === 'card' ? this.state()?.cards.find((item) => item.id === source.id) : undefined;
    return card?.groupId === null ? card.columnId : null;
  });
  private readonly sharedDiscussionTopic = computed(() => ({
    id: this.state()?.discussion?.currentTopicId ?? null,
    version: this.state()?.discussion?.selectionVersion ?? 0,
    canManage: this.state()?.canManage ?? false,
  }), { equal: (a, b) => a.id === b.id && a.version === b.version && a.canManage === b.canManage });
  private readonly viewedTopicId = linkedSignal(() => this.sharedDiscussionTopic().id);
  private readonly discussionDrafts = signal<Record<string, DiscussionDraft>>({});
  readonly discussionDraft = computed(() => {
    const topicId = this.viewedTopicId();
    return (topicId && this.discussionDrafts()[topicId]) || EMPTY_DISCUSSION_DRAFT;
  });
  readonly currentDiscussionItem = computed(() =>
    this.state()?.discussion?.queue.find((item) => item.topicId === this.viewedTopicId()));
  readonly hasNextDiscussionTopic = computed(() => {
    const item = this.currentDiscussionItem();
    const discussion = this.state()?.discussion;
    return !!item && !!discussion && item.position + 1 < discussion.queue.length;
  });
  readonly resultTopicId = signal<string | null>(null);
  readonly resultTopic = computed(() => {
    const id = this.resultTopicId();
    return id ? this.discussionTopic(id) : undefined;
  });
  readonly discussionStatusByTopic = computed(() =>
    new Map(this.state()?.discussion?.queue.map((item) => [item.topicId, item.status])));
  readonly discussionEntriesByTopic = computed(() => {
    const discussion = this.state()?.discussion;
    const byTopic = new Map<string, DiscussionEntry[]>();
    if (!discussion) {
      return byTopic;
    }
    const entries: DiscussionEntry[] = [
      ...discussion.comments.map((entry) => ({ kind: 'comments' as const, entry })),
      ...discussion.decisions.map((entry) => ({ kind: 'decisions' as const, entry })),
      ...discussion.actions.map((entry) => ({ kind: 'actions' as const, entry })),
    ];
    for (const entry of entries) {
      const topicId = entry.entry.topicId;
      const topicEntries = byTopic.get(topicId);
      if (topicEntries) {
        topicEntries.push(entry);
      } else {
        byTopic.set(topicId, [entry]);
      }
    }
    for (const topicEntries of byTopic.values()) {
      topicEntries.sort((a, b) => (a.entry.createdVersion ?? 0) - (b.entry.createdVersion ?? 0));
    }
    return byTopic;
  });
  readonly discussionEntries = computed(() => this.discussionEntriesByTopic().get(this.viewedTopicId() ?? '') ?? []);
  readonly closedExportableActions = computed(() => (this.state()?.discussion?.actions ?? [])
    .filter((action) => this.canExport(action)));
  private readonly discussionEntryLabels = { comments: 'Комментарий', decisions: 'Решение', actions: 'Задача' };
  readonly discussionSaveLabels = { comments: 'Сохранить комментарий', decisions: 'Сохранить решение', actions: 'Сохранить задачу' };
  readonly discussionAddLabels = { comments: 'Добавить комментарий', decisions: 'Добавить решение', actions: 'Добавить задачу' };
  readonly editingEntryKind = signal<DiscussionEntryKind | null>(null);
  readonly editingEntryId = signal<string | null>(null);
  readonly editingEntryText = signal('');
  readonly editingActionDescription = signal('');
  readonly editingActionAssignee = signal('');
  readonly participants = computed(() => {
    const meeting = this.state();
    return [...(meeting?.participants ?? [])].sort((left, right) =>
      Number(right.id === meeting?.facilitatorId) - Number(left.id === meeting?.facilitatorId)
      || left.subject.localeCompare(right.subject, 'ru'),
    );
  });
  readonly activeParticipants = computed(() =>
    this.participants().filter((participant) => participant.status === 'Active'));
  readonly facilitator = computed(() =>
    this.activeParticipants().find((participant) => participant.id === this.state()?.facilitatorId));
  readonly canManageParticipants = computed(() => {
    const meeting = this.state();
    return !this.revoked() && !!meeting?.canManage && meeting.phase !== 'Closed';
  });
  private readonly selectedParticipant = computed(() =>
    this.activeParticipants().find((participant) =>
      participant.id === this.selectedMember() && participant.id !== this.state()?.facilitatorId));
  readonly canChangeParticipant = computed(() =>
    this.canManageParticipants() && !this.commandsBlocked() && !!this.selectedParticipant());
  readonly roadmap = computed(() => {
    const meeting = this.state();
    const phases = PHASES.filter(({ id }) => id !== 'Vote' || meeting?.votingEnabled !== false);
    const currentIndex = phases.findIndex(({ id }) => id === meeting?.phase);
    return phases.map((phase, index) => ({ ...phase, completed: index < currentIndex, current: index === currentIndex }));
  });

  readonly name = signal('');
  readonly columns = signal(defaultColumns());
  readonly anonymousCards = signal(true);
  readonly privateCollection = signal(true);
  readonly voteSettings = signal(defaultVoteSettings());
  readonly votingEnabled = signal(true);
  private formVersion = 0;
  private pendingAdvancePhase: MeetingPhase | null = null;

  constructor() {
    const meetingHeader = inject(MeetingHeaderService);
    effect(() => meetingHeader.title.set(this.revoked() ? null : this.state()?.name ?? null));
    effect(() => meetingHeader.invite.set(this.canManageParticipants() ? this.headerInvite() ?? null : null));
    inject(DestroyRef).onDestroy(() => {
      meetingHeader.title.set(null);
      meetingHeader.invite.set(null);
    });
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

  openParticipants(dialog: HTMLDialogElement): void {
    this.selectedMember.set('');
    this.participantError.set(null);
    dialog.showModal();
  }

  changeParticipant(action: 'transfer' | 'exclude', dialog: HTMLDialogElement): void {
    const participant = this.selectedParticipant();
    if (!participant || !this.canChangeParticipant()) {
      return;
    }
    if (action === 'exclude' && !window.confirm(`Исключить участника «${participant.displayName}»?`)) {
      return;
    }

    const command = action === 'transfer'
      ? this.meetings.transfer(this.meetingId, participant.id)
      : this.meetings.exclude(this.meetingId, participant.id);
    this.busy.set(true);
    this.participantError.set(null);
    this.notice.set(null);
    command.subscribe({
      next: () => {
        this.selectedMember.set('');
        if (action === 'transfer') {
          dialog.close();
        }
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.participantError.set(apiErrorMessage(error));
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
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

  requestAdvance(dialog: HTMLDialogElement): void {
    const current = this.state();
    if (!current?.canManage || current.phase === 'Closed' || this.commandsBlocked()) {
      return;
    }
    const readiness = current.readiness;
    if (current.phase === 'Discuss' || (readiness && readiness.readyCount < readiness.totalCount)) {
      this.pendingAdvancePhase = current.phase;
      dialog.returnValue = '';
      dialog.showModal();
      return;
    }
    this.advance(current);
  }

  confirmAdvance(dialog: HTMLDialogElement): void {
    const phase = this.pendingAdvancePhase;
    this.pendingAdvancePhase = null;
    const current = this.state();
    if (dialog.returnValue === 'advance' && current?.canManage && current.phase === phase && !this.commandsBlocked()) {
      this.advance(current);
    }
  }

  private advance(current: MeetingState): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.meetings.advance(this.meetingId, current.phase, current.version).subscribe({
      next: () => {
        void this.sync.refresh().finally(() => this.busy.set(false));
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

  toggleGroup(groupId: string): void {
    const expanded = new Set(this.expandedGroups());
    if (!expanded.delete(groupId)) {
      expanded.add(groupId);
    }
    this.expandedGroups.set(expanded);
  }

  isDropTarget(kind: BoardTarget['kind'], id: string): boolean {
    const target = this.dropTarget();
    return target?.kind === kind && target.id === id;
  }

  startDrag(event: DragEvent, kind: 'card' | 'group', id: string): void {
    event.stopPropagation();
    if (!event.dataTransfer || this.state()?.phase !== 'Group' || this.commandsBlocked()) {
      event.preventDefault();
      return;
    }
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', id);
    this.dragSource.set({ kind, id });
  }

  allowDrop(event: DragEvent, kind: BoardTarget['kind'], id: string): void {
    event.stopPropagation();
    if (!this.canDrop(this.dragSource(), kind, id)) {
      this.dropTarget.set(null);
      return;
    }
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    if (!this.isDropTarget(kind, id)) {
      this.dropTarget.set({ kind, id });
    }
  }

  leaveBoard(event: DragEvent): void {
    if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null)) {
      this.dropTarget.set(null);
    }
  }

  finishDrag(): void {
    this.dragSource.set(null);
    this.dropTarget.set(null);
  }

  drop(event: DragEvent, kind: BoardTarget['kind'], id: string): void {
    event.preventDefault();
    event.stopPropagation();
    const source = this.dragSource();
    const allowed = this.canDrop(source, kind, id);
    this.finishDrag();
    if (allowed) {
      this.executeMove(source.kind, source.id, kind, id);
    }
  }

  private canDrop(source: BoardTarget | null, kind: BoardTarget['kind'], id: string): source is BoardTarget {
    return !!source && this.state()?.phase === 'Group' && !this.commandsBlocked()
      && (source.kind === 'card' || kind === 'column')
      && (kind !== 'column' || this.blockedDropColumn() !== id)
      && (source.kind !== kind || source.id !== id);
  }

  editGroupName(group: MeetingGroup): void {
    this.editingGroupName.set(group.name);
    this.editingGroupId.set(group.id);
    afterNextRender(() => this.groupNameInput()?.nativeElement.select(), { injector: this.injector });
  }

  renameGroup(group: MeetingGroup): void {
    const current = this.state();
    const name = this.editingGroupName();
    if (!current || this.commandsBlocked() || !name.trim()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.meetings.renameGroup(this.meetingId, group.id, name, current.boardVersion).subscribe({
      next: () => {
        this.editingGroupId.set(null);
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => this.commandFailed(error),
    });
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
    this.executeTimerCommand(this.meetings.startTimer(this.meetingId, seconds));
  }

  addTimer(seconds: number): void {
    const current = this.state();
    if (!current?.timer || seconds <= 0 || this.commandsBlocked()) {
      return;
    }
    this.executeTimerCommand(this.meetings.addTimer(this.meetingId, current.timer.runId, seconds));
  }

  stopTimer(): void {
    const current = this.state();
    if (!current?.timer || this.commandsBlocked()) {
      return;
    }
    this.executeTimerCommand(this.meetings.stopTimer(this.meetingId, current.timer.runId));
  }

  private executeTimerCommand(command: Observable<unknown>): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    command.subscribe({
      next: () => {
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
    return current?.topics.find((topic) => topic.id === this.viewedTopicId());
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
    if (!current || current.phase !== 'Discuss') {
      return;
    }
    if (!current.canManage) {
      this.viewedTopicId.set(topicId);
      return;
    }
    if (this.commandsBlocked()) {
      return;
    }
    this.executeMeetingCommand(this.meetings.selectCurrentTopic(this.meetingId, topicId, current.version));
  }

  setDiscussionStatus(status: DiscussionStatus, advance: boolean): void {
    const current = this.state();
    const topic = this.currentTopic();
    if (!current?.canManage || !topic || this.commandsBlocked() || (advance && !this.hasNextDiscussionTopic())) {
      return;
    }
    this.executeMeetingCommand(
      this.meetings.setTopicStatus(this.meetingId, topic.id, status, advance, current.version),
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

  updateDiscussionDraft(change: Partial<DiscussionDraft>): void {
    const topicId = this.viewedTopicId();
    if (topicId) {
      this.discussionDrafts.update((drafts) => ({
        ...drafts,
        [topicId]: { ...this.discussionDraft(), ...change },
      }));
    }
  }

  addDiscussionEntry(): void {
    const current = this.state();
    const topicId = this.viewedTopicId();
    const draft = this.discussionDraft();
    const text = draft.text.trim();
    if (current?.phase !== 'Discuss' || !topicId || !text || this.commandsBlocked()) {
      return;
    }

    const kind = !current.canManage && draft.kind === 'decisions' ? 'comments' : draft.kind;
    const command = kind === 'comments'
      ? this.meetings.createComment(this.meetingId, topicId, text, current.version)
      : kind === 'decisions'
        ? this.meetings.createDecision(this.meetingId, topicId, text, current.version)
        : this.meetings.createAction(
          this.meetingId, topicId, text, draft.description.trim() || null, draft.assignee || null, current.version,
        );
    this.executeMeetingCommand(command, () => this.discussionDrafts.update((drafts) => {
      if (drafts[topicId] !== draft) {
        return drafts;
      }
      return { ...drafts, [topicId]: { ...EMPTY_DISCUSSION_DRAFT, kind } };
    }));
  }

  discussionEntryLabel(kind: DiscussionEntryKind): string {
    return this.discussionEntryLabels[kind];
  }

  openResultTopic(topicId: string, dialog: HTMLDialogElement): void {
    this.resultTopicId.set(topicId);
    dialog.showModal();
  }

  archiveMeeting(): void {
    const current = this.state();
    if (!current?.canManage || current.isArchived || this.commandsBlocked()) {
      return;
    }

    this.executeMeetingCommand(this.meetings.archive(this.meetingId, current.version));
  }

  formatClosedAt(value: string): string {
    return new Date(value).toLocaleString('ru-RU', {
      dateStyle: 'short',
      timeStyle: 'short',
      timeZone: 'UTC',
    });
  }

  startEntryEdit(
    kind: DiscussionEntryKind,
    entry: TopicComment | TopicDecision | ActionItem,
  ): void {
    this.editingEntryKind.set(kind);
    this.editingEntryId.set(entry.id);
    this.editingEntryText.set(entry.text);
    this.editingActionDescription.set('description' in entry ? entry.description ?? '' : '');
    this.editingActionAssignee.set('assigneeId' in entry ? entry.assigneeId ?? '' : '');
  }

  cancelEntryEdit(): void {
    this.editingEntryKind.set(null);
    this.editingEntryId.set(null);
    this.editingEntryText.set('');
    this.editingActionDescription.set('');
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
          this.editingActionDescription().trim() || null,
          this.editingActionAssignee() || null,
          current.version,
        );
    this.executeMeetingCommand(command, () => this.cancelEntryEdit());
  }

  deleteEntry(kind: DiscussionEntryKind, entryId: string): void {
    const current = this.state();
    if (!current || this.commandsBlocked() || !window.confirm('Удалить запись?')) {
      return;
    }
    const command = kind === 'comments'
      ? this.meetings.deleteComment(this.meetingId, entryId, current.version)
      : kind === 'decisions'
        ? this.meetings.deleteDecision(this.meetingId, entryId, current.version)
        : this.meetings.deleteAction(this.meetingId, entryId, current.version);
    this.executeMeetingCommand(command);
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
    this.executeMeetingCommand(this.meetings.exportAction(this.meetingId, action.id));
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

  private executeMeetingCommand(
    command: ReturnType<MeetingService['updateComment']>,
    after?: () => void,
  ): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    command.subscribe({
      next: () => {
        after?.();
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.commandFailed(error);
        void this.sync.refresh();
      },
    });
  }

  openVoteBudget(dialog: HTMLDialogElement): void {
    this.voteBudgetDraft.set(this.state()?.voting?.budget ?? null);
    this.voteBudgetError.set(null);
    dialog.showModal();
  }

  saveVoteBudget(dialog: HTMLDialogElement): void {
    const current = this.state();
    const budget = this.voteBudgetDraft();
    if (!current?.canManage || !current.voting || budget === null || !this.validVoteBudget() || this.commandsBlocked()) {
      return;
    }
    if (budget === current.voting.budget) {
      dialog.close();
      return;
    }
    this.busy.set(true);
    this.voteBudgetError.set(null);
    const settings = { ...current.voteSettings, mode: 'fixed' as const, fixed: budget };
    this.meetings.updateVoteSettings(this.meetingId, current.version, settings).subscribe({
      next: () => {
        dialog.close();
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
      error: (error: HttpErrorResponse) => {
        this.voteBudgetError.set(apiErrorMessage(error));
        void this.sync.refresh().finally(() => this.busy.set(false));
      },
    });
  }

  changeVotes(topic: MeetingState['topics'][number], change: number): void {
    const current = this.state();
    if (!current?.voting || topic.myVotes === undefined || this.commandsBlocked()) {
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

  columnName(columnId: string): string {
    return this.state()?.columns.find((column) => column.id === columnId)?.name ?? '';
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
        void this.sync.refresh().finally(() => this.busy.set(false));
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
    this.voteSettings.set({ ...state.voteSettings });
    this.votingEnabled.set(state.votingEnabled !== false);
  }

  private payload(): MeetingSettingsPayload {
    return {
      name: this.name(),
      columns: this.columns(),
      anonymousCards: this.anonymousCards(),
      privateCollection: this.privateCollection(),
      voteSettings: this.voteSettings(),
      votingEnabled: this.votingEnabled(),
    };
  }
}
