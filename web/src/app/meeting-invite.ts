import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { TuiButton } from '@taiga-ui/core';
import qrcode from 'qrcode-generator';
import { apiErrorMessage } from './api-error';
import { Invite } from './meeting.models';
import { MeetingService } from './meeting.service';

@Component({
  selector: 'app-meeting-invite',
  imports: [TuiButton],
  template: `
    <button tuiButton type="button" appearance="secondary" size="s" [disabled]="disabled() || busy()" (click)="open()">Показать приглашение</button>
    <dialog #modal class="meeting-dialog" aria-labelledby="invite-title">
      <header class="meeting-dialog-heading">
        <h2 id="invite-title">Пригласить команду</h2>
        <button tuiButton type="button" appearance="secondary" size="s" aria-label="Закрыть приглашение" (click)="modal.close()">×</button>
      </header>
      @if (error(); as message) {
        <p class="field-error" role="alert">{{ message }}</p>
      }
      @if (invite(); as current) {
        <p>Отправьте участникам ссылку или QR-код.</p>
        <div class="invite-content">
          <div>
            <input #urlField class="invite-url" aria-label="Ссылка приглашения" readonly [value]="current.url" />
            <div class="invite-actions">
              <button tuiButton type="button" size="s" (click)="copy(urlField)" [disabled]="busy()">{{ copied() ? 'Скопировано' : 'Копировать ссылку' }}</button>
              @if (qrCode(); as image) {
                <a tuiButton appearance="secondary" size="s" [href]="image" download="t-retro-invite.gif">Скачать QR-код</a>
              }
              <button tuiButton type="button" size="s" appearance="flat" (click)="rotate()" [disabled]="busy() || disabled()">Заменить ссылку</button>
            </div>
            <p class="invite-version">Версия приглашения {{ current.version }}.</p>
          </div>
          @if (qrCode(); as image) {
            <img class="invite-qr" [src]="image" width="144" height="144" alt="QR-код приглашения" />
          }
        </div>
      } @else if (busy()) {
        <p role="status">Загружаем приглашение…</p>
      }
    </dialog>
  `,
  styleUrls: ['./meeting-dialog.scss', './meeting-invite.scss'],
})
export class MeetingInvite {
  private readonly service = inject(MeetingService);
  private readonly modal = viewChild.required<ElementRef<HTMLDialogElement>>('modal');
  readonly meetingId = input.required<string>();
  readonly disabled = input(false);
  readonly invite = signal<Invite | null>(null);
  readonly copied = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly qrCode = computed(() => {
    const url = this.invite()?.url;
    if (!url) {
      return null;
    }
    const code = qrcode(0, 'M');
    code.addData(url);
    code.make();
    return code.createDataURL(6);
  });

  open(): void {
    this.modal().nativeElement.showModal();
    this.invite.set(null);
    this.copied.set(false);
    this.error.set(null);
    this.busy.set(true);
    this.service.invite(this.meetingId()).subscribe({
      next: (invite) => this.finish(invite),
      error: (error: HttpErrorResponse) => this.fail(error),
    });
  }

  rotate(): void {
    if (!window.confirm('Отозвать прежнюю ссылку и создать новую?')) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.service.rotateInvite(this.meetingId()).subscribe({
      next: (invite) => this.finish(invite),
      error: (error: HttpErrorResponse) => this.fail(error),
    });
  }

  async copy(field: HTMLInputElement): Promise<void> {
    try {
      await navigator.clipboard.writeText(field.value);
      this.copied.set(true);
      this.error.set(null);
    } catch {
      field.focus();
      field.select();
      this.error.set('Скопируйте выделенную ссылку вручную.');
    }
  }

  private finish(invite: Invite): void {
    this.invite.set(invite);
    this.copied.set(false);
    this.busy.set(false);
  }

  private fail(error: HttpErrorResponse): void {
    this.error.set(apiErrorMessage(error));
    this.busy.set(false);
  }
}
