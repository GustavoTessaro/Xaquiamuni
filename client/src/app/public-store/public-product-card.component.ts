import { ChangeDetectionStrategy, Component, effect, input, signal } from '@angular/core';
import { ProdutoCatalogoResumo } from './catalogo-publico.service';

const saleUnitLabels: Record<string, string> = {
  UN: 'Unidade',
  KIT: 'Kit',
  PAR: 'Par',
  PCT: 'Pacote',
  CX: 'Caixa',
  CJ: 'Conjunto',
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-public-product-card',
  styleUrl: './public-product-card.component.css',
  templateUrl: './public-product-card.component.html',
})
export class PublicProductCardComponent {
  readonly product = input.required<ProdutoCatalogoResumo>();
  protected readonly imageUnavailable = signal(false);

  private readonly resetImageFallback = effect(() => {
    this.product().fotoPrincipal.conteudoUrl;
    this.imageUnavailable.set(false);
  });

  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('pt-BR', {
      style: 'currency',
      currency: 'BRL',
    }).format(price);
  }

  protected formatMeasure(value: number, unit: 'g' | 'ml'): string {
    return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(value)} ${unit}`;
  }

  protected get hasSelectableOptions(): boolean {
    const product = this.product();
    return (
      product.precoMinimo !== product.precoMaximo ||
      product.unidadesVenda.length > 1 ||
      product.medidas.length > 1
    );
  }

  protected get units(): string {
    return [...new Set(this.product().unidadesVenda)]
      .map((unit) => saleUnitLabels[unit.trim().toUpperCase()] ?? unit)
      .join(' · ');
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
