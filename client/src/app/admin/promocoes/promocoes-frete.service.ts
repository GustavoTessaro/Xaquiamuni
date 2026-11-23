import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap, timeout } from 'rxjs';
import { CsrfService } from '../../core/security/csrf.service';

export type ModalidadeFrete = 'EntregaPropria' | 'CorreiosManual';
export type TipoDescontoFrete = 'Percentual' | 'ValorFixo' | 'FreteGratis';
export interface ConfiguracaoFrete {
  nome: string;
  tipo: TipoDescontoFrete;
  valor: string;
  modalidades: ModalidadeFrete[];
  subtotalMinimoCentavos: string;
  inicioUtc: string | null;
  fimUtc: string | null;
}
export interface CampanhaFrete extends ConfiguracaoFrete {
  id: string;
  ativa: boolean;
  versao: number;
}
export interface PaginaCampanhas { itens: CampanhaFrete[]; temProxima: boolean }

const rota = '/api/admin/promocoes-frete';

@Injectable({ providedIn: 'root' })
export class PromocoesFreteService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  listar(pagina: number) {
    return this.http.get<PaginaCampanhas>(rota, { params: { pagina } }).pipe(timeout(15_000));
  }

  consultar(id: string) {
    return this.http.get<CampanhaFrete>(rota + '/' + id).pipe(timeout(15_000));
  }

  salvar(dados: ConfiguracaoFrete, atual: CampanhaFrete | null) {
    return this.escrever(atual ? 'PUT' : 'POST', atual ? '/' + atual.id : '', dados, atual?.versao);
  }

  alterarStatus(atual: CampanhaFrete) {
    return this.escrever('PATCH', '/' + atual.id + '/status', { ativa: !atual.ativa }, atual.versao);
  }

  private escrever(method: string, sufixo: string, body: object, versao?: number) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.request<CampanhaFrete>(method, rota + sufixo, {
        body,
        headers: { 'X-XSRF-TOKEN': requestToken, ...(versao !== undefined ? { 'If-Match': '"' + versao + '"' } : {}) },
      })),
      timeout(15_000),
    );
  }
}
