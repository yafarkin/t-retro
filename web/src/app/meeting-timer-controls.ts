import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TuiButton } from '@taiga-ui/core';

@Component({
  selector: 'app-meeting-timer-controls',
  imports: [FormsModule, TuiButton],
  template: `
    @if (canManage()) {
      <button tuiButton class="timer-button" type="button" appearance="secondary" size="s"
        [attr.aria-label]="remaining() === null ? 'Настроить таймер' : 'Управление таймером'"
        [disabled]="disabled()" (click)="modal.showModal()">
        @if (remaining() === null) {
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true">
            <circle cx="12" cy="14" r="7" /><path d="M9 2h6m-3 0v5m5 2 2-2m-7 3v4l2 2" />
          </svg>
          Таймер
        } @else {
          <span class="timer-countdown" role="timer">{{ formatted() }}</span>
        }
      </button>
      <dialog #modal class="meeting-dialog timer-dialog" aria-labelledby="timer-title">
        <header class="meeting-dialog-heading">
          <h2 id="timer-title">{{ remaining() === null ? 'Длительность таймера' : 'Управление таймером' }}</h2>
          <button tuiButton type="button" appearance="secondary" size="s" aria-label="Закрыть таймер" (click)="modal.close()">×</button>
        </header>
        <form (ngSubmit)="apply(modal)">
          <label for="timer-minutes">Минуты</label>
          <div class="timer-entry">
            <input id="timer-minutes" type="number" name="timerMinutes" step="any" autofocus
              [ngModel]="minutes()" (ngModelChange)="minutes.set($event)" />
            <button tuiButton type="submit" size="s" [disabled]="seconds() === 0 || disabled()">
              {{ remaining() === null ? 'Запустить таймер' : 'Добавить время' }}
            </button>
          </div>
        </form>
        <h3>Быстрый выбор</h3>
        <div class="timer-presets">
          @for (preset of presets; track preset) {
            <button tuiButton type="button" appearance="secondary" size="s" [attr.aria-pressed]="minutes() === preset"
              (click)="minutes.set(preset)">{{ preset }} мин</button>
          }
        </div>
        @if (remaining() !== null) {
          <button class="stop-timer" tuiButton type="button" appearance="secondary" size="s" [disabled]="disabled()"
            (click)="stop.emit(); modal.close()">Остановить таймер</button>
        }
      </dialog>
    } @else if (remaining() !== null) {
      <span class="timer-countdown" role="timer" aria-label="Осталось времени">{{ formatted() }}</span>
    }
  `,
  styleUrls: ['./meeting-dialog.scss', './meeting-timer-controls.scss'],
})
export class MeetingTimerControls {
  readonly remaining = input<number | null>(null);
  readonly canManage = input(false);
  readonly disabled = input(false);
  readonly start = output<number>();
  readonly add = output<number>();
  readonly stop = output();
  readonly minutes = signal<number | null>(1);
  readonly presets = [1, 5, 10, 15, 30, 60];
  readonly seconds = computed(() => {
    const minutes = this.minutes();
    if (minutes === null || !Number.isFinite(minutes)) {
      return 0;
    }
    const seconds = Math.round(minutes * 60);
    return seconds > 0 && seconds <= 2147483647 ? seconds : 0;
  });
  readonly formatted = computed(() => {
    const total = this.remaining() ?? 0;
    const minutes = String(Math.floor(total / 60)).padStart(2, '0');
    const seconds = String(total % 60).padStart(2, '0');
    return `${minutes}:${seconds}`;
  });

  apply(modal: HTMLDialogElement): void {
    const seconds = this.seconds();
    if (seconds === 0 || this.disabled()) {
      return;
    }
    if (this.remaining() === null) {
      this.start.emit(seconds);
    } else {
      this.add.emit(seconds);
    }
    modal.close();
  }
}
