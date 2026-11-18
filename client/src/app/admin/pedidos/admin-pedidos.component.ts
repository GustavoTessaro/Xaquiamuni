import { DatePipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import {
  AdminPedidosService,
  PaginaPedidosAdmin,
  SituacaoPedido,
  statusPedidos,
} from '../../core/admin-pedidos/admin-pedidos.service';
import { formatarCentavos } from '../../public-store/carrinho.service';
import { situacoesPedido } from '../../public-store/pedidos.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

type EstadoLista =
  | { tipo: 'carregando' }
  | { tipo: 'erro'; status: number }
  | { tipo: 'pronto'; dados: PaginaPedidosAdmin };

@Component({
  selector: 'app-admin-pedidos',
  imports: [AdminWorkspaceComponent, DatePipe, KeyValuePipe, RouterLink],
  templateUrl: './admin-pedidos.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPedidosComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly pedidos = inject(AdminPedidosService);
  private readonly atualizar = new Subject<void>();
  protected readonly situacoes = situacoesPedido;
  protected readonly formatarPreco = formatarCentavos;
  private readonly filtrosDaRota = this.route.queryParamMap.pipe(
    map((query) => {
      const valor = query.get('situacao') ?? '';
      const pagina = Number(query.get('pagina') ?? '1');
      return {
        situacao: Object.hasOwn(situacoesPedido, valor) ? (valor as SituacaoPedido) : ('' as const),
        pagina: Number.isSafeInteger(pagina) && pagina > 0 ? pagina : 1,
      };
    }),
  );
  protected readonly filtros = toSignal(this.filtrosDaRota, { requireSync: true });
  protected readonly estado = toSignal(
    this.filtrosDaRota.pipe(
      switchMap(({ pagina, situacao }) =>
        this.atualizar.pipe(
          startWith(undefined),
          switchMap(() => {
            return this.pedidos.listar(pagina, situacao).pipe(
              map((dados): EstadoLista => ({ tipo: 'pronto', dados })),
              catchError((erro: unknown) =>
                of<EstadoLista>({ tipo: 'erro', status: statusPedidos(erro) }),
              ),
              startWith<EstadoLista>({ tipo: 'carregando' }),
            );
          }),
        ),
      ),
    ),
    { initialValue: { tipo: 'carregando' } as EstadoLista },
  );

  protected filtrar(situacao: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { situacao: situacao || null },
    });
  }

  protected mudarPagina(pagina: number): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        situacao: this.filtros().situacao || null,
        pagina: pagina === 1 ? null : pagina,
      },
    });
  }

  protected recarregar(): void {
    this.atualizar.next();
  }
}
