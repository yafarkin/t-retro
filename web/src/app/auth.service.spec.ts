import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('stores the employee after login', () => {
    const employee = { id: 'id-a', subject: 'employee-a', displayName: 'Сотрудник А' };
    auth.login('employee-a', 'Сотрудник А').subscribe();

    const request = http.expectOne('/api/dev/login');
    expect(request.request.body).toEqual({ subject: 'employee-a', displayName: 'Сотрудник А' });
    request.flush(employee);

    expect(auth.user()).toEqual(employee);
    expect(auth.busy()).toBe(false);
  });

  it('shows a Russian connection error', () => {
    auth.login('employee-a', 'Сотрудник А').subscribe({ error: () => undefined });
    http.expectOne('/api/dev/login').error(new ProgressEvent('error'));

    expect(auth.errorMessage()).toBe('Нет соединения с сервером. Проверьте запуск API.');
  });
});
