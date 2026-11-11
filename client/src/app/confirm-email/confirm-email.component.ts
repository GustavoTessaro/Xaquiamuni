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
import { RouterLink } from '@angular/router';
import { TimeoutError } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';

type ConfirmationState =
  | 'loading'
  | 'success'
  | 'invalid'
  | 'retryableFailure'
  | 'failure';

interface ConfirmationCredentials {
  email: string;
  token: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-confirm-email',
  templateUrl: './confirm-email.component.html',
})
export class ConfirmEmailComponent implements OnDestroy {
  protected readonly state = signal<ConfirmationState>('loading');

  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private credentials: ConfirmationCredentials | null = null;
  private initialized = false;
  private requestInProgress = false;
  private confirmed = false;

  constructor() {
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
      this.state.set('invalid');
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
