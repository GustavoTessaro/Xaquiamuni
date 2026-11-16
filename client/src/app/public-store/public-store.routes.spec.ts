import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withInMemoryScrolling } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../app.routes';
import { AuthService } from '../core/auth/auth.service';
import {
  CatalogoPublicoService,
  CategoriaCatalogoPublico,
  ProdutoCatalogoDetalhe,
  ProdutoCatalogoResumo,
} from './catalogo-publico.service';
import { StoreLayoutComponent } from './store-layout.component';

describe('public store routes', () => {
  it('allows an anonymous visitor to open the homepage and catalog', async () => {
    const categories: CategoriaCatalogoPublico[] = [
      { id: 'category-1', nome: 'Sabonetes Artesanais', descricao: null },
    ];
    const listarCategorias = vi.fn(() => of(categories));
    const listarProdutos = vi.fn(() =>
      of({
        itens: [],
        pagina: 1,
        itensPorPagina: 8,
        totalItens: 0,
        totalPaginas: 0,
      }),
    );
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of(null)),
            logout: vi.fn(() => of(undefined)),
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias,
            listarProdutos,
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', StoreLayoutComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/');
    expect(harness.fixture.nativeElement.textContent).toContain('Transforme pequenos momentos');
    expect(harness.fixture.nativeElement.textContent).toContain('Criar conta');
    expect(harness.fixture.nativeElement.textContent).toContain('Sabonetes Artesanais');
    expect(listarProdutos).toHaveBeenCalledWith({
      pagina: 1,
      itensPorPagina: 8,
    });

    const categoryCard = harness.fixture.nativeElement.querySelector(
      '.category-card',
    ) as HTMLAnchorElement;
    categoryCard.click();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    expect(TestBed.inject(Router).url).toContain('categoriaId=category-1');
    expect(listarProdutos).toHaveBeenLastCalledWith({
      categoriaId: 'category-1',
      pagina: 1,
      itensPorPagina: 8,
    });

    const searchInput = harness.fixture.nativeElement.querySelector(
      '#store-search',
    ) as HTMLInputElement;
    searchInput.value = 'lavanda';
    searchInput.dispatchEvent(new Event('input', { bubbles: true }));
    harness.fixture.detectChanges();
    const searchForm = harness.fixture.nativeElement.querySelector(
      '.store-search',
    ) as HTMLFormElement;
    searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    expect(TestBed.inject(Router).url).toContain('/produtos?busca=lavanda&categoriaId=category-1');
    expect(listarProdutos).toHaveBeenLastCalledWith({
      busca: 'lavanda',
      categoriaId: 'category-1',
      pagina: 1,
      itensPorPagina: 12,
    });

    await harness.navigateByUrl('/produtos', StoreLayoutComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/produtos');
    expect(harness.fixture.nativeElement.textContent).toContain('Produtos artesanais');
    expect(listarCategorias).toHaveBeenCalled();
  });

  it('navigates from a filtered catalog to a public product and back with filters preserved', async () => {
    const product: ProdutoCatalogoResumo = {
      id: 'product-1',
      nome: 'Sabonete artesanal',
      categoriaId: 'category-1',
      categoriaNome: 'Sabonetes Artesanais',
      fotoPrincipal: { id: 'image-1', conteudoUrl: '/public-images/image-1' },
      precoMinimo: 18,
      precoMaximo: 18,
      unidadesVenda: ['UN'],
      medidas: [{ opcaoId: 'option-1', pesoLiquidoGramas: 100, volumeLiquidoMililitros: null }],
    };
    const details: ProdutoCatalogoDetalhe = {
      id: product.id,
      nome: product.nome,
      descricao: null,
      categoriaId: product.categoriaId,
      categoriaNome: product.categoriaNome,
      requerSelecaoDeOpcao: false,
      opcaoPrincipalId: 'option-1',
      fotoPrincipal: {
        ...product.fotoPrincipal,
        ordemExibicao: 1,
        principal: true,
        opcaoIds: [],
      },
      galeria: [
        {
          ...product.fotoPrincipal,
          ordemExibicao: 1,
          principal: true,
          opcaoIds: [],
        },
      ],
      caracteristicas: [],
      opcoes: [
        {
          id: 'option-1',
          identificacao: null,
          unidadeVenda: 'UN',
          precoVenda: 18,
          principal: true,
          pesoLiquidoGramas: 100,
          volumeLiquidoMililitros: null,
          caracteristicas: [],
        },
      ],
    };
    const listarProdutos = vi.fn(() =>
      of({
        itens: [product],
        pagina: 1,
        itensPorPagina: 12,
        totalItens: 1,
        totalPaginas: 1,
      }),
    );
    const consultarProduto = vi.fn(() => of(details));
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of(null)),
            logout: vi.fn(() => of(undefined)),
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias: vi.fn(() => of([])),
            listarProdutos,
            consultarProduto,
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(
      '/produtos?busca=sabonete&categoriaId=category-1',
      StoreLayoutComponent,
    );
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    const card = harness.fixture.nativeElement.querySelector('.product-card') as HTMLAnchorElement;
    card.click();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe(
      '/produtos/product-1?busca=sabonete&categoriaId=category-1',
    );
    expect(harness.fixture.nativeElement.textContent).toContain('Sabonete artesanal');
    expect(harness.fixture.nativeElement.querySelectorAll('main')).toHaveLength(1);
    expect(consultarProduto).toHaveBeenCalledWith('product-1');

    (
      harness.fixture.nativeElement.querySelector('.product-detail__back') as HTMLAnchorElement
    ).click();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/produtos?busca=sabonete&categoriaId=category-1');
    expect(listarProdutos).toHaveBeenLastCalledWith({
      busca: 'sabonete',
      categoriaId: 'category-1',
      pagina: 1,
      itensPorPagina: 12,
    });
  });

  it('closes the categories menu after selecting an API category', async () => {
    const category: CategoriaCatalogoPublico = {
      id: 'category-1',
      nome: 'Sabonetes Artesanais',
      descricao: null,
    };
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes, withInMemoryScrolling({ anchorScrolling: 'enabled' })),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of(null)),
            logout: vi.fn(() => of(undefined)),
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias: vi.fn(() => of([category])),
            listarProdutos: vi.fn(() =>
              of({
                itens: [],
                pagina: 1,
                itensPorPagina: 12,
                totalItens: 0,
                totalPaginas: 0,
              }),
            ),
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', StoreLayoutComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    const categoryMenu = harness.fixture.nativeElement.querySelector(
      '.store-nav__categories',
    ) as HTMLDetailsElement;
    const trigger = categoryMenu.querySelector('summary') as HTMLElement;
    trigger.click();
    harness.fixture.detectChanges();
    const categoryLink = harness.fixture.nativeElement.querySelector(
      '.store-nav__dropdown a[href*="categoriaId"]',
    ) as HTMLAnchorElement;
    categoryLink.click();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/produtos?categoriaId=category-1');
    expect(categoryMenu.open).toBe(false);
  });

  it('navigates from the catalog to the homepage fragment', async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideRouter(routes, withInMemoryScrolling({ anchorScrolling: 'enabled' })),
        {
          provide: AuthService,
          useValue: {
            obterSessao: vi.fn(() => of(null)),
            logout: vi.fn(() => of(undefined)),
          },
        },
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias: vi.fn(() => of([])),
            listarProdutos: vi.fn(() =>
              of({
                itens: [],
                pagina: 1,
                itensPorPagina: 12,
                totalItens: 0,
                totalPaginas: 0,
              }),
            ),
          },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/produtos', StoreLayoutComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    const aboutLink = harness.fixture.nativeElement.querySelector(
      '.store-nav a[href="/#sobre-nos"]',
    ) as HTMLAnchorElement;
    aboutLink.click();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/#sobre-nos');
    expect(harness.fixture.nativeElement.querySelector('#sobre-nos')).not.toBeNull();
  });
});
