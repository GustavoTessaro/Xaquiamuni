import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { TemaAdminService } from '../tema-admin.service';
import { AdminLoginComponent } from './admin-login.component';

describe('AdminLoginComponent', () => {
  let fixture: ComponentFixture<AdminLoginComponent>;
  let login: ReturnType<typeof vi.fn<AdminAuthService['login']>>;
  let navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>>;

  beforeEach(async () => {
    login = vi.fn<AdminAuthService['login']>();
    login.mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [AdminLoginComponent],
      providers: [
        provideRouter([]),
        {
          provide: AdminAuthService,
          useValue: {
            login,
            getCurrentSession: vi.fn(),
            logout: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(AdminLoginComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('aplica a preferência administrativa no login sem duplicar o controle', () => {
    const tema = TestBed.inject(TemaAdminService);
    tema.alternar();
    try {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('main').dataset.adminLoginTheme).toBe('dark');
      expect(fixture.nativeElement.querySelector('[data-admin-theme]')).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('button')).toHaveLength(1);
    } finally {
      localStorage.removeItem('xaquiamuni:admin:tema');
    }
  });

  it('validates required and malformed credentials without submitting', () => {
    submit(fixture);

    expect(fixture.nativeElement.textContent).toContain('Informe seu e-mail.');
    expect(fixture.nativeElement.textContent).toContain('Informe sua senha.');
    expect(login).not.toHaveBeenCalled();

    setField(fixture, 'admin-email', 'email-invalido');
    setField(fixture, 'admin-password', 'senha');
    submit(fixture);

    expect(fixture.nativeElement.textContent).toContain('Informe um endereço de e-mail válido.');
    expect(login).not.toHaveBeenCalled();
  });

  it('authenticates and navigates to the protected administrative panel', () => {
    setField(fixture, 'admin-email', 'admin@example.test');
    setField(fixture, 'admin-password', 'senha-ficticia');
    submit(fixture);
    fixture.detectChanges();

    expect(login).toHaveBeenCalledOnce();
    expect(login).toHaveBeenCalledWith('admin@example.test', 'senha-ficticia');
    expect(navigateByUrl).toHaveBeenCalledOnce();
    expect(navigateByUrl).toHaveBeenCalledWith('/admin');
    expect((fixture.nativeElement.querySelector('#admin-password') as HTMLInputElement).value).toBe('');
  });

  it('shows a generic message for invalid or unauthorized credentials', () => {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    setField(fixture, 'admin-email', 'nao-revelar@example.test');
    setField(fixture, 'admin-password', 'senha-incorreta');
    submit(fixture);
    fixture.detectChanges();

    const error = fixture.nativeElement.querySelector('[role="alert"]');
    expect(error.textContent).toContain('Não foi possível autenticar com os dados informados.');
    expect(error.textContent).not.toContain('nao-revelar@example.test');
    expect(error.textContent).not.toContain('senha-incorreta');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('reports an unavailable administrative service without exposing request data', () => {
    login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    setField(fixture, 'admin-email', 'admin@example.test');
    setField(fixture, 'admin-password', 'senha-incorreta');
    submit(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível acessar a administração.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});

function setField(fixture: ComponentFixture<AdminLoginComponent>, id: string, value: string): void {
  const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<AdminLoginComponent>): void {
  const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
  form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  fixture.detectChanges();
}
