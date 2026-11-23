import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnInit,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  ActivatedRoute,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
} from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, filter, finalize, of } from 'rxjs';
import { validarDestinoLoja } from '../core/auth/destino-login-cliente';
import { AuthService, AuthSession } from '../core/auth/auth.service';
import { CarrinhoService } from './carrinho.service';
import { CategoriaCatalogoPublico, CatalogoPublicoService } from './catalogo-publico.service';
import { StorefrontContext } from './storefront-context';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, RouterLinkActive],
  selector: 'app-store-header',
  styleUrl: './store-header.component.css',
  templateUrl: './store-header.component.html',
})
export class StoreHeaderComponent implements OnInit {
  protected readonly contexto = inject(StorefrontContext);
  protected readonly categories = signal<CategoriaCatalogoPublico[]>([]);
  protected readonly categoryError = signal('');
  protected readonly session = signal<AuthSession | null>(null);
  protected readonly sessionLoading = signal(true);
  protected readonly sessionError = signal('');
  protected readonly signingOut = signal(false);
  protected readonly accountError = signal('');
  protected readonly searchTerm = signal('');
  protected readonly quantidadeCarrinho = this.contexto.preview ? () => 0 : inject(CarrinhoService).quantidadeTotal;

  private readonly authService = this.contexto.preview ? null : inject(AuthService);
  private readonly catalogService = inject(CatalogoPublicoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly destinoLogin = signal(this.router.serializeUrl(validarDestinoLoja(this.router.url)));

  @ViewChild('categoryMenu') private categoryMenuElement?: ElementRef<HTMLDetailsElement>;
  @ViewChild('mobileNav') private mobileNav?: ElementRef<HTMLDetailsElement>;
  @ViewChild('mobileCategories') private mobileCategories?: ElementRef<HTMLDetailsElement>;

  ngOnInit(): void {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(event => {
        this.destinoLogin.set(this.router.serializeUrl(validarDestinoLoja(event.urlAfterRedirects)));
        this.closeNavigation();
      });

    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.searchTerm.set(params.get('busca') ?? ''));

    this.catalogService
      .listarCategorias()
      .pipe(
        catchError(() => {
          this.categoryError.set('Não foi possível carregar as categorias agora.');
          return of([]);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (categories) => this.categories.set(categories),
      });

    this.authService?.obterSessao()
      .pipe(
        catchError((error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 403) {
            return of(null);
          }

          this.sessionError.set(
            'Não foi possível verificar sua sessão. Você ainda pode entrar ou criar uma conta.',
          );
          return of(null);
        }),
        finalize(() => this.sessionLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((session) => this.session.set(session));
  }

  protected closeNavigation(): void {
    if (this.categoryMenuElement) {
      this.categoryMenuElement.nativeElement.open = false;
    }
    if (this.mobileNav) {
      this.mobileNav.nativeElement.open = false;
    }
    if (this.mobileCategories) {
      this.mobileCategories.nativeElement.open = false;
    }
  }

  @HostListener('document:click', ['$event'])
  protected closeNavigationOnOutsideClick(event: MouseEvent): void {
    const target = event.target;
    if (!(target instanceof Node)) {
      return;
    }

    if (!this.categoryMenuElement?.nativeElement.contains(target)) {
      if (this.categoryMenuElement) {
        this.categoryMenuElement.nativeElement.open = false;
      }
    }

    if (!this.mobileNav?.nativeElement.contains(target)) {
      if (this.mobileNav) {
        this.mobileNav.nativeElement.open = false;
      }
      if (this.mobileCategories) {
        this.mobileCategories.nativeElement.open = false;
      }
    }
  }

  @HostListener('document:keydown', ['$event'])
  protected closeNavigationOnEscape(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }

    const closeCategoryMenu = this.categoryMenuElement?.nativeElement.open ?? false;
    const closeMobileMenu = this.mobileNav?.nativeElement.open ?? false;
    if (!closeCategoryMenu && !closeMobileMenu) {
      return;
    }

    event.preventDefault();
    this.closeNavigation();
    if (closeCategoryMenu) {
      this.categoryMenuElement?.nativeElement.querySelector('summary')?.focus();
    } else {
      this.mobileNav?.nativeElement.querySelector('summary')?.focus();
    }
  }

  protected search(): void {
    const categoryId = this.route.snapshot.queryParamMap.get('categoriaId');
    void this.router.navigate(this.contexto.link('produtos'), {
      queryParams: {
        busca: this.searchTerm().trim() || null,
        categoriaId: categoryId,
      },
    });
  }

  protected logout(): void {
    if (!this.authService || this.signingOut()) {
      return;
    }

    this.signingOut.set(true);
    this.accountError.set('');
    this.authService
      .logout()
      .pipe(
        finalize(() => this.signingOut.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.session.set(null);
          void this.router.navigateByUrl('/');
        },
        error: () => {
          this.accountError.set('Não foi possível encerrar a sessão. Tente novamente.');
        },
      });
  }
}
