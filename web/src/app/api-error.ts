import { HttpErrorResponse } from '@angular/common/http';

export function apiErrorMessage(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return 'Нет соединения с сервером. Проверьте запуск API.';
  }
  if (typeof error.error?.message === 'string') {
    return error.error.message;
  }
  return 'Сервер не выполнил запрос. Попробуйте ещё раз.';
}
