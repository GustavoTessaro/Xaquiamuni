import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, switchMap, timeout } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export type TipoInsumoEstoque = 'MateriaPrima' | 'Embalagem' | 'Outro';
export type UnidadeFisicaEstoque = 'Grama' | 'Quilograma' | 'Mililitro' | 'Litro' | 'Unidade';
export type UnidadeBaseEstoque = 'Grama' | 'Mililitro' | 'Unidade';
export type SituacaoEstoqueInsumo =
  'SemEstoque' | 'AbaixoOuNoMinimo' | 'Normal' | 'MinimoNaoConfigurado';

export interface AdminInsumoFiltro {
  nome?: string;
  ativo?: boolean;
  situacao?: SituacaoEstoqueInsumo;
  pagina?: number;
  itensPorPagina?: number;
}

export interface AdminInsumoResumo {
  insumoId: string;
  nome: string;
  tipo: TipoInsumoEstoque;
  unidadeBase: UnidadeBaseEstoque;
  ativo: boolean;
  controlaLote: boolean;
  validadeObrigatoria: boolean;
  saldoFisico: number;
  saldoUtilizavel: number;
  saldoReservado: number;
  saldoDisponivel: number;
  estoqueMinimo: number | null;
  estoqueMinimoConfigurado: boolean;
  situacao: SituacaoEstoqueInsumo;
}

export interface AdminPagina<T> {
  itens: T[];
  totalItens: number;
  pagina: number;
  itensPorPagina: number;
}

export interface AdminFormaCompra {
  versaoCadastro?: string;
  id: string;
  nome: string;
  quantidadeEquivalentePorEmbalagem: number;
  quantidadeEquivalentePorEmbalagemDecimal?: string;
  unidade: UnidadeBaseEstoque;
  ativa: boolean;
}

export interface AdminPosicaoEstoque {
  loteId: string | null;
  identificacaoLote: string | null;
  fornecedor: string | null;
  dataEntrada: string | null;
  dataValidade: string | null;
  bloqueado: boolean | null;
  elegivel: boolean;
  temRegistroSaldo: boolean;
  versaoSaldo: string | null;
  saldoFisico: number;
  saldoUtilizavel: number;
  saldoReservado: number;
  saldoDisponivel: number;
}

export interface AdminInsumoDetalhe {
  versao?: string;
  resumo: AdminInsumoResumo;
  formasCompra: AdminFormaCompra[];
  posicoes: AdminPosicaoEstoque[];
}

export type AdminFormaCompraPrecisa = Omit<
  AdminFormaCompra,
  'quantidadeEquivalentePorEmbalagem'
> & {
  quantidadeEquivalentePorEmbalagem: string;
  quantidadeEquivalentePorEmbalagemDecimal: string;
};

export type AdminInsumoDetalhePreciso = Omit<
  AdminInsumoDetalhe,
  'resumo' | 'formasCompra' | 'posicoes'
> & {
  resumo: AdminResumoInsumoPreciso;
  formasCompra: AdminFormaCompraPrecisa[];
  posicoes: AdminPosicaoEstoquePrecisa[];
};

export interface CadastrarInsumoRequest {
  nome: string;
  tipo: TipoInsumoEstoque;
  unidadeBase: UnidadeBaseEstoque;
  estoqueMinimo: number | null;
  controlaLote: boolean;
  validadeObrigatoria: boolean;
}

export interface AtualizarInsumoRequest {
  nome: string;
  tipo: TipoInsumoEstoque;
  estoqueMinimo: number | null;
  controlaLote: boolean;
  validadeObrigatoria: boolean;
}

export interface StatusInsumoRequest {
  ativo: boolean;
}

export interface FormaCompraRequest {
  nome: string;
  quantidadeEquivalentePorEmbalagem: number;
  unidade: UnidadeFisicaEstoque;
}

export interface StatusFormaCompraRequest {
  ativa: boolean;
}

