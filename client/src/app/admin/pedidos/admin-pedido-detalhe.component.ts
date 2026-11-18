import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
  ElementRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  Subject,
  catchError,
  distinctUntilChanged,
  finalize,
  map,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';
import {
  AdminPedidosService,
  DetalhePedidoAdmin,
  statusPedidos,
} from '../../core/admin-pedidos/admin-pedidos.service';
import { formatarCentavos } from '../../public-store/carrinho.service';
import { situacoesPedido, uuidValido } from '../../public-store/pedidos.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

type Acao = 'pagamento' | 'cancelamento';
type EstadoDetalhe =
  | { tipo: 'carregando' }
  | { tipo: 'erro'; status: number }
  | { tipo: 'pronto'; dados: DetalhePedidoAdmin };

@Component({
  selector: 'app-admin-pedido-detalhe',
  imports: [AdminWorkspaceComponent, DatePipe, RouterLink],
  templateUrl: './admin-pedido-detalhe.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPedidoDetalheComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly pedidos = inject(AdminPedidosService);
  private readonly destroy = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly atualizar = new Subject<void>();
  private readonly confirmarCampo = viewChild<ElementRef<HTMLInputElement>>('confirmarCampo');
  private readonly motivoCampo = viewChild<ElementRef<HTMLTextAreaElement>>('motivoCampo');
  private readonly titulo = viewChild<ElementRef<HTMLElement>>('titulo');
  private readonly agora = signal(Date.now());
  private readonly prazoEncerradoServidor = signal(false);

  protected readonly estado = signal<EstadoDetalhe>({ tipo: 'carregando' });
  protected readonly acao = signal<Acao | null>(null);
  protected readonly conferido = signal(false);
  protected readonly motivo = signal('');
  protected readonly motivoInvalido = signal(false);
  protected readonly enviando = signal(false);
  protected readonly sucesso = signal('');
  protected readonly erro = signal('');
  protected readonly situacoes = situacoesPedido;
  protected readonly formatarPreco = formatarCentavos;
  private readonly pedido = computed(() => {
    const estado = this.estado();
    return estado.tipo === 'pronto' ? estado.dados.pedido : null;
  });
  protected readonly podeAlterar = computed(() => {
    const pedido = this.pedido();
    return (
      !this.prazoEncerradoServidor() &&
      pedido?.situacao === 'AguardandoPagamento' &&
      Date.parse(pedido.prazoPagamentoEmUtc) > this.agora()
    );
  });

  constructor() {
    this.route.paramMap
      .pipe(
        map((params) => params.get('pedidoId')),
        distinctUntilChanged(),
        tap(() => {
          this.prazoEncerradoServidor.set(false);
          this.sucesso.set('');
          this.erro.set('');
          this.limparAcao();
        }),
        switchMap((id) =>
          this.atualizar.pipe(
            startWith(undefined),
            switchMap(() => {
              if (!uuidValido(id)) {
                return of<EstadoDetalhe>({ tipo: 'erro', status: 404 });
              }
              return this.pedidos.consultar(id).pipe(
                map((dados): EstadoDetalhe => ({ tipo: 'pronto', dados })),
                catchError((erro: unknown) =>
                  of<EstadoDetalhe>({ tipo: 'erro', status: statusPedidos(erro) }),
                ),
                startWith<EstadoDetalhe>({ tipo: 'carregando' }),
              );
            }),
          ),
        ),
        takeUntilDestroyed(this.destroy),
      )
      .subscribe((estado) => {
        this.agora.set(Date.now());
        this.estado.set(estado);
      });

    effect((onCleanup) => {
      const pedido = this.pedido();
      if (pedido?.situacao !== 'AguardandoPagamento') return;
      const restante = Date.parse(pedido.prazoPagamentoEmUtc) - Date.now();
      if (!Number.isFinite(restante)) return;
      const timer = setTimeout(
        () => this.agora.set(Date.now()),
        Math.min(Math.max(0, restante), 2_147_483_647),
      );
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected abrir(acao: Acao): void {
    if (!this.podeAlterar() || this.enviando()) return;
    this.limparAcao();
    this.sucesso.set('');
    this.erro.set('');
    this.acao.set(acao);
    afterNextRender(
      () => {
        (acao === 'pagamento' ? this.confirmarCampo() : this.motivoCampo())?.nativeElement.focus();
      },
      { injector: this.injector },
    );
  }

  protected fechar(): void {
    if (this.enviando()) return;
    this.limparAcao();
    this.titulo()?.nativeElement.focus();
  }

  protected atualizarMotivo(motivo: string): void {
    this.motivo.set(motivo);
    this.motivoInvalido.set(false);
  }

  protected recarregar(): void {
    this.atualizar.next();
  }

  protected enviar(event: Event): void {
    event.preventDefault();
    const acao = this.acao();
    const pedido = this.pedido();
    if (this.enviando() || !acao || !pedido || pedido.situacao !== 'AguardandoPagamento') return;
    if (!(Date.parse(pedido.prazoPagamentoEmUtc) > Date.now())) {
      this.tratarConflito('prazo_encerrado');
      return;
    }

    if (acao === 'pagamento' && !this.conferido()) return;
    const motivo = this.motivo().trim();
    if (acao === 'cancelamento' && (!motivo || motivo.length > 500)) {
      this.motivoInvalido.set(true);
      this.motivoCampo()?.nativeElement.focus();
      return;
    }

    this.enviando.set(true);
    this.erro.set('');
    const operacao =
      acao === 'pagamento'
        ? this.pedidos.confirmarPagamento(pedido.id)
        : this.pedidos.cancelar(pedido.id, motivo);
    operacao
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.enviando.set(false)),
      )
      .subscribe({
        next: (resultado) => {
          const estado = this.estado();
          if (estado.tipo !== 'pronto' || estado.dados.pedido.id !== pedido.id) return;
          this.estado.set({
            tipo: 'pronto',
            dados: {
              ...estado.dados,
              pedido: { ...estado.dados.pedido, situacao: resultado.situacao },
            },
          });
          this.sucesso.set(
            acao === 'pagamento'
              ? resultado.repetida
                ? 'O pagamento já estava confirmado.'
                : 'Pagamento confirmado.'
              : resultado.repetida
                ? 'O pedido já estava cancelado com esse motivo.'
                : 'Pedido cancelado.',
          );
          this.limparAcao();
          this.titulo()?.nativeElement.focus();
        },
        error: (erro: unknown) => {
          if (this.pedido()?.id === pedido.id) this.tratarFalha(erro);
        },
      });
  }

  private tratarFalha(falha: unknown): void {
    const status = statusPedidos(falha);
    if (status === 401 || status === 403 || status === 404) {
      this.limparAcao();
      this.estado.set({ tipo: 'erro', status });
      this.titulo()?.nativeElement.focus();
    } else if (falha instanceof HttpErrorResponse && status === 409) {
      this.tratarConflito(falha.error?.codigo);
    } else if (status === 400) {
      this.erro.set('A solicitação foi recusada. Revise os dados e tente novamente.');
    } else {
      this.erro.set(
        'Não foi possível verificar o resultado da operação. Consulte a situação atual antes de tentar novamente.',
      );
      this.limparAcao();
      this.recarregar();
      this.titulo()?.nativeElement.focus();
    }
  }

  private tratarConflito(codigo: unknown): void {
    const mensagens = new Map([
      ['prazo_encerrado', 'O prazo de pagamento terminou.'],
      ['estado_incompativel', 'A situação atual do pedido não permite essa operação.'],
      ['motivo_divergente', 'O pedido já foi cancelado com outro motivo.'],
      ['reservas_inconsistentes', 'Uma divergência de estoque impediu a atualização do pedido.'],
      ['conflito_concorrencia', 'Outra operação está atualizando este pedido.'],
    ]);
    if (codigo === 'prazo_encerrado') this.prazoEncerradoServidor.set(true);
    this.erro.set(mensagens.get(String(codigo)) ?? 'O pedido mudou. Consulte a situação atual.');
    this.limparAcao();
    this.recarregar();
    this.titulo()?.nativeElement.focus();
  }

  private limparAcao(): void {
    this.acao.set(null);
    this.conferido.set(false);
    this.motivo.set('');
    this.motivoInvalido.set(false);
  }
}
