import { Component, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { EditableColumn } from './meeting.models';

@Component({
  selector: 'app-columns-editor',
  imports: [FormsModule, TuiButton, TuiTextfield],
  templateUrl: './columns-editor.html',
  styleUrl: './columns-editor.scss',
})
export class ColumnsEditor {
  readonly columns = model.required<EditableColumn[]>();

  add(): void {
    this.columns.update((columns) => [...columns, { name: 'Новая колонка', color: '#6B7A90' }]);
  }

  remove(index: number): void {
    this.columns.update((columns) => columns.filter((_, position) => position !== index));
  }

  move(index: number, offset: number): void {
    const target = index + offset;
    this.columns.update((columns) => {
      if (target < 0 || target >= columns.length) {
        return columns;
      }
      const copy = [...columns];
      [copy[index], copy[target]] = [copy[target], copy[index]];
      return copy;
    });
  }
}
