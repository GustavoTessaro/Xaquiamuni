import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, distinctUntilChanged, map, of, startWith, switchMap } from 'rxjs';
import { formatarCentavos } from './carrinho.service';
import { PaginaPedidosCliente, PedidosService, situacoesPedido } from './pedidos.service';

type Estado =
  | { tipo: 'carregando' }
  | { tipo: 'sucesso'; dados: PaginaPedidosCliente }
  | { tipo: 'sessao' | 'proibido' | 'falha' };

@Component({
  selector: 'app-meus-pedidos',
  imports: [DatePipe, RouterLink],
  templateUrl: './meus-pedidos.component.html',
  styleUrl: './compra.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MeusPedidosComponent {
  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly reais = formatarCentavos;
  protected readonly situacoes = situacoesPedido;
  private readonly rota = inject(ActivatedRoute);
  private readonly pedidos = inject(PedidosService);
  private readonly tentativas = new Subject<void>();

  constructor() {
    this.rota.queryParamMap
      .pipe(
        map((params) => Number(params.get('pagina'))),
        // Com 20 itens por página, o deslocamento SQL precisa caber em Int32.
        map((numero) =>
          Number.isSafeInteger(numero) && numero >= 1 && numero <= 107_374_183 ? numero : 1,
        ),
        distinctUntilChanged(),
        switchMap((pagina) =>
          this.tentativas.pipe(
            startWith(undefined),
            switchMap(() => this.consultar(pagina)),
          ),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((estado) => this.estado.set(estado));
  }

  protected tentarNovamente() {
    this.tentativas.next();
  }

  private consultar(pagina: number) {
    return this.pedidos.listar(pagina).pipe(
      map((dados): Estado => ({ tipo: 'sucesso', dados })),
      catchError((erro: unknown) => {
        const status = erro instanceof HttpErrorResponse ? erro.status : 0;
        return of<Estado>({
          tipo: status === 401 ? 'sessao' : status === 403 ? 'proibido' : 'falha',
        });
      }),
      startWith<Estado>({ tipo: 'carregando' }),
    );
  }
}
