import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export const canaisVenda = ['Presencial', 'Feira', 'WhatsApp', 'Outro'] as const;
export const meiosVenda = ['Pix', 'Dinheiro', 'Cartao', 'Outro'] as const;
export type CanalVenda = (typeof canaisVenda)[number];
export type MeioVenda = (typeof meiosVenda)[number];
export type EstadoVenda = 'Pendente' | 'Parcial' | 'Paga' | 'Cancelada';
export type OperacaoVenda = 'criar' | 'receber' | 'cancelar';

export interface RecebimentoInput {
  valorCentavos: string;
  data: string;
  meio: MeioVenda;
}
export interface ItemVendaInput {
  produtoId: string;
  varianteId: string;
  quantidade: number;
  precoUnitarioCentavos: string;
  descontoCentavos: string;
}
export interface CriarVendaInput {
  canal: CanalVenda;
  descricaoCanal: string | null;
  dataVenda: string;
  comprador: string | null;
  vencimento: string | null;
  itens: ItemVendaInput[];
  recebimentoInicial: RecebimentoInput | null;
}
export interface ResumoVenda {
  id: string;
  versao: string;
  dataVenda: string;
  canal: CanalVenda;
  comprador?: string;
  situacao: 'Registrada' | 'Cancelada';
  situacaoPagamento: EstadoVenda;
  totalCentavos: string;
  recebidoCentavos: string;
  saldoCentavos: string;
}
export interface DetalheVenda {
  resumo: ResumoVenda;
  comprador: string | null;
  descricaoCanal: string | null;
  vencimento: string | null;
  canceladaEmUtc: string | null;
  motivoCancelamento: string | null;
  itens: (ItemVendaInput & {
    id: string;
    nomeProduto: string;
    nomeVariante: string;
    subtotalCentavos: string;
  })[];
  recebimentos: (RecebimentoInput & { id: string })[];
  baixas: {
    itemId: string;
    loteId: string;
    movimentoId: string;
    quantidade: number;
    movimentoCancelamentoId: string | null;
  }[];
}
export type TentativaVenda =
  | { tipo: 'criar'; chave: string; corpo: CriarVendaInput }
  | { tipo: 'receber'; chave: string; id: string; versao: string; corpo: RecebimentoInput }
  | { tipo: 'cancelar'; chave: string; id: string; versao: string; corpo: { motivo: string } };

@Injectable({ providedIn: 'root' })
export class VendasExternasService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly base = '/api/admin/vendas-externas';

  listar(canal: string, situacao: string, pagina: number) {
    let params = new HttpParams().set('pagina', pagina);
    if (canal) params = params.set('canal', canal);
    if (situacao === 'Cancelada') params = params.set('situacao', situacao);
    else if (situacao) params = params.set('pagamento', situacao);
    return this.http.get<ResumoVenda[]>(this.base, { params });
  }
  detalhe(id: string) {
    return this.http.get<DetalheVenda>(`${this.base}/${encodeURIComponent(id)}`);
  }
  tentativa(chave: string, operacao: OperacaoVenda) {
    return this.http.get<ResumoVenda>(`${this.base}/tentativas/${encodeURIComponent(chave)}`, {
      params: { operacao },
    });
  }
  enviar(tentativa: TentativaVenda) {
    const url =
      tentativa.tipo === 'criar'
        ? this.base
        : `${this.base}/${encodeURIComponent(tentativa.id)}/${tentativa.tipo === 'receber' ? 'recebimentos' : 'cancelar'}`;
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<ResumoVenda>(url, tentativa.corpo, {
          headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': tentativa.chave },
        }),
      ),
    );
  }
}
