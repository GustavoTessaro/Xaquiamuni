import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import { CsrfService } from '../core/security/csrf.service';
import { precoValido } from './catalogo-formatacao';
import type { PrecoCatalogo } from './catalogo-publico.service';
import type { DadosEntrega, ModalidadeEntrega } from '../core/atendimento.service';

const url = '/api/pedidos';
export const limiteItensPedido = 100;
export const limiteQuantidadePedido = 2_147_483_647;
export const situacoesPedido = {
  AguardandoPagamento: 'Aguardando pagamento',
  PagamentoConfirmado: 'Pagamento confirmado',
  EmPreparacao: 'Em preparação',
  ProntoParaRetirada: 'Pronto para retirada',
  Postado: 'Postado',
  EmEntrega: 'Em entrega',
  Entregue: 'Entregue',
  Cancelado: 'Cancelado',
  Expirado: 'Expirado',
} as const;

export interface ItemCompra {
  produtoId: string;
  produtoVarianteId: string;
  quantidade: number;
  precoAceitoCentavos?: string;
}

export interface PrecoAtualPedido {
  produtoId: string;
  produtoVarianteId: string;
  preco: PrecoCatalogo;
}

export interface ItemPedido extends ItemCompra {
  nomeProduto: string;
  nomeVariante: string;
  precoUnitarioCentavos: string;
  subtotalCentavos: string;
  precoRegularCentavos?: string;
  promocaoId?: string;
}

export interface PostagemPostal {
  postadaEmUtc: string;
  servicoPostalUtilizado: string;
  codigoRastreamento: string | null;
  entregueEmUtc: string | null;
  urlConsultaRastreamento: string | null;
}

export interface PedidoCliente {
  postagem?: PostagemPostal | null;
  numeroPublico?: string;
  id: string;
  situacao: keyof typeof situacoesPedido;
  modalidadeEntrega: ModalidadeEntrega;
  entrega?: DadosEntrega | null;
  subtotalProdutosCentavos?: string;
  freteCentavos?: string;
  criadoEmUtc: string;
  prazoPagamentoEmUtc: string;
  totalCentavos: string;
  itens: ItemPedido[];
}

export type ResumoPedidoCliente = Pick<
  PedidoCliente,
  'id' | 'numeroPublico' | 'situacao' | 'criadoEmUtc' | 'prazoPagamentoEmUtc' | 'totalCentavos'
>;

export interface PaginaPedidosCliente {
  itens: ResumoPedidoCliente[];
  pagina: number;
  itensPorPagina: number;
  totalItens: number;
  totalPaginas: number;
}

export function uuidValido(valor: unknown): valor is string {
  return (
    typeof valor === 'string' &&
    /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(valor) &&
    valor !== '00000000-0000-0000-0000-000000000000'
  );
}

export function itensCompraValidos(itens: unknown): itens is ItemCompra[] {
  return (
    Array.isArray(itens) &&
    itens.length > 0 &&
    itens.length <= limiteItensPedido &&
    itens.every(
      (item) =>
        uuidValido(item?.produtoId) &&
        uuidValido(item?.produtoVarianteId) &&
        Number.isInteger(item?.quantidade) &&
        item.quantidade > 0 &&
        item.quantidade <= limiteQuantidadePedido &&
        (item.precoAceitoCentavos === undefined || precoValido(item.precoAceitoCentavos)),
    ) &&
    new Set(itens.map((item) => item.produtoVarianteId.toLowerCase())).size === itens.length
  );
}

export function mesmosItensCompra(a: readonly ItemCompra[], b: readonly ItemCompra[]): boolean {
  return (
    a.length === b.length &&
    a.every((item) =>
      b.some(
        (outro) =>
          item.produtoId.toLowerCase() === outro.produtoId.toLowerCase() &&
          item.produtoVarianteId.toLowerCase() === outro.produtoVarianteId.toLowerCase() &&
          item.quantidade === outro.quantidade,
      ),
    )
  );
}

