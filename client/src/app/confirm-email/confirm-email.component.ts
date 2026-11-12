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
import { TimeoutError } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';

type ConfirmationState =
  | 'loading'
  | 'success'
  | 'missing'
  | 'invalid'
  | 'retryableFailure'
  | 'failure';

type ResendFeedback = {
  kind: 'success' | 'error';
  message: string;
};

interface ConfirmationCredentials {
  email: string;
  token: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-confirm-email',
  templateUrl: './confirm-email.component.html',
})
export class ConfirmEmailComponent implements OnDestroy {
  protected readonly state = signal<ConfirmationState>('loading');
  protected readonly resendSubmitted = signal(false);
  protected readonly resending = signal(false);
  protected readonly resendFeedback = signal<ResendFeedback | null>(null);
  protected readonly resendForm = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(256)],
    }),
  });

  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private credentials: ConfirmationCredentials | null = null;
  private initialized = false;
  private requestInProgress = false;
  private confirmed = false;

  constructor() {
    this.resendForm.controls.email.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.resending()) {
          this.resendFeedback.set(null);
        }
      });
    this.confirmFromFragment();
  }

  private confirmFromFragment(): void {
    if (this.initialized) {
      return;
    }

    this.initialized = true;
    const window = this.document.defaultView;
    if (!window) {
      this.state.set('invalid');
      return;
    }

    const fragment = window.location.hash;
    if (fragment) {
      window.history.replaceState(
        window.history.state,
        '',
        `${window.location.pathname}${window.location.search}`,
      );
    }

    const credentials = this.parseFragment(fragment);
    if (!credentials) {
      this.state.set(fragment ? 'invalid' : 'missing');
      return;
    }

    this.credentials = credentials;
    this.submitConfirmation();
  }

  protected retryConfirmation(): void {
    if (this.state() === 'retryableFailure') {
      this.submitConfirmation();
    }
  }

  protected submitResend(): void {
    if (this.resending()) {
      return;
    }

    this.resendSubmitted.set(true);
    this.resendForm.markAllAsTouched();
    if (this.resendForm.invalid) {
      return;
    }

    this.resendFeedback.set(null);
    this.resending.set(true);
    this.authService.reenviarConfirmacaoEmail(this.resendForm.controls.email.value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.resending.set(false);
          this.resendFeedback.set({
            kind: 'success',
            message: 'Solicitação recebida. Verifique sua caixa de entrada e a pasta de spam.',
          });
        },
        error: (error: unknown) => {
          this.resending.set(false);
          this.resendFeedback.set({
            kind: 'error',
            message: this.getResendErrorMessage(error),
          });
        },
      });
  }

  protected resendEmailInvalid(): boolean {
    const email = this.resendForm.controls.email;
    return email.invalid && (email.touched || this.resendSubmitted());
  }

  ngOnDestroy(): void {
    this.credentials = null;
    this.requestInProgress = false;
  }

  private submitConfirmation(): void {
    const credentials = this.credentials;
    if (!credentials || this.requestInProgress || this.confirmed) {
      return;
    }

    this.requestInProgress = true;
    this.state.set('loading');
    this.authService.confirmarEmail(credentials.email, credentials.token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.requestInProgress = false;
          this.confirmed = true;
          this.credentials = null;
          this.state.set('success');
        },
        error: (error: unknown) => {
          this.requestInProgress = false;
          if (this.isRetryable(error)) {
            this.state.set('retryableFailure');
            return;
          }

          this.credentials = null;
          this.state.set(
            error instanceof HttpErrorResponse && error.status === 400
              ? 'invalid'
              : 'failure',
          );
        },
      });
  }

  private isRetryable(error: unknown): boolean {
    return error instanceof TimeoutError
      || (error instanceof HttpErrorResponse
        && (error.status === 0 || error.status === 408 || error.status >= 500));
  }

  private getResendErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        return 'Não foi possível processar a solicitação. Revise o e-mail e tente novamente.';
      }

      if (error.status === 429) {
        const waitSeconds = this.getRetryAfterSeconds(error.headers.get('Retry-After'));
        if (waitSeconds !== null) {
          const unit = waitSeconds === 1 ? 'segundo' : 'segundos';
          return `Muitas solicitações. Aguarde ${waitSeconds} ${unit} antes de tentar novamente.`;
        }

        return 'Muitas solicitações. Aguarde um pouco antes de tentar novamente.';
      }

      if (error.status === 0 || error.status === 408 || error.status >= 500) {
        return 'Não foi possível concluir a solicitação agora. Tente novamente mais tarde.';
      }
    } else if (error instanceof TimeoutError) {
      return 'Não foi possível concluir a solicitação agora. Tente novamente mais tarde.';
    }

    return 'Não foi possível processar a solicitação. Tente novamente mais tarde.';
  }

  private getRetryAfterSeconds(value: string | null): number | null {
    const retryAfter = value?.trim();
    if (!retryAfter) {
      return null;
    }

    if (/^\d+$/.test(retryAfter)) {
      const seconds = Number(retryAfter);
      return Number.isSafeInteger(seconds) ? seconds : null;
    }

    const retryAt = Date.parse(retryAfter);
    if (Number.isNaN(retryAt)) {
      return null;
    }

    const seconds = Math.ceil((retryAt - Date.now()) / 1000);
    return seconds > 0 ? seconds : null;
  }

  private parseFragment(fragment: string): ConfirmationCredentials | null {
    if (!fragment.startsWith('#') || fragment.length <= 1) {
      return null;
    }

    const parameters = new URLSearchParams(fragment.slice(1));
    const entries: Array<[string, string]> = [];
    parameters.forEach((value, key) => entries.push([key, value]));

    const emailValues = entries.filter(([key]) => key === 'email');
    const tokenValues = entries.filter(([key]) => key === 'token');
    if (entries.length !== 2 || emailValues.length !== 1 || tokenValues.length !== 1) {
      return null;
    }

    const email = emailValues[0][1].trim();
    const token = tokenValues[0][1];
    if (!this.isValidEmail(email)
      || email.length > 256
      || token.trim().length === 0
      || token.length > 4096) {
      return null;
    }

    return { email, token };
  }

  private isValidEmail(email: string): boolean {
    const separator = email.lastIndexOf('@');
    if (separator <= 0 || separator === email.length - 1 || /\s/.test(email)) {
      return false;
    }

    const domain = email.slice(separator + 1);
    return domain.includes('.') && !domain.startsWith('.') && !domain.endsWith('.');
  }
}
