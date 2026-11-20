import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export const meiosFinanceiros = [
  'Pix',
  'Dinheiro',
  'Cartao',
  'Transferencia',
  'Boleto',
  'Outro',
] as const;
export type MeioFinanceiro = (typeof meiosFinanceiros)[number];

export interface MovimentoFinanceiro {
  valorCentavos: string;
  data: string;
  meio: MeioFinanceiro;
}

export interface CriarLancamento extends MovimentoFinanceiro {
  tipo: 'Entrada' | 'Saida';
  categoria: string;
  descricao: string;
}

export interface LancamentoFinanceiro extends CriarLancamento {
  id: string;
  responsavelId: string;
  criadoEmUtc: string;
  canceladoPorId: string | null;
  canceladoEmUtc: string | null;
  motivoCancelamento: string | null;
}

export interface CompraFinanceira {
  compraId: string;
  fornecedorId: string;
  nomeFornecedor: string;
  dataCompra: string;
  situacaoCompra: 'Rascunho' | 'Recebida' | 'Cancelada';
  situacaoFinanceira: 'Cancelada' | 'ValorAConferir' | 'Quitada' | 'Pendente' | 'Parcial';
  composicaoValor: 'Conhecido' | 'ParcialmenteConhecido' | 'Desconhecido';
  totalCentavos: string | null;
  pagoCentavos: string;
  saldoCentavos: string | null;
}

export interface PagarCompra extends MovimentoFinanceiro {
  referencia: string | null;
}

export interface PagamentoFornecedor extends PagarCompra {
  id: string;
  compraId: string;
  responsavelId: string;
  criadoEmUtc: string;
}

export type ComandoFinanceiro =
  | { tipo: 'criar'; corpo: CriarLancamento }
  | { tipo: 'cancelar'; id: string; corpo: { motivo: string } }
  | { tipo: 'pagar'; id: string; corpo: PagarCompra };

export type TentativaFinanceira = ComandoFinanceiro & { chave: string };

@Injectable({ providedIn: 'root' })
export class FinanceiroService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly base = '/api/admin/financeiro';

  lancamentos(pagina: number) {
    return this.http.get<LancamentoFinanceiro[]>(this.base + '/lancamentos', {
      params: { pagina },
    });
  }

  lancamento(id: string) {
    return this.http.get<LancamentoFinanceiro>(
      this.base + '/lancamentos/' + encodeURIComponent(id),
    );
  }

  compra(id: string) {
    return this.http.get<CompraFinanceira>(this.base + '/compras/' + encodeURIComponent(id));
  }

  pagamentos(id: string, pagina: number) {
    return this.http.get<PagamentoFornecedor[]>(
      this.base + '/compras/' + encodeURIComponent(id) + '/pagamentos',
      { params: { pagina } },
    );
  }

  enviar(tentativa: TentativaFinanceira) {
    const rota =
      tentativa.tipo === 'criar'
        ? '/lancamentos'
        : tentativa.tipo === 'cancelar'
          ? '/lancamentos/' + encodeURIComponent(tentativa.id) + '/cancelar'
          : '/compras/' + encodeURIComponent(tentativa.id) + '/pagamentos';
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<LancamentoFinanceiro | PagamentoFornecedor>(
          this.base + rota,
          tentativa.corpo,
          {
            headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': tentativa.chave },
          },
        ),
      ),
    );
  }
}
