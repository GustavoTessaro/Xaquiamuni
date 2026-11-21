import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, switchMap, timeout } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { CsrfService } from '../../core/security/csrf.service';

export interface ConfiguracaoPromocao {
  produtoId: string;
  tipo: 'Percentual' | 'PrecoFixo';
  escopo: 'TodasVariantes' | 'VariantesSelecionadas';
  valor: string;
  inicioUtc: string | null;
  fimUtc: string | null;
  variantesIds: string[];
}

export interface Promocao extends ConfiguracaoPromocao {
  id: string;
  ativa: boolean;
  versao: number;
}

export interface PrecoPrevia {
  regularCentavos: string;
  finalCentavos: string;
  economiaCentavos: string;
  offPontosBase: number;
}

export interface PreviaVariante {
  varianteId: string;
  identificacao: string;
  ativa: boolean;
  abrangida: boolean;
  regularCentavos: string;
  precoProposto: PrecoPrevia | null;
  precoEfetivo: PrecoPrevia;
  precoIncompativel: boolean;
  promocaoEfetivaId: string | null;
  propostaEfetiva: boolean;
  especificaPrevalece: boolean;
}

export interface ConsultaPrevia {
  configuracao: ConfiguracaoPromocao;
  substituiId?: string;
  instanteUtc?: string;
}

export interface PreviaPromocao {
  instanteUtc: string;
  variantes: PreviaVariante[];
  conflitos: string[];
}

export type OperacaoPromocao =
  | { tipo: 'criar'; configuracao: ConfiguracaoPromocao }
  | { tipo: 'editar'; id: string; versao: number; configuracao: ConfiguracaoPromocao }
  | { tipo: 'status'; id: string; versao: number; ativa: boolean };

const rota = '/api/admin/promocoes';

@Injectable({ providedIn: 'root' })
export class PromocoesService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AdminAuthService);
  private readonly csrf = inject(CsrfService);
  private readonly tentativa = signal<{ autor: string; chave: string; operacao: OperacaoPromocao } | null>(null);
  readonly pendente = this.tentativa.asReadonly();
  private readonly executando = signal(false);
  readonly gravando = this.executando.asReadonly();

  listar(produtoId: string, pagina: number) {
    let params = new HttpParams().set('pagina', pagina);
    if (produtoId) params = params.set('produtoId', produtoId);
    return this.http.get<Promocao[]>(rota, { params }).pipe(timeout(15_000));
  }

  obter(id: string) {
    return this.http.get<Promocao>(`${rota}/${encodeURIComponent(id)}`).pipe(timeout(15_000));
  }

  prever(dados: ConsultaPrevia) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.post<PreviaPromocao>(`${rota}/previa`, dados, {
        headers: { 'X-XSRF-TOKEN': requestToken },
      })),
      timeout(15_000),
    );
  }

  async gravar(operacao?: OperacaoPromocao): Promise<Promocao> {
    if (this.gravando() || (!!operacao === !!this.pendente())) {
      throw new Error('Resolva a tentativa em andamento.');
    }
    this.executando.set(true);
    try {
      const { requestToken } = await firstValueFrom(this.csrf.getRequestToken().pipe(timeout(15_000)));
      const sessao = await firstValueFrom(this.auth.getCurrentSession().pipe(timeout(15_000)));
      if (!sessao?.userId) throw new Error('Entre novamente como administradora.');
      const tentativa = this.pendente() ?? { autor: sessao.userId, chave: crypto.randomUUID(), operacao: operacao! };
      if (tentativa.autor !== sessao.userId) throw new Error('Entre com a conta original para recuperar a operação.');
      const acao = tentativa.operacao;
      const url = acao.tipo === 'criar' ? rota : `${rota}/${encodeURIComponent(acao.id)}`;
      const headers = { 'Idempotency-Key': tentativa.chave, 'X-XSRF-TOKEN': requestToken };
      const chamada = acao.tipo === 'criar'
        ? this.http.post<Promocao>(url, { configuracao: acao.configuracao, ativa: false }, { headers })
        : acao.tipo === 'editar'
          ? this.http.put<Promocao>(url, { configuracao: acao.configuracao, versao: acao.versao }, { headers })
          : this.http.patch<Promocao>(`${url}/status`, { ativa: acao.ativa, versao: acao.versao }, { headers });
      this.tentativa.set(tentativa);
      try {
        const resultado = await firstValueFrom(chamada.pipe(timeout(30_000)));
        this.tentativa.set(null);
        return resultado;
      } catch (erro) {
        if (erro instanceof HttpErrorResponse && erro.status >= 400 && erro.status < 500 &&
            ![401, 403, 408, 429].includes(erro.status) &&
            !['conflito_concorrencia', 'chave_em_uso'].includes(erro.error?.codigo))
          this.tentativa.set(null);
        throw erro;
      }
    } finally {
      this.executando.set(false);
    }
  }
}
