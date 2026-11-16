import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProdutoCatalogoResumo } from './catalogo-publico.service';
import { PublicProductCardComponent } from './public-product-card.component';

describe('PublicProductCardComponent', () => {
  let fixture: ComponentFixture<PublicProductCardComponent>;
  const product: ProdutoCatalogoResumo = {
    id: 'product-1',
    nome: 'Sabonete artesanal de lavanda',
    categoriaId: 'category-1',
    categoriaNome: 'Sabonetes Artesanais',
    fotoPrincipal: {
      id: 'image-1',
      conteudoUrl: '/api/catalogo/produtos/product-1/imagens/image-1/conteudo',
    },
    precoMinimo: 18,
    precoMaximo: 18,
    unidadesVenda: ['UN'],
    medidas: [{ opcaoId: 'option-1', pesoLiquidoGramas: 100, volumeLiquidoMililitros: null }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PublicProductCardComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(PublicProductCardComponent);
    fixture.componentRef.setInput('product', product);
    fixture.detectChanges();
  });

  it('shows the public photo, category, price, unit and physical measure without a purchase action', () => {
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('img')?.getAttribute('src')).toBe(
      product.fotoPrincipal.conteudoUrl,
    );
    expect(element.textContent).toContain('Sabonetes Artesanais');
    expect(element.textContent?.replace(/\u00a0/g, ' ')).toContain('R$ 18,00');
    expect(element.textContent).toContain('Unidade');
    expect(element.textContent).toContain('100 g');
    expect(element.querySelector('button')).toBeNull();
    expect(element.querySelector('a')?.getAttribute('href')).toBe('/produtos/product-1');
    expect(element.textContent?.toLowerCase()).not.toContain('estoque');
  });

  it('uses a neutral placeholder when the public image fails', () => {
    const image = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Imagem não disponível');
  });

  it('shows a starting price and a variation hint when active options differ', () => {
    fixture.componentRef.setInput('product', {
      ...product,
      precoMinimo: 18,
      precoMaximo: 25,
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('A partir de');
    expect(fixture.nativeElement.textContent).toContain('Variações');
  });

  it('uses friendly labels for known units and preserves unknown legacy values', () => {
    fixture.componentRef.setInput('product', {
      ...product,
      unidadesVenda: ['KIT', 'PAR', 'PCT', 'CX', 'CJ', 'LEG'],
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Kit');
    expect(text).toContain('Par');
    expect(text).toContain('Pacote');
    expect(text).toContain('Caixa');
    expect(text).toContain('Conjunto');
    expect(text).toContain('LEG');
  });

  it('preserves catalog filters in the product link when provided', () => {
    fixture.componentRef.setInput('catalogReturn', {
      busca: 'lavanda',
      categoriaId: 'category-1',
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a')?.getAttribute('href')).toBe(
      '/produtos/product-1?busca=lavanda&categoriaId=category-1',
    );
  });
});
