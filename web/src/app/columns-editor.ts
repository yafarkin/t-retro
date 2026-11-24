import { afterNextRender, Component, ElementRef, inject, Injector, input, model, viewChildren } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { EditableColumn } from './meeting.models';
import { columnDescriptionError, columnNameError } from './template-validation';

@Component({
  selector: 'app-columns-editor',
  imports: [CdkDrag, CdkDragHandle, CdkDropList, FormsModule, TuiButton, TuiTextfield],
  templateUrl: './columns-editor.html',
  styleUrl: './columns-editor.scss',
})
export class ColumnsEditor {
  private readonly injector = inject(Injector);
  private readonly names = viewChildren<ElementRef<HTMLInputElement>>('columnName');
  private readonly handles = viewChildren<ElementRef<HTMLButtonElement>>('dragHandle');
  readonly columns = model.required<EditableColumn[]>();
  readonly cards = input(false);
  readonly showErrors = input(false);
  readonly columnNameError = columnNameError;
  readonly columnDescriptionError = columnDescriptionError;

  add(): void {
    this.columns.update((columns) => [...columns, { name: this.cards() ? '' : 'Новая колонка', color: '#6B7A90' }]);
    if (this.cards()) {
      afterNextRender(() => this.names().at(-1)?.nativeElement.focus(), { injector: this.injector });
    }
  }

  remove(index: number): void {
    this.columns.update((columns) => columns.filter((_, position) => position !== index));
  }

  move(index: number, offset: number): void {
    this.reorder(index, index + offset);
  }

  moveByKeyboard(event: Event, index: number, offset: number): void {
    event.preventDefault();
    this.move(index, offset);
    afterNextRender(() => this.handles()[index + offset]?.nativeElement.focus(), { injector: this.injector });
  }

  drop(event: CdkDragDrop<unknown>): void {
    if (event.isPointerOverContainer) {
      this.reorder(event.previousIndex, event.currentIndex);
    }
  }

  private reorder(index: number, target: number): void {
    this.columns.update((columns) => {
      if (index === target || target < 0 || target >= columns.length) {
        return columns;
      }
      const copy = [...columns];
      moveItemInArray(copy, index, target);
      return copy;
    });
  }
}
