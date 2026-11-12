import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DefaultUrlSerializer, Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, TimeoutError, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../core/auth/auth.service';
import { routes } from '../app.routes';
import { ResetPasswordComponent } from './reset-password.component';

describe('ResetPasswordComponent', () => {
  let fixture: ComponentFixture<ResetPasswordComponent> | undefined;
  let resetPassword: ReturnType<typeof vi.fn<AuthService['resetPassword']>>;
  let forgotPassword: ReturnType<typeof vi.fn<AuthService['forgotPassword']>>;
  let login: ReturnType<typeof vi.fn<AuthService['login']>>;
  let obterSessao: ReturnType<typeof vi.fn<AuthService['obterSessao']>>;
  let logout: ReturnType<typeof vi.fn<AuthService['logout']>>;
  let originalUrl: URL;
  let originalHistoryState: unknown;

  beforeEach(async () => {
    originalUrl = new URL(window.location.href);
    originalHistoryState = window.history.state;
    resetPassword = vi.fn<AuthService['resetPassword']>();
    resetPassword.mockReturnValue(new Observable<void>());
    forgotPassword = vi.fn<AuthService['forgotPassword']>();
    login = vi.fn<AuthService['login']>();
    obterSessao = vi.fn<AuthService['obterSessao']>();
    obterSessao.mockReturnValue(of(null));
    logout = vi.fn<AuthService['logout']>();

    await TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter(routes),
        {
          provide: AuthService,
          useValue: { resetPassword, forgotPassword, login, obterSessao, logout },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
    window.history.replaceState(
      originalHistoryState,
      '',
      `${originalUrl.pathname}${originalUrl.search}${originalUrl.hash}`,
    );
  });

  it('loads the public route without a session and shows the invalid-link state without HTTP', async () => {
    setLocation('', '');
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/reset-password', ResetPasswordComponent);
    harness.fixture.detectChanges();

    expect(component).toBeInstanceOf(ResetPasswordComponent);
    expect(harness.routeNativeElement?.textContent).toContain('Link de recuperação inválido');
    expect(harness.routeNativeElement?.querySelector('a[routerLink="/forgot-password"]'))
      .not.toBeNull();
    expect(harness.routeNativeElement?.querySelector('a[routerLink="/login"]')).not.toBeNull();
    expect(resetPassword).not.toHaveBeenCalled();
    expect(login).not.toHaveBeenCalled();
    expect(obterSessao).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
  });

  it('navigates from an invalid link to recovery or login only on explicit action', async () => {
    setLocation('', '');
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/reset-password', ResetPasswordComponent);

    const forgotLink = harness.routeNativeElement?.querySelector(
      'a[routerLink="/forgot-password"]',
    ) as HTMLAnchorElement;
    expect(forgotLink.href).toContain('/forgot-password');
    expect(new URL(forgotLink.href).search).toBe('');
    expect(new URL(forgotLink.href).hash).toBe('');
    forgotLink.click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/forgot-password');
    expect(harness.routeNativeElement?.textContent).toContain('Esqueci minha senha');
    expect(forgotPassword).not.toHaveBeenCalled();
    expect(resetPassword).not.toHaveBeenCalled();

    await harness.navigateByUrl('/reset-password', ResetPasswordComponent);
    const loginLink = harness.routeNativeElement?.querySelector(
      'a[routerLink="/login"]',
    ) as HTMLAnchorElement;
    expect(new URL(loginLink.href).search).toBe('');
    expect(new URL(loginLink.href).hash).toBe('');
    loginLink.click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(harness.routeNativeElement?.textContent).toContain('Entrar na sua conta');
    expect(resetPassword).not.toHaveBeenCalled();
    expect(forgotPassword).not.toHaveBeenCalled();
    expect(login).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
  });

  it('discards fragment credentials when leaving by Router and reopening without a fragment', async () => {
    const email = 'cliente@example.test';
    const token = 'synthetic-navigation-token';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/reset-password', ResetPasswordComponent);

    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
    expect(window.location.hash).toBe('');
    const loginLink = harness.routeNativeElement?.querySelector(
      'a[routerLink="/login"]',
    ) as HTMLAnchorElement;
    loginLink.click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(new URL(loginLink.href).search).toBe('');
    expect(new URL(loginLink.href).hash).toBe('');
    await harness.navigateByUrl('/reset-password', ResetPasswordComponent);

    expect(harness.routeNativeElement?.textContent).toContain('Link de recuperação inválido');
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
    expect(harness.routeNativeElement?.textContent).not.toContain(email);
    expect(harness.routeNativeElement?.textContent).not.toContain(token);
    expect(resetPassword).not.toHaveBeenCalled();
    expect(login).not.toHaveBeenCalled();
    expect(forgotPassword).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
  });

  it('accepts encoded fragment credentials through Angular Router without double decoding', async () => {
    const email = 'cliente+tag@example.test';
    const token = '+/=%2B%26& spaces';
    const password = '  Senha nova válida 987!  ';
    const serializer = new DefaultUrlSerializer();
    const url = `/reset-password#${encodedCredentials(email, token)}`;
    const canonicalUrl = serializer.serialize(serializer.parse(url));
    setLocation('', canonicalUrl.slice(canonicalUrl.indexOf('#')));
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(
      url,
      ResetPasswordComponent,
    );
    harness.fixture.detectChanges();

    expect(component).toBeInstanceOf(ResetPasswordComponent);
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
    expect(window.location.hash).toBe('');
    expect(window.location.pathname).toBe('/reset-password');

    const passwordInput = harness.routeNativeElement?.querySelector(
      '#new-password',
    ) as HTMLInputElement;
    const confirmationInput = harness.routeNativeElement?.querySelector(
      '#confirm-password',
    ) as HTMLInputElement;
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input', { bubbles: true }));
    confirmationInput.value = password;
    confirmationInput.dispatchEvent(new Event('input', { bubbles: true }));
    harness.fixture.detectChanges();
    resetPassword.mockReturnValue(of(undefined));
    (harness.routeNativeElement?.querySelector('form') as HTMLFormElement)
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.fixture.detectChanges();

    expect(resetPassword).toHaveBeenCalledWith({ email, token, newPassword: password });
    expect(harness.routeNativeElement?.textContent).toContain('Senha redefinida com sucesso');
    expect(window.location.hash).toBe('');
  });

  it('rejects duplicate token parameters after canonicalization and preserves literal marker text', () => {
    const email = 'cliente@example.test';
    const serializer = new DefaultUrlSerializer();
    const emailParameter = `email=${encodeURIComponent(email)}`;
    const canonicalize = (url: string): string =>
      serializer.serialize(serializer.parse(url));
    const tokenValueUrl = canonicalize(
      `/reset-password#${emailParameter}&token=${encodeURIComponent('abc&token=xyz')}`,
    );
    const duplicateParametersUrl = canonicalize(
      `/reset-password#${emailParameter}&token=abc&token=xyz`,
    );

    expect(tokenValueUrl).toBe(duplicateParametersUrl);
    for (const url of [tokenValueUrl, duplicateParametersUrl]) {
      setLocation('', url.slice(url.indexOf('#')));
      const page = createComponent();
      expect(page.nativeElement.textContent).toContain('Link de recuperação inválido');
      expect(page.nativeElement.querySelector('form')).toBeNull();
      page.destroy();
      fixture = undefined;
    }

    const literalToken = 'abc%26token%3Dxyz';
    const literalUrl = canonicalize(
      `/reset-password#${emailParameter}&token=${encodeURIComponent(literalToken)}`,
    );
    setLocation('', literalUrl.slice(literalUrl.indexOf('#')));
    const page = createComponent();
    const password = 'Senha válida 987!';
    setPassword(page, 'senha', password);
    setPassword(page, 'confirmacaoSenha', password);
    resetPassword.mockReturnValue(of(undefined));
    submit(page);

    expect(resetPassword).toHaveBeenCalledWith({
      email,
      token: literalToken,
      newPassword: password,
    });
  });

  it('removes the fragment before interaction while preserving query, history state, and history length', () => {
    const email = 'cliente+tag@example.test';
    const token = 'token-for-state-test';
    const historyState = { marker: 'preserve-state', page: 7 };
    window.history.replaceState(
      historyState,
      '',
      `/reset-password?campaign=natural&source=email#${encodedCredentials(email, token)}`,
    );
    const historyLength = window.history.length;

    const page = createComponent();

    expect(window.location.pathname).toBe('/reset-password');
    expect(window.location.search).toBe('?campaign=natural&source=email');
    expect(window.location.hash).toBe('');
    expect(window.history.state).toEqual(historyState);
    expect(window.history.length).toBe(historyLength);
    expect(page.nativeElement.textContent).toContain('Redefinir senha');
    expect(page.nativeElement.textContent).not.toContain(email);
    expect(page.nativeElement.textContent).not.toContain(token);
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('decodes the token once and sends only the exact credentials on manual submission', () => {
    const email = 'cliente+tag@example.test';
    const token = '+/=%2F%2B%26& spaces';
    const password = '  Senha nova válida 987!  ';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const page = createComponent();
    const response = new Subject<void>();
    resetPassword.mockReturnValue(response);

    expect(page.nativeElement.textContent).not.toContain(email);
    expect(page.nativeElement.textContent).not.toContain(token);
    expect(resetPassword).not.toHaveBeenCalled();
    setPassword(page, 'senha', password);
    setPassword(page, 'confirmacaoSenha', password);
    submit(page);

    expect(resetPassword).toHaveBeenCalledOnce();
    expect(resetPassword).toHaveBeenCalledWith({ email, token, newPassword: password });
    expect(page.nativeElement.textContent).toContain('Redefinindo...');
    expect((page.nativeElement.querySelector('#new-password') as HTMLInputElement).readOnly)
      .toBe(true);
    expect((page.nativeElement.querySelector('#confirm-password') as HTMLInputElement).readOnly)
      .toBe(true);
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);

    (page.nativeElement.querySelector('form') as HTMLFormElement).requestSubmit();
    expect(resetPassword).toHaveBeenCalledOnce();

    response.next();
    response.complete();
    page.detectChanges();
    expect(page.nativeElement.textContent).toContain('Senha redefinida com sucesso');
    expect(page.nativeElement.textContent)
      .toContain('Sua senha foi atualizada. Você já pode entrar na sua conta com a nova senha.');
    expect(page.nativeElement.querySelector('form')).toBeNull();
    expect(page.nativeElement.textContent).not.toContain(email);
    expect(page.nativeElement.textContent).not.toContain(token);
    expect(login).not.toHaveBeenCalled();
    expect(obterSessao).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).not.toBe('/login');
  });

  it('uses the explicit success action to navigate to login', async () => {
    setLocation('', `#${encodedCredentials('cliente@example.test', 'synthetic-token')}`);
    const page = createComponent();
    resetPassword.mockReturnValue(of(undefined));
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);
    page.detectChanges();

    const loginLink = page.nativeElement.querySelector(
      'a[routerLink="/login"]',
    ) as HTMLAnchorElement;
    expect(loginLink.textContent).toContain('Entrar na minha conta');
    loginLink.click();
    await page.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(login).not.toHaveBeenCalled();
  });

  it('rejects absent, incomplete, duplicate, malformed, invalid-email, and oversized credentials', () => {
    const malformedFragments = [
      '',
      '#email=cliente%40example.test',
      '#email=cliente%40example.test&token=one&token=two',
      '#email=cliente%40example.test&email=other%40example.test&token=one',
      '#email=cliente%40example.test&token=malformed%ZZ',
      '#email=invalid-email&token=one',
      `#email=cliente%40example.test&token=${encodeURIComponent('x'.repeat(4097))}`,
    ];

    for (const fragment of malformedFragments) {
      setLocation('', fragment);
      const page = createComponent();
      expect(page.nativeElement.textContent).toContain('Link de recuperação inválido');
      expect(page.nativeElement.querySelector('form')).toBeNull();
      expect(window.location.hash).toBe('');
      expect(resetPassword).not.toHaveBeenCalled();
      page.destroy();
      fixture = undefined;
    }
  });

  it('removes sensitive query parameters without using them as credentials', () => {
    setLocation(
      '?email=query%40example.test&campaign=spring&%74oken=query-token',
      '',
    );
    const page = createComponent();

    expect(page.nativeElement.textContent).toContain('Link de recuperação inválido');
    expect(window.location.search).toBe('?campaign=spring');
    expect(window.location.hash).toBe('');
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('uses fragment credentials, not query credentials, and preserves non-sensitive query values', () => {
    const email = 'fragment@example.test';
    const token = 'fragment-token';
    setLocation(
      '?email=query%40example.test&campaign=spring&%74oken=query-token',
      `#${encodedCredentials(email, token)}`,
    );
    const page = createComponent();
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);

    expect(window.location.search).toBe('?campaign=spring');
    expect(resetPassword).toHaveBeenCalledWith({
      email,
      token,
      newPassword: 'Senha válida 987!',
    });
  });

  it('does not continue if sensitive URL cleanup fails', () => {
    setLocation('', `#${encodedCredentials('cliente@example.test', 'synthetic-token')}`);
    vi.spyOn(window.history, 'replaceState').mockImplementation(() => {
      throw new DOMException('history update failed', 'SecurityError');
    });

    const page = createComponent();

    expect(page.nativeElement.textContent).toContain('Link de recuperação inválido');
    expect(page.nativeElement.querySelector('form')).toBeNull();
    expect(window.location.hash).not.toBe('');
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('validates required fields, each password rule, and exact confirmation without calling the service', () => {
    setLocation('', `#${encodedCredentials('cliente@example.test', 'synthetic-token')}`);
    const page = createComponent();
    submit(page);

    expect(page.nativeElement.querySelector('#new-password-error')?.textContent)
      .toContain('Informe uma nova senha.');
    expect(page.nativeElement.querySelector('#confirm-password-error')?.textContent)
      .toContain('Confirme sua nova senha.');
    expect(page.nativeElement.querySelector('[data-requirement="uniqueChars"]')
      ?.getAttribute('data-met')).toBe('false');

    const invalidPasswords: Array<[string, string]> = [
      ['Abc1!', 'minLength'],
      [`A${'a'.repeat(254)}1!`, 'maxLength'],
      ['abcdefghijk1!', 'uppercase'],
      ['ABCDEFGHIJK1!', 'lowercase'],
      ['Abcdefghijkl!', 'digit'],
      ['Abcdefghijkl1', 'nonAlphanumeric'],
    ];
    for (const [password, requirement] of invalidPasswords) {
      setPassword(page, 'senha', password);
      setPassword(page, 'confirmacaoSenha', password);
      submit(page);
      expect(page.nativeElement.querySelector(`[data-requirement="${requirement}"]`)
        ?.getAttribute('data-met')).toBe('false');
      expect(resetPassword).not.toHaveBeenCalled();
    }

    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!?');
    submit(page);
    expect(page.nativeElement.querySelector('#confirm-password-error')?.textContent)
      .toContain('As senhas não coincidem.');
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('preserves valid passwords with surrounding spaces and exposes accessible visibility controls', () => {
    setLocation('', `#${encodedCredentials('cliente@example.test', 'synthetic-token')}`);
    const page = createComponent();
    const password = '  Senha válida 987!  ';
    setPassword(page, 'senha', password);
    setPassword(page, 'confirmacaoSenha', password);

    const newPasswordInput = page.nativeElement.querySelector('#new-password') as HTMLInputElement;
    const showPassword = page.nativeElement.querySelector(
      'button[aria-label="Mostrar nova senha"]',
    ) as HTMLButtonElement;
    expect(newPasswordInput.autocomplete).toBe('new-password');
    showPassword.click();
    page.detectChanges();
    expect((page.nativeElement.querySelector('#new-password') as HTMLInputElement).type).toBe('text');
    expect(page.nativeElement.querySelector('button[aria-label="Ocultar nova senha"]')).not.toBeNull();

    const showConfirmation = page.nativeElement.querySelector(
      'button[aria-label="Mostrar confirmação da senha"]',
    ) as HTMLButtonElement;
    expect((page.nativeElement.querySelector('#confirm-password') as HTMLInputElement).autocomplete)
      .toBe('new-password');
    showConfirmation.click();
    page.detectChanges();
    expect((page.nativeElement.querySelector('#confirm-password') as HTMLInputElement).type).toBe('text');

    resetPassword.mockReturnValue(of(undefined));
    submit(page);
    expect(resetPassword).toHaveBeenCalledWith({
      email: 'cliente@example.test',
      token: 'synthetic-token',
      newPassword: password,
    });
  });

  it('shows a terminal generic state for token/account HTTP 400 and discards credentials', () => {
    const email = 'cliente@example.test';
    const token = 'synthetic-invalid-token';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const page = createComponent();
    resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Redefinição não concluída',
        status: 400,
        detail: 'private account or token information',
      },
    })));
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);
    page.detectChanges();

    expect(page.nativeElement.textContent).toContain('Link de recuperação inválido');
    expect(page.nativeElement.textContent)
      .toContain('Este link de recuperação é inválido ou não está mais disponível.');
    expect(page.nativeElement.textContent).not.toContain('private account or token information');
    expect(page.nativeElement.textContent).not.toContain(email);
    expect(page.nativeElement.textContent).not.toContain(token);
    expect(page.nativeElement.querySelector('form')).toBeNull();
    expect(page.nativeElement.querySelector('a[routerLink="/forgot-password"]')).not.toBeNull();
    expect(resetPassword).toHaveBeenCalledOnce();
  });

  it('keeps credentials on the structured password-policy 400 and allows a corrected retry', () => {
    const email = 'cliente@example.test';
    const token = 'synthetic-valid-token';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const page = createComponent();
    resetPassword
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 400,
        error: {
          title: 'Senha inválida',
          status: 400,
          errors: { NewPassword: ['A senha não atende aos requisitos de segurança.'] },
        },
      })))
      .mockReturnValueOnce(of(undefined));
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);
    page.detectChanges();

    expect(page.nativeElement.textContent)
      .toContain('A nova senha não atende à política de segurança.');
    expect(page.nativeElement.textContent).not.toContain('A senha não atende aos requisitos de segurança.');
    expect(page.nativeElement.querySelector('form')).not.toBeNull();
    expect((page.nativeElement.querySelector('#new-password') as HTMLInputElement).value)
      .toBe('Senha válida 987!');

    setPassword(page, 'senha', 'Outra senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Outra senha válida 987!');
    submit(page);
    page.detectChanges();

    expect(resetPassword).toHaveBeenCalledTimes(2);
    expect(resetPassword).toHaveBeenLastCalledWith({
      email,
      token,
      newPassword: 'Outra senha válida 987!',
    });
    expect(page.nativeElement.textContent).toContain('Senha redefinida com sucesso');
  });

  it('preserves credentials through Retry-After seconds and permits only manual retry after expiry', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00.000Z'));
    const email = 'cliente@example.test';
    const token = 'synthetic-token';
    const password = 'Senha válida 987!';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const page = createComponent();
    resetPassword
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 429,
        headers: new HttpHeaders({ 'Retry-After': '3' }),
      })))
      .mockReturnValueOnce(of(undefined));
    setPassword(page, 'senha', password);
    setPassword(page, 'confirmacaoSenha', password);
    submit(page);
    page.detectChanges();

    expect(page.nativeElement.textContent).toContain('Aguarde 3 segundos');
    expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);
    await vi.advanceTimersByTimeAsync(3000);
    page.detectChanges();
    expect(page.nativeElement.textContent)
      .toContain('O período de espera terminou. Você pode tentar novamente.');
    expect(resetPassword).toHaveBeenCalledOnce();

    submit(page);
    page.detectChanges();
    expect(resetPassword).toHaveBeenCalledTimes(2);
    expect(resetPassword).toHaveBeenLastCalledWith({ email, token, newPassword: password });
    expect(page.nativeElement.textContent).toContain('Senha redefinida com sucesso');
  });

  it('supports HTTP-date Retry-After and safely handles absent, invalid, and expired headers', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00.000Z'));
    const page = createComponentWithValidLink();
    resetPassword.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 429,
      headers: new HttpHeaders({ 'Retry-After': 'Thu, 01 Jan 2026 00:00:04 GMT' }),
    })));
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);
    expect(page.nativeElement.textContent).toContain('Aguarde 4 segundos');
    expect(resetPassword).toHaveBeenCalledOnce();

    const retryValues: Array<string | null> = [
      null,
      'not-a-date',
      'Wed, 31 Dec 2025 23:59:59 GMT',
    ];
    for (const [index, value] of retryValues.entries()) {
      const headers = value === null
        ? new HttpHeaders()
        : new HttpHeaders({ 'Retry-After': value });
      resetPassword.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 429,
        headers,
      })));

      await vi.advanceTimersByTimeAsync(5000);
      setPassword(page, 'senha', `Senha válida ${index}87!`);
      setPassword(page, 'confirmacaoSenha', `Senha válida ${index}87!`);
      submit(page);
      page.detectChanges();
      expect(page.nativeElement.textContent)
        .toContain('Muitas solicitações. Aguarde um pouco antes de tentar novamente.');
      expect((page.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
        .toBe(false);
    }

    expect(resetPassword).toHaveBeenCalledTimes(1 + retryValues.length);
  });

  it('shows safe temporary errors and preserves credentials for manual retry', () => {
    const email = 'cliente@example.test';
    const token = 'synthetic-token';
    const password = 'Senha válida 987!';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const page = createComponent();
    const failures: unknown[] = [
      new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }),
      new TimeoutError(),
      new HttpErrorResponse({ status: 408 }),
      new HttpErrorResponse({ status: 503, error: 'private server detail' }),
    ];
    for (const failure of failures) {
      resetPassword.mockReturnValueOnce(throwError(() => failure));
      setPassword(page, 'senha', password);
      setPassword(page, 'confirmacaoSenha', password);
      submit(page);
      page.detectChanges();

      expect(page.nativeElement.textContent)
        .toContain('Não foi possível concluir a redefinição agora.');
      expect(page.nativeElement.textContent).not.toContain('private server detail');
      expect(page.nativeElement.querySelector('form')).not.toBeNull();
      expect((page.nativeElement.querySelector('#new-password') as HTMLInputElement).value)
        .toBe(password);
      expect(resetPassword).toHaveBeenCalledTimes(failures.indexOf(failure) + 1);
    }

    resetPassword.mockReturnValueOnce(of(undefined));
    submit(page);
    page.detectChanges();
    expect(resetPassword).toHaveBeenCalledTimes(failures.length + 1);
    expect(resetPassword).toHaveBeenLastCalledWith({ email, token, newPassword: password });
    expect(page.nativeElement.textContent).toContain('Senha redefinida com sucesso');
  });

  it('cancels the request and discards credentials when destroyed and does not restore them on reopen', () => {
    const email = 'cliente@example.test';
    const token = 'synthetic-token';
    setLocation('', `#${encodedCredentials(email, token)}`);
    const page = createComponent();
    const response = new Subject<void>();
    resetPassword.mockReturnValue(response);
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);
    const textBeforeDestroy = page.nativeElement.textContent;

    page.destroy();
    fixture = undefined;
    expect(response.observed).toBe(false);
    response.next();
    expect(page.nativeElement.textContent).toBe(textBeforeDestroy);

    const reopened = createComponent();
    expect(reopened.nativeElement.textContent).toContain('Link de recuperação inválido');
    expect(reopened.nativeElement.querySelector('form')).toBeNull();
    expect(resetPassword).toHaveBeenCalledOnce();
  });

  it('clears the Retry-After timer when destroyed', () => {
    vi.useFakeTimers();
    setLocation('', `#${encodedCredentials('cliente@example.test', 'synthetic-token')}`);
    const page = createComponent();
    resetPassword.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 429,
      headers: new HttpHeaders({ 'Retry-After': '60' }),
    })));
    setPassword(page, 'senha', 'Senha válida 987!');
    setPassword(page, 'confirmacaoSenha', 'Senha válida 987!');
    submit(page);
    expect(vi.getTimerCount()).toBeGreaterThan(0);

    page.destroy();
    fixture = undefined;
    expect(vi.getTimerCount()).toBe(0);
  });

  function createComponent(): ComponentFixture<ResetPasswordComponent> {
    fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.detectChanges();
    return fixture;
  }

  function createComponentWithValidLink(): ComponentFixture<ResetPasswordComponent> {
    setLocation('', `#${encodedCredentials('cliente@example.test', 'synthetic-token')}`);
    return createComponent();
  }
});

function encodedCredentials(email: string, token: string): string {
  return `email=${encodeURIComponent(email)}&token=${encodeURIComponent(token)}`;
}

function setLocation(search: string, fragment: string): void {
  window.history.replaceState(
    window.history.state,
    '',
    `/reset-password${search}${fragment}`,
  );
}

function setPassword(
  fixture: ComponentFixture<ResetPasswordComponent>,
  control: 'senha' | 'confirmacaoSenha',
  value: string,
): void {
  const id = control === 'senha' ? 'new-password' : 'confirm-password';
  const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<ResetPasswordComponent>): void {
  (fixture.nativeElement.querySelector('form') as HTMLFormElement)
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  fixture.detectChanges();
}
