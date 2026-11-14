import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AdminAuthService } from '../core/admin-auth/admin-auth.service';

export const adminAuthGuard: CanActivateFn = () => {
  const adminAuthService = inject(AdminAuthService);
  const router = inject(Router);

  return adminAuthService.getCurrentSession().pipe(
    map((session) => session ? true : router.createUrlTree(['/admin/login'])),
  );
};