export type TipoEntradaInsumo = 'EstoqueInicial' | 'Aquisicao';
export type TipoMovimentoInsumo =
  TipoEntradaInsumo | 'SaidaManual' | 'Perda' | 'Descarte' | 'AjusteInventario' | 'Estorno';
export type DirecaoMovimentoInsumo = 'Entrada' | 'Saida';
export type OrigemMovimentoInsumo =
  'UnidadeFisica' | 'FormaCompra' | 'OperacaoManual' | 'ContagemFisica' | 'MovimentoOriginal';

type CamposDecimalSaldo = 'saldoFisico' | 'saldoUtilizavel' | 'saldoReservado' | 'saldoDisponivel';

export type AdminResumoInsumoPreciso = Omit<
  AdminInsumoResumo,
  CamposDecimalSaldo | 'estoqueMinimo'
> & {
  saldoFisico: string;
  saldoUtilizavel: string;
  saldoReservado: string;
  saldoDisponivel: string;
  estoqueMinimo: string | null;
};

export type AdminPosicaoEstoquePrecisa = Omit<AdminPosicaoEstoque, CamposDecimalSaldo> & {
  saldoFisico: string;
  saldoUtilizavel: string;
  saldoReservado: string;
  saldoDisponivel: string;
};

export interface AdminPosicaoInsumoPrecisa {
  resumo: AdminResumoInsumoPreciso;
  posicoes: AdminPosicaoEstoquePrecisa[];
}

export interface AdminMovimentoFiltro {
  tipo?: TipoMovimentoInsumo;
  loteId?: string;
  pagina?: number;
  itensPorPagina?: number;
}

export interface RegistrarEntradaInsumoRequest {
  semControleValidade?: boolean;
  tipo: TipoEntradaInsumo;
  quantidadeOriginal: string;
  unidadeOriginal: UnidadeFisicaEstoque | null;
  formaCompraId: string | null;
  loteId: string | null;
  identificacaoNovoLote: string | null;
  dataEntradaNovoLote: string | null;
  dataValidadeNovoLote: string | null;
  fornecedorNovoLote: string | null;
  novoLoteBloqueado: boolean;
  custoTotal: string | null;
  observacao: string | null;
}

export type TipoSaidaInsumo = 'SaidaManual' | 'Perda' | 'Descarte';

export interface RegistrarSaidaInsumoRequest {
  tipo: TipoSaidaInsumo;
  quantidadeOriginal: string;
  unidadeOriginal: UnidadeBaseEstoque;
  loteId: string | null;
  motivo: string;
  observacao: string | null;
}

export interface RegistrarContagemInsumoRequest {
  quantidadeEncontrada: string;
  unidadeOriginal: UnidadeBaseEstoque;
  loteId: string | null;
  versaoSaldoEsperada: string | null;
  motivo: string;
  observacao: string | null;
}

export interface EstornarMovimentoInsumoRequest {
  motivo: string;
  observacao: string | null;
}

export type ResultadoContagemInsumo =
  | { resultado: 'MovimentoRegistrado'; movimento: AdminMovimentoPreciso }
  | { resultado: 'SemDiferenca' };

export interface AdminMovimentoInsumo {
  id: string;
  tipo: TipoMovimentoInsumo;
  direcao: DirecaoMovimentoInsumo;
  quantidadeOriginal: number;
  unidadeOriginal: UnidadeFisicaEstoque | null;
  quantidadeBase: number;
  unidadeBase: UnidadeFisicaEstoque;
  saldoAnterior: number;
  saldoPosterior: number;
  motivo: string;
  observacao: string | null;
  origem: OrigemMovimentoInsumo;
  responsavelId: string;
  registradoEmUtc: string;
  loteId: string | null;
  identificacaoLote: string | null;
  formaCompraId: string | null;
  formaCompraNome: string | null;
  fatorCompra: number | null;
  unidadeFatorCompra: UnidadeFisicaEstoque | null;
  custoTotal: number | null;
  custoPorUnidadeBase: number | null;
  movimentoOriginalId: string | null;
  ordemProducaoId?: string | null;
}

