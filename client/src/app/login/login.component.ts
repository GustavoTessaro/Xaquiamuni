import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  inject,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription, TimeoutError, finalize, interval } from 'rxjs';
import { AuthService, AuthSession } from '../core/auth/auth.service';

type LoginControls = {
  email: FormControl<string>;
  senha: FormControl<string>;
};

type ConfirmationResendFeedback = {
  kind: 'success' | 'error';
  message: string;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-login',
  styleUrl: './login.component.css',
  templateUrl: './login.component.html',
})
export class LoginComponent implements OnDestroy {
  protected readonly submitted = signal(false);
  protected readonly passwordVisible = signal(false);
  protected readonly signingIn = signal(false);
  protected readonly signingOut = signal(false);
  protected readonly checkingSession = signal(true);
  protected readonly awaitingEmailConfirmation = signal(false);
  protected readonly resendingConfirmation = signal(false);
  protected readonly confirmationResendFeedback = signal<ConfirmationResendFeedback | null>(null);
  protected readonly retryRemainingSeconds = signal(0);
  protected readonly session = signal<AuthSession | null>(null);
  protected readonly sessionError = signal('');
  protected readonly successMessage = signal('');
  protected readonly errorMessage = signal('');

  private readonly destroyRef = inject(DestroyRef);
  private pendingConfirmationEmail: string | null = null;
  private retryUntil: number | null = null;
  private retryTimer: Subscription | null = null;

