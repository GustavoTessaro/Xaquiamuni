import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CategoriaCatalogoPublico, CatalogoPublicoService } from './catalogo-publico.service';

describe('CatalogoPublicoService', () => {
  let service: CatalogoPublicoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CatalogoPublicoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads categories from the public endpoint and shares the cached response', () => {
    const categories: CategoriaCatalogoPublico[] = [
      { id: 'cat-1', nome: 'Sabonetes artesanais', descricao: null },
    ];
    let firstResult: CategoriaCatalogoPublico[] | undefined;
    let secondResult: CategoriaCatalogoPublico[] | undefined;

    service.listarCategorias().subscribe((result) => (firstResult = result));
    service.listarCategorias().subscribe((result) => (secondResult = result));

    const request = http.expectOne('/api/catalogo/categorias');
    expect(request.request.method).toBe('GET');
    request.flush(categories);

    expect(firstResult).toEqual(categories);
    expect(secondResult).toEqual(categories);
    http.expectNone('/api/catalogo/categorias');
  });

  it('sends search, category and pagination using the public contract', () => {
    service
      .listarProdutos({
        busca: '  lavanda  ',
        categoriaId: 'category-1',
        pagina: 2,
        itensPorPagina: 8,
      })
      .subscribe();

    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/catalogo/produtos' &&
        candidate.params.get('busca') === 'lavanda' &&
        candidate.params.get('categoriaId') === 'category-1' &&
        candidate.params.get('pagina') === '2' &&
        candidate.params.get('itensPorPagina') === '8',
    );
    expect(request.request.method).toBe('GET');
    request.flush({
      itens: [],
      pagina: 2,
      itensPorPagina: 8,
      totalItens: 0,
      totalPaginas: 0,
    });
  });

  it('loads public product details by ID', () => {
    let result: unknown;
    service.consultarProduto('product-1').subscribe((product) => (result = product));

    const request = http.expectOne('/api/catalogo/produtos/product-1');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 'product-1' });

    expect(result).toEqual({ id: 'product-1' });
  });
});