export type AdminMovimentoPreciso = Omit<
  AdminMovimentoInsumo,
  | 'quantidadeOriginal'
  | 'quantidadeBase'
  | 'saldoAnterior'
  | 'saldoPosterior'
  | 'fatorCompra'
  | 'custoTotal'
  | 'custoPorUnidadeBase'
> & {
  quantidadeOriginal: string;
  quantidadeBase: string;
  saldoAnterior: string;
  saldoPosterior: string;
  fatorCompra: string | null;
  custoTotal: string | null;
  custoPorUnidadeBase: string | null;
};

@Injectable({ providedIn: 'root' })
export class InsumosService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly basePath = '/api/admin/insumos';

  listar(filtro: AdminInsumoFiltro = {}): Observable<AdminPagina<AdminResumoInsumoPreciso>> {
    let params = new HttpParams();
    const nome = filtro.nome?.trim();
    if (nome) {
      params = params.set('nome', nome);
    }
    if (filtro.ativo !== undefined) {
      params = params.set('ativo', filtro.ativo);
    }
    if (filtro.situacao) {
      params = params.set('situacao', filtro.situacao);
    }
    if (filtro.pagina !== undefined) {
      params = params.set('pagina', filtro.pagina);
    }
    if (filtro.itensPorPagina !== undefined) {
      params = params.set('itensPorPagina', filtro.itensPorPagina);
    }

    return this.http
      .get(this.basePath, { params, responseType: 'text' })
      .pipe(
        map((response) =>
          parseExactDecimalResponse<AdminPagina<AdminResumoInsumoPreciso>>(response, [
            'saldoFisico',
            'saldoUtilizavel',
            'saldoReservado',
            'saldoDisponivel',
            'estoqueMinimo',
          ]),
        ),
      );
  }

  obter(id: string): Observable<AdminInsumoDetalhe> {
    return this.http.get<AdminInsumoDetalhe>(`${this.basePath}/${encodeURIComponent(id)}`);
  }

  excluir(id: string, forma: string | null, versao: string, chave: string): Observable<void> {
    const recurso = `${this.basePath}/${encodeURIComponent(id)}`;
    const url = forma ? `${recurso}/formas-compra/${encodeURIComponent(forma)}` : recurso;
    return this.withCsrf(token => this.http.delete<void>(url, {
      headers: { 'X-XSRF-TOKEN': token, 'If-Match': `"${versao}"`, 'Idempotency-Key': chave },
    })).pipe(timeout(20_000));
  }

  consultarPosicao(id: string): Observable<AdminPosicaoInsumoPrecisa> {
    return this.http
      .get(`${this.basePath}/${encodeURIComponent(id)}/posicao`, { responseType: 'text' })
      .pipe(
        map((response) =>
          parseExactDecimalResponse<AdminPosicaoInsumoPrecisa>(response, [
            'saldoFisico',
            'saldoUtilizavel',
            'saldoReservado',
            'saldoDisponivel',
            'estoqueMinimo',
          ]),
        ),
      );
  }

  listarMovimentacoes(
    id: string,
    filtro: AdminMovimentoFiltro = {},
  ): Observable<AdminPagina<AdminMovimentoPreciso>> {
    let params = new HttpParams();
    if (filtro.tipo) {
      params = params.set('tipo', filtro.tipo);
    }
    if (filtro.loteId) {
      params = params.set('loteId', filtro.loteId);
    }
    if (filtro.pagina !== undefined) {
      params = params.set('pagina', filtro.pagina);
    }
    if (filtro.itensPorPagina !== undefined) {
      params = params.set('itensPorPagina', filtro.itensPorPagina);
    }
    return this.http
      .get(`${this.basePath}/${encodeURIComponent(id)}/movimentacoes`, {
        params,
        responseType: 'text',
      })
      .pipe(
        map((response) =>
          parseExactDecimalResponse<AdminPagina<AdminMovimentoPreciso>>(response, [
            'quantidadeOriginal',
            'quantidadeBase',
            'saldoAnterior',
            'saldoPosterior',
            'fatorCompra',
            'custoTotal',
            'custoPorUnidadeBase',
          ]),
        ),
      );
  }

  obterParaEntrada(id: string): Observable<AdminInsumoDetalhePreciso> {
    return this.http
      .get(`${this.basePath}/${encodeURIComponent(id)}`, { responseType: 'text' })
      .pipe(map((response) => parseDetailPreservingPurchaseFactors(response)));
  }

  criar(request: CadastrarInsumoRequest): Observable<AdminInsumoDetalhe> {
    return this.withCsrf((token) =>
      this.http.post<AdminInsumoDetalhe>(this.basePath, request, {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': token }),
      }),
    );
  }

  editar(id: string, request: AtualizarInsumoRequest): Observable<AdminInsumoDetalhe> {
    return this.withCsrf((token) =>
      this.http.put<AdminInsumoDetalhe>(`${this.basePath}/${encodeURIComponent(id)}`, request, {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': token }),
      }),
    );
  }

  alterarStatus(id: string, request: StatusInsumoRequest): Observable<AdminInsumoDetalhe> {
    return this.withCsrf((token) =>
      this.http.patch<AdminInsumoDetalhe>(
        `${this.basePath}/${encodeURIComponent(id)}/status`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': token }) },
      ),
    );
  }

  listarFormasCompra(id: string): Observable<AdminFormaCompra[]> {
    return this.http.get<AdminFormaCompra[]>(
      `${this.basePath}/${encodeURIComponent(id)}/formas-compra`,
    );
  }

  criarFormaCompra(id: string, request: FormaCompraRequest): Observable<AdminFormaCompra> {
    return this.withCsrf((token) =>
      this.http.post<AdminFormaCompra>(
        `${this.basePath}/${encodeURIComponent(id)}/formas-compra`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': token }) },
      ),
    );
  }

  editarFormaCompra(
    id: string,
    formaId: string,
    request: FormaCompraRequest,
  ): Observable<AdminFormaCompra> {
    return this.withCsrf((token) =>
      this.http.put<AdminFormaCompra>(
        `${this.basePath}/${encodeURIComponent(id)}/formas-compra/${encodeURIComponent(formaId)}`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': token }) },
      ),
    );
  }

  alterarStatusFormaCompra(
    id: string,
    formaId: string,
    request: StatusFormaCompraRequest,
  ): Observable<AdminFormaCompra> {
    return this.withCsrf((token) =>
      this.http.patch<AdminFormaCompra>(
        `${this.basePath}/${encodeURIComponent(id)}/formas-compra/${encodeURIComponent(formaId)}/status`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': token }) },
      ),
    );
  }

  registrarEntrada(
    id: string,
    request: RegistrarEntradaInsumoRequest,
    idempotencyKey: string,
  ): Observable<AdminMovimentoInsumo> {
    validarChaveIdempotencia(idempotencyKey);

    const body = serializeEntradaRequest(request);
    return this.withCsrf((token) =>
      this.http.post<AdminMovimentoInsumo>(
        `${this.basePath}/${encodeURIComponent(id)}/entradas`,
        body,
        {
          headers: new HttpHeaders({
            'Content-Type': 'application/json',
            'Idempotency-Key': idempotencyKey,
            'X-XSRF-TOKEN': token,
          }),
        },
      ),
    );
  }

  registrarSaida(
    id: string,
    request: RegistrarSaidaInsumoRequest,
    idempotencyKey: string,
  ): Observable<AdminMovimentoPreciso> {
    return this.postOperacao(
      `${this.basePath}/${encodeURIComponent(id)}/saidas`,
      serializeSaidaRequest(request),
      idempotencyKey,
    ).pipe(
      map((response) =>
        parseExactDecimalResponse<AdminMovimentoPreciso>(response, camposDecimalMovimento),
      ),
    );
  }

  registrarContagem(
    id: string,
    request: RegistrarContagemInsumoRequest,
    idempotencyKey: string,
  ): Observable<ResultadoContagemInsumo> {
    return this.postOperacao(
      `${this.basePath}/${encodeURIComponent(id)}/contagens`,
      serializeContagemRequest(request),
      idempotencyKey,
    ).pipe(
      map((response) => {
        const parsed: unknown = JSON.parse(response);
        if (typeof parsed === 'object' && parsed !== null && Object.keys(parsed).length === 0) {
          return { resultado: 'SemDiferenca' } as const;
        }
        return {
          resultado: 'MovimentoRegistrado',
          movimento: parseExactDecimalResponse<AdminMovimentoPreciso>(
            response,
            camposDecimalMovimento,
          ),
        } as const;
      }),
    );
  }

  estornarMovimento(
    id: string,
    movementId: string,
    request: EstornarMovimentoInsumoRequest,
    idempotencyKey: string,
  ): Observable<AdminMovimentoPreciso> {
    return this.postOperacao(
      `${this.basePath}/${encodeURIComponent(id)}/movimentacoes/${encodeURIComponent(movementId)}/estorno`,
      serializeEstornoRequest(request),
      idempotencyKey,
    ).pipe(
      map((response) =>
        parseExactDecimalResponse<AdminMovimentoPreciso>(response, camposDecimalMovimento),
      ),
    );
  }

  consultarMovimentacao(id: string, movementId: string): Observable<AdminMovimentoPreciso> {
    return this.http
      .get(
        `${this.basePath}/${encodeURIComponent(id)}/movimentacoes/${encodeURIComponent(movementId)}`,
        { responseType: 'text' },
      )
      .pipe(
        map((response) =>
          parseExactDecimalResponse<AdminMovimentoPreciso>(response, camposDecimalMovimento),
        ),
      );
  }

  private postOperacao(url: string, body: string, idempotencyKey: string): Observable<string> {
    validarChaveIdempotencia(idempotencyKey);
    return this.withCsrf((token) =>
      this.http.post(url, body, {
        headers: new HttpHeaders({
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'X-XSRF-TOKEN': token,
        }),
        responseType: 'text',
      }),
    );
  }

  private withCsrf<T>(request: (token: string) => Observable<T>): Observable<T> {
    return this.csrf.getRequestToken().pipe(switchMap(({ requestToken }) => request(requestToken)));
  }
}

