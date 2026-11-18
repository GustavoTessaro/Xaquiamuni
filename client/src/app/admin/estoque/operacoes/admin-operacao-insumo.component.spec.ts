import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminInsumoDetalhe,
  AdminMovimentoPreciso,
  AdminPosicaoInsumoPrecisa,
  InsumosService,
  RegistrarContagemInsumoRequest,
  RegistrarSaidaInsumoRequest,
  ResultadoContagemInsumo,
} from '../../../core/admin-estoque/insumos.service';
import { routes } from '../../../app.routes';
import { adminAuthGuard } from '../../admin-auth.guard';
import { AdminOperacaoInsumoComponent } from './admin-operacao-insumo.component';

const resumoBase: AdminInsumoDetalhe['resumo'] = {
  insumoId: 'insumo-1',
  nome: 'Base glicerinada',
  tipo: 'MateriaPrima',
  unidadeBase: 'Grama',
  ativo: true,
  controlaLote: false,
  validadeObrigatoria: false,
  saldoFisico: 5,
  saldoUtilizavel: 5,
  saldoReservado: 0,
  saldoDisponivel: 4,
  estoqueMinimo: null,
  estoqueMinimoConfigurado: false,
  situacao: 'Normal',
};

const detalheBase: AdminInsumoDetalhe = {
  resumo: resumoBase,
  formasCompra: [],
  posicoes: [],
};

const posicaoAgregada: AdminPosicaoInsumoPrecisa = {
  resumo: {
    ...resumoBase,
    saldoFisico: '5',
    saldoUtilizavel: '5',
    saldoReservado: '1',
    saldoDisponivel: '4',
    estoqueMinimo: null,
  },
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
      versaoSaldo: 'AQIDBAUG',
      saldoFisico: '5',
      saldoUtilizavel: '5',
      saldoReservado: '1',
      saldoDisponivel: '4',
    },
  ],
};

const movimento: AdminMovimentoPreciso = {
  id: 'movement-2',
  tipo: 'SaidaManual',
  direcao: 'Saida',
  quantidadeOriginal: '2',
  unidadeOriginal: 'Grama',
  quantidadeBase: '2',
  unidadeBase: 'Grama',
  saldoAnterior: '5',
  saldoPosterior: '3',
  motivo: 'Uso manual',
  observacao: null,
  origem: 'OperacaoManual',
  responsavelId: 'actor-1',
  registradoEmUtc: '2026-11-16T12:00:00Z',
  loteId: null,
  identificacaoLote: null,
  formaCompraId: null,
  formaCompraNome: null,
  fatorCompra: null,
  unidadeFatorCompra: null,
  custoTotal: null,
  custoPorUnidadeBase: null,
  movimentoOriginalId: null,
};

