import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, switchMap } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export interface AdminProdutoImagem {
  id: string;
  produtoId: string;
  nomeOriginal: string;
  tipoConteudo: string;
  tamanhoBytes: number;
  larguraPixels: number;
  alturaPixels: number;
  ordemExibicao: number;
  principal: boolean;
  varianteIds: string[];
  criadoEmUtc: string;
  conteudoUrl: string;
}

export interface AdminProdutoImagemRemocao {
  removidaDoCatalogo: boolean;
  limpezaFisicaConcluida: boolean;
  mensagem: string;
}

@Injectable({ providedIn: 'root' })
export class ImagensProdutoService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly basePath = '/api/admin/produtos';

  listar(produtoId: string): Observable<AdminProdutoImagem[]> {
    return this.http.get<AdminProdutoImagem[]>(`${this.produtoPath(produtoId)}/imagens`);
  }

  consultar(produtoId: string, imagemId: string): Observable<AdminProdutoImagem> {
    return this.http.get<AdminProdutoImagem>(this.imagemPath(produtoId, imagemId));
  }

  conteudo(produtoId: string, imagemId: string): Observable<Blob> {
    return this.http.get(this.conteudoPath(produtoId, imagemId), {
      responseType: 'blob',
    });
  }

  adicionar(
    produtoId: string,
    arquivo: File,
    varianteIds: readonly string[] = [],
  ): Observable<AdminProdutoImagem> {
    const formData = new FormData();
    formData.append('arquivo', arquivo, arquivo.name);
    for (const varianteId of varianteIds) {
      formData.append('varianteIds', varianteId);
    }

    return this.withCsrf((requestToken) =>
      this.http.post<AdminProdutoImagem>(`${this.produtoPath(produtoId)}/imagens`, formData, {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
      }),
    );
  }

  definirPrincipal(produtoId: string, imagemId: string): Observable<AdminProdutoImagem[]> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminProdutoImagem[]>(
        `${this.imagemPath(produtoId, imagemId)}/principal`,
        null,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  reordenar(produtoId: string, imagemIds: readonly string[]): Observable<AdminProdutoImagem[]> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminProdutoImagem[]>(
        `${this.produtoPath(produtoId)}/imagens/ordem`,
        { imagemIds },
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  associarVariante(
    produtoId: string,
    imagemId: string,
    varianteId: string,
  ): Observable<AdminProdutoImagem[]> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminProdutoImagem[]>(
        `${this.imagemPath(produtoId, imagemId)}/variantes/${encodeURIComponent(varianteId)}`,
        null,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  desassociarVariante(
    produtoId: string,
    imagemId: string,
    varianteId: string,
  ): Observable<AdminProdutoImagem[]> {
    return this.withCsrf((requestToken) =>
      this.http.delete<AdminProdutoImagem[]>(
        `${this.imagemPath(produtoId, imagemId)}/variantes/${encodeURIComponent(varianteId)}`,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  remover(
    produtoId: string,
    imagemId: string,
  ): Observable<HttpResponse<AdminProdutoImagemRemocao>> {
    return this.withCsrf((requestToken) =>
      this.http.delete<AdminProdutoImagemRemocao>(this.imagemPath(produtoId, imagemId), {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
        observe: 'response',
      }),
    );
  }

  private produtoPath(produtoId: string): string {
    return `${this.basePath}/${encodeURIComponent(produtoId)}`;
  }

  private imagemPath(produtoId: string, imagemId: string): string {
    return `${this.produtoPath(produtoId)}/imagens/${encodeURIComponent(imagemId)}`;
  }

  private conteudoPath(produtoId: string, imagemId: string): string {
    return `${this.imagemPath(produtoId, imagemId)}/conteudo`;
  }

  private withCsrf<T>(request: (requestToken: string) => Observable<T>): Observable<T> {
    return this.csrf.getRequestToken().pipe(switchMap(({ requestToken }) => request(requestToken)));
  }
}
