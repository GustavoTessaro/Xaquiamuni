import { ChangeDetectionStrategy, Component, input, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AdminAuthService } from '../core/admin-auth/admin-auth.service';

export type AdminWorkspacePage = 'overview' | 'categories' | 'products' | 'stock';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  selector: 'app-admin-workspace',
  styleUrl: './admin-workspace.component.css',
  templateUrl: './admin-workspace.component.html',
})
export class AdminWorkspaceComponent {
  readonly activePage = input.required<AdminWorkspacePage>();
  protected readonly signingOut = signal(false);
  protected readonly errorMessage = signal('');

  private readonly adminAuthService = inject(AdminAuthService);
  private readonly router = inject(Router);

  protected logout(): void {
    if (this.signingOut()) {
      return;
    }

    this.signingOut.set(true);
    this.errorMessage.set('');
    this.adminAuthService
      .logout()
      .pipe(finalize(() => this.signingOut.set(false)))
      .subscribe({
        next: () => {
          void this.router.navigateByUrl('/admin/login');
        },
        error: () => {
          this.errorMessage.set('Não foi possível encerrar a sessão. Tente novamente.');
        },
      });
  }
}