const camposDecimalMovimento = [
  'quantidadeOriginal',
  'quantidadeBase',
  'saldoAnterior',
  'saldoPosterior',
  'fatorCompra',
  'custoTotal',
  'custoPorUnidadeBase',
];

function validarChaveIdempotencia(idempotencyKey: string): void {
  if (!idempotencyKey.trim() || idempotencyKey.length > 128) {
    throw new RangeError('A chave de idempotência da operação é inválida.');
  }
}

function serializeEntradaRequest(request: RegistrarEntradaInsumoRequest): string {
  const quantidade = jsonDecimal(request.quantidadeOriginal);
  const custo = request.custoTotal === null ? 'null' : jsonDecimal(request.custoTotal);
  return (
    `{"tipo":${JSON.stringify(request.tipo)},"quantidadeOriginal":${quantidade},` +
    `"unidadeOriginal":${JSON.stringify(request.unidadeOriginal)},` +
    `"formaCompraId":${JSON.stringify(request.formaCompraId)},` +
    `"loteId":${JSON.stringify(request.loteId)},` +
    `"identificacaoNovoLote":${JSON.stringify(request.identificacaoNovoLote)},` +
    `"dataEntradaNovoLote":${JSON.stringify(request.dataEntradaNovoLote)},` +
    (request.semControleValidade === undefined
      ? ''
      : `"semControleValidade":${JSON.stringify(request.semControleValidade)},`) +
    `"dataValidadeNovoLote":${JSON.stringify(request.dataValidadeNovoLote)},` +
    `"fornecedorNovoLote":${JSON.stringify(request.fornecedorNovoLote)},` +
    `"novoLoteBloqueado":${request.novoLoteBloqueado},` +
    `"custoTotal":${custo},` +
    `"observacao":${JSON.stringify(request.observacao)}}`
  );
}

