import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnDestroy, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TimeoutError, Subscription, finalize, interval } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';

type ForgotPasswordControls = {
  email: FormControl<string>;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-forgot-password',
  templateUrl: './forgot-password.component.html',
})
export class ForgotPasswordComponent implements OnDestroy {
  protected readonly submitted = signal(false);
  protected readonly submitting = signal(false);
  protected readonly success = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly retryRemainingSeconds = signal(0);

  protected readonly form = new FormGroup<ForgotPasswordControls>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(256)],
    }),
  });

  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private retryUntil: number | null = null;
  private retryTimer: Subscription | null = null;

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.submitting() && this.retryRemainingSeconds() === 0) {
          this.errorMessage.set('');
        }
      });
  }

  protected emailInvalid(): boolean {
    const email = this.form.controls.email;
    return email.invalid && (email.touched || this.submitted());
  }

  protected submit(): void {
    if (this.submitting() || this.success() || this.retryRemainingSeconds() > 0) {
      return;
    }

    this.submitted.set(true);
    this.form.markAllAsTouched();
    const emailControl = this.form.controls.email;
    const normalizedEmail = emailControl.value.trim();
    if (normalizedEmail !== emailControl.value) {
      emailControl.setValue(normalizedEmail);
    }

    if (this.form.invalid) {
      return;
    }

    this.errorMessage.set('');
    this.submitting.set(true);
    this.authService.forgotPassword(normalizedEmail)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (!this.destroyRef.destroyed) {
            this.submitting.set(false);
          }
        }),
      )
      .subscribe({
        next: () => {
          this.success.set(true);
          this.submitted.set(false);
        },
        error: (error: unknown) => this.handleError(error),
      });
  }

  protected returnToForm(): void {
    if (this.submitting()) {
      return;
    }

    this.errorMessage.set('');
    this.success.set(false);
    this.submitted.set(false);
  }

  ngOnDestroy(): void {
    this.retryTimer?.unsubscribe();
    this.retryTimer = null;
    this.retryUntil = null;
  }

  private handleError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        this.errorMessage.set(
          'Não foi possível processar a solicitação. Revise o e-mail e tente novamente.',
        );
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
          'Não foi possível concluir a solicitação agora. Tente novamente mais tarde.',
        );
        return;
      }
    } else if (error instanceof TimeoutError) {
      this.errorMessage.set(
        'Não foi possível concluir a solicitação agora. Tente novamente mais tarde.',
      );
      return;
    }

    this.errorMessage.set('Não foi possível processar a solicitação. Tente novamente mais tarde.');
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
    this.retryTimer?.unsubscribe();
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
      this.retryTimer?.unsubscribe();
      this.retryTimer = null;
      this.retryUntil = null;
      this.errorMessage.set('O período de espera terminou. Você pode tentar novamente.');
    }
  }
}
