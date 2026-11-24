import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { map } from 'rxjs';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { AuthService } from './auth.service';
import { ColumnsEditor } from './columns-editor';
import { copyEditableColumns, defaultColumns, MeetingTemplate, TemplateVisibility } from './meeting.models';
import { TemplateService } from './template.service';
import { hasTemplateColumnErrors, templateNameError } from './template-validation';
import { WorkspaceLayout } from './workspace-layout';

@Component({
  selector: 'app-templates-page',
  imports: [ColumnsEditor, FormsModule, TuiButton, TuiTextfield, WorkspaceLayout],
  templateUrl: './templates-page.html',
  styleUrl: './templates-page.scss',
})
export class TemplatesPage {
  private readonly templatesService = inject(TemplateService);
  private readonly injector = inject(Injector);
  private readonly nameField = viewChild<ElementRef<HTMLInputElement>>('nameField');
  readonly auth = inject(AuthService);

  readonly templates = signal<MeetingTemplate[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly name = signal('');
  readonly visibility = signal<TemplateVisibility>('personal');
  readonly columns = signal(defaultColumns());
  readonly busy = signal(false);
  readonly submitted = signal(false);
  readonly nameError = computed(() => this.submitted() ? templateNameError(this.name()) : null);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  constructor() {
    this.load();
  }

  edit(template: MeetingTemplate): void {
    this.selectedId.set(template.id);
    this.name.set(template.name);
    this.visibility.set(template.visibility);
    this.columns.set(copyEditableColumns(template.columns));
    this.submitted.set(false);
    this.notice.set(null);
    afterNextRender(() => this.nameField()?.nativeElement.focus(), { injector: this.injector });
  }

  reset(): void {
    this.selectedId.set(null);
    this.name.set('');
    this.visibility.set('personal');
    this.columns.set(defaultColumns());
    this.submitted.set(false);
  }

  save(): void {
    if (this.busy()) {
      return;
    }
    this.submitted.set(true);
    this.error.set(null);
    this.notice.set(null);
    if (this.nameError() || hasTemplateColumnErrors(this.columns())) {
      return;
    }

    this.busy.set(true);
    const payload = {
      name: this.name().trim(),
      visibility: this.visibility(),
      columns: this.columns(),
    };
    const id = this.selectedId();
    const request = id
      ? this.templatesService.update(id, payload)
      : this.templatesService.create(payload).pipe(map(() => undefined));

    request.subscribe({
      next: () => {
        this.notice.set(id ? 'Шаблон обновлён.' : 'Шаблон создан.');
        this.reset();
        this.busy.set(false);
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(apiErrorMessage(error));
        this.busy.set(false);
      },
    });
  }

  delete(template: MeetingTemplate): void {
    if (!window.confirm(`Удалить шаблон «${template.name}»?`)) {
      return;
    }

    this.templatesService.delete(template.id).subscribe({
      next: () => {
        if (this.selectedId() === template.id) {
          this.reset();
        }
        this.load();
      },
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }

  private load(): void {
    this.templatesService.list().subscribe({
      next: (templates) => this.templates.set(templates),
      error: (error: HttpErrorResponse) => this.error.set(apiErrorMessage(error)),
    });
  }
}
