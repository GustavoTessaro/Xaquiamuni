import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminMovimentoFiltro,
  AdminMovimentoPreciso,
  AdminPagina,
  AdminPosicaoInsumoPrecisa,
  InsumosService,
} from '../../../core/admin-estoque/insumos.service';
import { AdminConsultaInsumoComponent } from './admin-consulta-insumo.component';

const resumoBase: AdminPosicaoInsumoPrecisa['resumo'] = {
  insumoId: 'insumo-1',
  nome: 'Base glicerinada',
  tipo: 'MateriaPrima',
  unidadeBase: 'Grama',
  ativo: true,
  controlaLote: false,
  validadeObrigatoria: false,
  saldoFisico: '1234.567001',
  saldoUtilizavel: '1000',
  saldoReservado: '200',
  saldoDisponivel: '800',
  estoqueMinimo: null,
  estoqueMinimoConfigurado: false,
  situacao: 'Normal',
};

const posicaoAgregada: AdminPosicaoInsumoPrecisa = {
  resumo: resumoBase,
  posicoes: [
    {
      loteId: null,
      identificacaoLote: null,
      fornecedor: null,
      dataEntrada: null,
      dataValidade: null,
      bloqueado: null,
      elegivel: true,
      temRegistroSaldo: true,
      versaoSaldo: 'AQID',
      saldoFisico: '1234.567001',
      saldoUtilizavel: '1000',
      saldoReservado: '200',
      saldoDisponivel: '800',
    },
  ],
};

const paginaMovimentos: AdminPagina<AdminMovimentoPreciso> = {
  itens: [
    {
      id: 'movement-1',
      tipo: 'Aquisicao',
      direcao: 'Entrada',
      quantidadeOriginal: '2',
      unidadeOriginal: 'Quilograma',
      quantidadeBase: '2000.000001',
      unidadeBase: 'Grama',
      saldoAnterior: '0',
      saldoPosterior: '2000.000001',
      motivo: 'Aquisicao',
      observacao: 'Reposição de material',
      origem: 'FormaCompra',
      responsavelId: 'actor-1',
      registradoEmUtc: '2026-11-16T12:00:00Z',
      loteId: 'lote-valido',
      identificacaoLote: 'Lote A',
      formaCompraId: 'forma-1',
      formaCompraNome: 'Pacote de 1 kg',
      fatorCompra: '1000',
      unidadeFatorCompra: 'Grama',
      custoTotal: null,
      custoPorUnidadeBase: null,
      movimentoOriginalId: null,
    },
  ],
  totalItens: 45,
  pagina: 1,
  itensPorPagina: 20,
};

