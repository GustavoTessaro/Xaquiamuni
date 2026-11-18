import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminPagina,
  AdminResumoInsumoPreciso,
  InsumosService,
} from '../../../core/admin-estoque/insumos.service';
import { AdminInsumosComponent } from './admin-insumos.component';

describe('AdminInsumosComponent', () => {
  let fixture: ComponentFixture<AdminInsumosComponent>;
  let listar: ReturnType<typeof vi.fn<InsumosService['listar']>>;
  let navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>>;

  const rows: AdminResumoInsumoPreciso[] = [
    {
      insumoId: 'insumo-a',
      nome: 'Base glicerinada',
      tipo: 'MateriaPrima',
      unidadeBase: 'Grama',
      ativo: true,
      controlaLote: true,
      validadeObrigatoria: true,
      saldoFisico: '1000',
      saldoUtilizavel: '800',
      saldoReservado: '0',
      saldoDisponivel: '800',
      estoqueMinimo: '500',
      estoqueMinimoConfigurado: true,
      situacao: 'Normal',
    },
    {
      insumoId: 'insumo-b',
      nome: 'Pavio',
      tipo: 'Outro',
      unidadeBase: 'Unidade',
      ativo: false,
      controlaLote: false,
      validadeObrigatoria: false,
      saldoFisico: '0',
      saldoUtilizavel: '0',
      saldoReservado: '0',
      saldoDisponivel: '0',
      estoqueMinimo: null,
      estoqueMinimoConfigurado: false,
      situacao: 'MinimoNaoConfigurado',
    },
  ];

  beforeEach(async () => {
    listar = vi.fn<InsumosService['listar']>().mockReturnValue(
      of({
        itens: rows,
        totalItens: rows.length,
        pagina: 1,
        itensPorPagina: 20,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [AdminInsumosComponent],
      providers: [
        provideRouter([]),
        { provide: InsumosService, useValue: { listar } },
        { provide: AdminAuthService, useValue: { logout: vi.fn(() => of(undefined)) } },
      ],
    }).compileComponents();
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('loads physical and available balances and keeps cadastro status distinct from stock situation', () => {
    render();

    expect(listar).toHaveBeenCalledWith({ pagina: 1, itensPorPagina: 20 });
    expect(text()).toContain('Insumos para produção');
    expect(text()).toContain('Total registrado, incluindo quantidades indisponíveis.');
    expect(text()).toContain('Quantidade liberada para utilização.');
    expect(text()).toContain('Referência para alertar sobre necessidade de reposição.');
    expect(text()).toContain('Base glicerinada');
    expect(text()).toContain('800 g');
    expect(text()).toContain('Pavio');
    const pavio = Array.from(root().querySelectorAll('article')).find(
      (article) => article.querySelector('h2')?.textContent?.trim() === 'Pavio',
    );
    expect(pavio?.textContent).toContain('Inativo');
    expect(pavio?.textContent).toContain('Mínimo não configurado');
    expect(pavio?.textContent).not.toContain('Sem estoque');
    const base = Array.from(root().querySelectorAll('article')).find(
      (article) => article.querySelector('h2')?.textContent?.trim() === 'Base glicerinada',
    );
    expect(
      base?.querySelector('a[href="/admin/estoque/insumos/entradas?insumoId=insumo-a"]'),
    ).not.toBeNull();
    expect(pavio?.querySelector('a[href*="/entradas"]')).toBeNull();
  });

  it('displays six-place balances without rounding large quantities', () => {
    listar.mockReturnValueOnce(
      of({
        itens: [
          {
            ...rows[0]!,
            saldoFisico: '999999999999.999999',
            saldoDisponivel: '999999999999.999999',
          },
        ],
        totalItens: 1,
        pagina: 1,
        itensPorPagina: 20,
      }),
    );
    render();

    expect(text()).toContain('999.999.999.999,999999 g');
  });

  it('links each supply card directly to its stock and history while preserving existing actions', () => {
    render();

    const cards = Array.from(root().querySelectorAll('article'));
    expect(cards).toHaveLength(rows.length);
    for (const [index, insumo] of rows.entries()) {
      const card = cards[index]!;
      const path = `/admin/estoque/insumos/${insumo.insumoId}`;
      const stock = card.querySelector<HTMLAnchorElement>(`a[href="${path}/consultas"]`)!;
      expect(stock.textContent?.trim()).toBe('Ver estoque e histórico');
      expect(stock.getAttribute('aria-label')).toBe(`Ver estoque e histórico de ${insumo.nome}`);
      expect(stock.tabIndex).toBe(0);
      expect(stock.classList.contains('focus-visible:outline-2')).toBe(true);
      expect(stock.parentElement?.classList.contains('flex-wrap')).toBe(true);
      expect(card.querySelector(`a[href="${path}"]`)?.textContent?.trim()).toBe('Ver e editar');
      const entrada = card.querySelector(
        `a[href="/admin/estoque/insumos/entradas?insumoId=${insumo.insumoId}"]`,
      );
      expect(entrada?.textContent?.trim() ?? null).toBe(insumo.ativo ? 'Registrar entrada' : null);

      stock.click();
      expect(String(navigateByUrl.mock.lastCall?.[0])).toBe(`${path}/consultas`);
    }
  });

  it('sends search, status, situation, and pagination to the API instead of filtering locally', () => {
    const page: AdminPagina<AdminResumoInsumoPreciso> = {
      itens: rows,
      totalItens: 45,
      pagina: 1,
      itensPorPagina: 20,
    };
    listar.mockReturnValue(of(page));
    render();
    vi.useFakeTimers();

    setField('stock-supply-search', '  base  ');
    vi.advanceTimersByTime(300);
    fixture.detectChanges();
    expect(listar).toHaveBeenLastCalledWith({
      nome: 'base',
      pagina: 1,
      itensPorPagina: 20,
    });

    setSelect('stock-supply-active-filter', 'inativos');
    expect(listar).toHaveBeenLastCalledWith({
      nome: 'base',
      ativo: false,
      pagina: 1,
      itensPorPagina: 20,
    });

    setSelect('stock-supply-situation-filter', 'Normal');
    expect(listar).toHaveBeenLastCalledWith({
      nome: 'base',
      ativo: false,
      situacao: 'Normal',
      pagina: 1,
      itensPorPagina: 20,
    });

    clickButton('Próxima');
    expect(listar).toHaveBeenLastCalledWith({
      nome: 'base',
      ativo: false,
      situacao: 'Normal',
      pagina: 2,
      itensPorPagina: 20,
    });
  });

  it('shows loading and empty states and retries a safe list error', () => {
    const pending = new Subject<AdminPagina<AdminResumoInsumoPreciso>>();
    listar
      .mockReturnValueOnce(pending)
      .mockReturnValueOnce(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 503,
              error: { message: 'SQL connection and infrastructure details' },
            }),
        ),
      )
      .mockReturnValueOnce(
        of({
          itens: [],
          totalItens: 0,
          pagina: 1,
          itensPorPagina: 20,
        }),
      );
    render();
    expect(text()).toContain('Carregando insumos');

    pending.next({ itens: [], totalItens: 0, pagina: 1, itensPorPagina: 20 });
    pending.complete();
    fixture.detectChanges();
    expect(text()).toContain('Nenhum insumo encontrado');
    expect(root().querySelector('a[href$="/consultas"]')).toBeNull();

    setSelect('stock-supply-active-filter', 'inativos');
    expect(text()).toContain('Não foi possível carregar os insumos.');
    expect(text()).not.toContain('SQL connection');
    clickButton('Tentar novamente');
    expect(text()).toContain('Nenhum insumo encontrado');
  });

  it('redirects an expired session to the ADMIN login', () => {
    listar.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    render();

    expect(navigateByUrl).toHaveBeenCalledWith('/admin/login');
    expect(text()).not.toContain('Não foi possível carregar os insumos.');
  });

  function render(): void {
    fixture = TestBed.createComponent(AdminInsumosComponent);
    fixture.detectChanges();
  }

  function text(): string {
    return root().textContent ?? '';
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setField(id: string, value: string): void {
    const field = root().querySelector(`#${id}`) as HTMLInputElement;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function setSelect(id: string, value: string): void {
    const select = root().querySelector(`#${id}`) as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function clickButton(label: string): void {
    const button = Array.from(root().querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    button.click();
    fixture.detectChanges();
  }
});
