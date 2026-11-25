import { Component, input, output, signal } from '@angular/core';
import { TuiButton } from '@taiga-ui/core';
import { MeetingTopic } from './meeting.models';

@Component({
  selector: 'app-meeting-topic-votes',
  imports: [TuiButton],
  template: `
    @let current = topic();
    <div class="vote-actions">
      <output class="my-votes" aria-label="Ваши голоса">Мои {{ current.myVotes }}</output>
      <button tuiButton type="button" appearance="secondary" size="s"
        [attr.aria-label]="'Отозвать голос: ' + topicLabel()" title="Отозвать голос"
        [disabled]="disabled() || current.myVotes === 0" (click)="adjust.emit(-1)">−</button>
      <button tuiButton type="button" appearance="secondary" size="s"
        [attr.aria-label]="'Проголосовать: ' + topicLabel()" title="Добавить голос"
        [disabled]="disabled() || remaining() === 0" (click)="adjust.emit(1)">+</button>
      @if (current.voters?.length) {
        <button class="total-votes breakdown-trigger" type="button" aria-label="Расклад голосов" title="Расклад голосов"
          [attr.aria-expanded]="breakdownOpen()" [attr.aria-controls]="'vote-breakdown-' + current.id"
          (click)="breakdownOpen.set(!breakdownOpen())">
          <output aria-label="Всего голосов">{{ current.totalVotes }}</output>
        </button>
      } @else {
        <output class="total-votes" aria-label="Всего голосов" title="Всего голосов">{{ current.totalVotes }}</output>
      }
    </div>
    @if (current.voters?.length) {
      <ul class="vote-breakdown" [id]="'vote-breakdown-' + current.id" [hidden]="!breakdownOpen()"
        role="list" aria-label="Расклад голосов">
        @for (voter of current.voters; track voter.userId) {
          <li><span>{{ voter.displayName }}</span><strong>{{ voter.count }}</strong></li>
        }
      </ul>
    }
  `,
  styleUrl: './meeting-topic-votes.scss',
})
export class MeetingTopicVotes {
  readonly topic = input.required<MeetingTopic>();
  readonly topicLabel = input.required<string>();
  readonly remaining = input.required<number>();
  readonly disabled = input(false);
  readonly adjust = output<-1 | 1>();
  readonly breakdownOpen = signal(false);
}
