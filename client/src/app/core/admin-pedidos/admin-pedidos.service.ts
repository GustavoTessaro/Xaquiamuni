import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, switchMap, timeout } from 'rxjs';
import type { PedidoCliente } from '../../public-store/pedidos.service';
import { CsrfService } from '../security/csrf.service';

export type SituacaoPedido = PedidoCliente['situacao'];

export interface ResumoPedidoAdmin {
  numeroPublico?: string;
  id: string;
  situacao: SituacaoPedido;
  criadoEmUtc: string;
  prazoPagamentoEmUtc: string;
  totalCentavos: string;
  nomeCliente: string;
}

export interface PaginaPedidosAdmin {
  itens: ResumoPedidoAdmin[];
  pagina: number;
  itensPorPagina: number;
  totalItens: number;
  totalPaginas: number;
}

export interface DetalhePedidoAdmin {
  pedido: PedidoCliente;
  cliente: { nome: string; email: string | null };
  etag?: string | null;
}

export interface CorrecaoRastreamentoAdmin {
  codigoAnterior: string | null;
  codigoNovo: string | null;
  realizadaEmUtc: string;
  administradorId: string;
}

export type AcaoEntregaPropria = 'iniciar-entrega-propria' | 'confirmar-recebimento';

export interface ResultadoPedidoAdmin {
  id: string;
  situacao: SituacaoPedido;
  repetida: boolean;
}

export function statusPedidos(erro: unknown): number {
  return erro instanceof HttpErrorResponse ? erro.status : 0;
}

@Injectable({ providedIn: 'root' })
export class AdminPedidosService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly basePath = '/api/admin/pedidos';

  listar(pagina: number, situacao: SituacaoPedido | ''): Observable<PaginaPedidosAdmin> {
    return this.http
      .get<PaginaPedidosAdmin>(this.basePath, {
        params: { pagina, ...(situacao ? { situacao } : {}) },
      })
      .pipe(timeout(30_000));
  }

  consultar(id: string): Observable<DetalhePedidoAdmin> {
    return this.http.get<DetalhePedidoAdmin>(this.urlPedido(id), { observe: 'response' }).pipe(
      timeout(30_000),
      map(resposta => ({ ...resposta.body!, etag: resposta.headers.get('ETag') })),
    );
  }

  consultarHistoricoRastreamento(id: string): Observable<CorrecaoRastreamentoAdmin[]> {
    return this.http.get<CorrecaoRastreamentoAdmin[]>(this.urlPedido(id) + '/historico-rastreamento')
      .pipe(timeout(30_000));
  }

  entregaPropria(id: string, acao: AcaoEntregaPropria, etag: string, chave: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<ResultadoPedidoAdmin>(`${this.urlPedido(id)}/${acao}`, { confirmado: true }, {
          headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave, 'If-Match': etag },
        }),
      ),
      timeout(30_000),
    );
  }

  entregaPostal(id: string, acao: 'registrar-postagem' | 'rastreamento-postal' | 'confirmar-entrega-postal',
    etag: string, chave: string, body: object) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.request<ResultadoPedidoAdmin>(
        acao === 'rastreamento-postal' ? 'PUT' : 'POST', this.urlPedido(id) + '/' + acao, {
          body, headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave, 'If-Match': etag },
        })),
      timeout(30_000),
    );
  }

  confirmarPagamento(id: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'confirmar-pagamento', { recebimentoConferido: true });
  }

  cancelar(id: string, motivo: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'cancelar', { motivo });
  }

  iniciarPreparacao(id: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'iniciar-preparacao', {});
  }

  liberarRetirada(id: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'liberar-retirada', {});
  }

  confirmarEntrega(id: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'confirmar-entrega', { entregaPresencialConfirmada: true });
  }

  private executar(
    id: string,
    acao:
      | 'confirmar-pagamento'
      | 'cancelar'
      | 'iniciar-preparacao'
      | 'liberar-retirada'
      | 'confirmar-entrega',
    corpo: object,
  ): Observable<ResultadoPedidoAdmin> {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<ResultadoPedidoAdmin>(`${this.urlPedido(id)}/${acao}`, corpo, {
          headers: { 'X-XSRF-TOKEN': requestToken },
        }),
      ),
      timeout(30_000),
    );
  }

  private urlPedido(id: string): string {
    return `${this.basePath}/${encodeURIComponent(id)}`;
  }
}
