import { HttpErrorResponse } from '@angular/common/http';
import { catchError, from, map, mergeMap, of, tap, timeout } from 'rxjs';
import { CarrinhoService } from './carrinho.service';
import { CatalogoPublicoService } from './catalogo-publico.service';

export function revalidarCarrinho(carrinho: CarrinhoService, catalogo: CatalogoPublicoService) {
  const produtos = [...new Set(carrinho.itens().map((item) => item.produtoId))];
  return from(produtos).pipe(
    mergeMap(
      (id) =>
        catalogo.consultarProduto(id).pipe(
          timeout(15_000),
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
    tap(({ id, produto, ausente }) => {
      if (produto?.id === id) {
        carrinho.atualizarDisponibilidade(produto);
      } else if (ausente) {
        carrinho.sinalizarDisponibilidade(id, false);
      }
    }),
  );
}
