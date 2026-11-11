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
    expect(fixture.nativeElement.textContent).not.toContain('not-an-email');
    expect(fixture.nativeElement.textContent).not.toContain('private-token');
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
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
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(email);
    expect(fixture.nativeElement.textContent).not.toContain(token);
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
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

  function setFragment(fragment: string): void {
    const current = new URL(window.location.href);
    window.history.replaceState(
      window.history.state,
      '',
      `${current.pathname}${current.search}${fragment}`,
    );
  }
});
