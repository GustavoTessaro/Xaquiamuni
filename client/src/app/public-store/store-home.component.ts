import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { CategoriaCatalogoPublico, CatalogoPublicoService } from './catalogo-publico.service';
import { CatalogoProdutosComponent } from './catalogo-produtos.component';
import { StorefrontContext } from './storefront-context';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CatalogoProdutosComponent, RouterLink],
  selector: 'app-store-home',
  styleUrl: './store-home.component.css',
  templateUrl: './store-home.component.html',
})
export class StoreHomeComponent implements OnInit {
  protected readonly contexto = inject(StorefrontContext);
  protected readonly categories = signal<CategoriaCatalogoPublico[]>([]);
  protected readonly categoriesLoading = signal(true);
  protected readonly categoriesError = signal('');

  private readonly catalogService = inject(CatalogoPublicoService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.loadCategories();
  }

  protected retryCategories(): void {
    this.categoriesError.set('');
    this.categoriesLoading.set(true);
    this.loadCategories();
  }

  protected categoryIcon(name: string): 'soap' | 'candle' | 'generic' {
    const normalized = name
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLowerCase();
    if (normalized.includes('sabonete')) {
      return 'soap';
    }
    if (normalized.includes('vela')) {
      return 'candle';
    }
    return 'generic';
  }

  private loadCategories(): void {
    this.catalogService
      .listarCategorias()
      .pipe(
        catchError(() => {
          this.categoriesError.set('Não foi possível carregar as categorias agora.');
          this.categoriesLoading.set(false);
          return of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((categories) => {
        this.categories.set(categories);
        this.categoriesLoading.set(false);
      });
  }
}
