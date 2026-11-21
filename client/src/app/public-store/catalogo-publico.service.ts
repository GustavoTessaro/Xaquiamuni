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

export interface PrecoCatalogo {
  regularCentavos: string;
  finalCentavos: string;
  economiaCentavos: string;
  offPontosBase: number;
  promocaoId: string | null;
}

export interface PrecoExtremoCatalogo {
  varianteId: string;
  preco: PrecoCatalogo;
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
  menorPreco?: PrecoExtremoCatalogo;
  maiorPreco?: PrecoExtremoCatalogo;
  unidadesVenda: string[];
  medidas: ProdutoCatalogoMedida[];
  disponivel: boolean;
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

export type CatalogoPublicoRetorno = Pick<CatalogoPublicoFiltro, 'busca' | 'categoriaId'>;

export interface ProdutoCatalogoImagem {
  id: string;
  conteudoUrl: string;
  ordemExibicao: number;
  principal: boolean;
  opcaoIds: string[];
}

export interface ProdutoCatalogoValor {
  id: string;
  nome: string;
  valorMedidaCanonico: number | null;
}

export interface ProdutoCatalogoCaracteristica {
  id: string;
  nome: string;
  tipoMedidaFisica: string | null;
  valores: ProdutoCatalogoValor[];
}

export interface ProdutoCatalogoCaracteristicaSelecao {
  caracteristicaId: string;
  caracteristicaNome: string;
  valorId: string;
  valorNome: string;
  valorMedidaCanonico: number | null;
}

export interface ProdutoCatalogoOpcao {
  id: string;
  identificacao: string | null;
  unidadeVenda: string;
  precoVenda: number;
  preco?: PrecoCatalogo;
  principal: boolean;
  pesoLiquidoGramas: number | null;
  volumeLiquidoMililitros: number | null;
  caracteristicas: ProdutoCatalogoCaracteristicaSelecao[];
  disponivel: boolean;
}

export interface ProdutoCatalogoDetalhe {
  id: string;
  nome: string;
  descricao: string | null;
  categoriaId: string;
  categoriaNome: string;
  requerSelecaoDeOpcao: boolean;
  opcaoPrincipalId: string;
  fotoPrincipal: ProdutoCatalogoImagem;
  galeria: ProdutoCatalogoImagem[];
  caracteristicas: ProdutoCatalogoCaracteristica[];
  opcoes: ProdutoCatalogoOpcao[];
  disponivel: boolean;
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

  consultarProduto(produtoId: string): Observable<ProdutoCatalogoDetalhe> {
    return this.http.get<ProdutoCatalogoDetalhe>(
      `/api/catalogo/produtos/${encodeURIComponent(produtoId)}`,
    );
  }
}
