import { provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { CsrfService } from '../core/security/csrf.service';
import { ConfirmEmailComponent } from './confirm-email.component';

describe('ConfirmEmailComponent', () => {
  let fixture: ComponentFixture<ConfirmEmailComponent>;
  let http: HttpTestingController;
  let originalUrl: string;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ConfirmEmailComponent],
      providers: [
        provideRouter([]),
        AuthService,
        CsrfService,
        provideHttpClient(
          withXsrfConfiguration({
            cookieName: 'XSRF-TOKEN',
            headerName: 'X-XSRF-TOKEN',
          }),
        ),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    originalUrl = window.location.href;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try {
      http.verify({ ignoreCancelled: true });
    } finally {
      window.history.replaceState(
        window.history.state,
        '',
        new URL(originalUrl).pathname + new URL(originalUrl).search,
      );
      TestBed.resetTestingModule();
    }
  });

  it('clears the fragment before requesting confirmation and shows success once', () => {
    const email = 'cliente+tag@example.test';
    const token = 'token+/=special';
    createComponent(email, token);

    expect(window.location.hash).toBe('');
    const csrf = http.expectOne('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
    csrf.flush({ requestToken: 'confirmation-request-token' });

    const confirmation = http.expectOne('/api/auth/confirm-email');
    expect(confirmation.request.method).toBe('POST');
    expect(confirmation.request.body).toEqual({ email, token });
    expect(confirmation.request.urlWithParams).toBe('/api/auth/confirm-email');
    expect(confirmation.request.headers.get('X-XSRF-TOKEN')).toBe('confirmation-request-token');
    confirmation.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('E-mail confirmado');
    expect(fixture.nativeElement.querySelector('#resend-email')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(email);
    expect(fixture.nativeElement.textContent).not.toContain(token);
    fixture.detectChanges();
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  it('clears an invalid fragment and does not make HTTP requests', () => {
    createComponent('not-an-email', 'private-token');

    expect(window.location.hash).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Link inválido');
    expect(fixture.nativeElement.querySelector('#resend-email')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('not-an-email');
    expect(fixture.nativeElement.textContent).not.toContain('private-token');
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  it('allows requesting a new confirmation email when opened without a fragment', () => {
    createComponentWithoutFragment();

    expect(fixture.nativeElement.textContent).toContain('Confirme seu e-mail');
    expect(fixture.nativeElement.querySelector('#resend-email')).not.toBeNull();
    http.expectNone('/api/auth/confirm-email');

    const request = submitResend('cliente@example.test');
    expect(request.request.method).toBe('POST');
    expect(request.request.url).toBe('/api/auth/resend-confirmation-email');
    expect(request.request.body).toEqual({ email: 'cliente@example.test' });
    request.flush({
      message: 'Se houver uma conta elegível, enviaremos um e-mail de confirmação.',
    }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Solicitação recebida.');
    expect(fixture.nativeElement.textContent).toContain('caixa de entrada');
    expect(fixture.nativeElement.textContent).toContain('pasta de spam');
    expect(fixture.nativeElement.textContent).not.toContain('e-mail foi enviado');
  });

  it('rejects duplicate or missing fragment fields without making a request', () => {
    setFragment('#email=cliente%40example.test&token=one&token=two');
    fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Link inválido');
    fixture.destroy();

    setFragment('#email=cliente%40example.test');
    fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Link inválido');

    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  it('shows the invalid-link state without retry after HTTP 400', () => {
    const email = 'cliente@example.test';
    const token = 'private-token';
    createComponent(email, token);

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'confirmation-request-token' });
    http.expectOne('/api/auth/confirm-email').flush(
      null,
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Link inválido');
    expect(fixture.nativeElement.querySelector('#resend-email')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(email);
    expect(fixture.nativeElement.textContent).not.toContain(token);
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  it('does not request resend for an invalid email', () => {
    createComponentWithoutFragment();
    enterEmail('invalid-email');
    clickResend();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Informe um endereço de e-mail válido.');
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/resend-confirmation-email');
  });

  it('prevents duplicate resend requests while the request is in progress', () => {
    createComponentWithoutFragment();
    enterEmail('cliente@example.test');
    clickResend();
    clickResend();

    const csrf = http.expectOne('/api/auth/csrf');
    csrf.flush({ requestToken: 'resend-request-token' });
    const request = http.expectOne('/api/auth/resend-confirmation-email');
    expect(request.request.body).toEqual({ email: 'cliente@example.test' });
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    request.flush({ message: 'Solicitação recebida.' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    http.expectNone('/api/auth/resend-confirmation-email');
  });

  it('allows correcting the email and retrying after HTTP 400 without exposing backend details', () => {
    createComponentWithoutFragment();
    const firstRequest = submitResend('cliente@example.test');
    firstRequest.flush(
      { detail: 'The account does not exist.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Revise o e-mail e tente novamente.');
    expect(fixture.nativeElement.textContent).not.toContain('does not exist');

    const secondRequest = submitResend('outro@example.test');
    expect(secondRequest.request.body).toEqual({ email: 'outro@example.test' });
    secondRequest.flush({ message: 'Solicitação recebida.' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Solicitação recebida.');
  });

  it('uses a valid Retry-After value in the HTTP 429 guidance', () => {
    createComponentWithoutFragment();
    const request = submitResend('cliente@example.test');
    request.flush(
      { detail: 'private rate limit information' },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '37' },
      },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Aguarde 37 segundos');
    expect(fixture.nativeElement.textContent).not.toContain('private rate limit information');
  });

  it('uses safe wait guidance when Retry-After is absent or invalid', () => {
    createComponentWithoutFragment();
    submitResend('cliente@example.test').flush(
      null,
      { status: 429, statusText: 'Too Many Requests' },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Aguarde um pouco');

    const retry = submitResend('cliente@example.test');
    retry.flush(
      null,
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': 'not-valid' },
      },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Aguarde um pouco');
    expect(fixture.nativeElement.textContent).not.toContain('not-valid');
  });

  it('allows retrying after a network failure', () => {
    createComponentWithoutFragment();
    submitResend('cliente@example.test').error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tente novamente mais tarde.');

    const retry = submitResend('cliente@example.test');
    retry.flush({ message: 'Solicitação recebida.' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Solicitação recebida.');
  });

  it('allows retrying after a server error', () => {
    createComponentWithoutFragment();
    submitResend('cliente@example.test').flush(
      null,
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tente novamente mais tarde.');

    const retry = submitResend('cliente@example.test');
    retry.flush({ message: 'Solicitação recebida.' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Solicitação recebida.');
  });

  it('cancels a pending resend request when the component is destroyed', () => {
    createComponentWithoutFragment();
    const request = submitResend('cliente@example.test');

    fixture.destroy();

    expect(request.cancelled).toBe(true);
  });

  it('allows an explicit retry after a network failure and completes without reloading', () => {
    const email = 'cliente@example.test';
    const token = 'private-token';
    createComponent(email, token);

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'first-request-token' });
    http.expectOne('/api/auth/confirm-email').error(new ProgressEvent('error'));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha temporária');
    expect(fixture.nativeElement.textContent).not.toContain(email);
    expect(fixture.nativeElement.textContent).not.toContain(token);

    const retryButton = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(retryButton.textContent).toContain('Tentar novamente');
    retryButton.click();
    fixture.detectChanges();

    const retryCsrf = http.expectOne('/api/auth/csrf');
    retryCsrf.flush({ requestToken: 'retry-request-token' });
    const retryRequest = http.expectOne('/api/auth/confirm-email');
    expect(retryRequest.request.body).toEqual({ email, token });
    retryRequest.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('E-mail confirmado');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    expect(window.location.hash).toBe('');
  });

  it('offers retry for HTTP 5xx failures', () => {
    createComponent('cliente@example.test', 'private-token');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'confirmation-request-token' });
    http.expectOne('/api/auth/confirm-email').flush(
      null,
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button')?.textContent).toContain('Tentar novamente');
  });

  it('offers retry for an HTTP timeout without retrying automatically', () => {
    createComponent('cliente@example.test', 'private-token');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'confirmation-request-token' });
    http.expectOne('/api/auth/confirm-email').flush(
      null,
      { status: 408, statusText: 'Request Timeout' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha temporária');
    expect(fixture.nativeElement.querySelector('button')?.textContent).toContain('Tentar novamente');
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  it('blocks simultaneous retry clicks and discards an in-flight request on destroy', () => {
    createComponent('cliente@example.test', 'private-token');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'first-request-token' });
    http.expectOne('/api/auth/confirm-email').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const retryButton = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    retryButton.click();
    retryButton.click();
    const retryCsrf = http.expectOne('/api/auth/csrf');
    retryCsrf.flush({ requestToken: 'retry-request-token' });
    const retryRequest = http.expectOne('/api/auth/confirm-email');

    fixture.destroy();

    expect(retryRequest.cancelled).toBe(true);
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  function createComponent(email: string, token: string): void {
    setFragment(`#email=${encodeURIComponent(email)}&token=${encodeURIComponent(token)}`);
    fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.detectChanges();
  }

  function createComponentWithoutFragment(): void {
    setFragment('');
    fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.detectChanges();
  }

  function enterEmail(email: string): void {
    const input = fixture.nativeElement.querySelector('#resend-email') as HTMLInputElement;
    input.value = email;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function clickResend(): void {
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
  }

  function submitResend(email: string): ReturnType<HttpTestingController['expectOne']> {
    enterEmail(email);
    clickResend();
    const csrf = http.expectOne('/api/auth/csrf');
    csrf.flush({ requestToken: 'resend-request-token' });
    return http.expectOne('/api/auth/resend-confirmation-email');
  }

  function setFragment(fragment: string): void {
    const current = new URL(window.location.href);
    window.history.replaceState(
      window.history.state,
      '',
      `${current.pathname}${current.search}${fragment}`,
    );
  }
});
