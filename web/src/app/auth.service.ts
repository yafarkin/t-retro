import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { catchError, finalize, map, Observable, of, tap, throwError } from 'rxjs';

export interface Employee {
  id: string;
  subject: string;
  displayName: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly user = signal<Employee | null>(null);
  readonly busy = signal(false);
  readonly errorMessage = signal<string | null>(null);

  constructor(private readonly http: HttpClient) {}

  load(): Observable<Employee | null> {
    return this.http.get<Employee>('/api/me').pipe(
      tap((employee) => this.user.set(employee)),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 401) {
          return of(null);
        }
        this.errorMessage.set(messageFor(error));
        return of(null);
      }),
    );
  }

  login(subject: string, displayName: string): Observable<Employee> {
    this.busy.set(true);
    this.errorMessage.set(null);
    return this.http.post<Employee>('/api/dev/login', { subject, displayName }).pipe(
      tap((employee) => this.user.set(employee)),
      catchError((error: HttpErrorResponse) => {
        const message = messageFor(error);
        this.errorMessage.set(message);
        return throwError(() => new Error(message));
      }),
      finalize(() => this.busy.set(false)),
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/dev/logout', {}).pipe(
      tap(() => this.user.set(null)),
      map(() => undefined),
    );
  }
}

function messageFor(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return 'Нет соединения с сервером. Проверьте запуск API.';
  }
  if (typeof error.error?.message === 'string') {
    return error.error.message;
  }
  return 'Сервер не выполнил запрос. Попробуйте ещё раз.';
}
