import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { EditableColumn, MeetingTemplate, TemplateVisibility } from './meeting.models';

export interface TemplatePayload {
  name: string;
  visibility: TemplateVisibility;
  columns: EditableColumn[];
}

@Injectable({ providedIn: 'root' })
export class TemplateService {
  constructor(private readonly http: HttpClient) {}

  list(): Observable<MeetingTemplate[]> {
    return this.http.get<MeetingTemplate[]>('/api/templates');
  }

  create(payload: TemplatePayload): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/templates', payload);
  }

  update(id: string, payload: TemplatePayload): Observable<void> {
    return this.http.put<void>(`/api/templates/${id}`, payload);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/templates/${id}`);
  }
}