describe('AdminConsultaInsumoComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('shows an aggregate position without presenting it as a lot', async () => {
    const api = criarApi(posicaoAgregada);
    const harness = await renderConsulta(api);
    const text = harness.routeNativeElement?.textContent ?? '';

    expect(api.consultarPosicao).toHaveBeenCalledWith('insumo-1');
    expect(api.listarMovimentacoes).toHaveBeenCalledWith('insumo-1', {
      pagina: 1,
      itensPorPagina: 20,
    });
    expect(text).toContain('1.234,567001 g');
    expect(text).toContain('800 g');
    expect(text).toContain('posição agregada, sem');
    expect(text).not.toContain('Nenhum lote possui posição');
    expect(text).toContain('2 kg');
    expect(text).toContain('Lote A');
    expect(text).toContain('Reposição de material');
    expect(text).toContain('0 g → 2.000,000001 g');
    expect(text).toContain('45 movimentação(ões)');
    expect(api.consultarPosicao.mock.calls.length).toBe(1);
    expect(api.listarMovimentacoes.mock.calls.length).toBe(1);
  });

  it('shows multiple lots with physical and usable balances and civil dates', async () => {
    const api = criarApi({
      resumo: { ...resumoBase, controlaLote: true, saldoFisico: '14', saldoUtilizavel: '4' },
      posicoes: [
        criarLote({
          loteId: 'lote-vencido',
          identificacaoLote: 'Vencido',
          dataEntrada: '2026-01-02',
          dataValidade: '2026-03-04',
          saldoFisico: '10',
          saldoUtilizavel: '0',
          saldoDisponivel: '0',
          elegivel: false,
        }),
        criarLote({
          loteId: 'lote-bloqueado',
          identificacaoLote: 'Bloqueado',
          dataValidade: '2027-01-01',
          saldoFisico: '4',
          saldoUtilizavel: '0',
          saldoDisponivel: '0',
          bloqueado: true,
          elegivel: false,
        }),
        criarLote({
          loteId: 'lote-valido',
          identificacaoLote: 'Válido',
          dataValidade: '2027-12-31',
          saldoFisico: '4',
          saldoUtilizavel: '4',
          saldoDisponivel: '4',
        }),
      ],
    });
    const harness = await renderConsulta(api);
    const root = harness.routeNativeElement!;

    expect(root.textContent).toContain('Vencido');
    expect(root.textContent).toContain('Bloqueado');
    expect(root.textContent).toContain('Válido / elegível');
    expect(root.textContent).toContain('04/03/2026');
    expect(root.textContent).toContain('10 g');
    expect(root.textContent).toContain('0 g');
    expect(root.querySelectorAll('section[aria-labelledby="posicao-titulo"] li').length).toBe(3);

    const lotFilter = root.querySelector<HTMLSelectElement>('select[aria-label="Lote"]');
    expect(lotFilter).not.toBeNull();
    lotFilter!.value = 'lote-vencido';
    lotFilter!.dispatchEvent(new Event('change', { bubbles: true }));
    harness.fixture.detectChanges();
    expect(api.listarMovimentacoes.mock.calls.at(-1)?.[1]).toEqual({
      pagina: 1,
      itensPorPagina: 20,
      loteId: 'lote-vencido',
    });
  });

  it('filters and paginates history on the server without issuing writes', async () => {
    const api = criarApi(posicaoAgregada);
    const harness = await renderConsulta(api);
    const selects = harness.routeNativeElement!.querySelectorAll('select');

    selects[0]!.value = 'Aquisicao';
    selects[0]!.dispatchEvent(new Event('change', { bubbles: true }));
    harness.fixture.detectChanges();
    expect(api.listarMovimentacoes.mock.calls.at(-1)?.[1]).toEqual({
      pagina: 1,
      itensPorPagina: 20,
      tipo: 'Aquisicao',
    });

    const next = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Próxima'),
    );
    next?.click();
    harness.fixture.detectChanges();
    expect(api.listarMovimentacoes.mock.calls.at(-1)?.[1]).toEqual({
      pagina: 2,
      itensPorPagina: 20,
      tipo: 'Aquisicao',
    });
    expect(api.consultarPosicao.mock.calls.length).toBe(1);
    expect(Object.keys(api)).toEqual(['consultarPosicao', 'listarMovimentacoes']);
  });

  it('shows empty and error states and allows retrying the history query', async () => {
    const api = criarApi(posicaoAgregada);
    api.listarMovimentacoes
      .mockReturnValueOnce(
        throwError(() => new Error('network unavailable')) as ReturnType<
          typeof api.listarMovimentacoes
        >,
      )
      .mockReturnValueOnce(
        of({
          itens: [],
          totalItens: 0,
          pagina: 1,
          itensPorPagina: 20,
        }),
      );
    const harness = await renderConsulta(api);
    expect(harness.routeNativeElement?.textContent).toContain(
      'Não foi possível carregar esta consulta',
    );

    const retry = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('Tentar novamente'),
    );
    retry?.click();
    harness.fixture.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Nenhuma movimentação encontrada');
  });

  it('shows a loading state while the paged history request is pending', async () => {
    const api = criarApi(posicaoAgregada);
    const pending = new Subject<AdminPagina<AdminMovimentoPreciso>>();
    api.listarMovimentacoes.mockReturnValue(pending);
    const harness = await renderConsulta(api);
    expect(harness.routeNativeElement?.textContent).toContain('Carregando movimentações');

    pending.next(paginaMovimentos);
    pending.complete();
    harness.fixture.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Histórico de movimentações');
    expect(harness.routeNativeElement?.textContent).toContain('Reposição de material');
  });

  it('explains forbidden access and missing resources from the API', async () => {
    const api = criarApi(posicaoAgregada);
    api.consultarPosicao.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 403 })),
    );
    api.listarMovimentacoes.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    const harness = await renderConsulta(api);
    const text = harness.routeNativeElement?.textContent ?? '';

    expect(text).toContain('Você não tem permissão para consultar o estoque');
    expect(text).toContain('Este insumo não foi encontrado.');
  });

  it('does not invent lots when a lot-controlled item has no positions', async () => {
    const api = criarApi({
      resumo: { ...resumoBase, controlaLote: true },
      posicoes: [],
    });
    const harness = await renderConsulta(api);
    const section = harness.routeNativeElement?.querySelector(
      'section[aria-labelledby="posicao-titulo"]',
    );

    expect(section?.textContent).toContain(
      'Nenhum lote possui posição registrada para este insumo.',
    );
    expect(section?.querySelectorAll('li')).toHaveLength(0);
  });
});

function criarLote(
  overrides: Partial<AdminPosicaoInsumoPrecisa['posicoes'][number]>,
): AdminPosicaoInsumoPrecisa['posicoes'][number] {
  return {
    loteId: 'lote-1',
    identificacaoLote: 'Lote',
    fornecedor: null,
    dataEntrada: '2026-01-01',
    dataValidade: '2027-01-01',
    bloqueado: false,
    elegivel: true,
    temRegistroSaldo: true,
    versaoSaldo: 'AQID',
    saldoFisico: '4',
    saldoUtilizavel: '4',
    saldoReservado: '0',
    saldoDisponivel: '4',
    ...overrides,
  };
}

function criarApi(position: AdminPosicaoInsumoPrecisa) {
  return {
    consultarPosicao: vi.fn(() => of(position)),
    listarMovimentacoes: vi.fn((_id: string, _filter: AdminMovimentoFiltro) =>
      of(paginaMovimentos),
    ),
  };
}

async function renderConsulta(api: ReturnType<typeof criarApi>): Promise<RouterTestingHarness> {
  await TestBed.configureTestingModule({
    imports: [AdminConsultaInsumoComponent],
    providers: [
      provideRouter([
        {
          path: 'admin/estoque/insumos/:id/consultas',
          component: AdminConsultaInsumoComponent,
        },
      ]),
      {
        provide: AdminAuthService,
        useValue: {
          getCurrentSession: vi.fn(() => of({ userId: 'actor-1' })),
          logout: vi.fn(() => of(undefined)),
        },
      },
      { provide: InsumosService, useValue: api },
    ],
  }).compileComponents();
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(
    '/admin/estoque/insumos/insumo-1/consultas',
    AdminConsultaInsumoComponent,
  );
  await harness.fixture.whenStable();
  harness.fixture.detectChanges();
  return harness;
}
