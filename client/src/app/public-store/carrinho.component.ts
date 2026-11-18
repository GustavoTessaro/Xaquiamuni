import { HttpErrorResponse } from '@angular/common/http';
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
import { catchError, from, map, mergeMap, of } from 'rxjs';
import { CarrinhoService, ItemCarrinho, formatarCentavos } from './carrinho.service';
import { CatalogoPublicoService } from './catalogo-publico.service';

@Component({
  selector: 'app-carrinho',
  imports: [RouterLink],
  templateUrl: './carrinho.component.html',
  styleUrl: './carrinho.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CarrinhoComponent implements OnInit {
  protected readonly carrinho = inject(CarrinhoService);
  protected readonly erroQuantidade = signal<string | null>(null);
  protected readonly mensagemQuantidade = signal('');
  protected readonly formatarPreco = formatarCentavos;
  private readonly catalogo = inject(CatalogoPublicoService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    const produtos = [...new Set(this.carrinho.itens().map((item) => item.produtoId))];
    for (const id of produtos) {
      this.carrinho.sinalizarDisponibilidade(id, null);
    }

    from(produtos)
      .pipe(
        mergeMap(
          (id) =>
            this.catalogo.consultarProduto(id).pipe(
              map((produto) => ({ id, produto, ausente: false })),
              catchError((erro: unknown) =>
                of({
                  id,
                  produto: null,
                  ausente: erro instanceof HttpErrorResponse && erro.status === 404,
                }),
              ),
            ),
          4,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ id, produto, ausente }) => {
        if (produto?.id === id) {
          this.carrinho.atualizarDisponibilidade(produto);
        } else {
          this.carrinho.sinalizarDisponibilidade(id, ausente ? false : null);
        }
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
