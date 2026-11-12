import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TimeoutError, Subscription, finalize, interval } from 'rxjs';
import {
  brazilianEmailValidator,
  identityPasswordValidator,
  passwordsMatchValidator,
  registrationPasswordRequirements,
} from '../cadastro/registration.validators';
import { AuthService } from '../core/auth/auth.service';

type ResetPasswordControls = {
  senha: FormControl<string>;
  confirmacaoSenha: FormControl<string>;
};

type ResetPasswordState = 'invalidLink' | 'form' | 'success';

interface ResetCredentials {
  email: string;
  token: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-reset-password',
  templateUrl: './reset-password.component.html',
})
export class ResetPasswordComponent implements OnDestroy {
  protected readonly passwordRequirements = registrationPasswordRequirements;
  protected readonly state = signal<ResetPasswordState>('invalidLink');
  protected readonly submitted = signal(false);
  protected readonly submitting = signal(false);
  protected readonly passwordVisible = signal(false);
  protected readonly confirmationVisible = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly retryRemainingSeconds = signal(0);

  protected readonly form = new FormGroup<ResetPasswordControls>({
    senha: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.maxLength(256),
        identityPasswordValidator,
      ],
    }),
    confirmacaoSenha: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)],
    }),
  }, { validators: [passwordsMatchValidator] });

  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private credentials: ResetCredentials | null = null;
  private retryUntil: number | null = null;
  private retryTimer: Subscription | null = null;

  constructor() {
    const credentials = this.readAndCleanCredentials();
    if (credentials) {
      this.credentials = credentials;
      this.state.set('form');
    }

    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.submitting() && this.retryRemainingSeconds() === 0) {
          this.errorMessage.set('');
        }
      });
  }

  protected passwordInvalid(): boolean {
    const password = this.form.controls.senha;
    return password.invalid && (password.touched || this.submitted());
  }

  protected confirmationInvalid(): boolean {
    const confirmation = this.form.controls.confirmacaoSenha;
    return (confirmation.invalid || this.form.hasError('passwordsMismatch'))
      && (confirmation.touched || this.submitted());
  }

  protected submit(): void {
    const credentials = this.credentials;
    if (
      !credentials
      || this.state() !== 'form'
      || this.submitting()
      || this.retryRemainingSeconds() > 0
    ) {
      return;
    }

    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }

    const newPassword = this.form.controls.senha.value;
    this.errorMessage.set('');
    this.submitting.set(true);
    this.authService.resetPassword({
      email: credentials.email,
      token: credentials.token,
      newPassword,
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (!this.destroyRef.destroyed) {
            this.submitting.set(false);
          }
        }),
      )
      .subscribe({
        next: () => this.handleSuccess(),
        error: (error: unknown) => this.handleError(error),
      });
  }

  protected togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  protected toggleConfirmationVisibility(): void {
    this.confirmationVisible.update((visible) => !visible);
  }

  ngOnDestroy(): void {
    this.clearRetryCountdown();
    this.credentials = null;
    this.clearPasswords();
  }

  private readAndCleanCredentials(): ResetCredentials | null {
    const window = this.document.defaultView;
    if (!window) {
      return null;
    }

    const fragment = window.location.hash;
    const safeSearch = this.removeSensitiveQueryParameters(window.location.search);
    const hasSensitiveUrlData = fragment.length > 0 || safeSearch !== window.location.search;

    if (hasSensitiveUrlData) {
      try {
        window.history.replaceState(
          window.history.state,
          '',
          `${window.location.pathname}${safeSearch}`,
        );
      } catch {
        return null;
      }
    }

    return this.parseFragment(fragment);
  }

  private removeSensitiveQueryParameters(search: string): string {
    if (!search) {
      return '';
    }

    const safeParameters = search.slice(1).split('&').filter((parameter) => {
      const rawKey = parameter.split('=', 1)[0].replace(/\+/g, ' ');
      try {
        const key = decodeURIComponent(rawKey).toLowerCase();
        return key !== 'email' && key !== 'token';
      } catch {
        return true;
      }
    });

    return safeParameters.length > 0 ? `?${safeParameters.join('&')}` : '';
  }

  private parseFragment(fragment: string): ResetCredentials | null {
    if (!fragment.startsWith('#') || fragment.length <= 1) {
      return null;
    }

    // Angular may decode and re-encode the whole fragment, making token ampersands ambiguous.
    const rawFragment = fragment.slice(1);
    const tokenSeparator = rawFragment.indexOf('&token=');
    if (
      !rawFragment.startsWith('email=')
      || tokenSeparator <= 'email='.length
      || rawFragment.indexOf('&token=', tokenSeparator + 1) !== -1
    ) {
      return null;
    }

    const rawEmail = rawFragment.slice('email='.length, tokenSeparator);
    const rawToken = rawFragment.slice(tokenSeparator + '&token='.length);
    if (rawToken.includes('&email=')) {
      return null;
    }

    let email: string;
    let token: string;
    try {
      email = decodeURIComponent(rawEmail);
      token = decodeURIComponent(rawToken);
    } catch {
      return null;
    }

    if (
      email.length > 256
      || brazilianEmailValidator(new FormControl(email)) !== null
      || token.trim().length === 0
      || token.length > 4096
    ) {
      return null;
    }

    return { email, token };
  }

  private handleSuccess(): void {
    this.credentials = null;
    this.clearPasswords();
    this.clearRetryCountdown();
    this.errorMessage.set('');
    this.state.set('success');
    this.submitted.set(false);
  }

  private handleError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        if (this.isPasswordPolicyProblem(error.error)) {
          this.errorMessage.set(
            'A nova senha não atende à política de segurança. Revise os requisitos e tente novamente.',
          );
          return;
        }

        this.credentials = null;
        this.clearPasswords();
        this.clearRetryCountdown();
        this.state.set('invalidLink');
        return;
      }

      if (error.status === 429) {
        const retryAt = this.getRetryAt(error.headers.get('Retry-After'));
        if (retryAt !== null) {
          this.startRetryCountdown(retryAt);
        } else {
          this.errorMessage.set('Muitas solicitações. Aguarde um pouco antes de tentar novamente.');
        }
        return;
      }

      if (error.status === 0 || error.status === 408 || error.status >= 500) {
        this.errorMessage.set(
          'Não foi possível concluir a redefinição agora. Tente novamente mais tarde.',
        );
        return;
      }
    } else if (error instanceof TimeoutError) {
      this.errorMessage.set(
        'Não foi possível concluir a redefinição agora. Tente novamente mais tarde.',
      );
      return;
    }

    this.errorMessage.set('Não foi possível processar a solicitação. Tente novamente mais tarde.');
  }

  private isPasswordPolicyProblem(body: unknown): boolean {
    if (typeof body !== 'object' || body === null || !('title' in body) || !('errors' in body)) {
      return false;
    }

    const problem = body as Record<string, unknown>;
    if (problem['title'] !== 'Senha inválida') {
      return false;
    }

    const errors = problem['errors'];
    if (typeof errors !== 'object' || errors === null || !('NewPassword' in errors)) {
      return false;
    }

    const passwordErrors = (errors as Record<string, unknown>)['NewPassword'];
    return Array.isArray(passwordErrors)
      && passwordErrors.some((message) => typeof message === 'string');
  }

  private getRetryAt(value: string | null): number | null {
    const retryAfter = value?.trim();
    if (!retryAfter) {
      return null;
    }

    const now = Date.now();
    if (/^\d+$/.test(retryAfter)) {
      const seconds = Number(retryAfter);
      if (!Number.isSafeInteger(seconds) || seconds <= 0) {
        return null;
      }

      const retryAt = now + seconds * 1000;
      return Number.isSafeInteger(retryAt) ? retryAt : null;
    }

    const retryAt = Date.parse(retryAfter);
    return Number.isNaN(retryAt) || retryAt <= now ? null : retryAt;
  }

  private startRetryCountdown(retryAt: number): void {
    this.clearRetryCountdown();
    this.retryUntil = retryAt;
    this.updateRetryCountdown();

    if (this.retryRemainingSeconds() > 0) {
      this.retryTimer = interval(1000)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(() => this.updateRetryCountdown());
    }
  }

  private updateRetryCountdown(): void {
    if (this.retryUntil === null) {
      return;
    }

    const remaining = Math.max(0, Math.ceil((this.retryUntil - Date.now()) / 1000));
    this.retryRemainingSeconds.set(remaining);
    if (remaining === 0) {
      this.clearRetryCountdown();
      this.errorMessage.set('O período de espera terminou. Você pode tentar novamente.');
    }
  }

  private clearRetryCountdown(): void {
    this.retryTimer?.unsubscribe();
    this.retryTimer = null;
    this.retryUntil = null;
    this.retryRemainingSeconds.set(0);
  }

  private clearPasswords(): void {
    this.form.reset({ senha: '', confirmacaoSenha: '' }, { emitEvent: false });
    this.passwordVisible.set(false);
    this.confirmationVisible.set(false);
  }
}
