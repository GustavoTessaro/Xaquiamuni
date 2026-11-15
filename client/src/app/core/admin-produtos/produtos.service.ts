import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, switchMap } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export interface AdminProdutoVariante {
  id: string;
  identificacao: string;
  unidadeVenda: string;
  precoVenda: number;
  ativa: boolean;
  padrao: boolean;
}

export interface AdminProduto {
  id: string;
  nome: string;
  descricao: string | null;
  categoriaId: string;
  categoriaNome: string;
  categoriaAtiva: boolean;
  publicado: boolean;
  variantes: AdminProdutoVariante[];
}

export interface AdminProdutoFiltro {
  busca?: string;
  categoriaId?: string;
  publicado?: boolean;
}

export interface AdminProdutoVarianteRequest {
  identificacao: string;
  unidadeVenda: string;
  precoVenda: number;
}

export interface AdminProdutoCadastroRequest {
  nome: string;
  descricao: string | null;
  categoriaId: string;
  variantePadrao: AdminProdutoVarianteRequest;
  variantes: AdminProdutoVarianteRequest[];
}

export interface AdminProdutoEdicaoRequest {
  nome: string;
  descricao: string | null;
  categoriaId: string;
}

export interface AdminProdutoVarianteStatusRequest {
  ativa: boolean;
  novoPadraoId?: string;
}

export interface AdminProdutoPublicacaoRequest {
  publicado: boolean;
}

@Injectable({ providedIn: 'root' })
export class ProdutosService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly basePath = '/api/admin/produtos';

  listar(filtro: AdminProdutoFiltro = {}): Observable<AdminProduto[]> {
    let params = new HttpParams();
    const busca = filtro.busca?.trim();
    if (busca) {
      params = params.set('busca', busca);
    }
    if (filtro.categoriaId) {
      params = params.set('categoriaId', filtro.categoriaId);
    }
    if (filtro.publicado !== undefined) {
      params = params.set('publicado', filtro.publicado);
    }

    return this.http.get<AdminProduto[]>(this.basePath, { params });
  }

  obter(id: string): Observable<AdminProduto> {
    return this.http.get<AdminProduto>(`${this.basePath}/${encodeURIComponent(id)}`);
  }

  criar(request: AdminProdutoCadastroRequest): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.post<AdminProduto>(this.basePath, request, {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
      }),
    );
  }

  editar(id: string, request: AdminProdutoEdicaoRequest): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminProduto>(`${this.basePath}/${encodeURIComponent(id)}`, request, {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
      }),
    );
  }

  adicionarVariante(id: string, request: AdminProdutoVarianteRequest): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.post<AdminProduto>(
        `${this.basePath}/${encodeURIComponent(id)}/variantes`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  editarVariante(
    id: string,
    varianteId: string,
    request: AdminProdutoVarianteRequest,
  ): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminProduto>(
        `${this.basePath}/${encodeURIComponent(id)}/variantes/${encodeURIComponent(varianteId)}`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  definirVariantePadrao(id: string, varianteId: string): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminProduto>(
        `${this.basePath}/${encodeURIComponent(id)}/variantes/${encodeURIComponent(varianteId)}/padrao`,
        null,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  alterarStatusVariante(
    id: string,
    varianteId: string,
    request: AdminProdutoVarianteStatusRequest,
  ): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.patch<AdminProduto>(
        `${this.basePath}/${encodeURIComponent(id)}/variantes/${encodeURIComponent(varianteId)}/status`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  alterarPublicacao(id: string, request: AdminProdutoPublicacaoRequest): Observable<AdminProduto> {
    return this.withCsrf((requestToken) =>
      this.http.patch<AdminProduto>(
        `${this.basePath}/${encodeURIComponent(id)}/publicacao`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  private withCsrf<T>(request: (requestToken: string) => Observable<T>): Observable<T> {
    return this.csrf.getRequestToken().pipe(switchMap(({ requestToken }) => request(requestToken)));
  }
}
