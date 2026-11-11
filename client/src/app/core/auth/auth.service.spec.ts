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
import { AuthService } from './auth.service';
import { CsrfService } from '../security/csrf.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        CsrfService,
        provideHttpClient(
          withXsrfConfiguration({
            cookieName: 'XSRF-TOKEN',
            headerName: 'X-XSRF-TOKEN',
          }),
        ),
        provideHttpClientTesting(),
        {
          provide: DOCUMENT,
          useValue: { cookie: 'XSRF-TOKEN=cookie-token-not-request-token' },
        },
      ],
    });

    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('gets the CSRF request token before posting login and handles 204', () => {
    let completed = false;
    service.login('cliente@example.test', 'senha-ficticia').subscribe(() => {
      completed = true;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/auth/login');
    csrf.flush({ requestToken: 'request-token-from-response' });

    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({
      email: 'cliente@example.test',
      senha: 'senha-ficticia',
    });
    expect(login.request.headers.get('X-XSRF-TOKEN')).toBe('request-token-from-response');
    expect(login.request.headers.get('X-XSRF-TOKEN')).not.toBe('cookie-token-not-request-token');
    expect(login.request.withCredentials).toBe(false);
    login.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('propagates login HTTP 401', () => {
    let error: HttpErrorResponse | undefined;
    service.login('cliente@example.test', 'senha-invalida').subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'login-request-token' });
    http.expectOne('/api/auth/login').flush(null, {
      status: 401,
      statusText: 'Unauthorized',
    });

    expect(error?.status).toBe(401);
  });

  it('gets the CSRF request token before posting email confirmation', () => {
    let completed = false;
    const email = 'cliente+tag@example.test';
    const token = 'token+/=special';
    service.confirmarEmail(email, token).subscribe(() => {
      completed = true;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/auth/confirm-email');
    csrf.flush({ requestToken: 'confirmation-request-token' });

    const confirmation = http.expectOne('/api/auth/confirm-email');
    expect(confirmation.request.method).toBe('POST');
    expect(confirmation.request.urlWithParams).toBe('/api/auth/confirm-email');
    expect(confirmation.request.body).toEqual({ email, token });
    expect(confirmation.request.headers.get('X-XSRF-TOKEN')).toBe('confirmation-request-token');
    confirmation.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('gets the current session from /me without accepting a user ID', () => {
    let session: unknown;
    service.obterSessao().subscribe((value) => {
      session = value;
    });

    const request = http.expectOne('/api/auth/me');
    expect(request.request.method).toBe('GET');
    expect(request.request.body).toBeNull();
    request.flush({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' });

    expect(session).toEqual({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' });
  });

  it('interprets HTTP 401 from /me as no session', () => {
    let session: unknown = 'pending';
    service.obterSessao().subscribe((value) => {
      session = value;
    });

    http.expectOne('/api/auth/me').flush(null, {
      status: 401,
      statusText: 'Unauthorized',
    });

    expect(session).toBeNull();
  });

  it('propagates HTTP 403 from /me instead of treating it as logged out', () => {
    let error: HttpErrorResponse | undefined;
    service.obterSessao().subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });

    http.expectOne('/api/auth/me').flush(null, {
      status: 403,
      statusText: 'Forbidden',
    });

    expect(error?.status).toBe(403);
  });

  it('gets the CSRF request token before posting logout', () => {
    let completed = false;
    service.logout().subscribe(() => {
      completed = true;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/auth/logout');
    csrf.flush({ requestToken: 'logout-request-token' });

    const logout = http.expectOne('/api/auth/logout');
    expect(logout.request.method).toBe('POST');
    expect(logout.request.body).toBeNull();
    expect(logout.request.headers.get('X-XSRF-TOKEN')).toBe('logout-request-token');
    expect(logout.request.headers.get('X-XSRF-TOKEN')).not.toBe('cookie-token-not-request-token');
    expect(logout.request.withCredentials).toBe(false);
    logout.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('propagates logout HTTP failures', () => {
    let error: HttpErrorResponse | undefined;
    service.logout().subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'logout-request-token' });
    http.expectOne('/api/auth/logout').flush(null, {
      status: 401,
      statusText: 'Unauthorized',
    });

    expect(error?.status).toBe(401);
  });
});
