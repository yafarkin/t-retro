import { Injectable, signal, TemplateRef } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class MeetingHeaderService {
  readonly title = signal<string | null>(null);
  readonly invite = signal<TemplateRef<unknown> | null>(null);
}
