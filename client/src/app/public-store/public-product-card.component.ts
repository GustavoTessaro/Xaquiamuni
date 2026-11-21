import { ChangeDetectionStrategy, inject, Component, effect, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogoPublicoRetorno, ProdutoCatalogoResumo } from './catalogo-publico.service';
import { formatarUnidadeVenda } from './catalogo-formatacao';
import { StorefrontContext } from './storefront-context';
import { formatarCentavos } from './carrinho.service';
import { PrecoExibidoComponent } from './preco-exibido.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PrecoExibidoComponent],
  selector: 'app-public-product-card',
  styleUrl: './public-product-card.component.css',
  templateUrl: './public-product-card.component.html',
})
export class PublicProductCardComponent {
  protected readonly contexto = inject(StorefrontContext);
  readonly product = input.required<ProdutoCatalogoResumo>();
  readonly catalogReturn = input<CatalogoPublicoRetorno | null>(null);
  readonly availabilityKnown = input(true);
  protected readonly imageUnavailable = signal(false);

  private readonly resetImageFallback = effect(() => {
    this.product().fotoPrincipal.conteudoUrl;
    this.imageUnavailable.set(false);
  });

  protected formatPrice(price: number, centavos?: string | null): string {
    if (centavos != null) return formatarCentavos(centavos);
    return new Intl.NumberFormat('pt-BR', {
      style: 'currency',
      currency: 'BRL',
    }).format(price);
  }

  protected get temFaixaPreco(): boolean {
    const produto = this.product();
    return produto.menorPreco && produto.maiorPreco
      ? produto.menorPreco.preco.finalCentavos !== produto.maiorPreco.preco.finalCentavos
      : produto.precoMinimo !== produto.precoMaximo;
  }

  protected formatMeasure(value: number, unit: 'g' | 'ml'): string {
    return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(value)} ${unit}`;
  }

  protected get hasSelectableOptions(): boolean {
    const product = this.product();
    return (
      this.temFaixaPreco ||
      product.unidadesVenda.length > 1 ||
      product.medidas.length > 1
    );
  }

  protected get units(): string {
    return [...new Set(this.product().unidadesVenda)].map(formatarUnidadeVenda).join(' · ');
  }

  protected get returnQueryParams(): CatalogoPublicoRetorno | null {
    return this.catalogReturn();
  }

  protected get measures(): string[] {
    const seen = new Set<string>();
    for (const measure of this.product().medidas) {
      if (measure.pesoLiquidoGramas !== null) {
        seen.add(this.formatMeasure(measure.pesoLiquidoGramas, 'g'));
      }
      if (measure.volumeLiquidoMililitros !== null) {
        seen.add(this.formatMeasure(measure.volumeLiquidoMililitros, 'ml'));
      }
    }
    return [...seen];
  }

  protected onImageError(): void {
    this.imageUnavailable.set(true);
  }
}
