import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import {
  CatalogoPublicoPagina,
  CatalogoPublicoService,
  ProdutoCatalogoResumo,
} from './catalogo-publico.service';
import { CatalogoProdutosComponent } from './catalogo-produtos.component';

describe('CatalogoProdutosComponent', () => {
  const product: ProdutoCatalogoResumo = {
    id: 'product-1',
    nome: 'Sabonete de lavanda',
    categoriaId: 'category-1',
    categoriaNome: 'Sabonetes Artesanais',
    fotoPrincipal: {
      id: 'image-1',
      conteudoUrl: '/api/catalogo/produtos/product-1/imagens/image-1/conteudo',
    },
    precoMinimo: 18,
    precoMaximo: 25,
    unidadesVenda: ['UN'],
    medidas: [],
  };
  const categories = [
    { id: 'category-1', nome: 'Sabonetes Artesanais', descricao: null },
    { id: 'category-2', nome: 'Velas Aromáticas', descricao: null },
  ];

  let listarProdutos: ReturnType<typeof vi.fn<CatalogoPublicoService['listarProdutos']>>;
  let pageElement: HTMLElement;
  let harness: RouterTestingHarness;

  function page(
    items: ProdutoCatalogoResumo[],
    pagina = 1,
    totalPaginas = items.length ? 1 : 0,
  ): CatalogoPublicoPagina {
    return {
      itens: items,
      pagina,
      itensPorPagina: 12,
      totalItens: totalPaginas > 1 ? 13 : items.length,
      totalPaginas,
    };
  }

  beforeEach(() => {
    listarProdutos = vi.fn<CatalogoPublicoService['listarProdutos']>()
      .mockReturnValue(of(page([product])));
  });

  async function createComponent(url = '/produtos'): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [CatalogoProdutosComponent],
      providers: [
        provideRouter([{ path: 'produtos', component: CatalogoProdutosComponent }]),
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias: vi.fn(() => of(categories)),
            listarProdutos,
          },
        },
      ],
    }).compileComponents();
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, CatalogoProdutosComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    pageElement = harness.routeNativeElement!;
  }

  it('loads products with the route search and category filters', async () => {
    await createComponent('/produtos?busca=lavanda&categoriaId=category-1');

    expect(listarProdutos).toHaveBeenCalledWith({
      busca: 'lavanda',
      categoriaId: 'category-1',
      pagina: 1,
      itensPorPagina: 12,
    });
    expect(pageElement.textContent).toContain('Sabonete de lavanda');
    expect(pageElement.textContent).toContain('Resultados para “lavanda”');
  });

  it('requests the next real API page and appends its products', async () => {
    listarProdutos
      .mockReturnValueOnce(of(page([product], 1, 2)))
      .mockReturnValueOnce(of(page([{ ...product, id: 'product-2', nome: 'Vela artesanal' }], 2, 2)));
    await createComponent();

    const button = pageElement.querySelector('.catalog-load-more button') as HTMLButtonElement;
    button.click();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(listarProdutos).toHaveBeenLastCalledWith({
      pagina: 2,
      itensPorPagina: 12,
    });
    expect(pageElement.textContent).toContain('Sabonete de lavanda');
    expect(pageElement.textContent).toContain('Vela artesanal');
  });

  it('presents a recoverable error state', async () => {
    listarProdutos.mockReturnValueOnce(throwError(() => new Error('network')));
    await createComponent();
    expect(pageElement.textContent).toContain('Não foi possível carregar os produtos agora');
    expect(pageElement.querySelector('.catalog-state--error button')?.textContent)
      .toContain('Tentar novamente');
  });

  it('shows loading feedback while the public endpoint is pending', async () => {
    const pending = new Subject<CatalogoPublicoPagina>();
    listarProdutos.mockReturnValueOnce(pending);
    await createComponent();
    expect(pageElement.textContent).toContain('Carregando produtos...');

    pending.next(page([product]));
    pending.complete();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    expect(pageElement.textContent).toContain('Sabonete de lavanda');
  });

  it('presents an empty result state', async () => {
    listarProdutos.mockReturnValue(of(page([])));
    await createComponent();
    expect(pageElement.textContent).toContain('Nenhum produto encontrado');
  });

  it('cancels the previous search when the route filters change', async () => {
    const obsoleteRequest = new Subject<CatalogoPublicoPagina>();
    listarProdutos
      .mockReturnValueOnce(obsoleteRequest)
      .mockReturnValueOnce(of(page([{ ...product, nome: 'Resultado atualizado' }])));
    await createComponent('/produtos?busca=antiga');

    await TestBed.inject(Router).navigateByUrl('/produtos?busca=nova');
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(obsoleteRequest.observed).toBe(false);
    expect(pageElement.textContent).toContain('Resultado atualizado');
  });

  it('does not render a purchase action or claim stock availability', async () => {
    await createComponent();
    expect(pageElement.querySelector('button.comprar, a.comprar')).toBeNull();
    expect(pageElement.textContent.toLowerCase()).not.toContain('em estoque');
  });
});
