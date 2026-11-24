import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap, timeout } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export interface DadosFornecedor {
  nome: string;
  telefone: string | null;
  email: string | null;
  observacoes: string | null;
}

export interface Fornecedor extends DadosFornecedor {
  id: string;
  ativo: boolean;
  versao?: string;
}

@Injectable({ providedIn: 'root' })
export class FornecedoresService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly url = '/api/admin/fornecedores';

  listar() {
    return this.http.get<Fornecedor[]>(this.url);
  }

  obter(id: string) {
    return this.http.get<Fornecedor>(`${this.url}/${encodeURIComponent(id)}`);
  }

  salvar(id: string | null, dados: DadosFornecedor, versao?: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => {
        const options = { headers: { 'X-XSRF-TOKEN': requestToken, ...(versao && { 'If-Match': `"${versao}"` }) } };
        return id
          ? this.http.put<Fornecedor>(`${this.url}/${encodeURIComponent(id)}`, dados, options)
          : this.http.post<Fornecedor>(this.url, dados, options);
      }),
    );
  }

  excluir(id: string, versao: string, chave: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.delete<void>(`${this.url}/${encodeURIComponent(id)}`, {
        headers: { 'X-XSRF-TOKEN': requestToken, 'If-Match': `"${versao}"`, 'Idempotency-Key': chave },
      })),
      timeout(20_000),
    );
  }

  inativar(id: string) {
    return this.csrf
      .getRequestToken()
      .pipe(
        switchMap(({ requestToken }) =>
          this.http.patch<Fornecedor>(
            `${this.url}/${encodeURIComponent(id)}/status`,
            { ativo: false },
            { headers: { 'X-XSRF-TOKEN': requestToken } },
          ),
        ),
      );
  }
}