describe('AdminOperacaoInsumoComponent', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('limits a manual output by available balance and confirms the exact request', async () => {
    const api = criarApi();
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=SaidaManual',
    );
    const root = harness.routeNativeElement!;
    expect(root.textContent).toContain('Disponível');
    expect(root.textContent).toContain('4 g');

    setInput(harness, '[formControlName="quantidade"]', '4,000001');
    setInput(harness, '[formControlName="motivo"]', 'Consumo manual');
    clickText(harness, 'Revisar operação');
    expect(root.textContent).toContain('excede o saldo disponível');
    expect(api.registrarSaida).not.toHaveBeenCalled();

    setInput(harness, '[formControlName="quantidade"]', '0');
    clickText(harness, 'Revisar operação');
    expect(root.textContent).toContain('Informe uma quantidade positiva válida');
    expect(api.registrarSaida).not.toHaveBeenCalled();

    setInput(harness, '[formControlName="quantidade"]', '-1');
    clickText(harness, 'Revisar operação');
    expect(root.textContent).toContain('Informe uma quantidade positiva válida');
    expect(api.registrarSaida).not.toHaveBeenCalled();

    setInput(harness, '[formControlName="quantidade"]', '2');
    clickText(harness, 'Revisar operação');
    expect(root.textContent).toContain('Confirme os dados');
    expect(api.registrarSaida).not.toHaveBeenCalled();
    clickText(harness, 'Confirmar operação');
    expect(api.registrarSaida).toHaveBeenCalledWith(
      'insumo-1',
      {
        tipo: 'SaidaManual',
        quantidadeOriginal: '2',
        unidadeOriginal: 'Grama',
        loteId: null,
        motivo: 'Consumo manual',
        observacao: null,
      } satisfies RegistrarSaidaInsumoRequest,
      expect.any(String),
    );
    expect(root.textContent).toContain('Operação confirmada');
    expect(root.textContent).toContain('movement-2');
    expect(api.consultarPosicao).toHaveBeenCalledTimes(2);
    expect(api.listarMovimentacoes).toHaveBeenCalledTimes(1);
  });

  it('allows a loss on a blocked lot but never treats it as usable stock', async () => {
    const detail: AdminInsumoDetalhe = {
      ...detalheBase,
      resumo: { ...resumoBase, controlaLote: true },
    };
    const stock: AdminPosicaoInsumoPrecisa = {
      ...posicaoAgregada,
      resumo: { ...posicaoAgregada.resumo, controlaLote: true },
      posicoes: [
        {
          ...posicaoAgregada.posicoes[0]!,
          loteId: 'lote-bloqueado',
          identificacaoLote: 'Lote bloqueado',
          bloqueado: true,
          elegivel: false,
          saldoFisico: '5',
          saldoUtilizavel: '0',
          saldoDisponivel: '0',
        },
      ],
    };
    const api = criarApi(detail, stock);
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=Perda&loteId=lote-bloqueado',
    );
    setInput(harness, '[formControlName="quantidade"]', '2,5');
    setInput(harness, '[formControlName="motivo"]', 'Lote avariado');
    expect(harness.routeNativeElement?.textContent).toContain('Bloqueado');
    expect(harness.routeNativeElement?.textContent).toContain('5 g');
    clickText(harness, 'Revisar operação');
    clickText(harness, 'Confirmar operação');

    const request = api.registrarSaida.mock.calls[0]?.[1];
    expect(request).toMatchObject({
      tipo: 'Perda',
      quantidadeOriginal: '2.5',
      loteId: 'lote-bloqueado',
    });
  });

  it('counts physical quantity with the captured version and never resends after a concurrency conflict', async () => {
    const detail: AdminInsumoDetalhe = {
      ...detalheBase,
      resumo: { ...resumoBase, controlaLote: true },
    };
    const stock: AdminPosicaoInsumoPrecisa = {
      ...posicaoAgregada,
      resumo: { ...posicaoAgregada.resumo, controlaLote: true },
      posicoes: [
        {
          ...posicaoAgregada.posicoes[0]!,
          loteId: 'lote-1',
          identificacaoLote: 'Lote A',
          saldoFisico: '4',
          saldoUtilizavel: '4',
          saldoDisponivel: '4',
        },
      ],
    };
    const api = criarApi(detail, stock);
    api.registrarContagem.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              detail:
                'A posição mudou desde a leitura da contagem; atualize o saldo e refaça a contagem.',
            },
          }),
      ),
    );
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=Contagem&loteId=lote-1',
    );
    setInput(harness, '[formControlName="quantidade"]', '4,5');
    setInput(harness, '[formControlName="motivo"]', 'Contagem presencial');
    expect(harness.routeNativeElement?.textContent).toContain('Diferença calculada: 0,5 g');
    clickText(harness, 'Revisar operação');
    clickText(harness, 'Confirmar operação');

    expect(api.registrarContagem).toHaveBeenCalledTimes(1);
    expect(api.registrarContagem.mock.calls[0]?.[1]).toMatchObject({
      quantidadeEncontrada: '4.5',
      unidadeOriginal: 'Grama',
      loteId: 'lote-1',
      versaoSaldoEsperada: 'AQIDBAUG',
    } satisfies Partial<RegistrarContagemInsumoRequest>);
    expect(harness.routeNativeElement?.textContent).toContain('O saldo mudou desde a conferência');

    clickText(harness, 'Atualizar posição e iniciar nova conferência');
    expect(api.consultarPosicao).toHaveBeenCalledTimes(2);
    expect(api.registrarContagem).toHaveBeenCalledTimes(1);
    expect(
      harness.routeNativeElement?.querySelector<HTMLInputElement>('[formControlName="quantidade"]')
        ?.value,
    ).toBe('');
  });

  it('accepts a zero physical count and reports no movement when the server confirms no difference', async () => {
    const detail: AdminInsumoDetalhe = {
      ...detalheBase,
      resumo: { ...resumoBase, unidadeBase: 'Unidade' },
    };
    const stock: AdminPosicaoInsumoPrecisa = {
      ...posicaoAgregada,
      resumo: {
        ...posicaoAgregada.resumo,
        unidadeBase: 'Unidade',
        saldoFisico: '0',
        saldoUtilizavel: '0',
        saldoReservado: '0',
        saldoDisponivel: '0',
      },
      posicoes: [
        {
          ...posicaoAgregada.posicoes[0]!,
          saldoFisico: '0',
          saldoUtilizavel: '0',
          saldoReservado: '0',
          saldoDisponivel: '0',
        },
      ],
    };
    const api = criarApi(detail, stock);
    api.registrarContagem.mockReturnValueOnce(of({ resultado: 'SemDiferenca' }));
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=Contagem',
    );
    setInput(harness, '[formControlName="quantidade"]', '0');
    setInput(harness, '[formControlName="motivo"]', 'Contagem sem saldo');
    clickText(harness, 'Revisar operação');
    expect(harness.routeNativeElement?.textContent).toContain('Diferença calculada: 0 un.');
    clickText(harness, 'Confirmar operação');

    expect(api.registrarContagem.mock.calls[0]?.[1]).toMatchObject({
      quantidadeEncontrada: '0',
      loteId: null,
      versaoSaldoEsperada: 'AQIDBAUG',
    });
    expect(harness.routeNativeElement?.textContent).toContain('Contagem confirmada sem diferença');
    expect(api.listarMovimentacoes).toHaveBeenCalledTimes(1);
  });

  it('retries an uncertain output with the exact same payload and idempotency key', async () => {
    const api = criarApi();
    api.registrarSaida
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(of(movimento));
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=SaidaManual',
    );
    setInput(harness, '[formControlName="quantidade"]', '1');
    setInput(harness, '[formControlName="motivo"]', 'Uso manual');
    clickText(harness, 'Revisar operação');
    clickText(harness, 'Confirmar operação');

    expect(harness.routeNativeElement?.textContent).toContain(
      'Não foi possível confirmar o resultado',
    );
    expect(sessionStorage.getItem('xaquiamuni:estoque:operacao:pendente')).not.toBeNull();
    clickText(harness, 'Reenviar a mesma operação');
    expect(api.registrarSaida).toHaveBeenCalledTimes(2);
    expect(api.registrarSaida.mock.calls[1]?.[1]).toEqual(api.registrarSaida.mock.calls[0]?.[1]);
    expect(api.registrarSaida.mock.calls[1]?.[2]).toBe(api.registrarSaida.mock.calls[0]?.[2]);
    expect(sessionStorage.getItem('xaquiamuni:estoque:operacao:pendente')).toBeNull();
    expect(harness.routeNativeElement?.textContent).toContain('movement-2');
  });

  it('blocks duplicate submissions while the first write is still in flight', async () => {
    const api = criarApi();
    const response = new Subject<AdminMovimentoPreciso>();
    api.registrarSaida.mockReturnValue(response);
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/operacoes?insumoId=insumo-1&acao=SaidaManual',
    );
    setInput(harness, '[formControlName="quantidade"]', '1');
    setInput(harness, '[formControlName="motivo"]', 'Uso manual');
    clickText(harness, 'Revisar operação');
    clickText(harness, 'Confirmar operação');
    expect(api.registrarSaida).toHaveBeenCalledTimes(1);
    expect(harness.routeNativeElement?.textContent).toContain('Enviando operação');
    response.next(movimento);
    response.complete();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Operação confirmada');
  });

  it('loads the original movement and treats server rejection of a reversal as authoritative', async () => {
    const api = criarApi();
    api.consultarMovimentacao.mockReturnValueOnce(
      of({ ...movimento, id: 'movement-1', tipo: 'AjusteInventario' }),
    );
    api.estornarMovimento.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'O movimento original já possui estorno.' },
          }),
      ),
    );
    const harness = await renderOperation(
      api,
      '/admin/estoque/insumos/insumo-1/movimentacoes/movement-1/estornar',
    );
    expect(api.consultarMovimentacao).toHaveBeenCalledWith('insumo-1', 'movement-1');
    expect(harness.routeNativeElement?.textContent).toContain('Movimento original');
    expect(harness.routeNativeElement?.textContent).toContain('Ajuste de inventário');
    expect(harness.routeNativeElement?.textContent).not.toContain('AjusteInventario');
    setInput(harness, '[formControlName="motivo"]', 'Correção');
    clickText(harness, 'Revisar estorno');
    clickText(harness, 'Confirmar operação');
    expect(api.estornarMovimento).toHaveBeenCalledWith(
      'insumo-1',
      'movement-1',
      { motivo: 'Correção', observacao: null },
      expect.any(String),
    );
    expect(harness.routeNativeElement?.textContent).toContain('já foi estornado');
    expect(api.listarMovimentacoes).not.toHaveBeenCalled();
  });

  it('shows human-readable insumo types in the operation selector', async () => {
    const harness = await renderOperation(criarApi(), '/admin/estoque/insumos/operacoes');
    const text = harness.routeNativeElement?.textContent ?? '';

    expect(text).toContain('Matéria-prima');
    expect(text).not.toContain('MateriaPrima');
  });

  it('declares both operation routes as ADMIN-protected and keeps the static operations path before :id', () => {
    const operationsIndex = routes.findIndex(
      (route) => route.path === 'admin/estoque/insumos/operacoes',
    );
    const dynamicIndex = routes.findIndex((route) => route.path === 'admin/estoque/insumos/:id');
    const reversal = routes.find(
      (route) => route.path === 'admin/estoque/insumos/:id/movimentacoes/:movimentoId/estornar',
    );
    expect(operationsIndex).toBeGreaterThanOrEqual(0);
    expect(operationsIndex).toBeLessThan(dynamicIndex);
    expect(routes[operationsIndex]?.canActivate).toContain(adminAuthGuard);
    expect(reversal?.canActivate).toContain(adminAuthGuard);
  });
});

