import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import {
  CatalogoPublicoService,
  ProdutoCatalogoDetalhe,
  ProdutoCatalogoImagem,
} from './catalogo-publico.service';
import { ProdutoDetalheComponent } from './produto-detalhe.component';

const soapMainImage: ProdutoCatalogoImagem = {
  id: 'soap-main',
  conteudoUrl: '/public-images/soap-main',
  ordemExibicao: 1,
  principal: true,
  opcaoIds: [],
};
const soapGalleryImage: ProdutoCatalogoImagem = {
  id: 'soap-gallery',
  conteudoUrl: '/public-images/soap-gallery',
  ordemExibicao: 2,
  principal: false,
  opcaoIds: [],
};

const simpleProduct: ProdutoCatalogoDetalhe = {
  id: 'soap',
  nome: 'Sabonete de Argila Verde',
  descricao: 'Produzido artesanalmente.',
  categoriaId: 'soaps',
  categoriaNome: 'Sabonetes Artesanais',
  requerSelecaoDeOpcao: false,
  opcaoPrincipalId: 'soap-default',
  disponivel: true,
  fotoPrincipal: soapMainImage,
  galeria: [soapMainImage, soapGalleryImage],
  caracteristicas: [],
  opcoes: [
    {
      id: 'soap-default',
      identificacao: null,
      unidadeVenda: 'UN',
      precoVenda: 18,
      disponivel: true,
      principal: true,
      pesoLiquidoGramas: 100,
      volumeLiquidoMililitros: null,
      caracteristicas: [],
    },
  ],
};

const candleMainImage: ProdutoCatalogoImagem = {
  id: 'candle-small',
  conteudoUrl: '/public-images/candle-small',
  ordemExibicao: 1,
  principal: true,
  opcaoIds: ['candle-small-option'],
};
const candleGeneralImage: ProdutoCatalogoImagem = {
  id: 'candle-general',
  conteudoUrl: '/public-images/candle-general',
  ordemExibicao: 2,
  principal: false,
  opcaoIds: [],
};
const candleLargeImage: ProdutoCatalogoImagem = {
  id: 'candle-large',
  conteudoUrl: '/public-images/candle-large',
  ordemExibicao: 3,
  principal: false,
  opcaoIds: ['candle-large-option'],
};

const candleProduct: ProdutoCatalogoDetalhe = {
  id: 'candle',
  nome: 'Vela Aromática Artesanal',
  descricao: null,
  categoriaId: 'candles',
  categoriaNome: 'Velas Aromáticas Artesanais',
  requerSelecaoDeOpcao: true,
  opcaoPrincipalId: 'candle-small-option',
  disponivel: true,
  fotoPrincipal: candleMainImage,
  galeria: [candleMainImage, candleGeneralImage, candleLargeImage],
  caracteristicas: [
    {
      id: 'characteristic-color',
      nome: 'Cor',
      tipoMedidaFisica: null,
      valores: [
        { id: 'value-white', nome: 'Branca', valorMedidaCanonico: null },
        { id: 'value-pink', nome: 'Rosa', valorMedidaCanonico: null },
      ],
    },
    {
      id: 'characteristic-fragrance',
      nome: 'Fragrância',
      tipoMedidaFisica: null,
      valores: [
        { id: 'value-lavender', nome: 'Lavanda', valorMedidaCanonico: null },
        { id: 'value-vanilla', nome: 'Baunilha', valorMedidaCanonico: null },
      ],
    },
    {
      id: 'characteristic-weight',
      nome: 'Peso',
      tipoMedidaFisica: 'Peso',
      valores: [
        { id: 'value-100g', nome: '100 g', valorMedidaCanonico: 100 },
        { id: 'value-200g', nome: '200 g', valorMedidaCanonico: 200 },
        { id: 'value-inactive', nome: '300 g', valorMedidaCanonico: 300 },
      ],
    },
  ],
  opcoes: [
    {
      id: 'candle-small-option',
      identificacao: 'Padrão',
      unidadeVenda: 'UN',
      precoVenda: 35,
      disponivel: false,
      principal: true,
      pesoLiquidoGramas: 100,
      volumeLiquidoMililitros: null,
      caracteristicas: [
        {
          caracteristicaId: 'characteristic-color',
          caracteristicaNome: 'Cor',
          valorId: 'value-white',
          valorNome: 'Branca',
          valorMedidaCanonico: null,
        },
        {
          caracteristicaId: 'characteristic-fragrance',
          caracteristicaNome: 'Fragrância',
          valorId: 'value-lavender',
          valorNome: 'Lavanda',
          valorMedidaCanonico: null,
        },
        {
          caracteristicaId: 'characteristic-weight',
          caracteristicaNome: 'Peso',
          valorId: 'value-100g',
          valorNome: '100 g',
          valorMedidaCanonico: 100,
        },
      ],
    },
    {
      id: 'candle-large-option',
      identificacao: 'Padrão',
      unidadeVenda: 'PCT',
      precoVenda: 55,
      disponivel: true,
      principal: false,
      pesoLiquidoGramas: 200,
      volumeLiquidoMililitros: null,
      caracteristicas: [
        {
          caracteristicaId: 'characteristic-color',
          caracteristicaNome: 'Cor',
          valorId: 'value-pink',
          valorNome: 'Rosa',
          valorMedidaCanonico: null,
        },
        {
          caracteristicaId: 'characteristic-fragrance',
          caracteristicaNome: 'Fragrância',
          valorId: 'value-vanilla',
          valorNome: 'Baunilha',
          valorMedidaCanonico: null,
        },
        {
          caracteristicaId: 'characteristic-weight',
          caracteristicaNome: 'Peso',
          valorId: 'value-200g',
          valorNome: '200 g',
          valorMedidaCanonico: 200,
        },
      ],
    },
  ],
};