  protected readonly form = new FormGroup<LoginControls>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    senha: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  constructor(private readonly authService: AuthService) {
    this.checkSession('initial');
    this.form.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.successMessage.set('');
        this.errorMessage.set('');
      });
  }

  protected fieldInvalid(control: FormControl<string>): boolean {
    return control.invalid && (control.touched || this.submitted());
  }

  protected togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  protected submit(): void {
    if (this.signingIn() || this.checkingSession()) {
      return;
    }

    this.submitted.set(true);
    this.successMessage.set('');
    this.errorMessage.set('');
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }

    const { email, senha } = this.form.getRawValue();
    this.signingIn.set(true);
    this.authService.login(email, senha)
      .pipe(finalize(() => this.signingIn.set(false)))
      .subscribe({
        next: () => {
          this.form.controls.senha.reset();
          this.passwordVisible.set(false);
          this.submitted.set(false);
          this.checkSession('login');
        },
        error: (error: unknown) => {
          if (this.isEmailNotConfirmed(error)) {
            this.showEmailConfirmationPending(email);
            return;
          }

          this.handleLoginError(error);
        },
      });
  }

  protected resendConfirmationEmail(): void {
    const email = this.pendingConfirmationEmail;
    if (
      !email
      || this.resendingConfirmation()
      || this.retryRemainingSeconds() > 0
    ) {
      return;
    }

    this.confirmationResendFeedback.set(null);
    this.resendingConfirmation.set(true);
    this.authService.reenviarConfirmacaoEmail(email)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (!this.destroyRef.destroyed) {
            this.resendingConfirmation.set(false);
          }
        }),
      )
      .subscribe({
        next: () => this.confirmationResendFeedback.set({
          kind: 'success',
          message: 'Solicitação recebida. Se houver uma conta elegível, enviaremos um e-mail de confirmação. '
            + 'Verifique também a caixa de entrada e a pasta de spam.',
        }),
        error: (error: unknown) => this.handleConfirmationResendError(error),
      });
  }

  protected returnToLogin(): void {
    this.pendingConfirmationEmail = null;
    this.awaitingEmailConfirmation.set(false);
    this.confirmationResendFeedback.set(null);
    this.form.controls.senha.reset('');
    this.passwordVisible.set(false);
    this.submitted.set(false);
    this.successMessage.set('');
    this.errorMessage.set('');
  }

  protected logout(): void {
    if (this.signingOut() || this.checkingSession()) {
      return;
    }

    this.errorMessage.set('');
    this.signingOut.set(true);
    this.authService.logout()
      .pipe(finalize(() => this.signingOut.set(false)))
      .subscribe({
        next: () => this.checkSession('logout'),
        error: () => this.errorMessage.set(
          'Não foi possível encerrar a sessão. Tente novamente.',
        ),
      });
  }

  protected retrySessionCheck(): void {
    if (!this.checkingSession()) {
      this.checkSession('retry');
    }
  }

  ngOnDestroy(): void {
    this.pendingConfirmationEmail = null;
    this.retryTimer?.unsubscribe();
    this.retryTimer = null;
    this.retryUntil = null;
  }

  private showEmailConfirmationPending(email: string): void {
    this.pendingConfirmationEmail = email.trim();
    this.awaitingEmailConfirmation.set(true);
    this.confirmationResendFeedback.set(null);
    this.form.controls.senha.reset('');
    this.passwordVisible.set(false);
    this.submitted.set(false);
    this.successMessage.set('');
    this.errorMessage.set('');
  }

  private isEmailNotConfirmed(error: unknown): boolean {
    if (!(error instanceof HttpErrorResponse) || error.status !== 403) {
      return false;
    }

    const body: unknown = error.error;
    return typeof body === 'object'
      && body !== null
      && 'code' in body
      && body.code === 'EMAIL_NOT_CONFIRMED';
  }

  private handleLoginError(error: unknown): void {
    this.errorMessage.set(
      error instanceof HttpErrorResponse && error.status === 401
        ? 'E-mail ou senha inválidos.'
        : 'Não foi possível entrar no momento. Verifique sua conexão e tente novamente.',
    );
  }

  private handleConfirmationResendError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        this.confirmationResendFeedback.set({
          kind: 'error',
          message: 'Não foi possível processar a solicitação. Revise o e-mail e tente novamente.',
        });
        return;
      }

      if (error.status === 429) {
        const retryAt = this.getRetryAt(error.headers.get('Retry-After'));
        if (retryAt !== null) {
          this.startRetryCountdown(retryAt);
        } else {
          this.confirmationResendFeedback.set({
            kind: 'error',
            message: 'Muitas solicitações. Aguarde um pouco antes de tentar novamente.',
          });
        }
        return;
      }

      if (error.status === 0 || error.status === 408 || error.status >= 500) {
        this.confirmationResendFeedback.set({
          kind: 'error',
          message: 'Não foi possível concluir a solicitação agora. Tente novamente mais tarde.',
        });
        return;
      }
    } else if (error instanceof TimeoutError) {
      this.confirmationResendFeedback.set({
        kind: 'error',
        message: 'Não foi possível concluir a solicitação agora. Tente novamente mais tarde.',
      });
      return;
    }

    this.confirmationResendFeedback.set({
      kind: 'error',
      message: 'Não foi possível processar a solicitação. Tente novamente mais tarde.',
    });
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
      this.confirmationResendFeedback.set({
        kind: 'error',
        message: 'O período de espera terminou. Você pode tentar novamente.',
      });
    }
  }

  private checkSession(purpose: 'initial' | 'login' | 'logout' | 'retry'): void {
    this.checkingSession.set(true);
    this.sessionError.set('');
    this.successMessage.set('');
    this.authService.obterSessao()
      .pipe(finalize(() => this.checkingSession.set(false)))
      .subscribe({
        next: (session) => {
          this.session.set(session);
          if (purpose === 'login' && session) {
            this.successMessage.set('Login realizado com sucesso.');
          } else if (purpose === 'login' && !session) {
            this.errorMessage.set(
              'Não foi possível confirmar a sessão. Tente entrar novamente.',
            );
          } else if (purpose === 'logout' && session) {
            this.errorMessage.set(
              'Não foi possível confirmar o encerramento da sessão. Tente novamente.',
            );
          } else {
            this.errorMessage.set('');
          }
        },
        error: () => {
          this.sessionError.set(
            'Não foi possível verificar sua sessão. Tente novamente.',
          );
          if (purpose === 'login') {
            this.errorMessage.set(
              'Não foi possível confirmar a sessão. Tente verificar novamente.',
            );
          } else if (purpose === 'logout') {
            this.errorMessage.set(
              'Não foi possível confirmar o encerramento da sessão. Tente novamente.',
            );
          } else {
            this.errorMessage.set('');
          }
        },
      });
  }
}
