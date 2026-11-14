import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { AdminDashboardComponent } from './admin-dashboard.component';

describe('AdminDashboardComponent', () => {
  let fixture: ComponentFixture<AdminDashboardComponent>;
  let logout: ReturnType<typeof vi.fn<AdminAuthService['logout']>>;
  let navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>>;

  beforeEach(async () => {
    logout = vi.fn<AdminAuthService['logout']>();
    logout.mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [
        provideRouter([]),
        {
          provide: AdminAuthService,
          useValue: {
            login: vi.fn(),
            getCurrentSession: vi.fn(),
            logout,
          },
        },
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the initial panel and clearly marks future modules as unavailable', () => {
    const content = fixture.nativeElement.textContent;

    expect(content).toContain('Visão geral');
    expect(content).toContain('Produtos');
    expect(content).toContain('Categorias');
    expect(content).toContain('Em breve');
    expect(content).toContain('serão disponibilizados nas próximas etapas');
    expect(fixture.nativeElement.querySelector('a[href="/admin/produtos"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/admin/categorias"]')).toBeNull();
  });

  it('logs out and returns to the administrative login', () => {
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(logout).toHaveBeenCalledOnce();
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/login');
  });

  it('keeps the panel visible and reports a logout failure', () => {
    logout.mockReturnValue(throwError(() => new Error('network failure')));
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível encerrar a sessão.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});
