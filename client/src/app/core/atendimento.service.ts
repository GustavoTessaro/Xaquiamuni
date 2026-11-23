import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap, timeout } from 'rxjs';
import { CsrfService } from './security/csrf.service';
import type { ItemCompra, PrecoAtualPedido } from '../public-store/pedidos.service';

export type ModalidadeEntrega = 'RetiradaLocal' | 'EntregaPropria';
export interface EnderecoEntrega {
  cep: string; uf: string; cidade: string; bairro: string; logradouro: string; numero: string;
  complemento: string | null;
}
export interface DadosEntrega {
  destino: (EnderecoEntrega & { codigoIbge: string }) | null;
  cidadeId: string | null;
  enderecoRetirada: string | null;
  instrucoesRetirada: string | null;
}
export interface OfertaEntrega {
  id: string;
  modalidade: ModalidadeEntrega;
  validaAteUtc: string;
  freteCentavos: string;
  totalCentavos: string;
  dadosEntrega: DadosEntrega;
}
export interface CotacaoEntrega {
  ofertas: OfertaEntrega[];
  precosAtuais: PrecoAtualPedido[];
  motivoEntregaPropriaIndisponivel: string | null;
}
export interface ModalidadesPublicas { retiradaLocal: boolean; entregaPropria: boolean }
export interface ConfiguracaoAtendimento {
  retiradaHabilitada: boolean;
  entregaPropriaHabilitada: boolean;
  enderecoRetirada: string | null;
  instrucoesRetirada: string | null;
  whatsApp: string | null;
  whatsAppConfirmado: boolean;
  whatsAppHabilitado: boolean;
  revisao: number;
}
export interface CidadeEntrega {
  id: string; codigoIbge: string; nome: string; uf: string; tarifaCentavos: string; ativa: boolean; revisao: number;
}

@Injectable({ providedIn: 'root' })
export class AtendimentoService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly admin = '/api/admin/loja/atendimento';

  modalidades() { return this.http.get<ModalidadesPublicas>('/api/loja/modalidades-entrega').pipe(timeout(15_000)); }
  contato() { return this.http.get<{ whatsApp: string | null }>('/api/loja/contato').pipe(timeout(15_000)); }
  configuracao() { return this.http.get<ConfiguracaoAtendimento>(this.admin).pipe(timeout(15_000)); }
  cidades() { return this.http.get<CidadeEntrega[]>(this.admin + '/cidades').pipe(timeout(15_000)); }

  cotar(itens: ItemCompra[], destino?: EnderecoEntrega | 'padrao') {
    const corpo = { itens, ...(destino === 'padrao' ? { usarEnderecoPadrao: true } : destino && { endereco: destino }) };
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.post<CotacaoEntrega>('/api/checkout/cotacoes-entrega', corpo,
        { headers: { 'X-XSRF-TOKEN': requestToken } })),
      timeout(30_000),
    );
  }

  escrever(metodo: 'POST' | 'PUT' | 'DELETE', rota: string, corpo: object | null, chave: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.request(metodo, this.admin + rota, {
        body: corpo, headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave },
      })),
      timeout(30_000),
    );
  }
}