@Injectable({ providedIn: 'root' })
export class PedidosService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  obterToken() {
    return this.csrf.getRequestToken().pipe(timeout(30_000));
  }

  criar(itens: readonly ItemCompra[], chave: string, requestToken: string, ofertaEntregaId?: string, conversaoCorreiosId?: string) {
    return this.http
      .post<PedidoCliente>(
        url,
        conversaoCorreiosId ? { conversaoCorreiosId } : {
          ...(ofertaEntregaId === undefined ? {} : { ofertaEntregaId }),
          itens: itens.map(({ produtoId, produtoVarianteId, quantidade, precoAceitoCentavos }) => ({
            produtoId,
            produtoVarianteId,
            quantidade,
            ...(precoAceitoCentavos === undefined ? {} : { precoAceitoCentavos }),
          })),
        },
        {
          headers: { 'Idempotency-Key': chave, 'X-XSRF-TOKEN': requestToken },
          observe: 'response',
        },
      )
      .pipe(
        timeout(30_000),
        map((response) => {
          const pedido = lerPedido(response, [200, 201]);
          if (conversaoCorreiosId) {
            if (pedido.modalidadeEntrega !== 'Correios') throw new Error('Modalidade do pedido inesperada.');
            return pedido;
          }
          if (!mesmosItensCompra(itens, pedido.itens)) {
            throw new Error('Itens do pedido diferentes da tentativa registrada.');
          }
          if (itens.some(item => item.precoAceitoCentavos !== undefined &&
              !pedido.itens.some(gravado => gravado.produtoVarianteId.toLowerCase() ===
                item.produtoVarianteId.toLowerCase() && gravado.precoUnitarioCentavos === item.precoAceitoCentavos))) {
            throw new Error('Preço do pedido diferente da condição enviada.');
          }
          return pedido;
        }),
      );
  }

  listar(pagina: number) {
    return this.http.get<PaginaPedidosCliente>(url, { params: { pagina } }).pipe(timeout(30_000));
  }

  consultar(id: string) {
    return this.http
      .get<PedidoCliente>(`${url}/${encodeURIComponent(id)}`, { observe: 'response' })
      .pipe(
        timeout(30_000),
        map((response) => {
          const pedido = lerPedido(response, [200]);
          if (pedido.id.toLowerCase() !== id.toLowerCase()) {
            throw new Error('Identificador do pedido inesperado.');
          }
          return pedido;
        }),
      );
  }
}

function lerPedido(
  { status, body }: HttpResponse<PedidoCliente>,
  esperados: number[],
): PedidoCliente {
  if (
    !esperados.includes(status) ||
    !body ||
    !uuidValido(body.id) ||
    !Object.hasOwn(situacoesPedido, body.situacao) ||
    (body.modalidadeEntrega !== 'RetiradaLocal' && body.modalidadeEntrega !== 'EntregaPropria' && body.modalidadeEntrega !== 'Correios') ||
    !dataUtcValida(body.criadoEmUtc) ||
    !dataUtcValida(body.prazoPagamentoEmUtc) ||
    !centavosValidos(body.totalCentavos) ||
    !itensCompraValidos(body.itens) ||
    !body.itens.every(
      (item) =>
        typeof item.nomeProduto === 'string' &&
        typeof item.nomeVariante === 'string' &&
        centavosValidos(item.precoUnitarioCentavos) &&
        centavosValidos(item.subtotalCentavos),
    )
  ) {
    throw new Error('Resposta inesperada da API de pedidos.');
  }

  return body;
}

function centavosValidos(valor: unknown): boolean {
  return typeof valor === 'string' && /^\d{1,19}$/.test(valor);
}

function dataUtcValida(valor: unknown): boolean {
  return (
    typeof valor === 'string' && /(?:Z|\+00:00)$/.test(valor) && Number.isFinite(Date.parse(valor))
  );
}