function serializeSaidaRequest(request: RegistrarSaidaInsumoRequest): string {
  return (
    `{"tipo":${JSON.stringify(request.tipo)},"quantidadeOriginal":${jsonDecimal(request.quantidadeOriginal)},` +
    `"unidadeOriginal":${JSON.stringify(request.unidadeOriginal)},` +
    `"loteId":${JSON.stringify(request.loteId)},` +
    `"motivo":${JSON.stringify(request.motivo)},` +
    `"observacao":${JSON.stringify(request.observacao)}}`
  );
}

function serializeContagemRequest(request: RegistrarContagemInsumoRequest): string {
  return (
    `{"quantidadeEncontrada":${jsonDecimal(request.quantidadeEncontrada)},` +
    `"unidadeOriginal":${JSON.stringify(request.unidadeOriginal)},` +
    `"loteId":${JSON.stringify(request.loteId)},` +
    `"versaoSaldoEsperada":${JSON.stringify(request.versaoSaldoEsperada)},` +
    `"motivo":${JSON.stringify(request.motivo)},` +
    `"observacao":${JSON.stringify(request.observacao)}}`
  );
}

function serializeEstornoRequest(request: EstornarMovimentoInsumoRequest): string {
  return `{"motivo":${JSON.stringify(request.motivo)},"observacao":${JSON.stringify(request.observacao)}}`;
}

