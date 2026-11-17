import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProdutosAcabadosService } from './produtos-acabados.service';
import { CsrfService } from '../security/csrf.service';

describe('ProdutosAcabadosService', () => {
  let service: ProdutosAcabadosService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ProdutosAcabadosService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    service = TestBed.inject(ProdutosAcabadosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('consulta posição e preserva datas civis', () => {
    let result: unknown;
    service.consultarPosicao('variant-1').subscribe((value) => (result = value));

    const request = http.expectOne(
      '/api/admin/estoque/produtos-acabados/variantes/variant-1/posicao',
    );
    request.flush({
      produtoVarianteId: 'variant-1',
      varianteAtiva: true,
      saldoFisico: 10,
      saldoUtilizavel: 8,
      saldoReservado: 0,
      saldoDisponivel: 8,
      estoqueMinimo: 3,
      estoqueMinimoConfigurado: true,
      situacao: 'Normal',
      lotes: [
        {
          loteId: 'lot-1',
          codigo: 'PA-20261117-ABC',
          dataFabricacao: '2026-11-17',
          dataValidade: '2026-12-17',
          origemValidade: 'PrazoPadrao',
          prazoValidadeAplicadoDias: 30,
          valido: true,
          temRegistroSaldo: true,
          versaoSaldo: 'AAAA',
          quantidadeVendavel: 8,
          quantidadeDanificada: 1,
          quantidadeQuarentena: 1,
          saldoFisico: 10,
          saldoUtilizavel: 8,
          saldoReservado: 0,
          saldoDisponivel: 8,
        },
      ],
    });

    expect(result).toBeDefined();
    expect((result as { lotes: Array<{ dataFabricacao: string }> }).lotes[0].dataFabricacao).toBe(
      '2026-11-17',
    );
  });

  it('lista histórico paginado e aplica filtro de lote', () => {
    service
      .listarMovimentacoes('variant-1', {
        loteId: 'lot-1',
        pagina: 2,
        itensPorPagina: 20,
      })
      .subscribe();

    const request = http.expectOne(
      (candidate) =>
        candidate.url ===
          '/api/admin/estoque/produtos-acabados/variantes/variant-1/movimentacoes' &&
        candidate.params.get('loteId') === 'lot-1' &&
        candidate.params.get('pagina') === '2',
    );
    request.flush({ itens: [], totalItens: 0, pagina: 2, itensPorPagina: 20 });
  });

  it('configura política com CSRF', () => {
    service
      .configurarPolitica('variant-1', {
        estoqueMinimo: 5,
        prazoValidadePadraoDias: 30,
      })
      .subscribe();

    const csrf = http.expectOne('/api/auth/csrf');
    csrf.flush({ requestToken: 'finished-goods-csrf' });

    const request = http.expectOne(
      '/api/admin/estoque/produtos-acabados/variantes/variant-1/politica',
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('finished-goods-csrf');
    expect(request.request.body).toEqual({
      estoqueMinimo: 5,
      prazoValidadePadraoDias: 30,
    });
    request.flush({
      produtoVarianteId: 'variant-1',
      estoqueMinimo: 5,
      prazoValidadePadraoDias: 30,
    });
  });

  it('envia entrada com antiforgery e chave de idempotência', () => {
    service
      .registrarEntrada(
        'variant-1',
        {
          tipo: 'EntradaManual',
          quantidade: 4,
          condicaoDestino: 'Vendavel',
          loteId: 'lot-1',
          motivo: 'Recebimento',
        },
        'entry-key',
      )
      .subscribe();

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'entry-csrf' });
    const request = http.expectOne(
      '/api/admin/estoque/produtos-acabados/variantes/variant-1/entradas',
    );
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('entry-csrf');
    expect(request.request.headers.get('Idempotency-Key')).toBe('entry-key');
    request.flush({ id: 'movement-1', tipo: 'EntradaManual' });
  });

  it('envia operação de perda e transferência com a chave lógica', () => {
    service
      .registrarSaida(
        'variant-1',
        {
          tipo: 'Perda',
          loteId: 'lot-1',
          quantidade: 2,
          condicaoOrigem: 'Danificada',
          motivo: 'Quebra',
        },
        'loss-key',
      )
      .subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'loss-csrf' });
    const loss = http.expectOne('/api/admin/estoque/produtos-acabados/variantes/variant-1/saidas');
    expect(loss.request.headers.get('Idempotency-Key')).toBe('loss-key');
    loss.flush({ id: 'loss-1', tipo: 'Perda' });

    service
      .transferir(
        'variant-1',
        {
          loteId: 'lot-1',
          quantidade: 1,
          condicaoOrigem: 'Vendavel',
          condicaoDestino: 'Quarentena',
          motivo: 'Bloqueio',
        },
        'transfer-key',
      )
      .subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'transfer-csrf' });
    const transfer = http.expectOne(
      '/api/admin/estoque/produtos-acabados/variantes/variant-1/transferencias',
    );
    expect(transfer.request.headers.get('Idempotency-Key')).toBe('transfer-key');
    transfer.flush({ id: 'transfer-1', tipo: 'TransferenciaCondicao' });
  });

  it('envia contagem com o snapshot original em Base64', () => {
    service
      .registrarContagem(
        'variant-1',
        {
          loteId: 'lot-1',
          quantidadeVendavel: 4,
          quantidadeDanificada: 1,
          quantidadeQuarentena: 1,
          motivo: 'Conferência',
          versaoSaldoEsperada: 'AQIDBAUG',
        },
        'count-key',
      )
      .subscribe();

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'count-csrf' });
    const request = http.expectOne(
      '/api/admin/estoque/produtos-acabados/variantes/variant-1/contagens',
    );
    expect(request.request.headers.get('Idempotency-Key')).toBe('count-key');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('count-csrf');
    expect(request.request.body).toMatchObject({
      loteId: 'lot-1',
      quantidadeVendavel: 4,
      quantidadeDanificada: 1,
      quantidadeQuarentena: 1,
      versaoSaldoEsperada: 'AQIDBAUG',
    });
    request.flush({ semDiferenca: false, movimentos: [], repetida: false });
  });

  it('envia estorno compensatório com CSRF e idempotência', () => {
    service
      .estornarMovimento(
        'variant-1',
        'movement-1',
        { motivo: 'Estorno administrativo' },
        'reverse-key',
      )
      .subscribe();

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reverse-csrf' });
    const request = http.expectOne(
      '/api/admin/estoque/produtos-acabados/variantes/variant-1/movimentacoes/movement-1/estorno',
    );
    expect(request.request.headers.get('Idempotency-Key')).toBe('reverse-key');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('reverse-csrf');
    request.flush({ id: 'reverse-movement', tipo: 'Estorno' });
  });
});
