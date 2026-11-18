import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  AdminInsumoDetalhe,
  AdminInsumoResumo,
  AdminMovimentoFiltro,
  AdminMovimentoPreciso,
  AdminPagina,
  AdminPosicaoInsumoPrecisa,
  AdminResumoInsumoPreciso,
  CadastrarInsumoRequest,
  FormaCompraRequest,
  InsumosService,
  RegistrarContagemInsumoRequest,
  RegistrarEntradaInsumoRequest,
  RegistrarSaidaInsumoRequest,
} from './insumos.service';

describe('InsumosService', () => {
  let service: InsumosService;
  let http: HttpTestingController;

  const resumo: AdminInsumoResumo = {
    insumoId: 'insumo-1',
    nome: 'Base glicerinada',
    tipo: 'MateriaPrima',
    unidadeBase: 'Grama',
    ativo: true,
    controlaLote: false,
    validadeObrigatoria: false,
    saldoFisico: 0,
    saldoUtilizavel: 0,
    saldoReservado: 0,
    saldoDisponivel: 0,
    estoqueMinimo: null,
    estoqueMinimoConfigurado: false,
    situacao: 'MinimoNaoConfigurado',
  };
  const detalhe: AdminInsumoDetalhe = {
    resumo,
    formasCompra: [],
    posicoes: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        InsumosService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: { cookie: 'XSRF-TOKEN=cookie-token' } },
      ],
    });
    service = TestBed.inject(InsumosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('sends listing filters and pagination to the server and reads the detail contract', () => {
    const filter = {
      nome: ' base ',
      ativo: false,
      situacao: 'SemEstoque' as const,
      pagina: 2,
      itensPorPagina: 20,
    };
    service.listar(filter).subscribe();
    service.obter('id/com-barra').subscribe();

    const listing = http.expectOne((request) => request.url === '/api/admin/insumos');
    expect(listing.request.method).toBe('GET');
    expect(listing.request.params.keys()).toEqual([
      'nome',
      'ativo',
      'situacao',
      'pagina',
      'itensPorPagina',
    ]);
    expect(listing.request.params.get('nome')).toBe('base');
    expect(listing.request.params.get('ativo')).toBe('false');
    expect(listing.request.params.get('situacao')).toBe('SemEstoque');
    expect(listing.request.params.get('pagina')).toBe('2');
    listing.flush(
      `{"itens":[${JSON.stringify(resumo)}],"totalItens":1,"pagina":2,"itensPorPagina":20}`,
    );

    const get = http.expectOne('/api/admin/insumos/id%2Fcom-barra');
    expect(get.request.method).toBe('GET');
    get.flush(detalhe);
  });

  it('preserves exact balances and minimums from the paged listing response', () => {
    const result: { page: AdminPagina<AdminResumoInsumoPreciso> | null } = { page: null };
    service.listar().subscribe((page) => (result.page = page));

    const request = http.expectOne('/api/admin/insumos');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('text');
    request.flush(
      '{"itens":[{"insumoId":"insumo-1","nome":"Base","tipo":"MateriaPrima","unidadeBase":"Grama","ativo":true,"controlaLote":false,"validadeObrigatoria":false,"saldoFisico":999999999999.999999,"saldoUtilizavel":0.000001,"saldoReservado":0,"saldoDisponivel":0.000001,"estoqueMinimo":0.000001,"estoqueMinimoConfigurado":true,"situacao":"Normal"}],"totalItens":1,"pagina":1,"itensPorPagina":20}',
    );

    expect(result.page?.itens[0]?.saldoFisico).toBe('999999999999.999999');
    expect(result.page?.itens[0]?.saldoUtilizavel).toBe('0.000001');
    expect(result.page?.itens[0]?.estoqueMinimo).toBe('0.000001');
  });

  it('reads aggregate and lot positions as GET while preserving exact decimal text', () => {
    const result: { response: AdminPosicaoInsumoPrecisa | null } = { response: null };
    service.consultarPosicao('insumo-1').subscribe((position) => (result.response = position));

    const request = http.expectOne('/api/admin/insumos/insumo-1/posicao');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('text');
    request.flush(
      '{"resumo":{"insumoId":"insumo-1","nome":"Base glicerinada","tipo":"MateriaPrima","unidadeBase":"Grama","ativo":true,"controlaLote":true,"validadeObrigatoria":true,"saldoFisico":123456789012.123456,"saldoUtilizavel":1000.000001,"saldoReservado":0,"saldoDisponivel":1000.000001,"estoqueMinimo":0.000001,"estoqueMinimoConfigurado":true,"situacao":"Normal"},"posicoes":[{"loteId":"lote-1","identificacaoLote":"Lote A","fornecedor":null,"dataEntrada":"2026-01-02","dataValidade":"2026-12-31","bloqueado":false,"elegivel":true,"temRegistroSaldo":true,"versaoSaldo":"AQID","saldoFisico":123456789012.123456,"saldoUtilizavel":1000.000001,"saldoReservado":0,"saldoDisponivel":1000.000001}]}',
    );

    expect(result.response?.resumo.saldoFisico).toBe('123456789012.123456');
    expect(result.response?.resumo.estoqueMinimo).toBe('0.000001');
    expect(result.response?.posicoes[0]?.saldoFisico).toBe('123456789012.123456');
    expect(result.response?.posicoes[0]?.dataValidade).toBe('2026-12-31');
  });

  it('sends only supported movement filters and preserves precise values from the paged GET', () => {
    const filter: AdminMovimentoFiltro = {
      tipo: 'Estorno',
      loteId: 'lote-1',
      pagina: 3,
      itensPorPagina: 20,
    };
    const result: { response: AdminPagina<AdminMovimentoPreciso> | null } = { response: null };
    service.listarMovimentacoes('insumo-1', filter).subscribe((page) => (result.response = page));

    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/admin/insumos/insumo-1/movimentacoes' &&
        candidate.params.get('tipo') === 'Estorno' &&
        candidate.params.get('loteId') === 'lote-1' &&
        candidate.params.get('pagina') === '3' &&
        candidate.params.get('itensPorPagina') === '20',
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('text');
    request.flush(
      '{"itens":[{"id":"movement-2","tipo":"Estorno","direcao":"Entrada","quantidadeOriginal":1,"unidadeOriginal":"Quilograma","quantidadeBase":123456789012.123456,"unidadeBase":"Grama","saldoAnterior":0.000001,"saldoPosterior":123456789012.123457,"motivo":"Estorno","observacao":"Ajuste","origem":"MovimentoOriginal","responsavelId":"actor-1","registradoEmUtc":"2026-11-16T12:00:00Z","loteId":"lote-1","identificacaoLote":"Lote A","formaCompraId":null,"formaCompraNome":null,"fatorCompra":null,"unidadeFatorCompra":null,"custoTotal":null,"custoPorUnidadeBase":null,"movimentoOriginalId":"movement-1"}],"totalItens":1,"pagina":3,"itensPorPagina":20}',
    );

    expect(result.response?.itens[0]?.quantidadeBase).toBe('123456789012.123456');
    expect(result.response?.itens[0]?.saldoAnterior).toBe('0.000001');
    expect(result.response?.itens[0]?.movimentoOriginalId).toBe('movement-1');
  });

  it('uses the shared antiforgery token for insumo mutations without sending a responsible id', () => {
    const request: CadastrarInsumoRequest = {
      nome: resumo.nome,
      tipo: resumo.tipo,
      unidadeBase: resumo.unidadeBase,
      estoqueMinimo: null,
      controlaLote: false,
      validadeObrigatoria: false,
    };
    service.criar(request).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'create-token' });
    const create = http.expectOne('/api/admin/insumos');
    expect(create.request.method).toBe('POST');
    expect(create.request.headers.get('X-XSRF-TOKEN')).toBe('create-token');
    expect(create.request.body).toEqual(request);
    expect(create.request.body).not.toHaveProperty('responsavelId');
    create.flush(detalhe, { status: 201, statusText: 'Created' });

    service
      .editar('insumo-1', {
        nome: 'Base atualizada',
        tipo: 'MateriaPrima',
        estoqueMinimo: 50,
        controlaLote: false,
        validadeObrigatoria: false,
      })
      .subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'edit-token' });
    const edit = http.expectOne('/api/admin/insumos/insumo-1');
    expect(edit.request.method).toBe('PUT');
    expect(edit.request.headers.get('X-XSRF-TOKEN')).toBe('edit-token');
    edit.flush(detalhe);

    service.alterarStatus('insumo-1', { ativo: false }).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'status-token' });
    const status = http.expectOne('/api/admin/insumos/insumo-1/status');
    expect(status.request.method).toBe('PATCH');
    expect(status.request.headers.get('X-XSRF-TOKEN')).toBe('status-token');
    status.flush(detalhe);
  });

  it('posts precise stock operations with antiforgery and idempotency without a client responsible id', () => {
    const output: RegistrarSaidaInsumoRequest = {
      tipo: 'Perda',
      quantidadeOriginal: '123456789012.123456',
      unidadeOriginal: 'Grama',
      loteId: 'lote-1',
      motivo: 'Avaria',
      observacao: null,
    };
    service.registrarSaida('insumo-1', output, 'key-output').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'output-token' });
    const outputRequest = http.expectOne('/api/admin/insumos/insumo-1/saidas');
    expect(outputRequest.request.method).toBe('POST');
    expect(outputRequest.request.responseType).toBe('text');
    expect(outputRequest.request.headers.get('Idempotency-Key')).toBe('key-output');
    expect(outputRequest.request.headers.get('X-XSRF-TOKEN')).toBe('output-token');
    expect(outputRequest.request.body).toContain('"quantidadeOriginal":123456789012.123456');
    expect(outputRequest.request.body).not.toContain('responsavelId');
    outputRequest.flush(
      '{"id":"movement-2","tipo":"Perda","direcao":"Saida","quantidadeOriginal":123456789012.123456,"unidadeOriginal":"Grama","quantidadeBase":123456789012.123456,"unidadeBase":"Grama","saldoAnterior":123456789012.123456,"saldoPosterior":0,"motivo":"Avaria","observacao":null,"origem":"OperacaoManual","responsavelId":"actor-1","registradoEmUtc":"2026-11-16T12:00:00Z","loteId":"lote-1","identificacaoLote":"Lote A","formaCompraId":null,"formaCompraNome":null,"fatorCompra":null,"unidadeFatorCompra":null,"custoTotal":null,"custoPorUnidadeBase":null,"movimentoOriginalId":null}',
    );

    const count: RegistrarContagemInsumoRequest = {
      quantidadeEncontrada: '0',
      unidadeOriginal: 'Unidade',
      loteId: null,
      versaoSaldoEsperada: 'AQIDBAUG',
      motivo: 'Conferência física',
      observacao: null,
    };
    service.registrarContagem('insumo-1', count, 'key-count').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'count-token' });
    const countRequest = http.expectOne('/api/admin/insumos/insumo-1/contagens');
    expect(countRequest.request.method).toBe('POST');
    expect(countRequest.request.headers.get('Idempotency-Key')).toBe('key-count');
    expect(countRequest.request.body).toContain('"quantidadeEncontrada":0');
    expect(countRequest.request.body).toContain('"versaoSaldoEsperada":"AQIDBAUG"');
    countRequest.flush('{}');

    service
      .estornarMovimento(
        'insumo-1',
        'movement-1',
        { motivo: 'Correção', observacao: null },
        'key-reversal',
      )
      .subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'reversal-token' });
    const reversalRequest = http.expectOne(
      '/api/admin/insumos/insumo-1/movimentacoes/movement-1/estorno',
    );
    expect(reversalRequest.request.method).toBe('POST');
    expect(reversalRequest.request.headers.get('Idempotency-Key')).toBe('key-reversal');
    expect(reversalRequest.request.headers.get('X-XSRF-TOKEN')).toBe('reversal-token');
    expect(reversalRequest.request.body).toBe('{"motivo":"Correção","observacao":null}');
    reversalRequest.flush(
      '{"id":"movement-3","tipo":"Estorno","direcao":"Saida","quantidadeOriginal":1,"unidadeOriginal":"Unidade","quantidadeBase":1,"unidadeBase":"Unidade","saldoAnterior":2,"saldoPosterior":1,"motivo":"Correção","observacao":null,"origem":"MovimentoOriginal","responsavelId":"actor-1","registradoEmUtc":"2026-11-16T12:00:00Z","loteId":null,"identificacaoLote":null,"formaCompraId":null,"formaCompraNome":null,"fatorCompra":null,"unidadeFatorCompra":null,"custoTotal":null,"custoPorUnidadeBase":null,"movimentoOriginalId":"movement-1"}',
    );
  });

  it('fetches an original movement through the supported read-only route', () => {
    service.consultarMovimentacao('insumo-1', 'movement-1').subscribe();
    const request = http.expectOne('/api/admin/insumos/insumo-1/movimentacoes/movement-1');
    expect(request.request.method).toBe('GET');
    request.flush(
      '{"id":"movement-1","tipo":"Aquisicao","direcao":"Entrada","quantidadeOriginal":1,"unidadeOriginal":"Unidade","quantidadeBase":1,"unidadeBase":"Unidade","saldoAnterior":0,"saldoPosterior":1,"motivo":"Aquisicao","observacao":null,"origem":"UnidadeFisica","responsavelId":"actor-1","registradoEmUtc":"2026-11-16T12:00:00Z","loteId":null,"identificacaoLote":null,"formaCompraId":null,"formaCompraNome":null,"fatorCompra":null,"unidadeFatorCompra":null,"custoTotal":null,"custoPorUnidadeBase":null,"movimentoOriginalId":null}',
    );
  });

  it('uses the real purchase-form routes and antiforgery for writes', () => {
    service.listarFormasCompra('insumo-1').subscribe();
    const list = http.expectOne('/api/admin/insumos/insumo-1/formas-compra');
    expect(list.request.method).toBe('GET');
    list.flush([]);

    const request: FormaCompraRequest = {
      nome: 'Pacote de 500 g',
      quantidadeEquivalentePorEmbalagem: 500,
      unidade: 'Grama',
    };
    service.criarFormaCompra('insumo-1', request).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'purchase-create-token' });
    const create = http.expectOne('/api/admin/insumos/insumo-1/formas-compra');
    expect(create.request.method).toBe('POST');
    expect(create.request.headers.get('X-XSRF-TOKEN')).toBe('purchase-create-token');
    expect(create.request.body).toEqual(request);
    create.flush(
      { id: 'forma-1', ...request, ativa: true },
      {
        status: 201,
        statusText: 'Created',
      },
    );

    service.editarFormaCompra('insumo-1', 'forma-1', request).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'purchase-edit-token' });
    const edit = http.expectOne('/api/admin/insumos/insumo-1/formas-compra/forma-1');
    expect(edit.request.method).toBe('PUT');
    expect(edit.request.headers.get('X-XSRF-TOKEN')).toBe('purchase-edit-token');
    edit.flush({ id: 'forma-1', ...request, ativa: true });

    service.alterarStatusFormaCompra('insumo-1', 'forma-1', { ativa: false }).subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'purchase-status-token' });
    const status = http.expectOne('/api/admin/insumos/insumo-1/formas-compra/forma-1/status');
    expect(status.request.method).toBe('PATCH');
    expect(status.request.headers.get('X-XSRF-TOKEN')).toBe('purchase-status-token');
    status.flush({ id: 'forma-1', ...request, ativa: false });
  });

  it('preserves purchase factors as exact JSON decimal text for entry calculations', () => {
    service.obterParaEntrada('insumo-1').subscribe((response) => {
      expect(response.formasCompra[0]?.quantidadeEquivalentePorEmbalagemDecimal).toBe(
        '999999999999.999999',
      );
      expect(response.resumo.saldoFisico).toBe('999999999999.999999');
      expect(response.posicoes[0]?.saldoFisico).toBe('999999999999.999999');
    });

    const request = http.expectOne('/api/admin/insumos/insumo-1');
    expect(request.request.responseType).toBe('text');
    request.flush(
      '{"resumo":{"insumoId":"insumo-1","nome":"Base glicerinada","tipo":"MateriaPrima","unidadeBase":"Grama","ativo":true,"controlaLote":true,"validadeObrigatoria":true,"saldoFisico":999999999999.999999,"saldoUtilizavel":0.000001,"saldoReservado":0,"saldoDisponivel":0.000001,"estoqueMinimo":0.000001,"estoqueMinimoConfigurado":true,"situacao":"Normal"},"formasCompra":[{"id":"forma-1","nome":"Fator preciso","quantidadeEquivalentePorEmbalagem":999999999999.999999,"unidade":"Grama","ativa":true}],"posicoes":[{"loteId":"lote-1","identificacaoLote":"Lote A","fornecedor":null,"dataEntrada":"2026-01-02","dataValidade":"2026-12-31","bloqueado":false,"elegivel":true,"temRegistroSaldo":true,"versaoSaldo":"AQID","saldoFisico":999999999999.999999,"saldoUtilizavel":0.000001,"saldoReservado":0,"saldoDisponivel":0.000001}]}',
    );
  });

  it('posts exact decimal tokens with antiforgery and idempotency headers', () => {
    const request: RegistrarEntradaInsumoRequest = {
      tipo: 'Aquisicao',
      quantidadeOriginal: '1.25',
      unidadeOriginal: 'Grama',
      formaCompraId: null,
      loteId: null,
      identificacaoNovoLote: null,
      dataEntradaNovoLote: null,
      dataValidadeNovoLote: null,
      fornecedorNovoLote: null,
      novoLoteBloqueado: false,
      custoTotal: '0',
      observacao: null,
    };
    service.registrarEntrada('insumo-1', request, 'operation-key-1').subscribe();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'entry-token' });

    const post = http.expectOne('/api/admin/insumos/insumo-1/entradas');
    expect(post.request.method).toBe('POST');
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe('entry-token');
    expect(post.request.headers.get('Idempotency-Key')).toBe('operation-key-1');
    expect(post.request.body).toContain('"quantidadeOriginal":1.25');
    expect(post.request.body).toContain('"custoTotal":0');
    expect(post.request.body).not.toContain('"responsavelId"');
    post.flush({ id: 'movement-1' });
  });
});
