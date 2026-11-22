import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { AuthService } from './auth.service';
import { ColumnsEditor } from './columns-editor';
import { defaultColumns, MeetingTemplate, TemplateVisibility } from './meeting.models';
import { TemplateService } from './template.service';

@Component({
  selector: 'app-templates-page',
  imports: [ColumnsEditor, FormsModule, RouterLink, TuiButton, TuiTextfield],
  templateUrl: './templates-page.html',
  styleUrl: './templates-page.scss',
})
export class TemplatesPage {
  private readonly templatesService = inject(TemplateService);
  readonly auth = inject(AuthService);

  readonly templates = signal<MeetingTemplate[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly name = signal('Новый шаблон');
  readonly visibility = signal<TemplateVisibility>('personal');
  readonly columns = signal(defaultColumns());
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  constructor() {
    this.load();
  }

  edit(template: MeetingTemplate): void {
    this.selectedId.set(template.id);
    this.name.set(template.name);
    this.visibility.set(template.visibility);
    this.columns.set(template.columns.map((column) => ({ name: column.name, color: column.color })));
    this.notice.set(null);
  }

  reset(): void {
    this.selectedId.set(null);
    this.name.set('Новый шаблон');
    this.visibility.set('personal');
    this.columns.set(defaultColumns());
  }

  save(): void {
    this.busy.set(true);
    this.error.set(null);
    const payload = {
      name: this.name(),
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
