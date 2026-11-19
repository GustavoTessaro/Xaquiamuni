import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../app.routes';
import { AdminAuthService, AdminSession } from '../core/admin-auth/admin-auth.service';
import { CategoriasService } from '../core/admin-categorias/categorias.service';
import { ProdutosService } from '../core/admin-produtos/produtos.service';
import { AdminCategoriasComponent } from './categorias/admin-categorias.component';
import { AdminDashboardComponent } from './dashboard/admin-dashboard.component';
import { AdminLoginComponent } from './login/admin-login.component';
import { AdminProdutosComponent } from './produtos/admin-produtos.component';
import { AdminInsumosComponent } from './estoque/insumos/admin-insumos.component';
import { AdminInsumoFormComponent } from './estoque/insumos/admin-insumo-form.component';
import { AdminEntradaInsumoComponent } from './estoque/entradas/admin-entrada-insumo.component';
import { AdminConsultaInsumoComponent } from './estoque/consultas/admin-consulta-insumo.component';
import { InsumosService } from '../core/admin-estoque/insumos.service';
import { AdminWorkspaceComponent, AdminWorkspacePage } from './admin-workspace.component';

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

  it.each([
    '/admin/produtos/produto-1/estoque',
    '/admin/estoque/produtos-acabados',
    '/admin/estoque/produtos-acabados/variante-1',
  ])('protege a rota de estoque %s', async (url) => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        { provide: AdminAuthService, useValue: { getCurrentSession } },
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, AdminLoginComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    expect(getCurrentSession).toHaveBeenCalledOnce();
  });

  it('allows a session confirmed by the protected administrative endpoint', async () => {
    getCurrentSession.mockReturnValue(
      of({
        userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df',
      } satisfies AdminSession),
    );
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

  it('protects the categories route and redirects a session without access', async () => {
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
    await harness.navigateByUrl('/admin/categorias', AdminLoginComponent);

    expect(TestBed.inject(Router).url).toBe('/admin/login');
    expect(getCurrentSession).toHaveBeenCalledOnce();
  });

  it('allows an authorized administrator to navigate to categories', async () => {
    getCurrentSession.mockReturnValue(
      of({
        userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df',
      } satisfies AdminSession),
    );
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
        {
          provide: CategoriasService,
          useValue: {
            listar: vi.fn(() => of([])),
            obter: vi.fn(),
            criar: vi.fn(),
            editar: vi.fn(),
            alterarStatus: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/categorias', AdminCategoriasComponent);
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/admin/categorias');
    expect(harness.routeNativeElement?.textContent).toContain('Categorias');
  });

  it('protects the products route and redirects a session without access', async () => {
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
    await harness.navigateByUrl('/admin/produtos', AdminLoginComponent);

    expect(TestBed.inject(Router).url).toBe('/admin/login');
    expect(getCurrentSession).toHaveBeenCalledOnce();
  });

  it('allows an authorized administrator to navigate to products', async () => {
    getCurrentSession.mockReturnValue(
      of({
        userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df',
      } satisfies AdminSession),
    );
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
        {
          provide: CategoriasService,
          useValue: { listar: vi.fn(() => of([])) },
        },
        {
          provide: ProdutosService,
          useValue: { listar: vi.fn(() => of([])) },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/produtos', AdminProdutosComponent);
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/admin/produtos');
    expect(harness.routeNativeElement?.textContent).toContain('Produtos');
  });

  it('protects stock list, create, entry, detail, and consultation routes', async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        {
          provide: AdminAuthService,
          useValue: { getCurrentSession, login: vi.fn(), logout: vi.fn() },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/estoque/insumos', AdminLoginComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    await harness.navigateByUrl('/admin/estoque/insumos/novo', AdminLoginComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    await harness.navigateByUrl('/admin/estoque/insumos/insumo-1', AdminLoginComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    await harness.navigateByUrl('/admin/estoque/insumos/entradas', AdminLoginComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    await harness.navigateByUrl('/admin/estoque/insumos/insumo-1/consultas', AdminLoginComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
  });

  it('allows an authorized administrator to navigate to stock list and create screens', async () => {
    getCurrentSession.mockReturnValue(
      of({
        userId: '4c4a31ce-0a27-4c0e-b903-a96f62fb81df',
      } satisfies AdminSession),
    );
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        {
          provide: AdminAuthService,
          useValue: { getCurrentSession, login: vi.fn(), logout: vi.fn() },
        },
        {
          provide: InsumosService,
          useValue: {
            listar: vi.fn(() =>
              of({
                itens: [],
                totalItens: 0,
                pagina: 1,
                itensPorPagina: 20,
              }),
            ),
            consultarPosicao: vi.fn(() =>
              of({
                resumo: {
                  insumoId: 'insumo-1',
                  nome: 'Base',
                  tipo: 'MateriaPrima',
                  unidadeBase: 'Grama',
                  ativo: true,
                  controlaLote: false,
                  validadeObrigatoria: false,
                  saldoFisico: '0',
                  saldoUtilizavel: '0',
                  saldoReservado: '0',
                  saldoDisponivel: '0',
                  estoqueMinimo: null,
                  estoqueMinimoConfigurado: false,
                  situacao: 'SemEstoque',
                },
                posicoes: [],
              }),
            ),
            listarMovimentacoes: vi.fn(() =>
              of({ itens: [], totalItens: 0, pagina: 1, itensPorPagina: 20 }),
            ),
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/estoque/insumos/novo', AdminInsumoFormComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/estoque/insumos/novo');
    expect(harness.routeNativeElement?.textContent).toContain('Novo insumo');

    await harness.navigateByUrl('/admin/estoque/insumos', AdminInsumosComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/estoque/insumos');
    expect(harness.routeNativeElement?.textContent).toContain('Insumos para produção');
    expect(harness.routeNativeElement?.textContent).not.toContain('Produtos prontos para venda');

    await harness.navigateByUrl(
      '/admin/estoque/insumos/insumo-1/consultas',
      AdminConsultaInsumoComponent,
    );
    expect(TestBed.inject(Router).url).toBe('/admin/estoque/insumos/insumo-1/consultas');
    expect(harness.routeNativeElement?.textContent).toContain('Histórico de movimentações');

    await harness.navigateByUrl('/admin/estoque/insumos/entradas', AdminEntradaInsumoComponent);
    expect(TestBed.inject(Router).url).toBe('/admin/estoque/insumos/entradas');
    expect(harness.routeNativeElement?.textContent).toContain('Registrar entrada de insumo');
  });
});

@Component({
  imports: [AdminWorkspaceComponent],
  standalone: true,
  template: '<app-admin-workspace [activePage]="activePage"></app-admin-workspace>',
})
class AdminWorkspaceNavigationTestPage {
  activePage: AdminWorkspacePage = 'stock';
}

const adminWorkspaceNavigationRoutes: Routes = [
  { path: 'admin', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/produtos', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/categorias', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/estoque/insumos', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/estoque/insumos/novo', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/estoque/insumos/entradas', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/estoque/insumos/operacoes', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/estoque/insumos/:id/consultas', component: AdminWorkspaceNavigationTestPage },
  {
    path: 'admin/estoque/insumos/:id/movimentacoes/:movimentoId/estornar',
    component: AdminWorkspaceNavigationTestPage,
  },
  { path: 'admin/estoque/insumos/:id', component: AdminWorkspaceNavigationTestPage },
  { path: 'admin/estoque/produtos-acabados', component: AdminWorkspaceNavigationTestPage },
  {
    path: 'admin/estoque/produtos-acabados/:varianteId',
    component: AdminWorkspaceNavigationTestPage,
  },
];

describe('AdminWorkspaceComponent stock navigation', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(adminWorkspaceNavigationRoutes),
        { provide: AdminAuthService, useValue: { logout: vi.fn(() => of(undefined)) } },
      ],
    }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('selects only the stock-insumos link on its index route', async () => {
    const harness = await renderAdminWorkspace('/admin/estoque/insumos');

    expectStockLinks(harness, 'insumos');
  });

  it('keeps only the stock-insumos link selected on its subroutes', async () => {
    const harness = await renderAdminWorkspace('/admin/estoque/insumos');
    const subroutes = [
      '/admin/estoque/insumos/novo',
      '/admin/estoque/insumos/entradas',
      '/admin/estoque/insumos/insumo-1',
      '/admin/estoque/insumos/insumo-1/consultas',
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=Contagem',
      '/admin/estoque/insumos/insumo-1/movimentacoes/movimento-1/estornar',
    ];

    for (const route of subroutes) {
      await harness.navigateByUrl(route, AdminWorkspaceNavigationTestPage);
      await settleAdminWorkspace(harness);
      expectStockLinks(harness, 'insumos');
    }
  });

  it('does not advertise the legacy finished-stock routes in the workspace', async () => {
    const harness = await renderAdminWorkspace('/admin/estoque/produtos-acabados');
    expectStockLinks(harness, null);
    await harness.navigateByUrl(
      '/admin/estoque/produtos-acabados/variante-1',
      AdminWorkspaceNavigationTestPage,
    );
    expectStockLinks(harness, null);
  });

  it('does not select the insumos link on other ADMIN pages', async () => {
    const harness = await renderAdminWorkspace('/admin');
    await settleAdminWorkspace(harness);
    expectStockLinks(harness, null);

    await harness.navigateByUrl('/admin/categorias', AdminWorkspaceNavigationTestPage);
    await settleAdminWorkspace(harness);
    expectStockLinks(harness, null);

    await harness.navigateByUrl('/admin/produtos', AdminWorkspaceNavigationTestPage);
    await settleAdminWorkspace(harness);
    expectStockLinks(harness, null);
  });
});

async function renderAdminWorkspace(url: string): Promise<RouterTestingHarness> {
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url, AdminWorkspaceNavigationTestPage);
  await settleAdminWorkspace(harness);
  return harness;
}

async function settleAdminWorkspace(harness: RouterTestingHarness): Promise<void> {
  await harness.fixture.whenStable();
  await Promise.resolve();
  harness.fixture.detectChanges();
}

function expectStockLinks(harness: RouterTestingHarness, active: 'insumos' | null): void {
  const root = harness.routeNativeElement!;
  const insumos = root.querySelector<HTMLAnchorElement>('a[href="/admin/estoque/insumos"]')!;
  expectSelectedLink(insumos, active === 'insumos');
  expect(root.querySelector('nav a[href="/admin/estoque/produtos-acabados"]')).toBeNull();
}

function expectSelectedLink(link: HTMLAnchorElement, selected: boolean): void {
  expect(link.getAttribute('aria-current')).toBe(selected ? 'page' : null);
  expect(link.classList.contains('bg-emerald-50')).toBe(selected);
  expect(link.classList.contains('text-emerald-900')).toBe(selected);
}
