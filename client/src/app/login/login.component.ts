import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService, AuthSession } from '../core/auth/auth.service';

type LoginControls = {
  email: FormControl<string>;
  senha: FormControl<string>;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-login',
  styleUrl: './login.component.css',
  templateUrl: './login.component.html',
})
export class LoginComponent {
  protected readonly submitted = signal(false);
  protected readonly signingIn = signal(false);
  protected readonly signingOut = signal(false);
  protected readonly checkingSession = signal(true);
  protected readonly session = signal<AuthSession | null>(null);
  protected readonly sessionError = signal('');
  protected readonly successMessage = signal('');
  protected readonly errorMessage = signal('');

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
          this.submitted.set(false);
          this.checkSession('login');
        },
        error: (error: unknown) => this.handleLoginError(error),
      });
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

  private handleLoginError(error: unknown): void {
    this.errorMessage.set(
      error instanceof HttpErrorResponse && error.status === 401
        ? 'E-mail ou senha inválidos.'
        : 'Não foi possível entrar no momento. Verifique sua conexão e tente novamente.',
    );
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
