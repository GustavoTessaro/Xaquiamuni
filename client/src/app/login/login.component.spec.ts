import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuthService, AuthSession } from '../core/auth/auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let login: ReturnType<typeof vi.fn<AuthService['login']>>;
  let obterSessao: ReturnType<typeof vi.fn<AuthService['obterSessao']>>;
  let logout: ReturnType<typeof vi.fn<AuthService['logout']>>;

  beforeEach(async () => {
    login = vi.fn<AuthService['login']>();
    login.mockReturnValue(new Observable<void>());
    obterSessao = vi.fn<AuthService['obterSessao']>();
    obterSessao.mockReturnValue(of(null));
    logout = vi.fn<AuthService['logout']>();
    logout.mockReturnValue(new Observable<void>());

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login, obterSessao, logout } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
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
  });

  it('shows the login form when /me indicates no session', () => {
    obterSessao.mockReturnValue(of(null));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#email')).not.toBeNull();
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

    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
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
});

function setValidFields(fixture: ComponentFixture<LoginComponent>): void {
  setField(fixture, 'email', 'ana@example.test');
  setField(fixture, 'senha', 'senha-de-teste');
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
