import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-admin-dashboard',
  styleUrl: './admin-dashboard.component.css',
  templateUrl: './admin-dashboard.component.html',
})
export class AdminDashboardComponent {
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
    this.adminAuthService.logout()
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
