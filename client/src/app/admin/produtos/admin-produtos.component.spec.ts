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
    variantes: [
      {
        id: 'soap-default',
        identificacao: '100 g',
        unidadeVenda: 'G',
        precoVenda: 20,
        ativa: true,
        padrao: true,
      },
      {
        id: 'soap-large',
        identificacao: '200 g',
        unidadeVenda: 'G',
        precoVenda: 35,
        ativa: true,
        padrao: false,
      },
    ],
  };
  const candle: AdminProduto = {
    id: 'candle',
    nome: 'Vela de Lavanda',
    descricao: null,
    categoriaId: 'candles',
    categoriaNome: 'Velas',
    categoriaAtiva: true,
    publicado: false,
    variantes: [
      {
        id: 'candle-default',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 25,
        ativa: true,
        padrao: true,
      },
    ],
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
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 18.5,
      },
      variantes: [],
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
  });

  it('loads a fresh product before editing and saves only the product fields', () => {
    const updated: AdminProduto = { ...soap, nome: 'Sabonete editado' };
    obterProduto.mockReturnValue(of(soap));
    editarProduto.mockReturnValue(of(updated));
    clickButton('Editar Sabonete de Lavanda');

    expect(obterProduto).toHaveBeenCalledWith(soap.id);
    setField('product-name', updated.nome);
    setField('product-category', 'soaps');
    submitForm();

    expect(editarProduto).toHaveBeenCalledWith(soap.id, {
      nome: updated.nome,
      descricao: null,
      categoriaId: 'soaps',
    } satisfies AdminProdutoEdicaoRequest);
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
    clickButton('Gerenciar variantes de Sabonete de Lavanda');
    clickButton('Desativar variante 100 g');
    setField('replacement-default-soap-default', 'soap-large');
    clickButton('Promover e desativar');

    expect(alterarStatusVariante).toHaveBeenCalledOnce();
    expect(alterarStatusVariante).toHaveBeenCalledWith(soap.id, 'soap-default', {
      ativa: false,
      novoPadraoId: 'soap-large',
    });
    expect(text()).toContain('Variante desativada com sucesso.');
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
    clickButton('Gerenciar variantes de Sabonete de Lavanda');
    clickButton('Ativar variante 200 g');
    expect(alterarStatusVariante).toHaveBeenCalledWith(soap.id, 'soap-large', { ativa: true });
    clickButton('Definir como padrão');
    expect(definirVariantePadrao).toHaveBeenCalledWith(soap.id, 'soap-large');
    expect(text()).toContain('Variante padrão atualizada.');
  });

  it('prevents deactivating the last active variant', () => {
    obterProduto.mockReturnValue(of(candle));
    clickButton('Gerenciar variantes de Vela de Lavanda');

    const deactivate = Array.from(root().querySelectorAll('button')).find(
      (button) => button.getAttribute('aria-label') === 'Desativar variante Padrão',
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
          unidadeVenda: 'G',
          precoVenda: 12,
          ativa: true,
          padrao: false,
        },
      ],
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
    clickButton('Gerenciar variantes de Sabonete de Lavanda');
    clickButton('Adicionar variante');
    setField('managed-variant-name', '50 g');
    setField('managed-variant-unit', 'g');
    setField('managed-variant-price', '12');
    submitForm();

    expect(adicionarVariante).toHaveBeenCalledWith(soap.id, {
      identificacao: '50 g',
      unidadeVenda: 'G',
      precoVenda: 12,
    } satisfies AdminProdutoVarianteRequest);
    expect(text()).toContain('Variante adicionada com sucesso.');

    clickButton('Editar variante 50 g');
    setField('managed-variant-name', '60 g');
    setField('managed-variant-price', '14');
    submitForm();

    expect(editarVariante).toHaveBeenCalledWith(soap.id, 'soap-small', {
      identificacao: '60 g',
      unidadeVenda: 'G',
      precoVenda: 14,
    });
    expect(text()).toContain('Variante atualizada com sucesso.');
    expect(text()).not.toContain('Excluir variante');
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
    return article?.querySelector('span')?.textContent?.trim() ?? null;
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
