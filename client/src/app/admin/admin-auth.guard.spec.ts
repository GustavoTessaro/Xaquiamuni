import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../app.routes';
import { AdminAuthService, AdminSession } from '../core/admin-auth/admin-auth.service';
import { AdminDashboardComponent } from './dashboard/admin-dashboard.component';
import { AdminLoginComponent } from './login/admin-login.component';

describe('adminAuthGuard', () => {
  let getCurrentSession: ReturnType<typeof vi.fn<AdminAuthService['getCurrentSession']>>;

  beforeEach(() => {
    getCurrentSession = vi.fn<AdminAuthService['getCurrentSession']>();
    getCurrentSession.mockReturnValue(of(null));
  });

  it('redirects sessions without ADMIN authorization to the administrative login', async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        {
          provide: AdminAuthService,
          useValue: {
            getCurrentSession,
            login: vi.fn(),
            logout: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin', AdminLoginComponent);

    expect(TestBed.inject(Router).url).toBe('/admin/login');
    expect(getCurrentSession).toHaveBeenCalledOnce();
  });

  it('allows a session confirmed by the protected administrative endpoint', async () => {
    getCurrentSession.mockReturnValue(of({
      userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df',
    } satisfies AdminSession));
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        {
          provide: AdminAuthService,
          useValue: {
            getCurrentSession,
            login: vi.fn(),
            logout: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin', AdminDashboardComponent);

    expect(TestBed.inject(Router).url).toBe('/admin');
    expect(harness.routeNativeElement?.textContent).toContain('Bem-vinda à administração');
  });
});