function criarApi(
  detail: AdminInsumoDetalhe = detalheBase,
  stock: AdminPosicaoInsumoPrecisa = posicaoAgregada,
) {
  return {
    listar: vi.fn(() =>
      of({ itens: [detail.resumo], totalItens: 1, pagina: 1, itensPorPagina: 20 }),
    ),
    obter: vi.fn(() => of(detail)),
    consultarPosicao: vi.fn(() => of(stock)),
    listarMovimentacoes: vi.fn(() =>
      of({ itens: [movimento], totalItens: 1, pagina: 1, itensPorPagina: 20 }),
    ),
    consultarMovimentacao: vi.fn(() => of({ ...movimento, id: 'movement-1' })),
    registrarSaida: vi.fn((_id: string, _request: RegistrarSaidaInsumoRequest, _key: string) =>
      of(movimento),
    ),
    registrarContagem: vi.fn(
      (
        _id: string,
        _request: RegistrarContagemInsumoRequest,
        _key: string,
      ): Observable<AdminMovimentoPreciso | ResultadoContagemInsumo> => of(movimento),
    ),
    estornarMovimento: vi.fn(
      (
        _id: string,
        _movementId: string,
        _request: { motivo: string; observacao: string | null },
        _key: string,
      ) => of({ ...movimento, tipo: 'Estorno' as const }),
    ),
  };
}

