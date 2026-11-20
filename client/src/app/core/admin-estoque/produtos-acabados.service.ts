import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, switchMap } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export type SituacaoEstoqueProdutoAcabado =
  'SemEstoque' | 'AbaixoOuNoMinimo' | 'Normal' | 'MinimoNaoConfigurado';

export type TipoMovimentoProdutoAcabado =
  | 'EstoqueInicial'
  | 'EntradaManual'
  | 'SaidaManual'
  | 'Perda'
  | 'Descarte'
  | 'AjusteInventario'
  | 'TransferenciaCondicao'
  | 'Estorno';

export type CondicaoEstoqueProdutoAcabado = 'Vendavel' | 'Danificada' | 'Quarentena';

export interface PoliticaEstoqueProdutoAcabado {
  produtoVarianteId: string;
  estoqueMinimo: number | null;
  prazoValidadePadraoDias: number | null;
}

export interface PoliticaEstoqueProdutoAcabadoRequest {
  estoqueMinimo: number | null;
  prazoValidadePadraoDias: number | null;
}

export interface LoteProdutoAcabado {
  loteId: string;
  codigo: string;
  dataFabricacao: string;
  dataValidade: string | null;
  origemValidade: 'PrazoPadrao' | 'InformadaManualmente';
  prazoValidadeAplicadoDias: number | null;
  valido: boolean;
  temRegistroSaldo: boolean;
  versaoSaldo: string | null;
  quantidadeVendavel: number;
  quantidadeDanificada: number;
  quantidadeQuarentena: number;
  saldoFisico: number;
  saldoUtilizavel: number;
  saldoReservado: number;
  saldoDisponivel: number;
}

export interface PosicaoProdutoAcabado {
  produtoVarianteId: string;
  varianteAtiva: boolean;
  saldoFisico: number;
  saldoUtilizavel: number;
  saldoReservado: number;
  saldoDisponivel: number;
  estoqueMinimo: number | null;
  estoqueMinimoConfigurado: boolean;
  situacao: SituacaoEstoqueProdutoAcabado;
  lotes: LoteProdutoAcabado[];
}

export interface MovimentoProdutoAcabado {
  id: string;
  tipo: TipoMovimentoProdutoAcabado;
  quantidade: number;
  condicaoOrigem: CondicaoEstoqueProdutoAcabado | null;
  condicaoDestino: CondicaoEstoqueProdutoAcabado | null;
  quantidadeContada: number | null;
  saldoAnteriorVendavel: number;
  saldoAnteriorDanificada: number;
  saldoAnteriorQuarentena: number;
  saldoPosteriorVendavel: number;
  saldoPosteriorDanificada: number;
  saldoPosteriorQuarentena: number;
  motivo: string;
  observacao: string | null;
  responsavelId: string;
  registradoEmUtc: string;
  loteId: string;
  codigoLote: string;
  movimentoOriginalId: string | null;
  ordemProducaoId?: string | null;
}

export interface AdminPaginaProdutoAcabado<T> {
  itens: T[];
  totalItens: number;
  pagina: number;
  itensPorPagina: number;
}

export interface MovimentoProdutoAcabadoFiltro {
  tipo?: TipoMovimentoProdutoAcabado;
  loteId?: string;
  pagina?: number;
  itensPorPagina?: number;
}

export interface EntradaProdutoAcabadoRequest {
  semControleValidade?: boolean;
  tipo: 'EstoqueInicial' | 'EntradaManual';
  quantidade: number;
  condicaoDestino: CondicaoEstoqueProdutoAcabado;
  loteId?: string | null;
  dataFabricacao?: string | null;
  dataValidade?: string | null;
  motivo?: string | null;
  observacao?: string | null;
}

export interface SaidaProdutoAcabadoRequest {
  tipo: 'SaidaManual' | 'Perda' | 'Descarte';
  loteId: string;
  quantidade: number;
  condicaoOrigem: CondicaoEstoqueProdutoAcabado;
  motivo: string;
  observacao?: string | null;
}

export interface TransferenciaProdutoAcabadoRequest {
  loteId: string;
  quantidade: number;
  condicaoOrigem: CondicaoEstoqueProdutoAcabado;
  condicaoDestino: CondicaoEstoqueProdutoAcabado;
  motivo: string;
  observacao?: string | null;
}

export interface ContagemProdutoAcabadoRequest {
  loteId: string;
  quantidadeVendavel: number;
  quantidadeDanificada: number;
  quantidadeQuarentena: number;
  motivo: string;
  versaoSaldoEsperada: string;
  observacao?: string | null;
}

export interface ResultadoContagemProdutoAcabado {
  semDiferenca: boolean;
  movimentos: MovimentoProdutoAcabado[];
  repetida: boolean;
}

