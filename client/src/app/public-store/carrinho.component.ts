import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CarrinhoService, ItemCarrinho, formatarCentavos } from './carrinho.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { revalidarCarrinho } from './revalidar-carrinho';
import { lerTentativaCompra } from './checkout.service';
import { PrecoExibidoComponent } from './preco-exibido.component';

@Component({
  selector: 'app-carrinho',
  imports: [RouterLink, PrecoExibidoComponent],
  templateUrl: './carrinho.component.html',
  styleUrl: './carrinho.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CarrinhoComponent implements OnInit {
  protected readonly tentativaPendente = !!lerTentativaCompra(inject(DOCUMENT).defaultView);
  protected readonly carrinho = inject(CarrinhoService);
  protected readonly erroQuantidade = signal<string | null>(null);
  protected readonly mensagemQuantidade = signal('');
  protected readonly precoMudou = signal(false);
  protected readonly formatarPreco = formatarCentavos;
  private readonly catalogo = inject(CatalogoPublicoService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    for (const id of new Set(this.carrinho.itens().map((item) => item.produtoId))) {
      this.carrinho.sinalizarDisponibilidade(id, null);
    }
    revalidarCarrinho(this.carrinho, this.catalogo)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((mudou) => {
        if (mudou) this.precoMudou.set(true);
      });
  }

  protected alterarQuantidade(item: ItemCarrinho, campo: HTMLInputElement): void {
    const valor = /^\d+$/.test(campo.value) ? Number(campo.value) : NaN;
    const resultado = this.carrinho.alterarQuantidade(item.varianteId, valor);
    this.erroQuantidade.set(resultado === 'ok' ? null : item.varianteId);
    if (resultado !== 'ok') {
      campo.value = String(item.quantidade);
      this.mensagemQuantidade.set(
        resultado === 'indisponivel'
          ? 'Para aumentar a quantidade, a opção precisa estar disponível.'
          : 'Informe uma quantidade inteira, positiva e dentro do limite numérico do carrinho.',
      );
    }
  }

  protected remover(varianteId: string): void {
    this.erroQuantidade.set(null);
    this.carrinho.remover(varianteId);
  }

  protected esvaziar(): void {
    this.erroQuantidade.set(null);
    this.carrinho.esvaziar();
  }
}
