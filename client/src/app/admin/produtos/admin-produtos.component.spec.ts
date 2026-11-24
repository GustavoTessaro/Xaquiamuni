import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { AdminCategoria, CategoriasService } from '../../core/admin-categorias/categorias.service';
import {
  AdminProduto,
  AdminProdutoCadastroRequest,
  AdminProdutoEdicaoRequest,
  AdminProdutoVarianteRequest,
  ProdutosService,
} from '../../core/admin-produtos/produtos.service';
import { AdminProdutosComponent } from './admin-produtos.component';

describe('AdminProdutosComponent', () => {
  let fixture: ComponentFixture<AdminProdutosComponent>;
  let listarProdutos: ReturnType<typeof vi.fn<ProdutosService['listar']>>;
  let obterProduto: ReturnType<typeof vi.fn<ProdutosService['obter']>>;
  let criarProduto: ReturnType<typeof vi.fn<ProdutosService['criar']>>;
  let editarProduto: ReturnType<typeof vi.fn<ProdutosService['editar']>>;
  let adicionarVariante: ReturnType<typeof vi.fn<ProdutosService['adicionarVariante']>>;
  let editarVariante: ReturnType<typeof vi.fn<ProdutosService['editarVariante']>>;
  let definirVariantePadrao: ReturnType<typeof vi.fn<ProdutosService['definirVariantePadrao']>>;
  let alterarStatusVariante: ReturnType<typeof vi.fn<ProdutosService['alterarStatusVariante']>>;
  let excluirVariante: ReturnType<typeof vi.fn<ProdutosService['excluirVariante']>>;
  let alterarPublicacao: ReturnType<typeof vi.fn<ProdutosService['alterarPublicacao']>>;
  let navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>>;

  const categories: AdminCategoria[] = [
    { id: 'soaps', nome: 'Sabonetes', descricao: null, ativa: true, ordemExibicao: 0 },
    { id: 'candles', nome: 'Velas', descricao: null, ativa: true, ordemExibicao: 1 },
  ];
  const soap: AdminProduto = {
    id: 'soap',
    nome: 'Sabonete de Lavanda',
    descricao: null,
    categoriaId: 'soaps',
    categoriaNome: 'Sabonetes',
    categoriaAtiva: true,
    publicado: true,
    quantidadeImagens: 2,
    fotoPrincipalUrl: '/api/admin/produtos/soap/imagens/soap-main/conteudo',
    variantes: [
      {
        id: 'soap-default',
        identificacao: '100 g',
        unidadeVenda: 'UN',
        precoVenda: 20,
        ativa: true,
        padrao: true,
        pesoLiquidoGramas: 100,
        volumeLiquidoMililitros: null,
        caracteristicas: [],
      },
      {
        id: 'soap-large',
        identificacao: '200 g',
        unidadeVenda: 'UN',
        precoVenda: 35,
        ativa: true,
        padrao: false,
        pesoLiquidoGramas: 200,
        volumeLiquidoMililitros: null,
        caracteristicas: [],
      },
    ],
    caracteristicas: [],
  };
  const candle: AdminProduto = {
    id: 'candle',
    nome: 'Vela de Lavanda',
    descricao: null,
    categoriaId: 'candles',
    categoriaNome: 'Velas',
    categoriaAtiva: true,
    publicado: false,
    quantidadeImagens: 0,
    fotoPrincipalUrl: null,
    variantes: [
      {
        id: 'candle-default',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 25,
        ativa: true,
        padrao: true,
        pesoLiquidoGramas: null,
        volumeLiquidoMililitros: null,
        caracteristicas: [],
      },
    ],
    caracteristicas: [],
  };

  beforeEach(async () => {
    listarProdutos = vi.fn<ProdutosService['listar']>().mockReturnValue(of([soap, candle]));
    obterProduto = vi
      .fn<ProdutosService['obter']>()
      .mockImplementation((id) => of(id === soap.id ? soap : candle));
    criarProduto = vi.fn<ProdutosService['criar']>();
    editarProduto = vi.fn<ProdutosService['editar']>();
    adicionarVariante = vi.fn<ProdutosService['adicionarVariante']>();
    editarVariante = vi.fn<ProdutosService['editarVariante']>();
    definirVariantePadrao = vi.fn<ProdutosService['definirVariantePadrao']>();
    alterarStatusVariante = vi.fn<ProdutosService['alterarStatusVariante']>();
    excluirVariante = vi.fn<ProdutosService['excluirVariante']>();
    alterarPublicacao = vi.fn<ProdutosService['alterarPublicacao']>();

    await TestBed.configureTestingModule({
      imports: [AdminProdutosComponent],
      providers: [
        provideRouter([]),
        {
          provide: AdminAuthService,
          useValue: { logout: vi.fn(() => of(undefined)) },
        },
        {
          provide: CategoriasService,
          useValue: { listar: vi.fn(() => of(categories)) },
        },
        {
          provide: ProdutosService,
          useValue: {
            listar: listarProdutos,
            obter: obterProduto,
            criar: criarProduto,
            editar: editarProduto,
            adicionarVariante,
            editarVariante,
            definirVariantePadrao,
            alterarStatusVariante,
            excluirVariante,
            alterarPublicacao,
          },
        },
      ],
    }).compileComponents();
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads products and keeps summary totals independent of search and filters', () => {
    expect(listarProdutos).toHaveBeenCalledOnce();
    expect(text()).toContain('Produtos');
    expect(root().querySelector('[aria-label="Total de produtos"]')?.textContent).toContain('2');
    expect(root().querySelector('[aria-label="Produtos publicados"]')?.textContent).toContain('1');
    expect(root().querySelector('[aria-label="Produtos não publicados"]')?.textContent).toContain(
      '1',
    );
    expect(text()).toContain('R$');

    setField('product-search', 'VELA');
    setField('category-filter', 'candles');
    clickButton('Não publicados');

    expect(productNames()).toEqual(['Vela de Lavanda']);
    expect(root().querySelector('[aria-label="Total de produtos"]')?.textContent).toContain('2');
    expect(root().querySelector('[aria-label="Produtos publicados"]')?.textContent).toContain('1');
    expect(root().querySelector('[aria-label="Produtos não publicados"]')?.textContent).toContain(
      '1',
    );
  });

  it('renders the primary photo, photo count, and an accessible no-photo placeholder', () => {
    const principal = root().querySelector<HTMLImageElement>(
      'img[alt="Foto principal de Sabonete de Lavanda"]',
    );

    expect(principal?.getAttribute('src')).toBe(
      '/api/admin/produtos/soap/imagens/soap-main/conteudo',
    );
    expect(text()).toContain('2 fotos cadastradas');
    expect(text()).toContain('0 fotos cadastradas');
    expect(
      root().querySelector('[aria-label="Sem foto principal para Vela de Lavanda"]'),
    ).not.toBeNull();
    expect(listarProdutos).toHaveBeenCalledOnce();
  });

  it('shows a neutral fallback when the principal image cannot be loaded', () => {
    const principal = root().querySelector<HTMLImageElement>(
      'img[alt="Foto principal de Sabonete de Lavanda"]',
    );

    principal?.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(
      root().querySelector('[aria-label="Foto principal indisponível para Sabonete de Lavanda"]'),
    ).not.toBeNull();
    expect(text()).toContain('Foto indisponível');
    expect(text()).toContain('2 fotos cadastradas');
  });

  it('shows actionable publication requirements without implying stock availability', () => {
    const blocked = {
      ...candle,
      categoriaAtiva: false,
      variantes: candle.variantes.map((variante) => ({ ...variante, padrao: false })),
    };
    listarProdutos.mockReturnValueOnce(of([blocked]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();

    expect(text()).toContain('Ative a categoria em Gerenciar categorias.');
    expect(text()).toContain('Defina uma opção ativa como principal em Gerenciar opções.');
    expect(text()).toContain('Adicione uma foto principal em Gerenciar fotos.');
    expect(text()).toContain('Publicação não indica disponibilidade em estoque.');
  });

  it('shows simple-product measures and sale units without zero placeholders or legacy conversion', () => {
    const measured = {
      ...candle,
      variantes: [
        {
          ...candle.variantes[0],
          pesoLiquidoGramas: 100,
          volumeLiquidoMililitros: 250,
        },
      ],
    };
    const legacy = {
      ...candle,
      id: 'legacy',
      nome: 'Produto legado',
      variantes: [
        {
          ...candle.variantes[0],
          unidadeVenda: 'G',
          pesoLiquidoGramas: null,
          volumeLiquidoMililitros: null,
        },
      ],
    };
    listarProdutos.mockReturnValueOnce(of([measured, legacy]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();

    expect(text()).toContain('100 g · 250 ml · Unidade');
    expect(text()).toContain('G');
    expect(text()).not.toContain('· 0 g');
    expect(text()).not.toContain('· 0 ml');
    expect(text()).not.toContain('Variantes');
    expect(text()).not.toContain('Opções do produto');
  });

  it('shows distinct empty catalog and no-results states', () => {
    listarProdutos.mockReturnValueOnce(of([]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();
    expect(text()).toContain('Nenhum produto cadastrado');

    listarProdutos.mockReturnValueOnce(of([soap, candle]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();
    setField('product-search', 'não existe');
    expect(text()).toContain('Nenhum produto encontrado');
    expect(text()).not.toContain('Nenhum produto cadastrado');
  });

  it('shows a safe load error and retries without substituting sample data', () => {
    listarProdutos
      .mockReturnValueOnce(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 503,
              error: { detail: 'database connection internals' },
            }),
        ),
      )
      .mockReturnValueOnce(of([soap, candle]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();

    expect(text()).toContain('Não foi possível carregar os produtos e categorias.');
    expect(text()).not.toContain('database connection internals');
    expect(productNames()).toEqual([]);
    clickButton('Tentar novamente');
    expect(listarProdutos).toHaveBeenCalledTimes(3);
    expect(productNames()).toContain(soap.nome);
  });

  it('renders persisted combinations using option names rather than internal references', () => {
    const configured: AdminProduto = {
      ...soap,
      caracteristicas: [
        {
          id: 'weight',
          nome: 'Peso',
          tipoMedidaFisica: 'PesoLiquido',
          valores: [{ id: '100g', nome: '', valorMedidaCanonico: 100 }],
        },
        {
          id: 'color',
          nome: 'Cor',
          tipoMedidaFisica: null,
          valores: [{ id: 'white', nome: 'Branca', valorMedidaCanonico: null }],
        },
        {
          id: 'fragrance',
          nome: 'Fragrância',
          tipoMedidaFisica: null,
          valores: [{ id: 'lavender', nome: 'Lavanda', valorMedidaCanonico: null }],
        },
      ],
      variantes: [
        {
          ...soap.variantes[0],
          caracteristicas: [
            {
              caracteristicaId: 'weight',
              caracteristicaNome: 'Peso',
              valorId: '100g',
              valorNome: '',
              valorMedidaCanonico: 100,
            },
            {
              caracteristicaId: 'color',
              caracteristicaNome: 'Cor',
              valorId: 'white',
              valorNome: 'Branca',
              valorMedidaCanonico: null,
            },
            {
              caracteristicaId: 'fragrance',
              caracteristicaNome: 'Fragrância',
              valorId: 'lavender',
              valorNome: 'Lavanda',
              valorMedidaCanonico: null,
            },
          ],
        },
        soap.variantes[1],
      ],
    };
    listarProdutos.mockReturnValueOnce(of([configured, candle]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();

    expect(text()).toContain('100 g · Branca · Lavanda');
    expect(text()).not.toContain('100g');
    obterProduto.mockReturnValue(of(configured));
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    expect(text()).toContain('100 g · Branca · Lavanda');
    expect(
      Array.from(root().querySelectorAll('button')).find(
        (button) => button.textContent?.trim() === 'Adicionar opção',
      )?.disabled,
    ).toBe(false);
    clickButton('Adicionar opção');
    expect(root().querySelector('app-admin-produto-variante-form select')).not.toBeNull();
  });

  it('creates a simple product and reports that it starts unpublished', () => {
    const newProduct: AdminProduto = {
      ...candle,
      id: 'new-product',
      nome: 'Sabonete simples',
      categoriaId: 'soaps',
      categoriaNome: 'Sabonetes',
      publicado: false,
    };
    const request: AdminProdutoCadastroRequest = {
      nome: newProduct.nome,
      descricao: null,
      categoriaId: 'soaps',
      variantePadrao: {
        unidadeVenda: 'UN',
        precoVenda: 18.5,
      },
      variantes: [],
      simples: true,
    };
    criarProduto.mockReturnValue(of(newProduct));
    clickButton('Novo produto');
    setField('product-name', newProduct.nome);
    setField('product-category', 'soaps');
    setField('default-variant-price', '18.50');
    submitForm();

    expect(criarProduto).toHaveBeenCalledOnce();
    expect(criarProduto).toHaveBeenCalledWith(request);
    expect(text()).toContain('Produto cadastrado com sucesso.');
    expect(text()).toContain('Ele começa não publicado.');
    expect(productNames()).toContain(newProduct.nome);
    clickButton('Gerenciar fotos do produto recém-cadastrado');
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/produtos/new-product/fotos');
  });

  it('opens the gallery from the product list without loading galleries for every product', () => {
    clickButton('Gerenciar fotos de Sabonete de Lavanda');

    expect(navigateByUrl).toHaveBeenCalledWith('/admin/produtos/soap/fotos');
    expect(listarProdutos).toHaveBeenCalledOnce();
  });

  it('abre diretamente o estoque da variante única', () => {
    clickButton('Estoque de Vela de Lavanda');
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/estoque/produtos-acabados/candle-default');
  });

  it('abre a seleção de variantes do produto com os filtros da listagem', () => {
    setField('product-search', 'Sabonete');
    setField('category-filter', 'soaps');
    clickButton('Publicados');
    clickButton('Estoque de Sabonete de Lavanda');
    expect(navigateByUrl).toHaveBeenCalledWith(
      '/admin/produtos/soap/estoque?busca=Sabonete&categoriaId=soaps&publicacao=published',
    );
  });

  it('preserves search and filters when opening the gallery', () => {
    setField('product-search', 'Sabonete');
    setField('category-filter', 'soaps');
    clickButton('Publicados');
    clickButton('Gerenciar fotos de Sabonete de Lavanda');

    expect(navigateByUrl).toHaveBeenCalledWith(
      '/admin/produtos/soap/fotos?busca=Sabonete&categoriaId=soaps&publicacao=published',
    );
  });

  it('reloads the product list and photo summary after returning from the gallery', () => {
    const updated = {
      ...soap,
      publicado: true,
      quantidadeImagens: 3,
      fotoPrincipalUrl: '/api/admin/produtos/soap/imagens/soap-main-updated/conteudo',
    };
    listarProdutos.mockReturnValueOnce(of([updated, candle]));
    clickButton('Gerenciar fotos de Sabonete de Lavanda');
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutosComponent);
    fixture.detectChanges();

    expect(listarProdutos).toHaveBeenCalledTimes(2);
    expect(text()).toContain('3 fotos cadastradas');
    expect(publicationStatus('Sabonete de Lavanda')).toBe('Publicado');
    expect(
      root().querySelector(
        'img[src="/api/admin/produtos/soap/imagens/soap-main-updated/conteudo"]',
      ),
    ).not.toBeNull();
  });

  it('keeps form data and shows a safe combination conflict after a failed save', () => {
    criarProduto.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              title: 'Conflito no produto',
              detail: 'Esta combinação já está cadastrada.',
            },
          }),
      ),
    );
    clickButton('Novo produto');
    setField('product-name', 'Sabonete com conflito');
    setField('product-category', 'soaps');
    setField('default-variant-price', '1500');
    submitForm();

    expect(text()).toContain('Esta combinação já está cadastrada.');
    expect((root().querySelector('#product-name') as HTMLInputElement).value).toBe(
      'Sabonete com conflito',
    );
    expect((root().querySelector('#default-variant-price') as HTMLInputElement).value).toBe(
      'R$ 15,00',
    );
    expect(text()).not.toContain('SQL');
  });

  it('loads complete details before editing and preserves existing variant IDs', () => {
    const updated: AdminProduto = { ...soap, nome: 'Sabonete editado' };
    obterProduto.mockReturnValue(of(soap));
    editarProduto.mockReturnValue(of(updated));
    clickButton('Editar Sabonete de Lavanda');

    expect(obterProduto).toHaveBeenCalledWith(soap.id);
    setField('product-name', updated.nome);
    setField('product-category', 'soaps');
    submitForm();

    expect(editarProduto).toHaveBeenCalledWith(
      soap.id,
      expect.objectContaining({
        nome: updated.nome,
        descricao: null,
        categoriaId: 'soaps',
      }) satisfies AdminProdutoEdicaoRequest,
    );
    expect(editarProduto.mock.calls[0]?.[1]).not.toHaveProperty('variantes');
    expect(productNames()).toContain(updated.nome);
    expect(text()).toContain('Produto atualizado com sucesso.');
  });

  it('publishes and unpublishes through the API and explains inactive-category conflicts', () => {
    alterarPublicacao
      .mockReturnValueOnce(of({ ...candle, publicado: true }))
      .mockReturnValueOnce(of(candle))
      .mockReturnValueOnce(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: {
                title: 'Categoria inativa',
                detail: 'A categoria está inativa e o produto não pode ser publicado.',
              },
            }),
        ),
      );

    clickButton('Publicar Vela de Lavanda');
    expect(alterarPublicacao).toHaveBeenNthCalledWith(1, candle.id, { publicado: true });
    expect(text()).toContain('Produto publicado.');
    clickButton('Despublicar Vela de Lavanda');
    expect(alterarPublicacao).toHaveBeenNthCalledWith(2, candle.id, { publicado: false });

    alterarPublicacao.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              title: 'Categoria inativa',
              detail: 'A categoria está inativa e o produto não pode ser publicado.',
            },
          }),
      ),
    );
    clickButton('Publicar Vela de Lavanda');
    expect(text()).toContain('A categoria está inativa');
    expect(publicationStatus('Vela de Lavanda')).toBe('Não publicado');
  });

  it('links to the gallery when publication is blocked because the product has no image', () => {
    alterarPublicacao.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              title: 'Conflito no produto',
              detail:
                'Um produto só pode ser publicado com ao menos uma imagem e exatamente uma imagem principal.',
            },
          }),
      ),
    );

    clickButton('Publicar Vela de Lavanda');

    expect(text()).toContain('Adicione ao menos uma foto principal antes de publicar o produto.');
    clickButton('Gerenciar fotos para concluir a publicação');
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/produtos/candle/fotos');
  });

  it('promotes an active replacement and deactivates the old default atomically', () => {
    const changed: AdminProduto = {
      ...soap,
      variantes: [
        { ...soap.variantes[0], ativa: false, padrao: false },
        { ...soap.variantes[1], padrao: true },
      ],
    };
    obterProduto.mockReturnValue(of(soap));
    alterarStatusVariante.mockReturnValue(of(changed));
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    clickButton('Desativar opção 100 g');
    setField('replacement-default-soap-default', 'soap-large');
    clickButton('Promover e desativar');

    expect(alterarStatusVariante).toHaveBeenCalledOnce();
    expect(alterarStatusVariante).toHaveBeenCalledWith(soap.id, 'soap-default', {
      ativa: false,
      novoPadraoId: 'soap-large',
    });
    expect(text()).toContain('Opção desativada com sucesso.');
    expect(text()).toContain('200 g');
  });

  it('activates an inactive variant before allowing it to become the default', () => {
    const productWithInactiveVariant: AdminProduto = {
      ...soap,
      variantes: [soap.variantes[0], { ...soap.variantes[1], ativa: false }],
    };
    const activated: AdminProduto = {
      ...productWithInactiveVariant,
      variantes: [
        productWithInactiveVariant.variantes[0],
        { ...productWithInactiveVariant.variantes[1], ativa: true },
      ],
    };
    const promoted: AdminProduto = {
      ...activated,
      variantes: [
        { ...activated.variantes[0], padrao: false },
        { ...activated.variantes[1], padrao: true },
      ],
    };
    obterProduto.mockReturnValue(of(productWithInactiveVariant));
    alterarStatusVariante.mockReturnValue(of(activated));
    definirVariantePadrao.mockReturnValue(of(promoted));
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    clickButton('Ativar opção 200 g');
    expect(alterarStatusVariante).toHaveBeenCalledWith(soap.id, 'soap-large', { ativa: true });
    clickButton('Apresentar primeiro');
    expect(definirVariantePadrao).toHaveBeenCalledWith(soap.id, 'soap-large');
    expect(text()).toContain('Opção principal atualizada.');
  });

  it('prevents deactivating the last active variant', () => {
    obterProduto.mockReturnValue(of(candle));
    clickButton('Gerenciar opções de Vela de Lavanda');

    const deactivate = Array.from(root().querySelectorAll('button')).find(
      (button) => button.getAttribute('aria-label') === 'Desativar opção Padrão',
    );
    expect(deactivate?.disabled).toBe(true);
    expect(alterarStatusVariante).not.toHaveBeenCalled();
  });

  it('adds and edits variants without exposing a physical delete action', () => {
    const added: AdminProduto = {
      ...soap,
      variantes: [
        ...soap.variantes,
        {
          id: 'soap-small',
          identificacao: '50 g',
          unidadeVenda: 'KIT',
          precoVenda: 12,
          ativa: true,
          padrao: false,
          pesoLiquidoGramas: null,
          volumeLiquidoMililitros: null,
          caracteristicas: [],
        },
      ],
      caracteristicas: [],
    };
    const edited: AdminProduto = {
      ...added,
      variantes: added.variantes.map((variant) =>
        variant.id === 'soap-small'
          ? { ...variant, identificacao: '60 g', precoVenda: 14 }
          : variant,
      ),
    };
    obterProduto.mockReturnValue(of(soap));
    adicionarVariante.mockReturnValue(of(added));
    editarVariante.mockReturnValue(of(edited));
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    clickButton('Adicionar opção');
    setField('managed-variant-name', '50 g');
    setField('managed-variant-unit', 'KIT');
    setField('managed-variant-price', '1200');
    submitForm();

    expect(adicionarVariante).toHaveBeenCalledWith(soap.id, {
      identificacao: '50 g',
      unidadeVenda: 'KIT',
      precoVenda: 12,
    } satisfies AdminProdutoVarianteRequest);
    expect(text()).toContain('Opção adicionada com sucesso.');

    clickButton('Editar opção 50 g');
    setField('managed-variant-name', '60 g');
    setField('managed-variant-price', '1400');
    submitForm();

    expect(editarVariante).toHaveBeenCalledWith(soap.id, 'soap-small', {
      identificacao: '60 g',
      unidadeVenda: 'KIT',
      precoVenda: 14,
    });
    expect(text()).toContain('Opção atualizada com sucesso.');
    expect(text()).not.toContain('Excluir variante');
  });

  it('recovers an uncertain variant deletion with the original key and keeps the product open', () => {
    const produto = { ...soap, versao: 'versao-consultada' };
    obterProduto.mockReturnValueOnce(of(produto)).mockReturnValueOnce(of({
      ...produto, versao: 'nova-versao', variantes: produto.variantes.filter(v => v.id !== 'soap-large'),
    }));
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const pendente = new Subject<void>();
    excluirVariante.mockReturnValueOnce(pendente).mockReturnValueOnce(of(undefined));
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    const extra = produto.variantes.find(v => v.id === 'soap-large')!;
    clickButton('Editar opção ' + extra.identificacao);
    clickButton('Excluir variante');
    expect(excluirVariante).not.toHaveBeenCalled();
    confirmacao.mockReturnValue(true);
    clickButton('Excluir variante');
    expect(excluirVariante).toHaveBeenCalledOnce();
    expect(Array.from(root().querySelectorAll('button')).find(b => b.textContent?.trim() === 'Recuperar exclusão')
      ?.disabled).toBe(true);
    pendente.error(new HttpErrorResponse({ status: 0 }));
    fixture.detectChanges();
    clickButton('Recuperar exclusão');
    expect(excluirVariante.mock.calls[1]).toEqual(excluirVariante.mock.calls[0]);
    expect(text()).toContain('Variante excluída.');
    expect(text()).toContain('Sabonete de Lavanda');
    expect(text()).not.toContain(extra.identificacao);
  });

  it.each([
    ['variante_com_historico_comercial', 'A variante possui vínculos comerciais.', true],
    ['ultima_variante', 'O produto precisa manter pelo menos uma variante.', false],
    ['variante_padrao', 'Defina outra variante padrão antes de excluir esta.', false],
  ])('shows the deletion conflict %s and retains the variant', (codigo, detail, permiteInativacao) => {
    const produto = { ...soap, versao: 'atual' };
    obterProduto.mockReturnValueOnce(of(produto));
    const extra = produto.variantes.find(v => v.id === 'soap-large')!;
    excluirVariante.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409, error: { codigo, detail },
    })));
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(true);
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    clickButton('Editar opção ' + extra.identificacao);
    clickButton('Excluir variante');
    expect(text()).toContain(detail);
    expect(text()).toContain(extra.identificacao);
    expect(text().includes('Desativar variante')).toBe(permiteInativacao);
    if (permiteInativacao) {
      confirmacao.mockReturnValue(false);
      clickButton('Desativar variante');
      expect(alterarStatusVariante).not.toHaveBeenCalled();
      expect(text()).toContain(detail);
    }
  });

  it('keeps the deletion key when refreshing the product fails after confirmation', () => {
    const produto = { ...soap, versao: 'atual' };
    const extra = produto.variantes.find(v => v.id === 'soap-large')!;
    obterProduto.mockReturnValueOnce(of(produto))
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })))
      .mockReturnValueOnce(of({ ...produto, variantes: produto.variantes.filter(v => v.id !== extra.id) }));
    excluirVariante.mockReturnValue(of(undefined));
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(true);
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    clickButton('Editar opção ' + extra.identificacao);
    clickButton('Excluir variante');
    expect(text()).toContain('Recupere a exclusão para atualizar a lista.');
    clickButton('Recuperar exclusão');
    expect(excluirVariante.mock.calls[1]).toEqual(excluirVariante.mock.calls[0]);
    expect(confirmacao).toHaveBeenCalledOnce();
    expect(text()).not.toContain(extra.identificacao);
  });

  it('requeries a stale variant and requires a new deletion confirmation', () => {
    const produto = { ...soap, versao: 'antiga' };
    obterProduto.mockReturnValueOnce(of(produto)).mockReturnValueOnce(of({ ...produto, versao: 'atual' }))
      .mockReturnValueOnce(of({ ...produto, versao: 'final' }));
    excluirVariante.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409, error: { codigo: 'versao_desatualizada', detail: 'Recarregue o produto.' },
    }))).mockReturnValueOnce(of(undefined));
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const extra = produto.variantes.find(v => v.id === 'soap-large')!;
    clickButton('Gerenciar opções de Sabonete de Lavanda');
    clickButton('Editar opção ' + extra.identificacao);
    clickButton('Excluir variante');
    expect(text()).toContain('Recarregue o produto.');
    clickButton('Recarregar produto');
    expect(excluirVariante).toHaveBeenCalledTimes(1);
    clickButton('Editar opção ' + extra.identificacao);
    clickButton('Excluir variante');
    expect(confirmacao).toHaveBeenCalledTimes(2);
    expect(excluirVariante.mock.calls[1][2]).toBe('atual');
    expect(excluirVariante.mock.calls[0][3]).not.toBe(excluirVariante.mock.calls[1][3]);
  });

  it('prevents duplicate create submissions and safely redirects expired sessions', () => {
    const pending = new Subject<AdminProduto>();
    criarProduto.mockReturnValue(pending);
    clickButton('Novo produto');
    setField('product-name', 'Sabonete pendente');
    setField('product-category', 'soaps');
    setField('default-variant-price', '15');
    submitForm();
    submitForm();

    expect(criarProduto).toHaveBeenCalledOnce();
    expect((root().querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(
      true,
    );

    pending.error(new HttpErrorResponse({ status: 401 }));
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/login');
    expect(text()).not.toContain('network details');
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return root().textContent ?? '';
  }

  function productNames(): string[] {
    return Array.from(root().querySelectorAll('article h2')).map(
      (heading) => heading.textContent?.trim() ?? '',
    );
  }

  function publicationStatus(name: string): string | null {
    const article = Array.from(root().querySelectorAll('article')).find(
      (element) => element.querySelector('h2')?.textContent?.trim() === name,
    );
    return (
      Array.from(article?.querySelectorAll('span') ?? [])
        .find((element) =>
          ['Publicado', 'Não publicado'].includes(element.textContent?.trim() ?? ''),
        )
        ?.textContent?.trim() ?? null
    );
  }

  function clickButton(label: string): void {
    const button = Array.from(root().querySelectorAll('button')).find(
      (item) => item.getAttribute('aria-label') === label || item.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    if (button.disabled) {
      throw new Error(`Button is disabled: ${label}`);
    }
    button.click();
    fixture.detectChanges();
  }

  function setField(id: string, value: string): void {
    const field = root().querySelector(`#${id}`) as
      HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
    field.value = value;
    field.dispatchEvent(
      new Event(field instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
    fixture.detectChanges();
  }

  function submitForm(): void {
    const form = root().querySelector('form') as HTMLFormElement | null;
    if (!form) {
      throw new Error('Form not found.');
    }
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
});
