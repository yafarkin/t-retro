import { afterNextRender, Component, computed, ElementRef, inject, Injector, input, model, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { ColumnsEditor } from './columns-editor';
import { copyEditableColumns, EditableColumn, MeetingSettingsPayload, VoteSettings } from './meeting.models';
import { hasTemplateColumnErrors } from './template-validation';

export type MeetingSetupOptions = Pick<MeetingSettingsPayload,
  'anonymousCards' | 'privateCollection' | 'anonymousVotes' | 'voteSettings'> & { votingEnabled: boolean };

export function initialMeetingOptions(): MeetingSetupOptions {
  return {
    anonymousCards: true,
    privateCollection: true,
    anonymousVotes: true,
    votingEnabled: true,
    voteSettings: { mode: 'fixed', fixed: 6, percent: 5, minimum: 2 },
  };
}

@Component({
  selector: 'app-meeting-setup',
  imports: [ColumnsEditor, FormsModule, TuiButton, TuiTextfield],
  templateUrl: './meeting-setup.html',
  styleUrl: './meeting-setup.scss',
})
export class MeetingSetup {
  private readonly injector = inject(Injector);
  private readonly heading = viewChild.required<ElementRef<HTMLHeadingElement>>('heading');
  private readonly modal = viewChild.required<ElementRef<HTMLDialogElement>>('modal');
  private readonly modalHeading = viewChild<ElementRef<HTMLHeadingElement>>('modalHeading');
  readonly name = model.required<string>();
  readonly columns = model.required<EditableColumn[]>();
  readonly options = model.required<MeetingSetupOptions>();
  readonly templateName = input.required<string>();
  readonly busy = input(false);
  readonly error = input<string | null>(null);
  readonly changeTemplate = output();
  readonly submitted = output();

  readonly attempted = signal(false);
  readonly nameError = computed(() => this.attempted() && !this.name().trim() ? 'Укажите название ретро.' : null);
  readonly activeDialog = signal<'columns' | 'settings' | null>(null);
  readonly tab = signal<'visibility' | 'voting'>('visibility');
  readonly draftColumns = signal<EditableColumn[]>([]);
  readonly draftOptions = signal(initialMeetingOptions());
  readonly columnsAttempted = signal(false);
  readonly voteError = signal<string | null>(null);

  constructor() {
    afterNextRender(() => this.heading().nativeElement.focus());
  }

  submit(): void {
    this.attempted.set(true);
    if (!this.busy() && !this.nameError()) {
      this.submitted.emit();
    }
  }

  editColumns(): void {
    this.draftColumns.set(copyEditableColumns(this.columns()));
    this.columnsAttempted.set(false);
    this.open('columns');
  }

  saveColumns(): void {
    this.columnsAttempted.set(true);
    if (hasTemplateColumnErrors(this.draftColumns())) {
      return;
    }
    this.columns.set(this.draftColumns());
    this.close();
  }

  editSettings(): void {
    const options = this.options();
    this.draftOptions.set({ ...options, voteSettings: { ...options.voteSettings } });
    this.voteError.set(null);
    this.tab.set('visibility');
    this.open('settings');
  }

  setVoteMode(mode: VoteSettings['mode']): void {
    const settings = this.draftOptions().voteSettings;
    settings.mode = mode;
    if (mode === 'fixed') {
      settings.fixed ??= 6;
    }
    this.voteError.set(null);
  }

  saveSettings(): void {
    const draft = this.draftOptions();
    const { mode, fixed, percent, minimum } = draft.voteSettings;
    const valid = mode === 'fixed'
      ? fixed !== null && Number.isInteger(fixed) && fixed >= 1 && fixed <= 2147483647
      : Number.isInteger(percent) && percent >= 0 && percent <= 100 &&
        Number.isInteger(minimum) && minimum >= 1 && minimum <= 2147483647;

    if (draft.votingEnabled && !valid) {
      this.voteError.set(mode === 'fixed'
        ? 'Укажите целое число голосов от 1 до 2 147 483 647.'
        : 'Укажите целый процент от 0 до 100 и целый минимум от 1 до 2 147 483 647.');
      this.tab.set('voting');
      return;
    }

    this.options.set({
      ...draft,
      voteSettings: valid ? draft.voteSettings : this.options().voteSettings,
    });
    this.close();
  }

  close(event?: Event): void {
    event?.preventDefault();
    this.modal().nativeElement.close();
    this.activeDialog.set(null);
  }

  private open(dialog: 'columns' | 'settings'): void {
    this.activeDialog.set(dialog);
    this.modal().nativeElement.showModal();
    afterNextRender(() => this.modalHeading()?.nativeElement.focus(), { injector: this.injector });
  }
}
