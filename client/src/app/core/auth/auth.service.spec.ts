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
import { AuthService, AuthSession, ResendConfirmationEmailResponse } from './auth.service';
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

  it('gets the existing CSRF request token before posting a resend request and preserves the 202 response', () => {
    let response: ResendConfirmationEmailResponse | undefined;
    const email = 'cliente+tag@example.test';
    service.reenviarConfirmacaoEmail(email).subscribe((value) => {
      response = value;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/auth/resend-confirmation-email');
    csrf.flush({ requestToken: 'resend-request-token' });

    const resend = http.expectOne('/api/auth/resend-confirmation-email');
    expect(resend.request.method).toBe('POST');
    expect(resend.request.urlWithParams).toBe('/api/auth/resend-confirmation-email');
    expect(resend.request.body).toEqual({ email });
    expect(Object.keys(resend.request.body as object)).toEqual(['email']);
    expect(resend.request.headers.get('X-XSRF-TOKEN')).toBe('resend-request-token');
    expect(resend.request.headers.get('X-XSRF-TOKEN')).not.toBe('cookie-token-not-request-token');
    expect(resend.request.withCredentials).toBe(false);
    resend.flush({
      message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.',
    }, { status: 202, statusText: 'Accepted' });

    expect(response).toEqual({
      message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.',
    });
  });

  it('propagates resend HTTP 400', () => {
    let error: HttpErrorResponse | undefined;
    service.reenviarConfirmacaoEmail('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'resend-request-token' });
    http.expectOne('/api/auth/resend-confirmation-email').flush(
      { title: 'Requisição inválida' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(error?.status).toBe(400);
  });

  it('propagates resend HTTP 429 and preserves Retry-After', () => {
    let error: HttpErrorResponse | undefined;
    service.reenviarConfirmacaoEmail('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'resend-request-token' });
    http.expectOne('/api/auth/resend-confirmation-email').flush(
      { message: 'Muitas solicitações. Tente novamente mais tarde.' },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '37' },
      },
    );

    expect(error?.status).toBe(429);
    expect(error?.headers.get('Retry-After')).toBe('37');
  });

  it('propagates resend network failures and HTTP 5xx without success conversion', () => {
    let networkError: HttpErrorResponse | undefined;
    service.reenviarConfirmacaoEmail('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        networkError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'network-request-token' });
    http.expectOne('/api/auth/resend-confirmation-email')
      .error(new ProgressEvent('error'));

    expect(networkError?.status).toBe(0);

    let serverError: HttpErrorResponse | undefined;
    service.reenviarConfirmacaoEmail('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        serverError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'server-request-token' });
    http.expectOne('/api/auth/resend-confirmation-email').flush(
      { title: 'Falha interna' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(serverError?.status).toBe(503);
  });

  it('does not change authentication session state when requesting a resend', () => {
    let session: AuthSession | null = null;
    service.obterSessao().subscribe((value) => {
      session = value;
    });
    const sessionResponse = { userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' };
    http.expectOne('/api/auth/me').flush(sessionResponse);
    const sessionBeforeResend = session;
    let completed = false;

    service.reenviarConfirmacaoEmail('cliente@example.test').subscribe(() => {
      completed = true;
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'state-request-token' });
    http.expectOne('/api/auth/resend-confirmation-email').flush(
      { message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.' },
      { status: 202, statusText: 'Accepted' },
    );

    expect(completed).toBe(true);
    expect(session).toBe(sessionBeforeResend);
    expect(session).toEqual(sessionResponse);
    http.expectNone('/api/auth/login');
    http.expectNone('/api/auth/logout');
    http.expectNone('/api/auth/me');
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
