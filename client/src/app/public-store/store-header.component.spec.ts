import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService, AuthSession } from '../core/auth/auth.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { StoreHeaderComponent } from './store-header.component';

describe('StoreHeaderComponent', () => {
  let fixture: ComponentFixture<StoreHeaderComponent>;

  it('offers the account session and sign-out action to an authenticated customer', async () => {
    const session: AuthSession = { userId: 'customer-1' };
    const logout = vi.fn(() => of(undefined));
    await TestBed.configureTestingModule({
      imports: [StoreHeaderComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of(session)),
            logout,
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: { listarCategorias: vi.fn(() => of([])) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreHeaderComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const brand = element.querySelector<HTMLAnchorElement>('.store-brand');
    expect(brand?.getAttribute('href')).toBe('/');
    expect(brand?.querySelector('img[alt="Xaquiamuni Produtos Naturais"]')?.getAttribute('src'))
      .toBe('/logo-xaquiamuni.jpeg');
    expect(element.textContent).toContain('Meus pedidos');
    const accountLink = element.querySelector('.store-account__link') as HTMLAnchorElement;
    expect(accountLink.getAttribute('href')).toBe('/pedidos');
    expect(element.querySelector('.store-mobile-nav a[href="/pedidos"]')).not.toBeNull();
    expect(element.querySelector('.store-account__logout')?.textContent).toContain('Sair');
    expect(element.querySelector('.store-account__register')).toBeNull();
    expect(logout).not.toHaveBeenCalled();
  });

  it('clears the authenticated header after logout and returns to the public homepage', async () => {
    const logout = vi.fn(() => of(undefined));
    await TestBed.configureTestingModule({
      imports: [StoreHeaderComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of({ userId: 'customer-1' })),
            logout,
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: { listarCategorias: vi.fn(() => of([])) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreHeaderComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    (fixture.nativeElement.querySelector('.store-account__logout') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(logout).toHaveBeenCalledOnce();
    expect(navigateByUrl).toHaveBeenCalledWith('/');
    expect(fixture.nativeElement.textContent).toContain('Entrar');
    expect(fixture.nativeElement.textContent).toContain('Criar conta');
    expect(fixture.nativeElement.querySelector('.store-account__logout')).toBeNull();
    expect(fixture.nativeElement.querySelector('.store-nav a[href="/produtos"]')).not.toBeNull();
  });

  it('uses one accessible categories disclosure and closes on Escape or an outside click', async () => {
    await TestBed.configureTestingModule({
      imports: [StoreHeaderComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of(null)),
            logout: vi.fn(() => of(undefined)),
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias: vi.fn(() =>
              of([
                { id: 'soap', nome: 'Sabonetes Artesanais', descricao: null },
                { id: 'future', nome: 'Novidades', descricao: null },
              ]),
            ),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreHeaderComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const categoryMenu = element.querySelector('.store-nav__categories') as HTMLDetailsElement;
    const trigger = categoryMenu.querySelector('summary') as HTMLElement;
    expect(element.querySelectorAll('.store-nav__categories')).toHaveLength(1);
    expect(element.textContent).not.toContain('Explorar categorias');
    expect(categoryMenu.open).toBe(false);

    trigger.click();
    fixture.detectChanges();
    expect(categoryMenu.open).toBe(true);
    expect(element.querySelector('.store-nav__dropdown')?.textContent).toContain('Novidades');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(categoryMenu.open).toBe(false);
    expect(document.activeElement).toBe(trigger);

    trigger.click();
    fixture.detectChanges();
    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(categoryMenu.open).toBe(false);
  });
});
