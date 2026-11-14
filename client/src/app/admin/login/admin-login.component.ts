import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';

type AdminLoginControls = {
  email: FormControl<string>;
  senha: FormControl<string>;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-admin-login',
  styleUrl: './admin-login.component.css',
  templateUrl: './admin-login.component.html',
})
export class AdminLoginComponent {
  protected readonly submitted = signal(false);
  protected readonly signingIn = signal(false);
  protected readonly errorMessage = signal('');

  private readonly destroyRef = inject(DestroyRef);
  private readonly adminAuthService = inject(AdminAuthService);
  private readonly router = inject(Router);

  protected readonly form = new FormGroup<AdminLoginControls>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    senha: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  protected fieldInvalid(control: FormControl<string>): boolean {
    return control.invalid && (control.touched || this.submitted());
  }

  protected submit(): void {
    if (this.signingIn()) {
      return;
    }

    this.submitted.set(true);
    this.errorMessage.set('');
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }

    const { email, senha } = this.form.getRawValue();
    this.signingIn.set(true);
    this.adminAuthService.login(email, senha)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.signingIn.set(false)),
      )
      .subscribe({
        next: () => {
          this.form.controls.senha.reset();
          void this.router.navigateByUrl('/admin');
        },
        error: (error: unknown) => {
          this.form.controls.senha.reset();
          this.submitted.set(false);
          this.showLoginError(error);
        },
      });
  }

  private showLoginError(error: unknown): void {
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      this.errorMessage.set('Não foi possível autenticar com os dados informados.');
      return;
    }

    this.errorMessage.set('Não foi possível acessar a administração. Tente novamente.');
  }
}
