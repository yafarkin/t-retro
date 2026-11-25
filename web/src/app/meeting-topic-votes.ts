import { Component, input, output } from '@angular/core';
import { TuiButton } from '@taiga-ui/core';
import { MeetingTopic } from './meeting.models';

@Component({
  selector: 'app-meeting-topic-votes',
  imports: [TuiButton],
  template: `
    @let count = topic().myVotes ?? 0;
    <div class="vote-actions">
      <button tuiButton type="button" appearance="secondary" size="s"
        [attr.aria-label]="'Отозвать голос: ' + topicLabel()" title="Отозвать голос"
        [disabled]="disabled() || count === 0" (click)="adjust.emit(-1)">−</button>
      <output class="my-votes" aria-label="Ваши голоса">{{ count }}</output>
      <button tuiButton type="button" appearance="secondary" size="s"
        [attr.aria-label]="'Проголосовать: ' + topicLabel()" title="Добавить голос"
        [disabled]="disabled() || remaining() === 0" (click)="adjust.emit(1)">+</button>
    </div>
  `,
  styleUrl: './meeting-topic-votes.scss',
})
export class MeetingTopicVotes {
  readonly topic = input.required<MeetingTopic>();
  readonly topicLabel = input.required<string>();
  readonly remaining = input.required<number>();
  readonly disabled = input(false);
  readonly adjust = output<-1 | 1>();
}
