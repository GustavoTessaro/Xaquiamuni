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
import { AuthService } from '../core/auth/auth.service';

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
    if (this.signingIn()) {
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
          this.successMessage.set('Login realizado com sucesso.');
        },
        error: (error: unknown) => this.handleLoginError(error),
      });
  }

  private handleLoginError(error: unknown): void {
    this.errorMessage.set(
      error instanceof HttpErrorResponse && error.status === 401
        ? 'E-mail ou senha inválidos.'
        : 'Não foi possível entrar no momento. Verifique sua conexão e tente novamente.',
    );
  }
}
