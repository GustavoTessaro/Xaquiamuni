import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, distinctUntilChanged, map, of, switchMap } from 'rxjs';
import {
  CatalogoPublicoFiltro,
  CatalogoPublicoPagina,
  CatalogoPublicoRetorno,
  CatalogoPublicoService,
  CategoriaCatalogoPublico,
  ProdutoCatalogoResumo,
} from './catalogo-publico.service';
import { PublicProductCardComponent } from './public-product-card.component';
import { StorefrontContext } from './storefront-context';

interface ProductRequest {
  generation: number;
  filter: CatalogoPublicoFiltro;
  page: number;
  append: boolean;
}

interface ProductRequestResult {
  request: ProductRequest;
  response: CatalogoPublicoPagina | null;
  failed: boolean;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PublicProductCardComponent, RouterLink],
  selector: 'app-catalogo-produtos',
  styleUrl: './catalogo-produtos.component.css',
  templateUrl: './catalogo-produtos.component.html',
})
export class CatalogoProdutosComponent implements OnInit {
  protected readonly contexto = inject(StorefrontContext);
  readonly homePage = input(false);
  protected readonly products = signal<ProdutoCatalogoResumo[]>([]);
  protected readonly categories = signal<CategoriaCatalogoPublico[]>([]);
  protected readonly selectedCategoryId = signal('');
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly categoryError = signal('');
  protected readonly currentSearch = signal('');
  protected readonly page = signal(1);
  protected readonly totalPages = signal(0);
  protected readonly totalItems = signal(0);
  protected readonly hasProducts = signal(false);

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalogService = inject(CatalogoPublicoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<ProductRequest>();
  private currentFilter: CatalogoPublicoFiltro = {};
  private generation = 0;
  private retryRequest: ProductRequest | null = null;

  ngOnInit(): void {
    this.loadCategories();

    this.requests
      .pipe(
        switchMap((request) =>
          this.catalogService
            .listarProdutos({
              ...request.filter,
              pagina: request.page,
              itensPorPagina: this.homePage() ? 8 : 12,
            })
            .pipe(
              map((response) => ({ request, response, failed: false })),
              catchError(() => of({ request, response: null, failed: true })),
            ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyResult(result));

    this.route.queryParamMap
      .pipe(
        map((params) => ({
          busca: params.get('busca')?.trim() || undefined,
          categoriaId: params.get('categoriaId') || undefined,
        })),
        distinctUntilChanged(
          (previous, current) =>
            previous.busca === current.busca && previous.categoriaId === current.categoriaId,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((filter) => {
        this.currentFilter = filter;
        this.currentSearch.set(filter.busca ?? '');
        this.selectedCategoryId.set(filter.categoriaId ?? '');
        this.products.set([]);
        this.page.set(1);
        this.totalPages.set(0);
        this.totalItems.set(0);
        this.hasProducts.set(false);
        this.errorMessage.set('');
        this.loading.set(true);
        this.loadingMore.set(false);
        this.retryRequest = null;
        this.requestPage(1, false);
      });
  }

  protected selectCategory(categoryId: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { categoriaId: categoryId || null },
      queryParamsHandling: 'merge',
      fragment: this.homePage() ? 'produtos' : undefined,
    });
  }

  protected retryCategories(): void {
    this.categoryError.set('');
    this.loadCategories();
  }

  protected loadMore(): void {
    if (this.loading() || this.loadingMore() || this.page() >= this.totalPages()) {
      return;
    }
    this.requestPage(this.page() + 1, true);
  }

  protected retry(): void {
    if (this.retryRequest) {
      this.requestPage(this.retryRequest.page, this.retryRequest.append);
      return;
    }
    this.requestPage(1, false);
  }

  protected get title(): string {
    return this.homePage() ? 'Nossos produtos' : 'Produtos artesanais';
  }

  protected get subtitle(): string {
    if (this.currentFilter.busca) {
      return `Resultados para “${this.currentFilter.busca}”`;
    }
    const category = this.categories().find((item) => item.id === this.currentFilter.categoriaId);
    return category ? category.nome : 'Feitos à mão para acompanhar seus momentos de cuidado.';
  }

  protected get showCatalogLink(): boolean {
    return this.homePage() && this.totalItems() > 0;
  }

  protected get catalogReturn(): CatalogoPublicoRetorno | null {
    return this.homePage()
      ? null
      : {
          busca: this.currentFilter.busca,
          categoriaId: this.currentFilter.categoriaId,
        };
  }

  private requestPage(page: number, append: boolean): void {
    const request: ProductRequest = {
      generation: ++this.generation,
      filter: { ...this.currentFilter },
      page,
      append,
    };
    this.retryRequest = request;
    this.errorMessage.set('');
    if (append) {
      this.loadingMore.set(true);
    } else {
      this.loading.set(true);
    }
    this.requests.next(request);
  }

  private loadCategories(): void {
    this.catalogService
      .listarCategorias()
      .pipe(
        catchError(() => {
          this.categoryError.set('Não foi possível carregar as categorias.');
          return of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((categories) => this.categories.set(categories));
  }

  private applyResult(result: ProductRequestResult): void {
    if (result.request.generation !== this.generation) {
      return;
    }

    this.loading.set(false);
    this.loadingMore.set(false);
    if (result.failed || result.response === null) {
      this.errorMessage.set('Não foi possível carregar os produtos agora. Tente novamente.');
      return;
    }

    const response = result.response;
    this.products.set(
      result.request.append ? [...this.products(), ...response.itens] : response.itens,
    );
    this.page.set(response.pagina);
    this.totalPages.set(response.totalPaginas);
    this.totalItems.set(response.totalItens);
    this.hasProducts.set(response.itens.length > 0 || this.products().length > 0);
    this.retryRequest = null;
  }
}
