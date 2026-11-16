import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import {
  AdminProdutoImagem,
  AdminProdutoImagemRemocao,
  ImagensProdutoService,
} from '../../core/admin-produtos/imagens-produto.service';
import { AdminProduto, ProdutosService } from '../../core/admin-produtos/produtos.service';
import { AdminProdutoImagensComponent } from './admin-produto-imagens.component';

describe('AdminProdutoImagensComponent', () => {
  let fixture: ComponentFixture<AdminProdutoImagensComponent>;
  let obterProduto: ReturnType<typeof vi.fn<ProdutosService['obter']>>;
  let listarImagens: ReturnType<typeof vi.fn<ImagensProdutoService['listar']>>;
  let adicionar: ReturnType<typeof vi.fn<ImagensProdutoService['adicionar']>>;
  let definirPrincipal: ReturnType<typeof vi.fn<ImagensProdutoService['definirPrincipal']>>;
  let reordenar: ReturnType<typeof vi.fn<ImagensProdutoService['reordenar']>>;
  let associarVariante: ReturnType<typeof vi.fn<ImagensProdutoService['associarVariante']>>;
  let desassociarVariante: ReturnType<typeof vi.fn<ImagensProdutoService['desassociarVariante']>>;
  let remover: ReturnType<typeof vi.fn<ImagensProdutoService['remover']>>;
  let createObjectUrl: ReturnType<typeof vi.fn>;
  let revokeObjectUrl: ReturnType<typeof vi.fn>;
  let originalCreateObjectUrl: PropertyDescriptor | undefined;
  let originalRevokeObjectUrl: PropertyDescriptor | undefined;

  const simpleProduct: AdminProduto = {
    id: 'product-1',
    nome: 'Sabonete de Lavanda',
    descricao: null,
    categoriaId: 'soaps',
    categoriaNome: 'Sabonetes',
    categoriaAtiva: true,
    publicado: false,
    variantes: [
      {
        id: 'variant-1',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 18,
        ativa: true,
        padrao: true,
        pesoLiquidoGramas: null,
        volumeLiquidoMililitros: null,
        caracteristicas: [],
      },
    ],
    caracteristicas: [],
  };
  const secondVariant = {
    ...simpleProduct.variantes[0],
    id: 'variant-2',
    identificacao: 'Conjunto',
    padrao: false,
  };
  const image: AdminProdutoImagem = {
    id: 'image-1',
    produtoId: simpleProduct.id,
    nomeOriginal: 'lavanda.png',
    tipoConteudo: 'image/webp',
    tamanhoBytes: 1200,
    larguraPixels: 800,
    alturaPixels: 600,
    ordemExibicao: 0,
    principal: true,
    varianteIds: [],
    criadoEmUtc: '2026-11-16T03:00:00+00:00',
    conteudoUrl: '/api/admin/produtos/product-1/imagens/image-1/conteudo',
  };
  const secondImage: AdminProdutoImagem = {
    ...image,
    id: 'image-2',
    nomeOriginal: 'camomila.webp',
    ordemExibicao: 1,
    principal: false,
    conteudoUrl: '/api/admin/produtos/product-1/imagens/image-2/conteudo',
  };

  beforeEach(async () => {
    obterProduto = vi.fn<ProdutosService['obter']>().mockReturnValue(of(simpleProduct));
    listarImagens = vi.fn<ImagensProdutoService['listar']>().mockReturnValue(of([]));
    adicionar = vi.fn<ImagensProdutoService['adicionar']>().mockReturnValue(of(image));
    definirPrincipal = vi
      .fn<ImagensProdutoService['definirPrincipal']>()
      .mockReturnValue(of([image]));
    reordenar = vi
      .fn<ImagensProdutoService['reordenar']>()
      .mockReturnValue(of([image, secondImage]));
    associarVariante = vi
      .fn<ImagensProdutoService['associarVariante']>()
      .mockReturnValue(of([{ ...image, varianteIds: ['variant-2'] }]));
    desassociarVariante = vi
      .fn<ImagensProdutoService['desassociarVariante']>()
      .mockReturnValue(of([image]));
    remover = vi
      .fn<ImagensProdutoService['remover']>()
      .mockReturnValue(of(new HttpResponse<AdminProdutoImagemRemocao>({ status: 204 })));

    originalCreateObjectUrl = Object.getOwnPropertyDescriptor(URL, 'createObjectURL');
    originalRevokeObjectUrl = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL');
    createObjectUrl = vi.fn(() => 'blob:preview');
    revokeObjectUrl = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: createObjectUrl,
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: revokeObjectUrl,
    });

    await TestBed.configureTestingModule({
      imports: [AdminProdutoImagensComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => simpleProduct.id } } },
        },
        { provide: AdminAuthService, useValue: { logout: vi.fn(() => of(undefined)) } },
        {
          provide: ProdutosService,
          useValue: { obter: obterProduto },
        },
        {
          provide: ImagensProdutoService,
          useValue: {
            listar: listarImagens,
            consultar: vi.fn(),
            conteudo: vi.fn(),
            adicionar,
            definirPrincipal,
            reordenar,
            associarVariante,
            desassociarVariante,
            remover,
          },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    restoreUrlMethod('createObjectURL', originalCreateObjectUrl);
    restoreUrlMethod('revokeObjectURL', originalRevokeObjectUrl);
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('shows the product, empty-gallery guidance, and no required option link for a simple product', () => {
    expect(text()).toContain('Sabonete de Lavanda');
    expect(text()).toContain('0 de 5 fotos cadastradas');
    expect(text()).toContain('Sua galeria está vazia');
    expect(text()).toContain('rascunhos sem fotos');
    expect(text()).not.toContain('Opções do produto');
    expect(listarImagens).toHaveBeenCalledWith(simpleProduct.id);
  });

  it('limits selection by remaining gallery slots and rejects files over 5 MiB', () => {
    listarImagens.mockReturnValue(
      of([
        image,
        secondImage,
        { ...secondImage, id: 'image-3' },
        { ...secondImage, id: 'image-4' },
      ]),
    );
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();

    selectFiles([
      new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'large.jpg', { type: 'image/jpeg' }),
      new File(['ok'], 'one.jpg', { type: 'image/jpeg' }),
      new File(['ok'], 'two.png', { type: 'image/png' }),
    ]);

    expect(text()).toContain('4 de 5 fotos cadastradas');
    expect(text()).toContain('one.jpg');
    expect(text()).not.toContain('two.png');
    expect(text()).toContain('large.jpg: o tamanho máximo é 5 MiB.');
  });

  it('uploads selected files sequentially, retains a failed file, and refreshes the gallery after success', async () => {
    const first = new File(['one'], 'invalid.png', { type: 'image/png' });
    const second = new File(['two'], 'valid.webp', { type: 'image/webp' });
    adicionar
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 400 })))
      .mockReturnValueOnce(of(secondImage));
    listarImagens.mockReturnValue(of([secondImage]));
    selectFiles([first, second]);
    clickButton('Enviar fotos');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(adicionar).toHaveBeenCalledTimes(2);
      expect(text()).toContain('invalid.png');
      expect(text()).toContain('Envio não concluído');
      expect(text()).not.toContain('valid.webp');
      expect(text()).toContain('camomila.webp');
    });
    expect(adicionar).toHaveBeenNthCalledWith(1, simpleProduct.id, first);
    expect(adicionar).toHaveBeenNthCalledWith(2, simpleProduct.id, second);
    expect(listarImagens).toHaveBeenCalledTimes(2);
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:preview');
    expect(button('Tentar novamente o envio de invalid.png')).not.toBeNull();
  });

  it('does not retry automatically when the server reports an uncertain upload result', async () => {
    adicionar.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 503,
            error: { title: 'Resultado da operação desconhecido' },
          }),
      ),
    );
    selectFiles([new File(['bytes'], 'unknown.png', { type: 'image/png' })]);
    clickButton('Enviar fotos');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(text()).toContain('Envio não confirmado');
      expect(text()).toContain('Confira a galeria antes de tentar novamente.');
    });
    expect(adicionar).toHaveBeenCalledOnce();
    expect(button('Tentar novamente o envio de unknown.png')).toBeNull();
    expect(listarImagens).toHaveBeenCalledTimes(2);
  });

  it('keeps order unchanged until the API confirms the full reorder', () => {
    listarImagens.mockReturnValue(of([image, secondImage]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();
    const pending = new Subject<AdminProdutoImagem[]>();
    reordenar.mockReturnValue(pending);

    clickButton('Visualizar camomila.webp em destaque');
    expect(root().querySelector('figure img')?.getAttribute('src')).toBe(secondImage.conteudoUrl);
    clickButton('Mover lavanda.png para baixo');
    expect(reordenar).toHaveBeenCalledWith(simpleProduct.id, ['image-2', 'image-1']);
    expect(imageNames()).toEqual(['lavanda.png', 'camomila.webp']);
    expect(root().querySelector('figure img')?.getAttribute('src')).toBe(secondImage.conteudoUrl);

    pending.next([
      { ...secondImage, ordemExibicao: 0 },
      { ...image, ordemExibicao: 1 },
    ]);
    pending.complete();
    fixture.detectChanges();
    expect(imageNames()).toEqual(['camomila.webp', 'lavanda.png']);
  });

  it('reflects confirmed primary and individual option-link changes only', () => {
    const productWithOptions: AdminProduto = {
      ...simpleProduct,
      variantes: [simpleProduct.variantes[0], secondVariant],
    };
    obterProduto.mockReturnValue(of(productWithOptions));
    listarImagens.mockReturnValue(of([{ ...image, principal: false }, secondImage]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();

    expect(text()).toContain('Opções do produto');
    associarVariante.mockReturnValue(
      of([{ ...image, principal: false, varianteIds: ['variant-2'] }]),
    );
    const linkCheckbox = root().querySelectorAll('input[type="checkbox"]')[1] as HTMLInputElement;
    expect(linkCheckbox.checked).toBe(false);
    linkCheckbox.checked = true;
    linkCheckbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(associarVariante).toHaveBeenCalledWith(simpleProduct.id, image.id, 'variant-2');
    expect((root().querySelectorAll('input[type="checkbox"]')[1] as HTMLInputElement).checked).toBe(
      true,
    );

    desassociarVariante.mockReturnValue(of([{ ...image, principal: false, varianteIds: [] }]));
    const updatedLinkCheckbox = root().querySelectorAll(
      'input[type="checkbox"]',
    )[1] as HTMLInputElement;
    updatedLinkCheckbox.checked = false;
    updatedLinkCheckbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(desassociarVariante).toHaveBeenCalledWith(simpleProduct.id, image.id, 'variant-2');
    expect(updatedLinkCheckbox.checked).toBe(false);

    clickButton('Definir lavanda.png como foto principal');
    expect(definirPrincipal).toHaveBeenCalledWith(simpleProduct.id, image.id);
  });

  it('does not change the visual order when the server rejects a reorder', () => {
    listarImagens.mockReturnValue(of([image, secondImage]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();
    reordenar.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'Conflito na galeria', detail: 'A ordem mudou.' },
          }),
      ),
    );

    clickButton('Mover lavanda.png para baixo');

    expect(imageNames()).toEqual(['lavanda.png', 'camomila.webp']);
    expect(text()).toContain('A operação conflita com o estado atual do produto.');
  });

  it('confirms removals, reports pending physical cleanup, and prevents deleting the last published photo', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    listarImagens.mockReturnValueOnce(of([image])).mockReturnValueOnce(of([]));
    remover.mockReturnValue(
      of(
        new HttpResponse({
          status: 200,
          body: {
            removidaDoCatalogo: true,
            limpezaFisicaConcluida: false,
            mensagem: 'A exclusão física está pendente.',
          },
        }),
      ),
    );
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();
    clickButton('Remover lavanda.png');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(text()).toContain(
        'A foto foi removida da galeria. A limpeza do arquivo físico ficou pendente.',
      );
      expect(text()).toContain('Sua galeria está vazia');
    });
    expect(globalThis.confirm).toHaveBeenCalledOnce();
    expect(remover).toHaveBeenCalledWith(simpleProduct.id, image.id);

    obterProduto.mockReturnValue(of({ ...simpleProduct, publicado: true }));
    listarImagens.mockReturnValue(of([image]));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();
    expect(text()).toContain('despublique o produto ou adicione outra foto');
    expect(button('Remover lavanda.png')?.disabled).toBe(true);
    expect(remover).toHaveBeenCalledOnce();
  });

  it('uses the server-promoted primary after removing the previous primary', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    const promoted = { ...secondImage, ordemExibicao: 0, principal: true };
    listarImagens.mockReturnValueOnce(of([image, secondImage])).mockReturnValueOnce(of([promoted]));
    remover.mockReturnValue(of(new HttpResponse<AdminProdutoImagemRemocao>({ status: 204 })));
    fixture.destroy();
    fixture = TestBed.createComponent(AdminProdutoImagensComponent);
    fixture.detectChanges();

    clickButton('Remover lavanda.png');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(imageNames()).toEqual(['camomila.webp']);
      expect(root().querySelector('figure img')?.getAttribute('src')).toBe(promoted.conteudoUrl);
    });
  });

  it('releases preview object URLs when the component is destroyed', () => {
    selectFiles([new File(['preview'], 'preview.png', { type: 'image/png' })]);
    fixture.destroy();
    expect(createObjectUrl).toHaveBeenCalledOnce();
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:preview');
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return root().textContent ?? '';
  }

  function imageNames(): string[] {
    return Array.from(root().querySelectorAll('li > div p.break-words')).map(
      (element) => element.textContent?.trim() ?? '',
    );
  }

  function button(label: string): HTMLButtonElement | null {
    return (
      Array.from(root().querySelectorAll('button')).find(
        (item) => item.getAttribute('aria-label') === label || item.textContent?.trim() === label,
      ) ?? null
    );
  }

  function clickButton(label: string): void {
    const target = button(label);
    if (!target) {
      throw new Error(`Button not found: ${label}`);
    }
    if (target.disabled) {
      throw new Error(`Button is disabled: ${label}`);
    }
    target.click();
    fixture.detectChanges();
  }

  function selectFiles(files: File[]): void {
    const input = root().querySelector('#selecionar-imagens') as HTMLInputElement;
    Object.defineProperty(input, 'files', { configurable: true, value: files });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function restoreUrlMethod(
    name: 'createObjectURL' | 'revokeObjectURL',
    descriptor: PropertyDescriptor | undefined,
  ): void {
    if (descriptor) {
      Object.defineProperty(URL, name, descriptor);
    } else {
      Reflect.deleteProperty(URL, name);
    }
  }
});
