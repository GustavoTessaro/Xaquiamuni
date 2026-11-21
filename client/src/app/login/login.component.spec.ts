import { TemaAdminService } from '../admin/tema-admin.service';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import {
  AuthService,
  AuthSession,
  ResendConfirmationEmailResponse,
} from '../core/auth/auth.service';
import { routes } from '../app.routes';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let login: ReturnType<typeof vi.fn<AuthService['login']>>;
  let forgotPassword: ReturnType<typeof vi.fn<AuthService['forgotPassword']>>;
  let obterSessao: ReturnType<typeof vi.fn<AuthService['obterSessao']>>;
  let logout: ReturnType<typeof vi.fn<AuthService['logout']>>;
  let reenviarConfirmacaoEmail: ReturnType<
    typeof vi.fn<AuthService['reenviarConfirmacaoEmail']>
  >;

  beforeEach(async () => {
    login = vi.fn<AuthService['login']>();
    login.mockReturnValue(new Observable<void>());
    forgotPassword = vi.fn<AuthService['forgotPassword']>();
    obterSessao = vi.fn<AuthService['obterSessao']>();
    obterSessao.mockReturnValue(of(null));
    logout = vi.fn<AuthService['logout']>();
    logout.mockReturnValue(new Observable<void>());
    reenviarConfirmacaoEmail = vi.fn<AuthService['reenviarConfirmacaoEmail']>();
    reenviarConfirmacaoEmail.mockReturnValue(
      new Observable<ResendConfirmationEmailResponse>(),
    );

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter(routes),
        {
          provide: AuthService,
          useValue: {
            login,
            forgotPassword,
            obterSessao,
            logout,
            reenviarConfirmacaoEmail,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('mantém o login público fora do tema administrativo', () => {
    TestBed.inject(TemaAdminService).alternar();
    try {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('[data-admin-theme], [data-admin-login-theme]')).toBeNull();
    } finally {
      localStorage.removeItem('xaquiamuni:admin:tema');
    }
  });

  it('checks /me on initialization and shows a discreet loading state', () => {
    const result = new Subject<AuthSession | null>();
    obterSessao.mockClear();
    obterSessao.mockReturnValue(result);
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(obterSessao).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent)
      .toContain('Verificando sua sessão');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();

    result.next(null);
    result.complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
  });

  it('shows an active session and the logout button when /me returns a client', () => {
    obterSessao.mockClear();
    obterSessao.mockReturnValue(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sessão ativa');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.querySelector('button').textContent).toContain('Sair');
    expectNoResendLink(fixture);
  });

  it('shows the login form when /me indicates no session', () => {
    obterSessao.mockReturnValue(of(null));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#email')).not.toBeNull();
    expectResendLink(fixture);
  });

  it('does not treat HTTP 403 as no session and allows retry', () => {
    obterSessao.mockClear();
    obterSessao
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 403 })))
      .mockReturnValueOnce(of(null));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Não foi possível verificar sua sessão.');
    clickButton(fixture, 'Tentar novamente');
    fixture.detectChanges();

    expect(obterSessao).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
  });

  it('keeps the session check in an error state after network failure until retry', () => {
    obterSessao.mockClear();
    obterSessao
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 0,
        error: new ProgressEvent('error'),
      })))
      .mockReturnValueOnce(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível verificar sua sessão.');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    clickButton(fixture, 'Tentar novamente');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sessão ativa');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('renders and validates required and malformed login fields', () => {
    expect(fixture.nativeElement.querySelector('h2').textContent).toContain('Entrar na sua conta');
    expect(fixture.nativeElement.querySelector('#email')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#senha')).not.toBeNull();

    submit(fixture);
    expect(fixture.nativeElement.querySelector('#email-error').textContent).toContain('Informe seu e-mail.');
    expect(fixture.nativeElement.querySelector('#senha-error').textContent).toContain('Informe sua senha.');

    setField(fixture, 'email', 'email-invalido');
    setField(fixture, 'senha', 'senha');
    submit(fixture);
    expect(fixture.nativeElement.querySelector('#email-error').textContent)
      .toContain('Informe um endereço de e-mail válido.');
    expect(login).not.toHaveBeenCalled();
  });

  it('toggles the login password independently without changing the form or submitting', () => {
    setValidFields(fixture);
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    const password = fixture.nativeElement.querySelector('#senha') as HTMLInputElement;
    const submitEvent = vi.fn();
    form.addEventListener('submit', submitEvent);
    const initialValue = password.value;
    const initialValidity = form.checkValidity();
    const sessionChecks = obterSessao.mock.calls.length;

    expect(password.type).toBe('password');
    expect(password.autocomplete).toBe('current-password');
    const show = fixture.nativeElement.querySelector(
      'button[aria-label="Mostrar senha"]',
    ) as HTMLButtonElement;
    expect(show.type).toBe('button');
    expect(show.querySelector('svg[aria-hidden="true"]')).not.toBeNull();

    show.click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('text');
    expect(fixture.nativeElement.querySelector('button[aria-label="Ocultar senha"]')).not.toBeNull();
    expect(password.value).toBe(initialValue);
    expect(form.checkValidity()).toBe(initialValidity);

    const hide = fixture.nativeElement.querySelector(
      'button[aria-label="Ocultar senha"]',
    ) as HTMLButtonElement;
    hide.click();
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('password');
    expect(fixture.nativeElement.querySelector('button[aria-label="Mostrar senha"]')).not.toBeNull();
    expect(submitEvent).not.toHaveBeenCalled();
    expect(login).not.toHaveBeenCalled();
    expect(obterSessao).toHaveBeenCalledTimes(sessionChecks);
  });

  it('does not send an invalid form', () => {
    submit(fixture);
    expect(login).not.toHaveBeenCalled();
  });

  it('shows an active session only after login succeeds and /me confirms it', () => {
    login.mockReturnValue(of(undefined));
    obterSessao.mockClear();
    obterSessao
      .mockReturnValueOnce(of(null))
      .mockReturnValueOnce(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    setValidFields(fixture);

    submit(fixture);
    fixture.detectChanges();

    expect(login).toHaveBeenCalledOnce();
    expect(login).toHaveBeenCalledWith('ana@example.test', 'senha-de-teste');
    expect(obterSessao).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).toContain('Sessão ativa');
    expect(fixture.nativeElement.textContent).toContain('Login realizado com sucesso.');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('updates the login DOM after asynchronous login and session confirmation', () => {
    const loginResult = new Subject<void>();
    const sessionResult = new Subject<AuthSession | null>();
    login.mockReturnValue(loginResult);
    obterSessao.mockClear();
    obterSessao
      .mockReturnValueOnce(of(null))
      .mockReturnValueOnce(sessionResult);
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
    setValidFields(fixture);
    submit(fixture);
    expect(fixture.nativeElement.textContent).toContain('Autenticando sua conta.');

    loginResult.next();
    loginResult.complete();
    fixture.detectChanges();

    expect(obterSessao).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent)
      .toContain('Verificando sua sessão');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Você está conectado');

    sessionResult.next({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' });
    sessionResult.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sessão ativa');
    expect(fixture.nativeElement.textContent).toContain('Você está conectado.');
    expect(fixture.nativeElement.textContent).toContain('Login realizado com sucesso.');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('does not claim login when /me confirms there is no session', () => {
    login.mockReturnValue(of(undefined));
    obterSessao.mockClear();
    obterSessao.mockReturnValueOnce(of(null)).mockReturnValueOnce(of(null));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    setValidFields(fixture);
    (fixture.nativeElement.querySelector(
      'button[aria-label="Mostrar senha"]',
    ) as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('text');

    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('password');
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).value).toBe('');
    expect(fixture.nativeElement.querySelector('button[aria-label="Mostrar senha"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Não foi possível confirmar a sessão.');
    expect(fixture.nativeElement.textContent).not.toContain('Login realizado com sucesso.');
  });

  it('prevents duplicate login submissions while login is pending', () => {
    const result = new Subject<void>();
    login.mockReturnValue(result);
    setValidFields(fixture);

    submit(fixture);
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    submit(fixture);
    expect(login).toHaveBeenCalledTimes(1);

    result.next();
    result.complete();
    fixture.detectChanges();
    expect(obterSessao).toHaveBeenCalledTimes(2);
  });

  it('shows a generic message for HTTP 401 login failure', () => {
    login.mockReturnValue(throwError(() =>
      new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' }),
    ));
    setValidFields(fixture);

    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent.trim())
      .toBe('E-mail ou senha inválidos.');
    expectResendLink(fixture);
  });

  it('shows safe messages for server and network login failures', () => {
    const failures = [
      new HttpErrorResponse({ status: 500, error: 'internal server details' }),
      new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }),
    ];

    for (const failure of failures) {
      login.mockReturnValue(throwError(() => failure));
      setValidFields(fixture);
      submit(fixture);
      fixture.detectChanges();

      const message = fixture.nativeElement.querySelector('[role="alert"]').textContent;
      expect(message).toContain('Não foi possível entrar no momento.');
      expect(message).not.toContain('internal server details');
    }
  });

  it('shows pending confirmation only for the exact HTTP 403 code and does not check the session', () => {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 403,
      error: {
        code: 'EMAIL_NOT_CONFIRMED',
        detail: 'Confirme seu e-mail antes de entrar na sua conta.',
      },
    })));
    setValidFields(fixture);

    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#email-confirmation-title').textContent.trim())
      .toBe('Confirme seu e-mail');
    expect(fixture.nativeElement.textContent)
      .toContain('Sua conta está quase pronta! Confirme seu endereço de e-mail para acessar sua conta.');
    expect(fixture.nativeElement.textContent).toContain('caixa de entrada e a pasta de spam');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Confirme seu e-mail antes de entrar na sua conta.');
    expect(obterSessao).toHaveBeenCalledOnce();
    expect(reenviarConfirmacaoEmail).not.toHaveBeenCalled();
    expect(login).toHaveBeenCalledOnce();
    expectNoResendLink(fixture);
  });

  it('keeps HTTP 401 and other HTTP 403 login failures generic', () => {
    const failures = [
      new HttpErrorResponse({ status: 401, error: { code: 'EMAIL_NOT_CONFIRMED' } }),
      new HttpErrorResponse({ status: 403, error: { code: 'OTHER_FORBIDDEN' } }),
      new HttpErrorResponse({ status: 403, error: { detail: 'private backend detail' } }),
    ];

    for (const failure of failures) {
      login.mockReturnValue(throwError(() => failure));
      setValidFields(fixture);
      submit(fixture);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#email-confirmation-title')).toBeNull();
      expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
      expect(fixture.nativeElement.textContent).not.toContain('private backend detail');
      expect(reenviarConfirmacaoEmail).not.toHaveBeenCalled();
    }

    expect(obterSessao).toHaveBeenCalledOnce();
  });

  it('resends manually to the login email through AuthService and prevents simultaneous requests', () => {
    const result = new Subject<ResendConfirmationEmailResponse>();
    reenviarConfirmacaoEmail.mockReturnValue(result);
    enterEmailConfirmationPending(fixture, login);

    clickButton(fixture, 'Reenviar e-mail de confirmação');
    fixture.detectChanges();

    const resendButton = fixture.nativeElement.querySelector(
      'button[aria-busy="true"]',
    ) as HTMLButtonElement;
    expect(reenviarConfirmacaoEmail).toHaveBeenCalledOnce();
    expect(reenviarConfirmacaoEmail).toHaveBeenCalledWith('ana@example.test');
    expect(resendButton.disabled).toBe(true);
    resendButton.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(reenviarConfirmacaoEmail).toHaveBeenCalledOnce();
    expect(obterSessao).toHaveBeenCalledOnce();

    result.next({ message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.' });
    result.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Solicitação recebida.');
    expect(fixture.nativeElement.textContent).not.toContain('e-mail foi enviado');
  });

  it('respects Retry-After on HTTP 429 and waits for a manual retry', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00.000Z'));

    try {
      const page = fixture;
      enterEmailConfirmationPending(page, login);
      reenviarConfirmacaoEmail
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
          status: 429,
          headers: new HttpHeaders({ 'Retry-After': '3' }),
          error: { detail: 'private rate limit detail' },
        })))
        .mockReturnValueOnce(of({
          message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.',
        }));

      clickButton(page, 'Reenviar e-mail de confirmação');
      page.detectChanges();

      const resendButton = Array.from<HTMLButtonElement>(page.nativeElement.querySelectorAll('button'))
        .find((button) => button.textContent?.includes('Reenviar e-mail de confirmação')
          || button.textContent?.includes('Solicitando'));
      expect(page.nativeElement.textContent).toContain('Aguarde 3 segundos');
      expect(page.nativeElement.textContent).not.toContain('private rate limit detail');
      expect(resendButton?.disabled).toBe(true);

      await vi.advanceTimersByTimeAsync(2000);
      page.detectChanges();
      expect(page.nativeElement.textContent).toContain('Aguarde 1 segundo');
      expect(reenviarConfirmacaoEmail).toHaveBeenCalledOnce();

      await vi.advanceTimersByTimeAsync(1000);
      page.detectChanges();
      expect(page.nativeElement.textContent)
        .toContain('O período de espera terminou. Você pode tentar novamente.');

      clickButton(page, 'Reenviar e-mail de confirmação');
      page.detectChanges();
      expect(reenviarConfirmacaoEmail).toHaveBeenCalledTimes(2);
      expect(page.nativeElement.textContent).toContain('Solicitação recebida.');
    } finally {
      vi.useRealTimers();
    }
  });

  it('allows manual retry after a temporary resend failure', () => {
    enterEmailConfirmationPending(fixture, login);
    reenviarConfirmacaoEmail
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 503,
        error: 'private server detail',
      })))
      .mockReturnValueOnce(of({
        message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.',
      }));

    clickButton(fixture, 'Reenviar e-mail de confirmação');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tente novamente mais tarde.');
    expect(fixture.nativeElement.textContent).not.toContain('private server detail');

    clickButton(fixture, 'Reenviar e-mail de confirmação');
    fixture.detectChanges();
    expect(reenviarConfirmacaoEmail).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).toContain('Solicitação recebida.');
  });

  it('returns to login with the email retained in memory and the password cleared', () => {
    const urlBefore = window.location.href;
    const localStorageWrite = vi.spyOn(window.localStorage, 'setItem');
    const sessionStorageWrite = vi.spyOn(window.sessionStorage, 'setItem');
    enterEmailConfirmationPending(fixture, login);
    clickButton(fixture, 'Voltar para o login');

    const emailInput = fixture.nativeElement.querySelector('#email') as HTMLInputElement;
    const passwordInput = fixture.nativeElement.querySelector('#senha') as HTMLInputElement;
    expect(emailInput.value).toBe('ana@example.test');
    expect(passwordInput.value).toBe('');
    expect(passwordInput.type).toBe('password');
    expectResendLink(fixture);
    expect(fixture.nativeElement.querySelector('#email-confirmation-title')).toBeNull();
    expect(window.location.href).toBe(urlBefore);
    expect(window.location.search).toBe('');
    expect(window.location.hash).toBe('');
    expect(urlBefore).not.toContain('ana@example.test');
    expect(urlBefore).not.toContain('senha-de-teste');
    expect(localStorageWrite).not.toHaveBeenCalled();
    expect(sessionStorageWrite).not.toHaveBeenCalled();
    expect(obterSessao).toHaveBeenCalledOnce();
  });

  it('allows the same client to log in after returning from email confirmation', () => {
    enterEmailConfirmationPending(fixture, login);
    clickButton(fixture, 'Voltar para o login');

    login.mockReturnValue(of(undefined));
    obterSessao.mockReturnValue(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }));
    setField(fixture, 'senha', 'senha-de-teste');
    submit(fixture);
    fixture.detectChanges();

    expect(login).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).toContain('Sessão ativa');
    expect(fixture.nativeElement.textContent).toContain('Login realizado com sucesso.');
  });

  it('calls logout and returns to the form only after /me confirms no session', () => {
    obterSessao.mockClear();
    obterSessao
      .mockReturnValueOnce(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }))
      .mockReturnValueOnce(of(null));
    logout.mockReturnValue(of(undefined));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    clickButton(fixture, 'Sair');
    fixture.detectChanges();

    expect(logout).toHaveBeenCalledOnce();
    expect(obterSessao).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
  });

  it('prevents duplicate logout actions while logout is pending', () => {
    obterSessao.mockClear();
    obterSessao.mockReturnValue(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }));
    const result = new Subject<void>();
    logout.mockReturnValue(result);
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    clickButton(fixture, 'Sair');
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    clickButton(fixture, 'Saindo');
    expect(logout).toHaveBeenCalledOnce();

    result.next();
    result.complete();
    fixture.detectChanges();
    expect(obterSessao).toHaveBeenCalledTimes(2);
  });

  it('keeps the active session state when logout fails', () => {
    obterSessao.mockClear();
    obterSessao.mockReturnValue(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }));
    logout.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    clickButton(fixture, 'Sair');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sessão ativa');
    expect(fixture.nativeElement.textContent).toContain('Não foi possível encerrar a sessão.');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('shows a retryable session-check error when logout cannot be confirmed', () => {
    obterSessao.mockClear();
    obterSessao
      .mockReturnValueOnce(of({ userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df' }))
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 403 })));
    logout.mockReturnValue(of(undefined));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    clickButton(fixture, 'Sair');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível verificar sua sessão.');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('links to client registration', () => {
    const link = fixture.nativeElement.querySelector('a[routerLink="/cadastro"]') as HTMLAnchorElement;
    expect(link).not.toBeNull();
    expect(link.textContent).toContain('Cadastre-se');
  });

  it('navigates from an empty login form to password recovery without submitting or requesting', async () => {
    obterSessao.mockClear();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/login', LoginComponent);
    harness.fixture.detectChanges();

    const form = harness.routeNativeElement?.querySelector('form') as HTMLFormElement;
    const link = harness.routeNativeElement?.querySelector(
      'a[routerLink="/forgot-password"]',
    ) as HTMLAnchorElement;
    const submit = vi.fn();
    form.addEventListener('submit', submit);

    expect(form).not.toBeNull();
    expect((harness.routeNativeElement?.querySelector('#email') as HTMLInputElement).value).toBe('');
    expect((harness.routeNativeElement?.querySelector('#senha') as HTMLInputElement).value).toBe('');
    expect(form.checkValidity()).toBe(false);
    expect(link).not.toBeNull();
    expect(link.href).toContain('/forgot-password');
    expect(link.closest('form')).toBe(form);
    expect(new URL(link.href).search).toBe('');
    expect(new URL(link.href).hash).toBe('');

    const sessionChecks = obterSessao.mock.calls.length;
    link.click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/forgot-password');
    expect(harness.routeNativeElement?.textContent).toContain('Esqueci minha senha');
    expect(submit).not.toHaveBeenCalled();
    expect(login).not.toHaveBeenCalled();
    expect(forgotPassword).not.toHaveBeenCalled();
    expect(obterSessao).toHaveBeenCalledTimes(sessionChecks);
    expect(logout).not.toHaveBeenCalled();
  });

  it('keeps the confirmation link in the normal login form without sensitive route data', () => {
    expectResendLink(fixture);
  });
});

