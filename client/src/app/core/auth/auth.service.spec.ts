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
import {
  AuthService,
  AuthSession,
  ForgotPasswordResponse,
  ResendConfirmationEmailResponse,
  ResetPasswordRequest,
} from './auth.service';
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

  it('gets the CSRF request token before posting forgot password and preserves the 202 response', () => {
    let response: ForgotPasswordResponse | undefined;
    const email = 'cliente+tag@example.test';
    service.forgotPassword(email).subscribe((value) => {
      response = value;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/auth/forgot-password');
    csrf.flush({ requestToken: 'forgot-password-request-token' });

    const forgot = http.expectOne('/api/auth/forgot-password');
    expect(forgot.request.method).toBe('POST');
    expect(forgot.request.urlWithParams).toBe('/api/auth/forgot-password');
    expect(forgot.request.body).toEqual({ email });
    expect(Object.keys(forgot.request.body as object)).toEqual(['email']);
    expect(forgot.request.headers.get('X-XSRF-TOKEN')).toBe('forgot-password-request-token');
    expect(forgot.request.headers.get('X-XSRF-TOKEN')).not.toBe('cookie-token-not-request-token');
    expect(forgot.request.withCredentials).toBe(false);
    const message = 'Se existir uma conta elegível, você receberá instruções.';
    forgot.flush({ message }, { status: 202, statusText: 'Accepted' });

    expect(response).toEqual({ message });
  });

  it('gets the CSRF request token before posting reset password without transforming its values', () => {
    let completed = false;
    const request: ResetPasswordRequest = {
      email: 'cliente+tag@example.test',
      token: 'token+/=%& with spaces',
      newPassword: '  Senha nova válida 987!  ',
    };
    service.resetPassword(request).subscribe(() => {
      completed = true;
    });

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/auth/reset-password');
    csrf.flush({ requestToken: 'reset-password-request-token' });

    const reset = http.expectOne('/api/auth/reset-password');
    expect(reset.request.method).toBe('POST');
    expect(reset.request.urlWithParams).toBe('/api/auth/reset-password');
    expect(reset.request.body).toEqual({
      email: request.email,
      token: request.token,
      newPassword: request.newPassword,
    });
    expect(Object.keys(reset.request.body as object)).toEqual([
      'email',
      'token',
      'newPassword',
    ]);
    expect(reset.request.body.token).toBe(request.token);
    expect(reset.request.body.newPassword).toBe(request.newPassword);
    expect(reset.request.headers.get('X-XSRF-TOKEN')).toBe('reset-password-request-token');
    expect(reset.request.headers.get('X-XSRF-TOKEN')).not.toBe('cookie-token-not-request-token');
    expect(reset.request.withCredentials).toBe(false);
    reset.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('propagates forgot password HTTP 400 and HTTP 429 including Retry-After without retrying', () => {
    let validationError: HttpErrorResponse | undefined;
    service.forgotPassword('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        validationError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'forgot-400-token' });
    http.expectOne('/api/auth/forgot-password').flush(
      { title: 'Requisição inválida' },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(validationError?.status).toBe(400);

    let rateLimitError: HttpErrorResponse | undefined;
    service.forgotPassword('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        rateLimitError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'forgot-429-token' });
    http.expectOne('/api/auth/forgot-password').flush(
      { message: 'Muitas solicitações. Tente novamente mais tarde.' },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '37' },
      },
    );

    expect(rateLimitError?.status).toBe(429);
    expect(rateLimitError?.headers.get('Retry-After')).toBe('37');
    expect(rateLimitError?.error).toEqual({
      message: 'Muitas solicitações. Tente novamente mais tarde.',
    });
    http.expectNone('/api/auth/forgot-password');
  });

  it('propagates reset password validation and policy HTTP 400 responses', () => {
    const errors: HttpErrorResponse[] = [];
    service.resetPassword({
      email: 'cliente@example.test',
      token: 'invalid-token',
      newPassword: 'senha-ficticia',
    }).subscribe({
      error: (value: HttpErrorResponse) => errors.push(value),
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reset-400-token' });
    http.expectOne('/api/auth/reset-password').flush(
      { detail: 'Não foi possível redefinir a senha.' },
      { status: 400, statusText: 'Bad Request' },
    );

    service.resetPassword({
      email: 'cliente@example.test',
      token: 'valid-synthetic-token',
      newPassword: 'senha-fraca',
    }).subscribe({
      error: (value: HttpErrorResponse) => errors.push(value),
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reset-policy-token' });
    http.expectOne('/api/auth/reset-password').flush(
      { errors: { NewPassword: ['A senha não atende à política.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(errors.map((error) => error.status)).toEqual([400, 400]);
    expect(errors[0].error).toEqual({ detail: 'Não foi possível redefinir a senha.' });
    expect(errors[1].error.errors.NewPassword).toEqual(['A senha não atende à política.']);
  });

  it('propagates reset password HTTP 429 and preserves Retry-After without retrying', () => {
    let error: HttpErrorResponse | undefined;
    service.resetPassword({
      email: 'cliente@example.test',
      token: 'valid-synthetic-token',
      newPassword: '  Senha válida 987!  ',
    }).subscribe({
      error: (value: HttpErrorResponse) => {
        error = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reset-429-token' });
    http.expectOne('/api/auth/reset-password').flush(
      { message: 'Muitas solicitações. Tente novamente mais tarde.' },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '51' },
      },
    );

    expect(error?.status).toBe(429);
    expect(error?.headers.get('Retry-After')).toBe('51');
    expect(error?.error.message).toBe('Muitas solicitações. Tente novamente mais tarde.');
    http.expectNone('/api/auth/reset-password');
  });

  it('propagates network and HTTP 5xx failures for both recovery methods', () => {
    let forgotNetworkError: HttpErrorResponse | undefined;
    service.forgotPassword('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        forgotNetworkError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'forgot-network-token' });
    http.expectOne('/api/auth/forgot-password').error(new ProgressEvent('error'));
    expect(forgotNetworkError?.status).toBe(0);

    let forgotServerError: HttpErrorResponse | undefined;
    service.forgotPassword('cliente@example.test').subscribe({
      error: (value: HttpErrorResponse) => {
        forgotServerError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'forgot-server-token' });
    http.expectOne('/api/auth/forgot-password').flush(
      { title: 'Falha interna' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    expect(forgotServerError?.status).toBe(503);

    let resetNetworkError: HttpErrorResponse | undefined;
    service.resetPassword({
      email: 'cliente@example.test',
      token: 'synthetic-token',
      newPassword: 'Senha válida 987!',
    }).subscribe({
      error: (value: HttpErrorResponse) => {
        resetNetworkError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reset-network-token' });
    http.expectOne('/api/auth/reset-password').error(new ProgressEvent('error'));
    expect(resetNetworkError?.status).toBe(0);

    let resetServerError: HttpErrorResponse | undefined;
    service.resetPassword({
      email: 'cliente@example.test',
      token: 'synthetic-token',
      newPassword: 'Senha válida 987!',
    }).subscribe({
      error: (value: HttpErrorResponse) => {
        resetServerError = value;
      },
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reset-server-token' });
    http.expectOne('/api/auth/reset-password').flush(
      { title: 'Falha interna' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    expect(resetServerError?.status).toBe(503);
  });

  it('calls recovery methods anonymously without login, logout, or session requests', () => {
    service.forgotPassword('cliente@example.test').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'anonymous-forgot-token' });
    http.expectOne('/api/auth/forgot-password').flush(
      { message: 'Solicitação recebida.' },
      { status: 202, statusText: 'Accepted' },
    );

    service.resetPassword({
      email: 'cliente@example.test',
      token: 'synthetic-token',
      newPassword: 'Senha válida 987!',
    }).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'anonymous-reset-token' });
    http.expectOne('/api/auth/reset-password').flush(null, {
      status: 204,
      statusText: 'No Content',
    });

    http.expectNone('/api/auth/login');
    http.expectNone('/api/auth/logout');
    http.expectNone('/api/auth/me');
  });

  it('does not change an established session when calling recovery methods', () => {
    let session: AuthSession | null = null;
    service.obterSessao().subscribe((value) => {
      session = value;
    });
    const sessionResponse = { userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' };
    http.expectOne('/api/auth/me').flush(sessionResponse);
    const sessionBeforeRecovery = session;

    service.forgotPassword('cliente@example.test').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'authenticated-forgot-token' });
    http.expectOne('/api/auth/forgot-password').flush(
      { message: 'Solicitação recebida.' },
      { status: 202, statusText: 'Accepted' },
    );

    service.resetPassword({
      email: 'cliente@example.test',
      token: 'synthetic-token',
      newPassword: 'Senha válida 987!',
    }).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'authenticated-reset-token' });
    http.expectOne('/api/auth/reset-password').flush(null, {
      status: 204,
      statusText: 'No Content',
    });

    expect(session).toBe(sessionBeforeRecovery);
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
