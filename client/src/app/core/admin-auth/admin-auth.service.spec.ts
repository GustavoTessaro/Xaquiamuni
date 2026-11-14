import { DOCUMENT } from '@angular/common';
import {
  HttpErrorResponse,
  provideHttpClient,
  withXsrfConfiguration,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminAuthService } from './admin-auth.service';

describe('AdminAuthService', () => {
  let service: AdminAuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AdminAuthService,
        provideHttpClient(
          withXsrfConfiguration({
            cookieName: 'XSRF-TOKEN',
            headerName: 'X-XSRF-TOKEN',
          }),
        ),
        provideHttpClientTesting(),
        {
          provide: DOCUMENT,
          useValue: { cookie: 'XSRF-TOKEN=cookie-token' },
        },
      ],
    });

    service = TestBed.inject(AdminAuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('gets an antiforgery token before posting administrator credentials', () => {
    let completed = false;
    service.login('admin@example.test', 'senha-ficticia').subscribe(() => {
      completed = true;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/admin/auth/login');
    csrf.flush({ requestToken: 'admin-login-request-token' });

    const login = http.expectOne('/api/admin/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({
      email: 'admin@example.test',
      senha: 'senha-ficticia',
    });
    expect(login.request.headers.get('X-XSRF-TOKEN')).toBe('admin-login-request-token');
    login.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('returns no session for anonymous or non-admin sessions', () => {
    let session: unknown = 'pending';
    service.getCurrentSession().subscribe((value) => {
      session = value;
    });

    const request = http.expectOne('/api/admin/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush(null, { status: 403, statusText: 'Forbidden' });

    expect(session).toBeNull();
  });

  it('propagates session-check failures other than unauthorized and forbidden', () => {
    let error: unknown;
    service.getCurrentSession().subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });

    http.expectOne('/api/admin/auth/me').flush(
      { title: 'Internal Server Error' },
      { status: 500, statusText: 'Internal Server Error' },
    );

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(500);
  });

  it('uses the shared antiforgery flow for administrator logout', () => {
    let completed = false;
    service.logout().subscribe(() => {
      completed = true;
    });

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'admin-logout-token' });
    const logout = http.expectOne('/api/admin/auth/logout');
    expect(logout.request.method).toBe('POST');
    expect(logout.request.body).toBeNull();
    expect(logout.request.headers.get('X-XSRF-TOKEN')).toBe('admin-logout-token');
    logout.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });
});
