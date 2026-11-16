import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';

export interface CategoriaCatalogoPublico {
  id: string;
  nome: string;
  descricao: string | null;
}

export interface ProdutoCatalogoMedida {
  opcaoId: string;
  pesoLiquidoGramas: number | null;
  volumeLiquidoMililitros: number | null;
}

export interface ProdutoCatalogoResumo {
  id: string;
  nome: string;
  categoriaId: string;
  categoriaNome: string;
  fotoPrincipal: {
    id: string;
    conteudoUrl: string;
  };
  precoMinimo: number;
  precoMaximo: number;
  unidadesVenda: string[];
  medidas: ProdutoCatalogoMedida[];
}

export interface CatalogoPublicoPagina {
  itens: ProdutoCatalogoResumo[];
  pagina: number;
  itensPorPagina: number;
  totalItens: number;
  totalPaginas: number;
}

export interface CatalogoPublicoFiltro {
  busca?: string;
  categoriaId?: string;
  pagina?: number;
  itensPorPagina?: number;
}

@Injectable({ providedIn: 'root' })
export class CatalogoPublicoService {
  private readonly http = inject(HttpClient);
  private categoriasCache?: Observable<CategoriaCatalogoPublico[]>;

  listarCategorias(): Observable<CategoriaCatalogoPublico[]> {
    this.categoriasCache ??= this.http
      .get<CategoriaCatalogoPublico[]>('/api/catalogo/categorias')
      .pipe(shareReplay({ bufferSize: 1, refCount: false }));
    return this.categoriasCache;
  }

  listarProdutos(filtro: CatalogoPublicoFiltro = {}): Observable<CatalogoPublicoPagina> {
    let params = new HttpParams();
    const busca = filtro.busca?.trim();
    if (busca) {
      params = params.set('busca', busca);
    }
    if (filtro.categoriaId) {
      params = params.set('categoriaId', filtro.categoriaId);
    }
    if (filtro.pagina !== undefined) {
      params = params.set('pagina', filtro.pagina);
    }
    if (filtro.itensPorPagina !== undefined) {
      params = params.set('itensPorPagina', filtro.itensPorPagina);
    }

    return this.http.get<CatalogoPublicoPagina>('/api/catalogo/produtos', { params });
  }
}