function expectResendLink(fixture: ComponentFixture<LoginComponent>): void {
  const link = fixture.nativeElement.querySelector(
    'a[routerLink="/confirm-email"]',
  ) as HTMLAnchorElement;
  expect(link).not.toBeNull();
  expect(link.parentElement?.textContent).toContain('Não recebeu o e-mail de confirmação?');
  expect(link.textContent).toContain('Solicitar nova confirmação');
  expect(link.href).toContain('/confirm-email');
  expect(new URL(link.href).search).toBe('');
  expect(new URL(link.href).hash).toBe('');
}

function expectNoResendLink(fixture: ComponentFixture<LoginComponent>): void {
  expect(fixture.nativeElement.querySelector('a[routerLink="/confirm-email"]')).toBeNull();
}

function setValidFields(fixture: ComponentFixture<LoginComponent>): void {
  setField(fixture, 'email', 'ana@example.test');
  setField(fixture, 'senha', 'senha-de-teste');
}

function enterEmailConfirmationPending(
  fixture: ComponentFixture<LoginComponent>,
  login: ReturnType<typeof vi.fn<AuthService['login']>>,
): void {
  login.mockReturnValue(throwError(() => new HttpErrorResponse({
    status: 403,
    error: { code: 'EMAIL_NOT_CONFIRMED' },
  })));
  setValidFields(fixture);
  submit(fixture);
  fixture.detectChanges();
}

function setField(fixture: ComponentFixture<LoginComponent>, id: string, value: string): void {
  const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<LoginComponent>): void {
  (fixture.nativeElement.querySelector('form') as HTMLFormElement)
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  fixture.detectChanges();
}

function clickButton(fixture: ComponentFixture<LoginComponent>, text: string): void {
  const button = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
    .find((candidate) => candidate.textContent?.includes(text));
  if (!button) {
    throw new Error(`Button containing "${text}" was not found.`);
  }
  button.click();
  fixture.detectChanges();
}
