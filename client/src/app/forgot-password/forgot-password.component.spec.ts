import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, TimeoutError, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuthService, ForgotPasswordResponse } from '../core/auth/auth.service';
import { routes } from '../app.routes';
import { ForgotPasswordComponent } from './forgot-password.component';

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent> | undefined;
  let forgotPassword: ReturnType<typeof vi.fn<AuthService['forgotPassword']>>;
  let login: ReturnType<typeof vi.fn<AuthService['login']>>;
  let obterSessao: ReturnType<typeof vi.fn<AuthService['obterSessao']>>;
  let logout: ReturnType<typeof vi.fn<AuthService['logout']>>;

  beforeEach(async () => {
    forgotPassword = vi.fn<AuthService['forgotPassword']>();
    forgotPassword.mockReturnValue(new Observable<ForgotPasswordResponse>());
    login = vi.fn<AuthService['login']>();
    obterSessao = vi.fn<AuthService['obterSessao']>();
    logout = vi.fn<AuthService['logout']>();

    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter(routes),
        { provide: AuthService, useValue: { forgotPassword, login, obterSessao, logout } },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('renders the public route with the recovery form and login link', async () => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/forgot-password', ForgotPasswordComponent);
    harness.fixture.detectChanges();

    expect(component).toBeInstanceOf(ForgotPasswordComponent);
    expect(harness.routeNativeElement?.textContent).toContain('Esqueci minha senha');
    expect(harness.routeNativeElement?.textContent)
      .toContain('Informe o e-mail cadastrado para receber as instruções de recuperação da sua senha.');
    const emailInput = harness.routeNativeElement?.querySelector('#email') as HTMLInputElement;
    expect(emailInput).not.toBeNull();
    expect(emailInput.type).toBe('email');
    expect(emailInput.autocomplete).toBe('email');
    expect(harness.routeNativeElement?.querySelector('label[for="email"]')).not.toBeNull();
    expect(harness.routeNativeElement?.querySelector('button[type="submit"]')?.textContent)
      .toContain('Enviar instruções');
    const loginLink = harness.routeNativeElement?.querySelector(
      'a[routerLink="/login"]',
    ) as HTMLAnchorElement;
    expect(loginLink).not.toBeNull();
    expect(loginLink.href).toContain('/login');
    expect(harness.routeNativeElement?.textContent).toContain('Voltar para o login');
    expect(forgotPassword).not.toHaveBeenCalled();
    expect(login).not.toHaveBeenCalled();
    expect(obterSessao).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
  });

  it('validates required, malformed, and overlong email without calling the service', () => {
    const page = createComponent();
    submit(page);
    page.detectChanges();
    expect(page.nativeElement.querySelector('#email-error')?.textContent).toContain(
      'Informe seu e-mail.',
    );

    setEmail(page, 'endereco-invalido');
    submit(page);
    page.detectChanges();
    expect(page.nativeElement.querySelector('#email-error')?.textContent).toContain(
      'Informe um endereço de e-mail válido.',
    );

    setEmail(page, `${'a'.repeat(250)}@example.test`);
    submit(page);
    page.detectChanges();
    expect(page.nativeElement.querySelector('#email-error')?.textContent).toContain(
      'O e-mail deve ter até 256 caracteres.',
    );
    expect(forgotPassword).not.toHaveBeenCalled();
  });

  it('trims surrounding email whitespace and shows a generic success without navigating', () => {
    const page = createComponent();
    const response = new Subject<ForgotPasswordResponse>();
    forgotPassword.mockReturnValue(response);
    setEmail(page, '  cliente@example.test  ');
    submit(page);

    expect(forgotPassword).toHaveBeenCalledOnce();
    expect(forgotPassword).toHaveBeenCalledWith('cliente@example.test');
    expect(page.nativeElement.textContent).toContain('Enviando...');
    expect(page.nativeElement.textContent).toContain('Enviando sua solicitação.');
    expect((page.nativeElement.querySelector('#email') as HTMLInputElement).readOnly).toBe(true);
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);

    response.next({
      message: 'Resposta variável do backend que não deve ser exibida.',
    });
    response.complete();
    page.detectChanges();

    expect(page.nativeElement.textContent).toContain('Solicitação recebida');
    expect(page.nativeElement.textContent)
      .toContain('Se existir uma conta elegível para recuperação, você receberá instruções para redefinir sua senha.');
    expect(page.nativeElement.textContent).toContain('Verifique também sua caixa de spam.');
    expect(page.nativeElement.textContent).not.toContain('cliente@example.test');
    expect(page.nativeElement.textContent).not.toContain('Resposta variável do backend');
    expect(page.nativeElement.textContent).not.toContain('foi enviado');
    expect(page.nativeElement.textContent).not.toContain('foi entregue');
    expect(TestBed.inject(Router).url).not.toBe('/reset-password');
    expect(login).not.toHaveBeenCalled();
    expect(obterSessao).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
  });

  it('blocks duplicate submissions from button and Enter while a request is pending', () => {
    const page = createComponent();
    const response = new Subject<ForgotPasswordResponse>();
    forgotPassword.mockReturnValue(response);
    setEmail(page, 'cliente@example.test');
    submit(page);

    const form = page.nativeElement.querySelector('form') as HTMLFormElement;
    form.requestSubmit();
    page.detectChanges();

    expect(forgotPassword).toHaveBeenCalledOnce();
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);

    response.next({ message: 'Solicitação recebida.' });
    response.complete();
    page.detectChanges();
  });

  it('shows a safe HTTP 400 error and allows a corrected manual retry', () => {
    const page = createComponent();
    const email = 'cliente@example.test';
    forgotPassword
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 400,
        error: { detail: 'internal email validation detail' },
      })))
      .mockReturnValueOnce(of({ message: 'Solicitação recebida.' }));
    setEmail(page, email);
    submit(page);
    page.detectChanges();

    expect(page.nativeElement.textContent)
      .toContain('Não foi possível processar a solicitação. Revise o e-mail e tente novamente.');
    expect(page.nativeElement.textContent).not.toContain('internal email validation detail');
    expect((page.nativeElement.querySelector('#email') as HTMLInputElement).value).toBe(email);
    expect((page.nativeElement.querySelector('#email') as HTMLInputElement).readOnly).toBe(false);

    setEmail(page, 'corrigido@example.test');
    submit(page);
    page.detectChanges();
    expect(forgotPassword).toHaveBeenCalledTimes(2);
    expect(page.nativeElement.textContent).toContain('Solicitação recebida');
  });

  it('blocks manual resend for Retry-After seconds until expiry without automatic requests', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00.000Z'));
    const page = createComponent();
    forgotPassword
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 429,
        headers: new HttpHeaders({ 'Retry-After': '3' }),
        error: { message: 'private limit detail' },
      })))
      .mockReturnValueOnce(of({ message: 'Solicitação recebida.' }));
    setEmail(page, 'cliente@example.test');
    submit(page);
    page.detectChanges();

    expect(page.nativeElement.textContent).toContain('Aguarde 3 segundos');
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);
    expect(page.nativeElement.textContent).not.toContain('private limit detail');

    (page.nativeElement.querySelector('form') as HTMLFormElement).requestSubmit();
    await vi.advanceTimersByTimeAsync(2000);
    page.detectChanges();
    expect(page.nativeElement.textContent).toContain('Aguarde 1 segundo');
    expect(forgotPassword).toHaveBeenCalledOnce();

    await vi.advanceTimersByTimeAsync(1000);
    page.detectChanges();
    expect(page.nativeElement.textContent)
      .toContain('O período de espera terminou. Você pode tentar novamente.');
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(false);
    expect(forgotPassword).toHaveBeenCalledOnce();

    submit(page);
    page.detectChanges();
    expect(forgotPassword).toHaveBeenCalledTimes(2);
    expect(page.nativeElement.textContent).toContain('Solicitação recebida');
  });

  it('supports an HTTP-date Retry-After value', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00.000Z'));
    const page = createComponent();
    forgotPassword.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 429,
      headers: new HttpHeaders({ 'Retry-After': 'Thu, 01 Jan 2026 00:00:04 GMT' }),
    })));
    setEmail(page, 'cliente@example.test');
    submit(page);
    page.detectChanges();

    expect(page.nativeElement.textContent).toContain('Aguarde 4 segundos');
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('uses safe feedback for absent, invalid, and expired Retry-After values', () => {
    const page = createComponent();
    const values: Array<string | null> = [
      null,
      'not-a-date',
      'Wed, 31 Dec 2025 23:59:59 GMT',
    ];

    for (const [index, value] of values.entries()) {
      const headers = value === null
        ? new HttpHeaders()
        : new HttpHeaders({ 'Retry-After': value });
      forgotPassword.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 429,
        headers,
      })));
      setEmail(page, `cliente${index}@example.test`);
      submit(page);
      page.detectChanges();

      expect(page.nativeElement.textContent)
        .toContain('Muitas solicitações. Aguarde um pouco antes de tentar novamente.');
      expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
        .toBe(false);
    }

    expect(forgotPassword).toHaveBeenCalledTimes(values.length);
  });

  it('shows temporary errors safely and allows a manual retry without automatic retry', () => {
    const page = createComponent();
    const email = 'cliente@example.test';
    const failures: unknown[] = [
      new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }),
      new HttpErrorResponse({ status: 408 }),
      new HttpErrorResponse({ status: 503, error: 'private server detail' }),
      new TimeoutError(),
    ];
    for (const failure of failures) {
      forgotPassword.mockReturnValueOnce(throwError(() => failure));
      setEmail(page, email);
      submit(page);
      page.detectChanges();

      expect(page.nativeElement.textContent)
        .toContain('Não foi possível concluir a solicitação agora. Tente novamente mais tarde.');
      expect(page.nativeElement.textContent).not.toContain('private server detail');
      expect((page.nativeElement.querySelector('#email') as HTMLInputElement).value).toBe(email);
      expect(forgotPassword).toHaveBeenCalledTimes(failures.indexOf(failure) + 1);
    }

    forgotPassword.mockReturnValueOnce(of({ message: 'Solicitação recebida.' }));
    submit(page);
    page.detectChanges();
    expect(forgotPassword).toHaveBeenCalledTimes(failures.length + 1);
    expect(page.nativeElement.textContent).toContain('Solicitação recebida');
  });

  it('cancels a pending service subscription when destroyed', () => {
    const page = createComponent();
    const response = new Subject<ForgotPasswordResponse>();
    forgotPassword.mockReturnValue(response);
    setEmail(page, 'cliente@example.test');
    submit(page);

    page.destroy();
    fixture = undefined;
    expect(response.observed).toBe(false);
    response.next({ message: 'late response' });
  });

  it('clears the Retry-After timer when destroyed', () => {
    vi.useFakeTimers();
    const page = createComponent();
    forgotPassword.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 429,
      headers: new HttpHeaders({ 'Retry-After': '60' }),
    })));
    setEmail(page, 'cliente@example.test');
    submit(page);
    expect(vi.getTimerCount()).toBeGreaterThan(0);

    page.destroy();
    fixture = undefined;
    expect(vi.getTimerCount()).toBe(0);
  });

  it('returns to the form only after the user explicitly requests it', () => {
    const page = createComponent();
    forgotPassword.mockReturnValue(of({ message: 'Solicitação recebida.' }));
    setEmail(page, 'cliente@example.test');
    submit(page);
    page.detectChanges();

    const returnButton = Array.from<HTMLButtonElement>(
      page.nativeElement.querySelectorAll('button'),
    ).find((button) => button.textContent?.includes('Solicitar novamente'));
    expect(returnButton).toBeDefined();
    returnButton?.click();
    page.detectChanges();

    expect(page.nativeElement.querySelector('#email')).not.toBeNull();
    expect(forgotPassword).toHaveBeenCalledOnce();
  });

  function createComponent(): ComponentFixture<ForgotPasswordComponent> {
    fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
    return fixture;
  }
});

function setEmail(fixture: ComponentFixture<ForgotPasswordComponent>, value: string): void {
  const input = fixture.nativeElement.querySelector('#email') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<ForgotPasswordComponent>): void {
  (fixture.nativeElement.querySelector('form') as HTMLFormElement)
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  fixture.detectChanges();
}
