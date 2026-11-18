import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, switchMap, timeout } from 'rxjs';
import type { PedidoCliente } from '../../public-store/pedidos.service';
import { CsrfService } from '../security/csrf.service';

export type SituacaoPedido = PedidoCliente['situacao'];

export interface ResumoPedidoAdmin {
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
}

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
    return this.http.get<DetalhePedidoAdmin>(this.urlPedido(id)).pipe(timeout(30_000));
  }

  confirmarPagamento(id: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'confirmar-pagamento', { recebimentoConferido: true });
  }

  cancelar(id: string, motivo: string): Observable<ResultadoPedidoAdmin> {
    return this.executar(id, 'cancelar', { motivo });
  }

  private executar(
    id: string,
    acao: 'confirmar-pagamento' | 'cancelar',
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
