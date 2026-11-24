import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { UnidadeBaseEstoque } from './admin-estoque/insumos.service';
import { CsrfService } from './security/csrf.service';

export interface ItemReceitaInput {
  insumoId: string;
  quantidade: string;
  unidadeBase: UnidadeBaseEstoque;
}

export interface ReceitaInput {
  produtoVarianteId: string;
  nome: string;
  rendimento: number;
  itens: ItemReceitaInput[];
  token: string | null;
}

export interface ResumoReceita {
  id: string;
  nome: string;
  ativa: boolean;
  produtoId: string;
  produto: string;
  varianteId: string;
  variante: string;
  numeroVersao: number;
  rendimento: number;
}

export interface RevisaoReceita {
  id: string;
  numero: number;
  rendimento: number;
  criadaEmUtc: string;
}

export interface VersaoReceita extends RevisaoReceita {
  itens: (ItemReceitaInput & { nomeInsumo: string })[];
}

export interface DetalheReceita {
  resumo: ResumoReceita;
  token: string;
  atual: VersaoReceita;
  historico: RevisaoReceita[];
}

@Injectable({ providedIn: 'root' })
export class ReceitasProducaoService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly rota = '/api/admin/receitas';

  listar() {
    return this.http.get<ResumoReceita[]>(this.rota);
  }

  obter(id: string) {
    return this.http.get<DetalheReceita>(`${this.rota}/${encodeURIComponent(id)}`);
  }

  versao(id: string, numero: number) {
    return this.http.get<VersaoReceita>(`${this.rota}/${encodeURIComponent(id)}/versoes/${numero}`);
  }

  excluir(id: string, revisao: string | null, token: string, chave: string) {
    const ficha = `${this.rota}/${encodeURIComponent(id)}`;
    const url = revisao ? `${ficha}/versoes/${encodeURIComponent(revisao)}` : ficha;
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.delete<void>(url, {
          headers: { 'X-XSRF-TOKEN': requestToken, 'If-Match': `"${token}"`, 'Idempotency-Key': chave },
        }),
      ),
    );
  }

  salvar(id: string | null, dados: ReceitaInput) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => {
        const options = { headers: { 'X-XSRF-TOKEN': requestToken } };
        return id
          ? this.http.put<DetalheReceita>(`${this.rota}/${encodeURIComponent(id)}`, dados, options)
          : this.http.post<DetalheReceita>(this.rota, dados, options);
      }),
    );
  }

  alterarStatus(id: string, ativa: boolean, token: string) {
    return this.csrf
      .getRequestToken()
      .pipe(
        switchMap(({ requestToken }) =>
          this.http.patch<DetalheReceita>(
            `${this.rota}/${encodeURIComponent(id)}/status`,
            { ativa, token },
            { headers: { 'X-XSRF-TOKEN': requestToken } },
          ),
        ),
      );
  }
}
