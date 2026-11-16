import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  AdminProduto,
  AdminProdutoCadastroRequest,
  AdminProdutoEdicaoRequest,
  AdminProdutoVarianteRequest,
  ProdutosService,
} from './produtos.service';

describe('ProdutosService', () => {
  let service: ProdutosService;
  let http: HttpTestingController;

  const product: AdminProduto = {
    id: 'product-1',
    nome: 'Sabonete de Lavanda',
    descricao: null,
    categoriaId: 'category-1',
    categoriaNome: 'Sabonetes',
    categoriaAtiva: true,
    publicado: false,
    quantidadeImagens: 0,
    fotoPrincipalUrl: null,
    variantes: [
      {
        id: 'variant-1',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 19.9,
        ativa: true,
        padrao: true,
        pesoLiquidoGramas: null,
        volumeLiquidoMililitros: null,
        caracteristicas: [],
      },
    ],
    caracteristicas: [],
  };
  const variantRequest: AdminProdutoVarianteRequest = {
    identificacao: '200 g',
    unidadeVenda: 'UN',
    precoVenda: 29.9,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ProdutosService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: { cookie: 'XSRF-TOKEN=cookie-token' } },
      ],
    });

    service = TestBed.inject(ProdutosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('lists with the backend filters and reads a product by ID', () => {
    let listed: AdminProduto[] | undefined;
    let selected: AdminProduto | undefined;
    service
      .listar({ busca: '  lavanda ', categoriaId: 'category-1', publicado: false })
      .subscribe((value) => (listed = value));
    service.obter(product.id).subscribe((value) => (selected = value));

    const listRequest = http.expectOne(
      (request) =>
        request.url === '/api/admin/produtos' &&
        request.params.get('busca') === 'lavanda' &&
        request.params.get('categoriaId') === 'category-1' &&
        request.params.get('publicado') === 'false',
    );
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([product]);

    const getRequest = http.expectOne(`/api/admin/produtos/${product.id}`);
    expect(getRequest.request.method).toBe('GET');
    getRequest.flush(product);
    expect(listed).toEqual([product]);
    expect(selected).toEqual(product);
  });

  it('creates a simple or multi-variant product with the actual API request shape', () => {
    const request: AdminProdutoCadastroRequest = {
      nome: product.nome,
      descricao: product.descricao,
      categoriaId: product.categoriaId,
      simples: false,
      variantePadrao: {
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 19.9,
      },
      variantes: [variantRequest],
    };

    expectWrite(service.criar(request), 'POST', '/api/admin/produtos', request, 201, 'Created');
  });

  it('sends temporary references and persisted IDs using the aggregate API contracts', () => {
    const create: AdminProdutoCadastroRequest = {
      nome: 'Vela artesanal',
      descricao: null,
      categoriaId: 'category-1',
      simples: false,
      variantePadrao: {
        identificacao: '100 g · Branca · Lavanda',
        unidadeVenda: 'UN',
        precoVenda: 35,
        caracteristicas: [
          { referenciaCaracteristica: 'peso', referenciaValor: 'peso-100' },
          { referenciaCaracteristica: 'cor', referenciaValor: 'cor-branca' },
          { referenciaCaracteristica: 'fragrancia', referenciaValor: 'aroma-lavanda' },
        ],
      },
      variantes: [],
      caracteristicas: [
        {
          referencia: 'peso',
          nome: 'Peso',
          tipoMedidaFisica: 'PesoLiquido',
          valores: [{ referencia: 'peso-100', valorMedidaCanonico: 100 }],
        },
        {
          referencia: 'cor',
          nome: 'Cor',
          tipoMedidaFisica: null,
          valores: [{ referencia: 'cor-branca', nome: 'Branca' }],
        },
        {
          referencia: 'fragrancia',
          nome: 'Fragrância',
          tipoMedidaFisica: null,
          valores: [{ referencia: 'aroma-lavanda', nome: 'Lavanda' }],
        },
      ],
    };
    expectWrite(service.criar(create), 'POST', '/api/admin/produtos', create, 201, 'Created');

    const edit: AdminProdutoEdicaoRequest = {
      nome: product.nome,
      descricao: product.descricao,
      categoriaId: product.categoriaId,
      caracteristicas: [
        {
          id: 'feature-weight',
          nome: 'Peso',
          tipoMedidaFisica: 'PesoLiquido',
          valores: [{ id: 'value-100', valorMedidaCanonico: 100 }],
        },
      ],
      variantes: [
        {
          id: 'variant-1',
          identificacao: '100 g',
          unidadeVenda: 'UN',
          precoVenda: 19.9,
          caracteristicas: [{ caracteristicaId: 'feature-weight', valorId: 'value-100' }],
          ativa: true,
          padrao: true,
          atualizarMedidas: true,
        },
      ],
    };
    expectWrite(service.editar(product.id, edit), 'PUT', `/api/admin/produtos/${product.id}`, edit);
  });

  it('uses the real edit, variant, default, status, and publication contracts with antiforgery', () => {
    const edit: AdminProdutoEdicaoRequest = {
      nome: 'Sabonete Natural',
      descricao: 'Lavanda',
      categoriaId: 'category-1',
    };
    expectWrite(service.editar(product.id, edit), 'PUT', `/api/admin/produtos/${product.id}`, edit);
    expectWrite(
      service.adicionarVariante(product.id, variantRequest),
      'POST',
      `/api/admin/produtos/${product.id}/variantes`,
      variantRequest,
    );
    expectWrite(
      service.editarVariante(product.id, 'variant-2', variantRequest),
      'PUT',
      `/api/admin/produtos/${product.id}/variantes/variant-2`,
      variantRequest,
    );
    expectWrite(
      service.definirVariantePadrao(product.id, 'variant-2'),
      'PUT',
      `/api/admin/produtos/${product.id}/variantes/variant-2/padrao`,
      null,
    );
    expectWrite(
      service.alterarStatusVariante(product.id, 'variant-1', {
        ativa: false,
        novoPadraoId: 'variant-2',
      }),
      'PATCH',
      `/api/admin/produtos/${product.id}/variantes/variant-1/status`,
      { ativa: false, novoPadraoId: 'variant-2' },
    );
    expectWrite(
      service.alterarPublicacao(product.id, { publicado: true }),
      'PATCH',
      `/api/admin/produtos/${product.id}/publicacao`,
      { publicado: true },
    );
  });

  function expectWrite(
    operation: ReturnType<ProdutosService['criar']>,
    method: string,
    url: string,
    body: unknown,
    status = 200,
    statusText = 'OK',
  ): void {
    let result: AdminProduto | undefined;
    operation.subscribe((value) => (result = value));

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone(url);
    csrf.flush({ requestToken: 'products-request-token' });

    const request = http.expectOne(url);
    expect(request.request.method).toBe(method);
    expect(request.request.body).toEqual(body);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('products-request-token');
    request.flush(product, { status, statusText });
    expect(result).toEqual(product);
  }
});
