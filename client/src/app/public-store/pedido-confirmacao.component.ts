import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EMPTY, Subject, catchError, finalize, of, startWith, switchMap } from 'rxjs';
import { formatarCentavos } from './carrinho.service';
import { PrecoExibidoComponent } from './preco-exibido.component';
import { PedidoPixComponent } from './pedido-pix.component';
import { PostagemPedidoComponent } from '../shared/postagem-pedido.component';
import { FretePedidoComponent } from '../shared/frete-pedido.component';
import { DadosEntregaComponent } from '../shared/dados-entrega.component';
import { PedidoCliente, PedidosService, situacoesPedido, uuidValido } from './pedidos.service';

@Component({
  selector: 'app-pedido-confirmacao',
  imports: [DatePipe, RouterLink, PedidoPixComponent, PrecoExibidoComponent, DadosEntregaComponent, FretePedidoComponent, PostagemPedidoComponent],
  templateUrl: './pedido-confirmacao.component.html',
  styleUrl: './compra.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PedidoConfirmacaoComponent {
  protected readonly pedido = signal<PedidoCliente | null>(null);
  protected readonly carregando = signal(false);
  protected readonly erro = signal('');
  protected readonly avisoCopia = signal('');
  protected readonly requerLogin = signal(false);
  protected readonly formatarPreco = formatarCentavos;
  protected readonly situacoes = situacoesPedido;
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly pedidos = inject(PedidosService);
  private readonly destroy = inject(DestroyRef);
  protected readonly idPedido = signal<string | null>(null);
  private readonly recarregar = new Subject<void>();

  constructor() {
    this.route.paramMap
      .pipe(
        switchMap((params) => {
          const id = params.get('id');
          this.idPedido.set(id);
          this.avisoCopia.set('');
          const confirmado = this.router.currentNavigation()?.extras.info as
            PedidoCliente | undefined;
          const carregamento =
            confirmado?.id === id ? this.recarregar : this.recarregar.pipe(startWith(undefined));
          if (confirmado?.id === id) {
            this.pedido.set(confirmado);
            this.carregando.set(false);
            this.erro.set('');
            this.requerLogin.set(false);
          }
          return carregamento.pipe(switchMap(() => this.consultar(id)));
        }),
        takeUntilDestroyed(this.destroy),
      )
      .subscribe((pedido) => this.pedido.set(pedido));
  }

  protected async copiarNumero(numero: string): Promise<void> {
    let aviso: string;
    try {
      await navigator.clipboard.writeText(numero);
      aviso = 'Número do pedido copiado.';
    } catch {
      aviso = 'Não foi possível copiar. Selecione o número para copiar manualmente.';
    }
    if (this.pedido()?.numeroPublico === numero) this.avisoCopia.set(aviso);
  }

  protected tentarNovamente() {
    this.recarregar.next();
  }

  private consultar(id: string | null) {
    if (this.pedido()?.id !== id) this.pedido.set(null);
    this.erro.set('');
    this.requerLogin.set(false);
    if (!uuidValido(id)) {
      this.erro.set('Pedido não encontrado.');
      return EMPTY;
    }

    this.carregando.set(!this.pedido());
    return this.pedidos.consultar(id).pipe(
      catchError((falha: unknown) => {
        const status = falha instanceof HttpErrorResponse ? falha.status : 0;
        this.requerLogin.set(status === 401);
        this.erro.set(
          status === 401
            ? 'Entre na sua conta para consultar o pedido.'
            : status === 404 || status === 403
              ? 'Pedido não encontrado para esta conta.'
              : 'Não foi possível consultar o pedido agora. Tente novamente.',
        );
        return of(null);
      }),
      finalize(() => this.carregando.set(false)),
    );
  }
}