describe('ProdutoDetalheComponent', () => {
  async function createPage(
    consultarProduto: (id: string) => Observable<ProdutoCatalogoDetalhe>,
    url = '/produtos/soap',
  ): Promise<RouterTestingHarness> {
    await TestBed.configureTestingModule({
      imports: [ProdutoDetalheComponent],
      providers: [
        provideRouter([{ path: 'produtos/:id', component: ProdutoDetalheComponent }]),
        {
          provide: CatalogoPublicoService,
          useValue: { consultarProduto },
        },
      ],
    }).compileComponents();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, ProdutoDetalheComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    return harness;
  }

  function choose(harness: RouterTestingHarness, optionId: string): void {
    const select = harness.fixture.nativeElement.querySelector('#produto-opcao') as HTMLSelectElement;
    select.value = optionId;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    harness.fixture.detectChanges();
  }

  function text(harness: RouterTestingHarness): string {
    return (harness.fixture.nativeElement.textContent as string).replace(/\u00a0/g, ' ');
  }

  it('loads a simple product and selects its default option without exposing a selector or internal label', async () => {
    const consultarProduto = vi.fn(() => of(simpleProduct));
    const harness = await createPage(consultarProduto);

    expect(consultarProduto).toHaveBeenCalledWith('soap');
    expect(text(harness)).toContain('Sabonete de Argila Verde');
    expect(text(harness)).toContain('R$ 18,00');
    expect(text(harness)).toContain('Unidade');
    expect(text(harness)).toContain('100 g');
    expect(text(harness)).toContain('Disponível');
    expect(text(harness)).not.toContain('Padrão');
    expect(harness.fixture.nativeElement.querySelector('.product-detail__selectors')).toBeNull();
    expect(text(harness).toLowerCase()).not.toContain('estoque');
    expect(text(harness).toLowerCase()).not.toContain('comprar');
  });

  it('shows only active combinations and updates price, unit, measures and variant photos', async () => {
    const harness = await createPage(
      vi.fn(() => of(candleProduct)),
      '/produtos/candle',
    );
    const disponibilidade = harness.fixture.nativeElement.querySelector(
      '[data-testid="product-availability"][role="status"]',
    ) as HTMLElement;
    expect(disponibilidade.textContent).toContain('Opção esgotada');
    const adicionar = harness.fixture.nativeElement.querySelector(
      '.product-detail__add-to-cart',
    ) as HTMLButtonElement;
    expect(adicionar.disabled).toBe(true);

    expect(text(harness)).toContain('R$ 35,00');
    expect(text(harness)).toContain('100 g');
    expect(
      harness.fixture.nativeElement
        .querySelector('.product-detail__main-image img')
        ?.getAttribute('src'),
    ).toBe(candleMainImage.conteudoUrl);

    const select = harness.fixture.nativeElement.querySelector('#produto-opcao') as HTMLSelectElement;
    expect(select.labels?.[0]?.textContent?.trim()).toMatch(/^Opção/);
    expect([...select.options].map((option) => option.value)).toEqual(candleProduct.opcoes.map(o => o.id));
    choose(harness, 'candle-large-option');
    expect(disponibilidade.textContent).toContain('Disponível');
    expect(adicionar.disabled).toBe(false);
    expect(text(harness)).toContain('R$ 55,00');
    expect(text(harness)).toContain('Pacote');
    expect(text(harness)).toContain('200 g');
    expect(
      harness.fixture.nativeElement
        .querySelector('.product-detail__main-image img')
        ?.getAttribute('src'),
    ).toBe(candleLargeImage.conteudoUrl);
    const variantThumbnails = [
      ...harness.fixture.nativeElement.querySelectorAll('.product-detail__thumbnails img'),
    ].map((image) => image.getAttribute('src'));
    expect(variantThumbnails).toContain(candleGeneralImage.conteudoUrl);
    expect(variantThumbnails).not.toContain(candleMainImage.conteudoUrl);
  });

  it('mostra esgotado no detalhe simples e omite o indicador quando a consulta falha', async () => {
    const consultar = vi
      .fn<() => Observable<ProdutoCatalogoDetalhe>>()
      .mockReturnValueOnce(
        of({
          ...simpleProduct,
          disponivel: false,
          opcoes: simpleProduct.opcoes.map((opcao) => ({ ...opcao, disponivel: false })),
        }),
      )
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));
    const harness = await createPage(consultar);
    expect(text(harness)).toContain('Esgotado');

    await harness.navigateByUrl('/produtos/falha', ProdutoDetalheComponent);
    harness.fixture.detectChanges();
    expect(
      harness.fixture.nativeElement.querySelector('[data-testid="product-availability"]'),
    ).toBeNull();
  });

  it('uses the current shared feature label and falls back to option after the configuration changes', async () => {
    const peso = { ...candleProduct, opcoes: candleProduct.opcoes.map((opcao) => ({
      ...opcao, caracteristicas: opcao.caracteristicas.filter((c) => c.caracteristicaId === 'characteristic-weight'),
    })) };
    const consultar = vi.fn().mockReturnValueOnce(of(peso)).mockReturnValueOnce(of(candleProduct));
    const harness = await createPage(consultar, '/produtos/candle');
    const seletor = () => harness.fixture.nativeElement.querySelector('#produto-opcao') as HTMLSelectElement;
    expect(seletor().labels?.[0]?.textContent?.trim()).toMatch(/^Peso/);
    expect([...seletor().options].map(o => o.textContent?.trim())).toEqual(['100 g', '200 g']);
    await harness.navigateByUrl('/produtos/candle-atualizado', ProdutoDetalheComponent);
    harness.fixture.detectChanges();
    expect(seletor().labels?.[0]?.textContent?.trim()).toMatch(/^Opção/);
  });

  it('selects thumbnails and provides a fallback when the selected image fails', async () => {
    const harness = await createPage(vi.fn(() => of(simpleProduct)));
    const secondThumbnail = harness.fixture.nativeElement.querySelector(
      '.product-detail__thumbnails button[aria-label*="foto 2"]',
    ) as HTMLButtonElement;
    secondThumbnail.click();
    harness.fixture.detectChanges();

    const mainImage = harness.fixture.nativeElement.querySelector(
      '.product-detail__main-image img',
    ) as HTMLImageElement;
    expect(mainImage.getAttribute('src')).toBe(soapGalleryImage.conteudoUrl);
    mainImage.dispatchEvent(new Event('error'));
    harness.fixture.detectChanges();

    expect(
      harness.fixture.nativeElement.querySelector('.product-detail__main-image img'),
    ).toBeNull();
    expect(text(harness)).toContain('Imagem indisponível');
  });

  it('renders a not-found state for products outside the public catalog', async () => {
    const consultarProduto = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 })));
    const harness = await createPage(consultarProduto, '/produtos/private');

    expect(text(harness)).toContain('Produto não encontrado');
    expect(text(harness)).toContain('não está disponível no catálogo público');
    expect(harness.fixture.nativeElement.querySelector('.product-detail__layout')).toBeNull();
  });

  it('allows retrying a temporary error', async () => {
    const consultarProduto = vi
      .fn<() => Observable<ProdutoCatalogoDetalhe>>()
      .mockImplementationOnce(() => throwError(() => new HttpErrorResponse({ status: 503 })))
      .mockImplementationOnce(() => of(simpleProduct));
    const harness = await createPage(consultarProduto);

    expect(text(harness)).toContain('Não foi possível carregar o produto');
    (harness.fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(consultarProduto).toHaveBeenCalledTimes(2);
    expect(text(harness)).toContain('Sabonete de Argila Verde');
  });

  it('cancels the previous request and clears obsolete product data when the route ID changes', async () => {
    const firstRequest = new Subject<ProdutoCatalogoDetalhe>();
    const consultarProduto = vi.fn((id: string) =>
      id === 'soap' ? firstRequest.asObservable() : of(candleProduct),
    );
    const harness = await createPage(consultarProduto);

    expect(text(harness)).toContain('Carregando produto');
    await harness.navigateByUrl('/produtos/candle', ProdutoDetalheComponent);
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();

    expect(firstRequest.observed).toBe(false);
    expect(consultarProduto).toHaveBeenCalledTimes(2);
    expect(text(harness)).toContain('Vela Aromática Artesanal');
    expect(text(harness)).not.toContain('Sabonete de Argila Verde');
  });

  it('preserves search and category filters in the return link', async () => {
    const harness = await createPage(
      vi.fn(() => of(candleProduct)),
      '/produtos/candle?busca=vela&categoriaId=candles',
    );
    const backLink = harness.fixture.nativeElement.querySelector(
      '.product-detail__back',
    ) as HTMLAnchorElement;

    expect(backLink.getAttribute('href')).toBe('/produtos?busca=vela&categoriaId=candles');
    expect(
      harness.fixture.nativeElement
        .querySelector('.product-detail__breadcrumb a:nth-of-type(2)')
        ?.getAttribute('href'),
    ).toBe('/produtos?categoriaId=candles');
  });
});
