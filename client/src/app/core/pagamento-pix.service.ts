import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap, timeout } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export const tiposChavePix = {
  Aleatoria: 'Aleatória',
  Email: 'E-mail',
  Telefone: 'Telefone',
  Cnpj: 'CNPJ',
};

export interface DadosPix {
  tipoChave: keyof typeof tiposChavePix;
  chavePix: string;
  nomeRecebedor: string;
  instrucoesAdicionais: string | null;
}

export interface ConfiguracaoPix {
  pix: DadosPix | null;
  revisao: number;
  pedidosPendentes: number;
}

export interface AlteracaoPix extends DadosPix {
  revisao: number;
  confirmarPendentes: boolean;
}

export interface PagamentoPixPedido {
  legado?: boolean;
  situacao: 'disponivel' | 'configuracao_ausente' | 'pagamento_indisponivel';
  valorCentavos: string;
  prazoPagamentoEmUtc: string;
  pix: DadosPix | null;
}

export function statusPix(erro: unknown): number {
  return erro instanceof HttpErrorResponse ? erro.status : 0;
}

@Injectable({ providedIn: 'root' })
export class PagamentoPixService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly adminUrl = '/api/admin/configuracoes/pix-manual';

  consultarConfiguracao() {
    return this.http.get<ConfiguracaoPix>(this.adminUrl).pipe(timeout(30_000));
  }

  salvarConfiguracao(dados: AlteracaoPix, chave: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.put<ConfiguracaoPix>(this.adminUrl, dados, {
          headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave },
        }),
      ),
      timeout(30_000),
    );
  }

  consultarPedido(id: string) {
    return this.http
      .get<PagamentoPixPedido>(`/api/pedidos/${encodeURIComponent(id)}/pagamento`)
      .pipe(timeout(30_000));
  }
}
