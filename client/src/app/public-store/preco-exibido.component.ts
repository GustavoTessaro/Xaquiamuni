import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { formatarCentavos } from './carrinho.service';
import { precoValido } from './catalogo-formatacao';
import type { PrecoCatalogo } from './catalogo-publico.service';

@Component({
  selector: 'app-preco-exibido',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (desconto(); as oferta) {
      <del><span class="sr-only">Preço regular: </span>{{ formatar(oferta.regular) }}</del>
      <strong><span class="sr-only">Preço promocional: </span>{{ formatar(oferta.final) }}</strong>
      @if (oferta.off > 0) {
        <span class="selo">{{ percentual(oferta.off) }}% <span aria-hidden="true">OFF</span><span class="sr-only">de desconto</span></span>
      }
    } @else {
      <span>{{ textoNormal() }}</span>
    }
  `,
  styles: `
    :host { display: inline-flex; flex-wrap: wrap; align-items: baseline; column-gap: .4em; row-gap: .2em; max-width: 100%; }
    del { font-size: .8em; font-weight: 400; }
    .selo {
      border-radius: var(--brand-radius-pill);
      padding: .13em .5em;
      font: 700 .72rem/1.4 var(--brand-text);
      white-space: nowrap;
      color: var(--brand-ink);
      background: var(--brand-gold-light);
    }
  `,
})
export class PrecoExibidoComponent {
  readonly textoNormal = input.required<string>();
  readonly preco = input<PrecoCatalogo | null>();
  readonly regularHistorico = input<string>();
  readonly finalHistorico = input<string>();
  protected readonly formatar = formatarCentavos;
  protected readonly percentual = formatarOff;

  protected readonly desconto = computed(() => {
    const preco = this.preco();
    if (preco?.promocaoId) {
      return { regular: preco.regularCentavos, final: preco.finalCentavos, off: preco.offPontosBase };
    }
    const regular = this.regularHistorico();
    const final = this.finalHistorico();
    if (!precoValido(regular) || !precoValido(final) || BigInt(final) >= BigInt(regular)) return null;
    // O pedido já tem os valores congelados. Deriva só o rótulo histórico.
    const off = Number((BigInt(regular) - BigInt(final)) * 10_000n / BigInt(regular));
    return { regular, final, off };
  });
}

export function formatarOff(pontosBase: number): string {
  return new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 }).format(pontosBase / 100);
}
