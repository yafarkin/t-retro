import { Component, input, model, output } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { ColumnsEditor } from './columns-editor';
import { EditableColumn, VoteSettings } from './meeting.models';

@Component({
  selector: 'app-meeting-form',
  imports: [ColumnsEditor, FormsModule, TuiButton, TuiTextfield],
  templateUrl: './meeting-form.html',
  styleUrl: './meeting-form.scss',
})
export class MeetingForm {
  readonly name = model.required<string>();
  readonly columns = model.required<EditableColumn[]>();
  readonly anonymousCards = model.required<boolean>();
  readonly privateCollection = model.required<boolean>();
  readonly anonymousVotes = model.required<boolean>();
  readonly voteSettings = model.required<VoteSettings>();
  readonly busy = input(false);
  readonly submitLabel = input.required<string>();
  readonly submitted = output();

  submit(form: NgForm): void {
    if (form.valid) {
      this.submitted.emit();
    } else {
      form.form.markAllAsTouched();
    }
  }
}
