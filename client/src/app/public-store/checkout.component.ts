import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { CarrinhoService, formatarCentavos } from './carrinho.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { CheckoutService } from './checkout.service';
import { itensCompraValidos, mesmosItensCompra } from './pedidos.service';
import { revalidarCarrinho } from './revalidar-carrinho';

@Component({
  selector: 'app-checkout',
  imports: [RouterLink],
  templateUrl: './checkout.component.html',
  styleUrl: './compra.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckoutComponent {
  protected readonly carrinho = inject(CarrinhoService);
  protected readonly checkout = inject(CheckoutService);
  protected readonly formatarPreco = formatarCentavos;
  protected readonly erro = signal('');
  protected readonly consultando = signal(true);
  private readonly destroy = inject(DestroyRef);
  private readonly router = inject(Router);
  protected readonly itensCompra = computed(() =>
    this.carrinho.itens().map((item) => ({
      produtoId: item.produtoId,
      produtoVarianteId: item.varianteId,
      quantidade: item.quantidade,
    })),
  );
  protected readonly indisponivel = computed(() =>
    this.carrinho.itens().some((item) => item.disponivel === false),
  );
  protected readonly valido = computed(() => itensCompraValidos(this.itensCompra()));
  protected readonly carrinhoMudou = computed(() => {
    const pendente = this.checkout.pendente();
    return !!pendente && !mesmosItensCompra(this.itensCompra(), pendente.itens);
  });

  protected readonly itensOriginais = computed(() => {
    const atuais = new Map(this.carrinho.itens().map((item) => [item.varianteId, item]));
    return (this.checkout.pendente()?.itens ?? []).map((item) => ({
      ...item,
      atual: atuais.get(item.produtoVarianteId),
    }));
  });

  constructor() {
    this.checkout.atualizarTentativa();
    revalidarCarrinho(this.carrinho, inject(CatalogoPublicoService))
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.consultando.set(false)),
      )
      .subscribe();
  }

  protected async enviar(recuperar: boolean): Promise<void> {
    if (
      this.checkout.enviando() ||
      (!recuperar && (this.consultando() || this.indisponivel() || !this.valido()))
    ) {
      return;
    }
    this.erro.set('');
    const resultado = await this.checkout.enviar(this.itensCompra(), recuperar);
    if (this.destroy.destroyed || !resultado) return;
    switch (resultado.estado) {
      case 'login':
        await this.router.navigate(['/login'], { queryParams: { retorno: 'checkout' } });
        break;
      case 'erro':
        this.erro.set(resultado.mensagem);
        break;
      case 'sucesso':
        await this.router.navigate(['/pedidos', resultado.pedido.id], { info: resultado.pedido });
        break;
    }
  }
}
