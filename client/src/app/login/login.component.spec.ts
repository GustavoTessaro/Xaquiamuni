import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../core/auth/auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let login: ReturnType<typeof vi.fn<AuthService['login']>>;

  beforeEach(async () => {
    login = vi.fn<AuthService['login']>();
    login.mockReturnValue(new Observable<void>());

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  });

  it('renders the login fields and validates required and malformed values', () => {
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

  it('calls AuthService with the valid email and password', () => {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    setValidFields(fixture);

    submit(fixture);

    expect(login).toHaveBeenCalledOnce();
    expect(login).toHaveBeenCalledWith('ana@example.test', 'senha-de-teste');
  });

  it('disables the button while submitting and prevents duplicate submissions', () => {
    const result = new Subject<void>();
    login.mockReturnValue(result);
    setValidFields(fixture);

    submit(fixture);
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');

    submit(fixture);
    expect(login).toHaveBeenCalledTimes(1);

    result.next();
    result.complete();
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
  });

  it('shows a discreet confirmation when login succeeds with HTTP 204', () => {
    login.mockReturnValue(of(undefined));
    setValidFields(fixture);

    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent)
      .toContain('Login realizado com sucesso.');
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).value).toBe('');
  });

  it('shows a generic message for HTTP 401', () => {
    login.mockReturnValue(throwError(() =>
      new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' }),
    ));
    setValidFields(fixture);

    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent.trim())
      .toBe('E-mail ou senha inválidos.');
  });

  it('shows safe messages for server and network failures', () => {
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
