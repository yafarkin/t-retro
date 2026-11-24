import { afterNextRender, Component, computed, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { ColumnsEditor } from './columns-editor';
import { initialMeetingOptions, MeetingSetup } from './meeting-setup';
import { MeetingService } from './meeting.service';
import { copyEditableColumns, CreateMeetingPayload, defaultColumns, EditableColumn, TemplateVisibility } from './meeting.models';
import { TemplateService } from './template.service';
import { hasTemplateColumnErrors, templateNameError } from './template-validation';

type TemplateCategory = 'standard' | 'personal' | 'shared';
type CreateStep = 'picker' | 'meeting' | 'personal-template';

interface TemplatePreset {
  id: string;
  name: string;
  columns: readonly EditableColumn[];
  description?: string;
}

const STANDARD_TEMPLATES: readonly TemplatePreset[] = [
  {
    id: 'start-stop-continue',
    name: 'Начать, перестать, продолжать',
    columns: defaultColumns(),
    description: 'Определите, какие практики стоит попробовать, от каких отказаться и какие сохранить.',
  },
  {
    id: 'well-wrong-improve',
    name: 'Удачи, проблемы, улучшения',
    columns: [
      { name: 'Что получилось', color: '#168268' },
      { name: 'Что пошло не так', color: '#C7544B' },
      { name: 'Что улучшить', color: '#3D71B6' },
    ],
    description: 'Вспомните итоги работы и выберите конкретные улучшения на следующий цикл.',
  },
  {
    id: 'sailboat',
    name: 'Парусник',
    columns: [
      { name: 'Цель', color: '#14745D' },
      { name: 'Что помогает', color: '#357AAC' },
      { name: 'Что тормозит', color: '#8266A6' },
      { name: 'Риски', color: '#C4683E' },
    ],
    description: 'Согласуйте цель команды, обсудите препятствия и риски, найдите то, что ускоряет работу.',
  },
  {
    id: 'four-ls',
    name: 'Четыре вопроса (4L)',
    columns: [
      { name: 'Что понравилось', color: '#2C896D' },
      { name: 'Чему научились', color: '#4179AF' },
      { name: 'Чего не хватило', color: '#B2643F' },
      { name: 'Чего хотим дальше', color: '#8664AD' },
    ],
    description: 'Соберите наблюдения о прошедшем цикле и обсудите ожидания на следующий.',
  },
];

@Component({
  selector: 'app-create-meeting-page',
  imports: [ColumnsEditor, FormsModule, MeetingSetup, RouterLink, TuiButton, TuiTextfield],
  templateUrl: './create-meeting-page.html',
  styleUrl: './create-meeting-page.scss',
})
export class CreateMeetingPage {
  private readonly meetings = inject(MeetingService);
  private readonly templateService = inject(TemplateService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLHeadingElement>>('stepHeading');

  readonly categories: readonly { id: TemplateCategory; label: string }[] = [
    { id: 'standard', label: 'Готовые' },
    { id: 'personal', label: 'Личные' },
    { id: 'shared', label: 'Общие' },
  ];
  readonly category = signal<TemplateCategory>('standard');
  readonly selectedId = signal<string | null>(null);
  private readonly savedTemplates = signal<(TemplatePreset & { visibility: TemplateVisibility })[]>([]);
  readonly loadingTemplates = signal(true);
  readonly templateError = signal<string | null>(null);
  readonly step = signal<CreateStep>('picker');
  readonly personalName = signal('');
  readonly personalColumns = signal<EditableColumn[]>([]);
  readonly savingTemplate = signal(false);
  readonly personalSubmitted = signal(false);
  readonly personalNameError = computed(() => this.personalSubmitted() ? templateNameError(this.personalName()) : null);
  readonly saveError = signal<string | null>(null);
  readonly templates = computed<readonly TemplatePreset[]>(() => {
    const category = this.category();
    return category === 'standard'
      ? STANDARD_TEMPLATES
      : this.savedTemplates().filter((template) => template.visibility === category);
  });
  readonly selectedTemplate = computed(() =>
    this.templates().find((template) => template.id === this.selectedId()) ?? this.templates()[0],
  );

  readonly name = signal(new Intl.DateTimeFormat('ru-RU', { dateStyle: 'long' }).format(new Date()));
  readonly meetingTemplateName = signal('');
  readonly columns = signal(defaultColumns());
  readonly options = signal(initialMeetingOptions());
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  constructor() {
    this.templateService.list().pipe(takeUntilDestroyed()).subscribe({
      next: (templates) => {
        this.savedTemplates.update((saved) => [
          ...saved.filter((template) => !templates.some(({ id }) => id === template.id)),
          ...templates,
        ]);
        this.loadingTemplates.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.templateError.set(apiErrorMessage(error));
        this.loadingTemplates.set(false);
      },
    });
  }

  selectCategory(category: TemplateCategory): void {
    if (this.category() !== category) {
      this.category.set(category);
      this.selectedId.set(null);
    }
  }

  useTemplate(template: TemplatePreset): void {
    this.columns.set(copyEditableColumns(template.columns));
    this.meetingTemplateName.set(template.name);
    this.error.set(null);
    this.showStep('meeting');
  }

  backToTemplates(): void {
    this.showStep('picker');
  }

  cancelTemplateChange(): void {
    this.showStep('meeting');
  }

  newPersonalTemplate(): void {
    const source = this.selectedTemplate();
    this.personalName.set('');
    this.personalColumns.set(source ? copyEditableColumns(source.columns) : defaultColumns());
    this.personalSubmitted.set(false);
    this.saveError.set(null);
    this.showStep('personal-template');
  }

  savePersonalTemplate(): void {
    if (this.savingTemplate()) {
      return;
    }

    this.personalSubmitted.set(true);
    this.saveError.set(null);
    if (this.personalNameError() || hasTemplateColumnErrors(this.personalColumns())) {
      return;
    }

    const name = this.personalName().trim();
    const columns = this.personalColumns().map((column) => ({
      name: column.name.trim(),
      color: column.color.toUpperCase(),
      description: column.description?.trim() || null,
    }));
    this.savingTemplate.set(true);
    this.templateService.create({ name, columns, visibility: 'personal' }).subscribe({
      next: ({ id }) => {
        const template = { id, name, columns, visibility: 'personal' } as const;
        this.savedTemplates.update((templates) => [
          template,
          ...templates.filter((template) => template.id !== id),
        ]);
        this.category.set('personal');
        this.selectedId.set(id);
        this.savingTemplate.set(false);
        this.useTemplate(template);
      },
      error: (error: HttpErrorResponse) => {
        this.saveError.set(apiErrorMessage(error));
        this.savingTemplate.set(false);
      },
    });
  }

  private showStep(step: CreateStep): void {
    this.step.set(step);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

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

  private payload(): CreateMeetingPayload {
    return {
      name: this.name().trim(),
      columns: this.columns(),
      startCollecting: true,
      ...this.options(),
    };
  }
}
