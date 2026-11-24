import { EditableColumn } from './meeting.models';

export function templateNameError(name: string): string | null {
  return requiredNameError(name, 'Укажите название шаблона.');
}

export function columnNameError(name: string): string | null {
  return requiredNameError(name, 'Укажите название колонки.');
}

export function columnDescriptionError(description?: string | null): string | null {
  return (description?.trim().length ?? 0) > 400 ? 'Не более 400 символов.' : null;
}

export function hasTemplateColumnErrors(columns: readonly EditableColumn[]): boolean {
  return !columns.length || columns.some(({ name, description }) =>
    columnNameError(name) !== null || columnDescriptionError(description) !== null);
}

function requiredNameError(name: string, emptyMessage: string): string | null {
  const length = name.trim().length;
  if (length === 0) {
    return emptyMessage;
  }
  return length > 120 ? 'Не более 120 символов.' : null;
}