async function renderOperation(
  api: ReturnType<typeof criarApi>,
  url: string,
): Promise<RouterTestingHarness> {
  await TestBed.configureTestingModule({
    imports: [AdminOperacaoInsumoComponent],
    providers: [
      provideRouter([
        { path: 'admin/estoque/insumos/operacoes', component: AdminOperacaoInsumoComponent },
        {
          path: 'admin/estoque/insumos/:id/movimentacoes/:movimentoId/estornar',
          component: AdminOperacaoInsumoComponent,
        },
      ]),
      {
        provide: AdminAuthService,
        useValue: { getCurrentSession: vi.fn(() => of({ userId: 'actor-1' })) },
      },
      { provide: InsumosService, useValue: api },
    ],
  }).compileComponents();
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url, AdminOperacaoInsumoComponent);
  await harness.fixture.whenStable();
  harness.fixture.detectChanges();
  return harness;
}

function setInput(harness: RouterTestingHarness, selector: string, value: string): void {
  const input = harness.routeNativeElement?.querySelector<HTMLInputElement>(selector);
  if (!input) {
    throw new Error(`Input not found: ${selector}`);
  }
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
  harness.fixture.detectChanges();
}

function clickText(harness: RouterTestingHarness, text: string): void {
  const button = Array.from(harness.routeNativeElement?.querySelectorAll('button') ?? []).find(
    (candidate) => candidate.textContent?.includes(text),
  );
  if (!button) {
    throw new Error(`Button not found: ${text}`);
  }
  button.click();
  harness.fixture.detectChanges();
}
