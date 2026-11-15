import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, switchMap } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export interface AdminCategoria {
  id: string;
  nome: string;
  descricao: string | null;
  ativa: boolean;
  ordemExibicao: number;
}

export interface CategoriaWriteRequest {
  nome: string;
  descricao: string | null;
  ordemExibicao: number;
}

export interface CategoriaStatusRequest {
  ativa: boolean;
}

@Injectable({ providedIn: 'root' })
export class CategoriasService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  listar(): Observable<AdminCategoria[]> {
    return this.http.get<AdminCategoria[]>('/api/admin/categorias');
  }

  obter(id: string): Observable<AdminCategoria> {
    return this.http.get<AdminCategoria>(`/api/admin/categorias/${encodeURIComponent(id)}`);
  }

  criar(request: CategoriaWriteRequest): Observable<AdminCategoria> {
    return this.withCsrf((requestToken) =>
      this.http.post<AdminCategoria>('/api/admin/categorias', request, {
        headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
      }),
    );
  }

  editar(id: string, request: CategoriaWriteRequest): Observable<AdminCategoria> {
    return this.withCsrf((requestToken) =>
      this.http.put<AdminCategoria>(
        `/api/admin/categorias/${encodeURIComponent(id)}`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  alterarStatus(id: string, request: CategoriaStatusRequest): Observable<AdminCategoria> {
    return this.withCsrf((requestToken) =>
      this.http.patch<AdminCategoria>(
        `/api/admin/categorias/${encodeURIComponent(id)}/status`,
        request,
        { headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }) },
      ),
    );
  }

  private withCsrf<T>(request: (requestToken: string) => Observable<T>): Observable<T> {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => request(requestToken)),
    );
  }
}