function jsonDecimal(value: string): string {
  if (!/^(?:0|[1-9]\d*)(?:\.\d+)?$/.test(value)) {
    throw new RangeError('A quantidade e o custo devem ser números decimais válidos.');
  }
  return value;
}

function parseDetailPreservingPurchaseFactors(response: string): AdminInsumoDetalhePreciso {
  const detail = parseExactDecimalResponse<AdminInsumoDetalhePreciso>(response, [
    'saldoFisico',
    'saldoUtilizavel',
    'saldoReservado',
    'saldoDisponivel',
    'estoqueMinimo',
    'quantidadeEquivalentePorEmbalagem',
  ]);
  if (
    detail.formasCompra.some((forma) => typeof forma.quantidadeEquivalentePorEmbalagem !== 'string')
  ) {
    throw new Error('A resposta de estoque não contém os fatores de compra esperados.');
  }
  return {
    ...detail,
    formasCompra: detail.formasCompra.map((forma) => ({
      ...forma,
      quantidadeEquivalentePorEmbalagemDecimal: forma.quantidadeEquivalentePorEmbalagem,
    })),
  };
}

function parseExactDecimalResponse<T>(response: string, fields: string[]): T {
  const fieldNames = fields.map((field) => field.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|');
  const decimalProperty = new RegExp(
    `("(?:${fieldNames})"\\s*:\\s*)(-?(?:0|[1-9]\\d*)(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)(?=\\s*[,}])`,
    'g',
  );
  const exactJson = response.replace(
    decimalProperty,
    (_match, property: string, value: string) => `${property}"${expandJsonDecimal(value)}"`,
  );
  return JSON.parse(exactJson) as T;
}

function expandJsonDecimal(value: string): string {
  const parts = /^(-?)(0|[1-9]\d*)(?:\.(\d+))?(?:[eE]([+-]?\d+))?$/.exec(value);
  if (!parts) {
    throw new RangeError('A resposta contém um valor decimal inválido.');
  }

  const sign = parts[1] ?? '';
  const integer = parts[2] ?? '0';
  const fraction = parts[3] ?? '';
  const exponent = Number(parts[4] ?? '0');
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 128) {
    throw new RangeError('A resposta contém um expoente decimal fora do limite.');
  }
  const digits = `${integer}${fraction}`;
  const point = integer.length + exponent;
  if (point <= 0) {
    return `${sign}0.${'0'.repeat(-point)}${digits}`;
  }
  if (point >= digits.length) {
    return `${sign}${digits}${'0'.repeat(point - digits.length)}`;
  }
  return `${sign}${digits.slice(0, point)}.${digits.slice(point)}`;
}
