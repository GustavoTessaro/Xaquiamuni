import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  withXsrfConfiguration,
} from '@angular/common/http';
import { HttpStatusCode } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { CadastroComponent } from './cadastro.component';
import { CadastroService } from './cadastro.service';
import { CsrfService } from '../core/security/csrf.service';

describe('CadastroComponent registration flow', () => {
  let fixture: ComponentFixture<CadastroComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CadastroComponent],
      providers: [
        CadastroService,
        CsrfService,
        provideRouter([]),
        provideHttpClient(
          withXsrfConfiguration({
            cookieName: 'XSRF-TOKEN',
            headerName: 'X-XSRF-TOKEN',
          }),
        ),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    document.cookie = 'XSRF-TOKEN=cookie-token-not-request-token; path=/';
    fixture = TestBed.createComponent(CadastroComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http?.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; path=/';
  });

  it('marks required fields and does not request while the form is invalid', () => {
    submit(fixture);

    expect(fixture.nativeElement.querySelector('#nomeCompleto[aria-invalid="true"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#nomeCompleto-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#cep-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent.trim()).toBe('');
    http.expectNone('/api/auth/csrf');
  });

  it('shows a password mismatch without requesting a CSRF token', () => {
    fillValidFields(fixture, 'Outra senha');
    submit(fixture);

    expect(fixture.nativeElement.textContent).toContain('As senhas não coincidem.');
    http.expectNone('/api/auth/csrf');
  });

  it('updates accessible password requirement indicators while typing', () => {
    const password = fixture.nativeElement.querySelector('#senha') as HTMLInputElement;
    password.value = 'curta';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    const requirements = fixture.nativeElement.querySelector('#senha-requisitos') as HTMLUListElement;
    expect(password.getAttribute('aria-describedby')).toContain('senha-requisitos');
    expect(requirements.textContent).toContain('Pendente');
    expect(requirements.querySelector('[data-met="false"]')).not.toBeNull();

    password.value = 'Senha Forte 123!';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    expect(requirements.querySelectorAll('[data-met="true"]').length).toBe(7);
  });

  it('blocks registration when password rules fail', () => {
    fillValidFields(fixture, 'curta');
    const password = fixture.nativeElement.querySelector('#senha') as HTMLInputElement;
    password.value = 'curta';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    const confirmation = fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement;
    confirmation.value = 'curta';
    confirmation.dispatchEvent(new Event('input', { bubbles: true }));
    submit(fixture);

    expect(fixture.nativeElement.textContent).toContain('Atenda aos requisitos de senha indicados');
    expect(fixture.nativeElement.querySelector('#senha[aria-invalid="true"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#confirmacaoSenha[aria-invalid="true"]')).toBeNull();
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/register');
  });

  it('toggles password fields independently without changing values, validation, or submitting', () => {
    fillValidFields(fixture);
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    const password = fixture.nativeElement.querySelector('#senha') as HTMLInputElement;
    const confirmation = fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement;
    const submitEvent = vi.fn();
    form.addEventListener('submit', submitEvent);
    const initialValidity = form.checkValidity();
    const passwordValue = password.value;
    const confirmationValue = confirmation.value;
    const passwordInvalid = password.getAttribute('aria-invalid');
    const confirmationInvalid = confirmation.getAttribute('aria-invalid');

    expect(password.type).toBe('password');
    expect(confirmation.type).toBe('password');
    expect(password.autocomplete).toBe('new-password');
    expect(confirmation.autocomplete).toBe('new-password');

    const showPassword = fixture.nativeElement.querySelector(
      'button[aria-label="Mostrar senha"]',
    ) as HTMLButtonElement;
    expect(showPassword.type).toBe('button');
    showPassword.click();
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('text');
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).type)
      .toBe('password');
    expect(fixture.nativeElement.querySelector('button[aria-label="Ocultar senha"]')).not.toBeNull();

    const showConfirmation = fixture.nativeElement.querySelector(
      'button[aria-label="Mostrar confirmação de senha"]',
    ) as HTMLButtonElement;
    expect(showConfirmation.type).toBe('button');
    showConfirmation.click();
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('text');
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).type)
      .toBe('text');
    expect(fixture.nativeElement.querySelector('button[aria-label="Ocultar confirmação de senha"]'))
      .not.toBeNull();

    (fixture.nativeElement.querySelector(
      'button[aria-label="Ocultar senha"]',
    ) as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('password');
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).type)
      .toBe('text');
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).value)
      .toBe(passwordValue);
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).value)
      .toBe(confirmationValue);
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement)
      .getAttribute('aria-invalid')).toBe(passwordInvalid);
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement)
      .getAttribute('aria-invalid')).toBe(confirmationInvalid);
    expect(form.checkValidity()).toBe(initialValidity);
    expect(submitEvent).not.toHaveBeenCalled();
    expect(http.match('/api/auth/csrf')).toHaveLength(0);
    expect(http.match('/api/auth/register')).toHaveLength(0);
  });

  it('sends the CSRF request token before POST, independent of the cookie token', () => {
    fillValidFields(fixture);
    submit(fixture);

    const csrfRequest = http.expectOne('/api/auth/csrf');
    expect(csrfRequest.request.method).toBe('GET');
    http.expectNone('/api/auth/register');
    csrfRequest.flush({ requestToken: 'request-token-from-response' });

    const registerRequest = http.expectOne('/api/auth/register');
    expect(registerRequest.request.method).toBe('POST');
    expect(registerRequest.request.headers.get('X-XSRF-TOKEN')).toBe('request-token-from-response');
    expect(registerRequest.request.body).toEqual({
      nomeCompleto: 'Ana Maria Silva',
      cpf: '52998224725',
      email: 'ana@example.com',
      telefone: '11912345678',
      senha: 'Senha forte 123!',
      enderecoInicial: {
        cep: '95000000',
        uf: 'RS',
        cidade: 'Vacaria',
        bairro: 'Centro',
        logradouro: 'Rua das Flores',
        numero: '10',
        complemento: '',
      },
    });
    expect(registerRequest.request.body.confirmacaoSenha).toBeUndefined();
    expect(registerRequest.request.body.role).toBeUndefined();
    expect(registerRequest.request.headers.has('Authorization')).toBe(false);
    registerRequest.flush({ userId: '5b0d0e35-0ba1-4d54-bcf8-85452a8f6588' }, {
      status: HttpStatusCode.Created,
      statusText: 'Created',
    });
  });

  it('reports creation without claiming email verification or authentication, then clears personal fields', () => {
    fillValidFields(fixture);
    (fixture.nativeElement.querySelector(
      'button[aria-label="Mostrar senha"]',
    ) as HTMLButtonElement).click();
    (fixture.nativeElement.querySelector(
      'button[aria-label="Mostrar confirmação de senha"]',
    ) as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('text');
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).type)
      .toBe('text');
    submit(fixture);
    completeCsrfAndGetRegisterRequest(http).flush({
      userId: '5b0d0e35-0ba1-4d54-bcf8-85452a8f6588',
    }, { status: HttpStatusCode.Created, statusText: 'Created' });
    fixture.detectChanges();

    const feedback = fixture.nativeElement.querySelector('[aria-live="polite"]').textContent;
    expect(feedback).toContain('Cadastro criado.');
    expect(feedback).not.toContain('e-mail enviado');
    expect(feedback).not.toContain('autenticado');
    const resendLink = fixture.nativeElement.querySelector(
      'a[routerLink="/confirm-email"]',
    ) as HTMLAnchorElement;
    expect(resendLink.textContent).toContain('Solicitar nova confirmação');
    expect(resendLink.href).toContain('/confirm-email');
    expect(new URL(resendLink.href).search).toBe('');
    expect(new URL(resendLink.href).hash).toBe('');
    expect((fixture.nativeElement.querySelector('#cpf') as HTMLInputElement).value).toBe('');
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).value).toBe('');
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).value).toBe('');
    expect((fixture.nativeElement.querySelector('#senha') as HTMLInputElement).type).toBe('password');
    expect((fixture.nativeElement.querySelector('#confirmacaoSenha') as HTMLInputElement).type)
      .toBe('password');
    expect(fixture.nativeElement.querySelector('button[aria-label="Mostrar senha"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('button[aria-label="Mostrar confirmação de senha"]'))
      .not.toBeNull();
  });

  it('displays available field errors from HTTP 400', () => {
    fillValidFields(fixture);
    submit(fixture);
    completeCsrfAndGetRegisterRequest(http).flush({
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { Cpf: ['O CPF informado é inválido.'] },
    }, { status: HttpStatusCode.BadRequest, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-live="assertive"]').textContent)
      .toContain('Revise os campos indicados');
    expect(fixture.nativeElement.textContent).toContain('O CPF informado é inválido.');
  });

  it('shows a generic duplicate message for HTTP 409', () => {
    fillValidFields(fixture);
    submit(fixture);
    completeCsrfAndGetRegisterRequest(http).flush({
      title: 'Cadastro não concluído',
      detail: 'Não foi possível concluir o cadastro com os dados informados.',
      status: 409,
    }, { status: HttpStatusCode.Conflict, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-live="assertive"]').textContent)
      .toContain('Verifique os dados');
    expect(fixture.nativeElement.textContent).not.toContain('52998224725');
    expect(fixture.nativeElement.textContent).not.toContain('ana@example.com');
  });

  it('shows a retryable generic message for server and network errors', () => {
    fillValidFields(fixture);
    submit(fixture);
    completeCsrfAndGetRegisterRequest(http).flush(
      { detail: 'internal SQL details' },
      { status: HttpStatusCode.InternalServerError, statusText: 'Server Error' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-live="assertive"]').textContent)
      .toContain('Tente novamente');
    expect(fixture.nativeElement.textContent).not.toContain('internal SQL details');
  });

  it('does not send registration if the CSRF request fails and allows retry', () => {
    fillValidFields(fixture);
    submit(fixture);
    http.expectOne('/api/auth/csrf').flush(
      { detail: 'internal details' },
      { status: HttpStatusCode.InternalServerError, statusText: 'Server Error' },
    );
    fixture.detectChanges();

    expect(http.match('/api/auth/register')).toHaveLength(0);
    expect(fixture.nativeElement.querySelector('[aria-live="assertive"]').textContent)
      .toContain('Tente novamente');
    expect((fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(false);
  });

  it('shows a retryable generic message for a network failure', () => {
    fillValidFields(fixture);
    submit(fixture);
    completeCsrfAndGetRegisterRequest(http).error(new ProgressEvent('error'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-live="assertive"]').textContent)
      .toContain('Tente novamente');
    expect(fixture.nativeElement.textContent).not.toContain('52998224725');
    expect((fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(false);
  });

  it('disables the submit button and prevents duplicate requests while in flight', () => {
    fillValidFields(fixture);
    submit(fixture);
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent)
      .toContain('Enviando');
    submit(fixture);
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'request-token-from-response' });
    const request = http.expectOne('/api/auth/register');
    http.expectNone('/api/auth/csrf');
    request.flush({ status: 500, detail: 'generic' }, {
      status: HttpStatusCode.InternalServerError,
      statusText: 'Server Error',
    });
  });
});

function completeCsrfAndGetRegisterRequest(http: HttpTestingController) {
  http.expectOne('/api/auth/csrf').flush({ requestToken: 'request-token-from-response' });
  return http.expectOne('/api/auth/register');
}

function fillValidFields(
  fixture: ComponentFixture<CadastroComponent>,
  confirmPassword = 'Senha forte 123!',
): void {
  const values: Record<string, string> = {
    nomeCompleto: 'Ana Maria Silva',
    cpf: '52998224725',
    telefone: '11912345678',
    email: 'ana@example.com',
    senha: 'Senha forte 123!',
    confirmacaoSenha: confirmPassword,
    cep: '95000000',
    uf: 'rs',
    cidade: 'Vacaria',
    bairro: 'Centro',
    logradouro: 'Rua das Flores',
    numero: '10',
    complemento: '',
  };

  for (const [id, value] of Object.entries(values)) {
    const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<CadastroComponent>): void {
  (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).click();
  fixture.detectChanges();
}
