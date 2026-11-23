import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { DescontoFrete } from '../core/atendimento.service';
import { formatarCentavos } from '../public-store/carrinho.service';

@Component({
  selector: 'app-frete-pedido',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [':host { display: block; min-width: 0; overflow-wrap: anywhere; }'],
  template: `
    @if (desconto(); as promocao) {
      <p>Frete original: {{ moeda(promocao.originalCentavos) }}</p>
      <p>Desconto de frete: −{{ moeda(promocao.descontoCentavos) }}</p>
      <p>Campanha: {{ promocao.nomeCampanha }}</p>
      <p>Frete líquido: <strong>{{ frete() === '0' ? 'Frete grátis' : moeda(frete()) }}</strong></p>
    } @else {
      <p>Frete: <strong>{{ moeda(frete()) }}</strong></p>
    }
  `,
})
export class FretePedidoComponent {
  readonly frete = input.required<string>();
  readonly desconto = input<DescontoFrete | null>();
  protected readonly moeda = formatarCentavos;
}