export interface EstornoProdutoAcabadoRequest {
  motivo: string;
  observacao?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ProdutosAcabadosService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly basePath = '/api/admin/estoque/produtos-acabados';

  consultarPosicao(varianteId: string): Observable<PosicaoProdutoAcabado> {
    return this.http.get<PosicaoProdutoAcabado>(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/posicao`,
    );
  }

  listarLotes(varianteId: string): Observable<LoteProdutoAcabado[]> {
    return this.http.get<LoteProdutoAcabado[]>(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/lotes`,
    );
  }

  consultarLote(varianteId: string, loteId: string): Observable<LoteProdutoAcabado> {
    return this.http.get<LoteProdutoAcabado>(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/lotes/${encodeURIComponent(loteId)}`,
    );
  }

  consultarPolitica(varianteId: string): Observable<PoliticaEstoqueProdutoAcabado> {
    return this.http.get<PoliticaEstoqueProdutoAcabado>(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/politica`,
    );
  }

  configurarPolitica(
    varianteId: string,
    request: PoliticaEstoqueProdutoAcabadoRequest,
  ): Observable<PoliticaEstoqueProdutoAcabado> {
    return this.csrf
      .getRequestToken()
      .pipe(
        switchMap(({ requestToken }) =>
          this.http.put<PoliticaEstoqueProdutoAcabado>(
            `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/politica`,
            request,
            { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
          ),
        ),
      );
  }

  listarMovimentacoes(
    varianteId: string,
    filtro: MovimentoProdutoAcabadoFiltro = {},
  ): Observable<AdminPaginaProdutoAcabado<MovimentoProdutoAcabado>> {
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

    return this.http.get<AdminPaginaProdutoAcabado<MovimentoProdutoAcabado>>(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/movimentacoes`,
      { params },
    );
  }

  consultarMovimento(varianteId: string, movimentoId: string): Observable<MovimentoProdutoAcabado> {
    return this.http.get<MovimentoProdutoAcabado>(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/movimentacoes/${encodeURIComponent(movimentoId)}`,
    );
  }

  registrarEntrada(
    varianteId: string,
    request: EntradaProdutoAcabadoRequest,
    idempotencyKey: string,
  ): Observable<MovimentoProdutoAcabado> {
    return this.withWriteCsrf(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/entradas`,
      'POST',
      request,
      idempotencyKey,
    );
  }

  registrarSaidaAutomatica(
    varianteId: string,
    request: { quantidade: number; motivo: string },
    idempotencyKey: string,
  ): Observable<{ movimentoIds: string[]; repetida: boolean }> {
    return this.withWriteCsrf(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/saidas-automaticas`,
      'POST',
      request,
      idempotencyKey,
    );
  }

  registrarSaida(
    varianteId: string,
    request: SaidaProdutoAcabadoRequest,
    idempotencyKey: string,
  ): Observable<MovimentoProdutoAcabado> {
    return this.withWriteCsrf(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/saidas`,
      'POST',
      request,
      idempotencyKey,
    );
  }

  transferir(
    varianteId: string,
    request: TransferenciaProdutoAcabadoRequest,
    idempotencyKey: string,
  ): Observable<MovimentoProdutoAcabado> {
    return this.withWriteCsrf(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/transferencias`,
      'POST',
      request,
      idempotencyKey,
    );
  }

  registrarContagem(
    varianteId: string,
    request: ContagemProdutoAcabadoRequest,
    idempotencyKey: string,
  ): Observable<ResultadoContagemProdutoAcabado | MovimentoProdutoAcabado[]> {
    return this.withWriteCsrf(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/contagens`,
      'POST',
      request,
      idempotencyKey,
    );
  }

  estornarMovimento(
    varianteId: string,
    movimentoId: string,
    request: EstornoProdutoAcabadoRequest,
    idempotencyKey: string,
  ): Observable<MovimentoProdutoAcabado> {
    return this.withWriteCsrf(
      `${this.basePath}/variantes/${encodeURIComponent(varianteId)}/movimentacoes/${encodeURIComponent(movimentoId)}/estorno`,
      'POST',
      request,
      idempotencyKey,
    );
  }

  private withWriteCsrf<TRequest, TResponse>(
    url: string,
    method: 'POST',
    request: TRequest,
    idempotencyKey: string,
  ): Observable<TResponse> {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.request<TResponse>(method, url, {
          body: request,
          headers: new HttpHeaders({
            'Content-Type': 'application/json',
            'Idempotency-Key': idempotencyKey,
            'X-XSRF-TOKEN': requestToken,
          }),
        }),
      ),
    );
  }
}
