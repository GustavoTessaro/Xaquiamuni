import { ComprovantePixComponent } from '../../shared/comprovante-pix/comprovante-pix.component';
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
  viewChildren,
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
import { PostagemPedidoComponent } from '../../shared/postagem-pedido.component';
import { DadosEntregaComponent } from '../../shared/dados-entrega.component';

const acoes = {
  pagamento: {
    rotulo: 'Confirmar pagamento',
    titulo: 'Conferência bancária',
    enviar: 'Registrar confirmação',
    declaracao: 'Conferi que o pagamento deste pedido foi recebido na conta bancária.',
    sucesso: 'Pagamento confirmado.',
    repetida: 'O pagamento já estava confirmado.',
  },
  cancelamento: {
    rotulo: 'Cancelar pedido',
    titulo: 'Confirmação de cancelamento',
    enviar: 'Confirmar cancelamento',
    orientacao: 'Confira o motivo antes de confirmar. Esta ação encerra o pedido.',
    sucesso: 'Pedido cancelado.',
    repetida: 'O pedido já estava cancelado com esse motivo.',
  },
  preparacao: {
    rotulo: 'Iniciar preparação',
    titulo: 'Iniciar preparação do pedido',
    enviar: 'Confirmar início',
    orientacao: 'Confirme para registrar que a preparação deste pedido começou.',
    sucesso: 'Preparação iniciada.',
    repetida: 'A preparação deste pedido já havia sido iniciada.',
  },
  retirada: {
    rotulo: 'Liberar para retirada',
    titulo: 'Liberar pedido para retirada',
    enviar: 'Confirmar liberação',
    orientacao:
      'Esta ação registra que os produtos estão prontos para retirada. A entrega presencial é confirmada depois.',
    sucesso: 'Pedido pronto para retirada.',
    repetida: 'O pedido já estava pronto para retirada.',
  },
  entrega: {
    rotulo: 'Confirmar entrega',
    titulo: 'Confirmar entrega presencial',
    enviar: 'Registrar entrega',
    orientacao:
      'Esta ação registra a entrega presencial e realiza a baixa física definitiva do estoque.',
    declaracao: 'Confirmo que os produtos deste pedido foram entregues presencialmente ao cliente.',
    sucesso: 'Entrega registrada.',
    repetida: 'A entrega deste pedido já estava registrada.',
  },
  saidaPropria: {
    rotulo: 'Iniciar entrega própria',
    titulo: 'Confirmar saída da loja',
    enviar: 'Registrar saída e baixar estoque',
    orientacao: 'A saída física consome a reserva e registra a baixa definitiva de estoque.',
    declaracao: 'A preparação terminou e os produtos saíram da loja.',
    sucesso: 'Pedido em entrega.',
    repetida: 'A saída já estava registrada.',
  },
  recebimento: {
    rotulo: 'Confirmar recebimento',
    titulo: 'Confirmar recebimento pelo cliente',
    enviar: 'Registrar recebimento',
    declaracao: 'O cliente recebeu os produtos.',
    sucesso: 'Recebimento registrado.',
    repetida: 'O recebimento já estava registrado.',
  },
  postagem: {
    rotulo: 'Registrar postagem', titulo: 'Registrar saída para os Correios', enviar: 'Registrar postagem e baixar estoque',
    declaracao: 'Confirmo que o pacote foi fisicamente entregue aos Correios.',
    orientacao: 'A postagem consome a reserva e registra a baixa definitiva do estoque.',
    sucesso: 'Postagem registrada.', repetida: 'A postagem já estava registrada.',
  },
  rastreamento: {
    rotulo: 'Corrigir rastreamento', titulo: 'Informar ou corrigir rastreamento', enviar: 'Salvar código',
    declaracao: 'Conferi o código de rastreamento informado.',
    sucesso: 'Rastreamento atualizado.', repetida: 'O rastreamento já estava atualizado.',
  },
  entregaPostal: {
    rotulo: 'Confirmar entrega postal', titulo: 'Confirmar recebimento', enviar: 'Registrar entrega postal',
    declaracao: 'Confirmo que o cliente recebeu o pedido.',
    sucesso: 'Entrega postal registrada.', repetida: 'A entrega postal já estava registrada.',
  },
};

type Acao = keyof typeof acoes;
type EstadoDetalhe =
  | { tipo: 'carregando' }
  | { tipo: 'erro'; status: number }
  | { tipo: 'pronto'; dados: DetalhePedidoAdmin };

@Component({
  selector: 'app-admin-pedido-detalhe',
  imports: [AdminWorkspaceComponent, DatePipe, RouterLink, ComprovantePixComponent, DadosEntregaComponent, PostagemPedidoComponent],
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
  private readonly confirmarBotao = viewChild<ElementRef<HTMLButtonElement>>('confirmarBotao');
  private readonly motivoCampo = viewChild<ElementRef<HTMLTextAreaElement>>('motivoCampo');
  private readonly botoesAcao = viewChildren<ElementRef<HTMLButtonElement>>('botaoAcao');
  private readonly titulo = viewChild<ElementRef<HTMLElement>>('titulo');
  private readonly agora = signal(Date.now());
  private readonly prazoEncerradoServidor = signal(false);
  private tentativaEntrega?: { id: string; acao: Acao; etag: string; chave: string; assinatura?: string };

  protected readonly estado = signal<EstadoDetalhe>({ tipo: 'carregando' });
  protected readonly acao = signal<Acao | null>(null);
  protected readonly operacaoPostalIncerta = signal(false);
  protected readonly servicoPostal = signal('');
  protected readonly codigoPostal = signal('');
  protected readonly conferido = signal(false);
  protected readonly motivo = signal('');
  protected readonly motivoInvalido = signal(false);
  protected readonly enviando = signal(false);
  protected readonly sucesso = signal('');
  protected readonly erro = signal('');
  protected readonly acoes = acoes;
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

  protected readonly acoesDisponiveis = computed((): Acao[] => {
    switch (this.pedido()?.situacao) {
      case 'AguardandoPagamento':
        return this.podeAlterar() ? ['pagamento', 'cancelamento'] : [];
      case 'PagamentoConfirmado':
        return ['preparacao'];
      case 'EmPreparacao':
        if (this.pedido()?.modalidadeEntrega === 'Correios') return ['postagem'];
        return this.pedido()?.modalidadeEntrega === 'EntregaPropria' ? ['saidaPropria'] : ['retirada'];
      case 'EmEntrega':
        return this.pedido()?.modalidadeEntrega === 'EntregaPropria' ? ['recebimento'] : [];
      case 'Postado':
        return this.pedido()?.modalidadeEntrega === 'Correios' ? ['rastreamento', 'entregaPostal'] : [];
      case 'Entregue':
        return this.pedido()?.modalidadeEntrega === 'Correios' ? ['rastreamento'] : [];
      case 'ProntoParaRetirada':
        return this.pedido()?.modalidadeEntrega === 'RetiradaLocal' ? ['entrega'] : [];
      default:
        return [];
    }
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
          this.tentativaEntrega = undefined;
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
    if (!this.acoesDisponiveis().includes(acao) || this.enviando()) return;
    this.limparAcao();
    this.sucesso.set('');
    this.erro.set('');
    this.acao.set(acao);
    afterNextRender(
      () => {
        (
          this.confirmarCampo() ??
          this.motivoCampo() ??
          this.confirmarBotao()
        )?.nativeElement.focus();
      },
      { injector: this.injector },
    );
  }

  protected fechar(): void {
    if (this.enviando()) return;
    const acao = this.acao();
    this.limparAcao();
    afterNextRender(
      () => {
        const botao = this.botoesAcao().find(
          ({ nativeElement }) => nativeElement.dataset['acao'] === acao,
        );
        (botao?.nativeElement ?? this.titulo()?.nativeElement)?.focus();
      },
      { injector: this.injector },
    );
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
    if (this.enviando() || !acao || !pedido) return;
    if (
      pedido.situacao === 'AguardandoPagamento' &&
      !(Date.parse(pedido.prazoPagamentoEmUtc) > Date.now())
    ) {
      this.tratarConflito('prazo_encerrado');
      return;
    }

    if (
      !this.acoesDisponiveis().includes(acao) ||
      ('declaracao' in acoes[acao] && !this.conferido())
    )
      return;
    const motivo = this.motivo().trim();
    if (acao === 'cancelamento' && (!motivo || motivo.length > 500)) {
      this.motivoInvalido.set(true);
      this.motivoCampo()?.nativeElement.focus();
      return;
    }

    const estadoAntes = this.estado();
    const etag = estadoAntes.tipo === 'pronto' ? estadoAntes.dados.etag : null;
    const entregaPropria = acao === 'saidaPropria' || acao === 'recebimento' ||
      acao === 'postagem' || acao === 'rastreamento' || acao === 'entregaPostal';
    const servico = this.servicoPostal().trim();
    const codigo = this.codigoPostal().trim();
    if (acao === 'postagem' && (!servico || servico.length > 60)) {
      this.erro.set('Informe o serviço postal utilizado, com até 60 caracteres.');
      return;
    }
    if ((acao === 'rastreamento' || (acao === 'postagem' && codigo)) && !/^[a-z]{2}\d{9}[a-z]{2}$/i.test(codigo.replaceAll(' ', ''))) {
      this.erro.set('Informe um código com duas letras, nove dígitos e duas letras finais.');
      return;
    }
    if (entregaPropria && !etag) {
      this.tratarConflito('versao_desatualizada');
      return;
    }
    this.enviando.set(true);
    this.erro.set('');
    const { id } = pedido;
    const operacoes = {
      pagamento: () => this.pedidos.confirmarPagamento(id),
      cancelamento: () => this.pedidos.cancelar(id, motivo),
      preparacao: () => this.pedidos.iniciarPreparacao(id),
      retirada: () => this.pedidos.liberarRetirada(id),
      entrega: () => this.pedidos.confirmarEntrega(id),
      saidaPropria: () => executarEntrega('iniciar-entrega-propria'),
      recebimento: () => executarEntrega('confirmar-recebimento'),
      postagem: () => executarPostal('registrar-postagem', {
        confirmado: true, servicoPostalUtilizado: servico, codigoRastreamento: codigo || null,
      }),
      rastreamento: () => executarPostal('rastreamento-postal', { codigoRastreamento: codigo }),
      entregaPostal: () => executarPostal('confirmar-entrega-postal', { confirmado: true }),
    };
    const executarEntrega = (rota: 'iniciar-entrega-propria' | 'confirmar-recebimento') => {
      const anterior = this.tentativaEntrega;
      const tentativa = anterior?.id === id && anterior.acao === acao && anterior.etag === etag
        ? anterior : { id, acao, etag: etag!, chave: crypto.randomUUID() };
      this.tentativaEntrega = tentativa;
      return this.pedidos.entregaPropria(id, rota, tentativa.etag, tentativa.chave);
    };
    const executarPostal = (rota: 'registrar-postagem' | 'rastreamento-postal' | 'confirmar-entrega-postal', corpo: object) => {
      const assinatura = JSON.stringify(corpo);
      const anterior = this.tentativaEntrega;
      const tentativa = anterior?.id === id && anterior.acao === acao && anterior.etag === etag && anterior.assinatura === assinatura
        ? anterior : { id, acao, etag: etag!, chave: crypto.randomUUID(), assinatura };
      this.tentativaEntrega = tentativa;
      return this.pedidos.entregaPostal(id, rota, tentativa.etag, tentativa.chave, corpo);
    };
    const operacao = operacoes[acao]();
    operacao
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.enviando.set(false)),
      )
      .subscribe({
        next: (resultado) => {
          const estado = this.estado();
          if (estado.tipo !== 'pronto' || estado.dados.pedido.id !== pedido.id) return;
          if (entregaPropria) this.tentativaEntrega = undefined;
          this.estado.set({
            tipo: 'pronto',
            dados: {
              ...estado.dados,
              pedido: { ...estado.dados.pedido, situacao: resultado.situacao },
            },
          });
          this.sucesso.set(resultado.repetida ? acoes[acao].repetida : acoes[acao].sucesso);
          this.limparAcao();
          if (entregaPropria || acao === 'preparacao' || acao === 'retirada' || acao === 'entrega') {
            this.recarregar();
          }
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
      this.tentativaEntrega = undefined;
      this.tratarConflito(falha.error?.codigo);
    } else if (status === 400) {
      this.operacaoPostalIncerta.set(false);
      this.erro.set(falha instanceof HttpErrorResponse && falha.error?.codigo === 'codigo_rastreamento_invalido'
        ? 'Informe um código de rastreamento válido.'
        : 'A solicitação foi recusada. Revise os dados e tente novamente.');
    } else if (this.tentativaEntrega?.assinatura !== undefined && (!status || status >= 500 || status === 408)) {
      this.operacaoPostalIncerta.set(true);
      this.erro.set('Resultado incerto. Use Recuperar operação para consultar a tentativa original.');
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
      ['codigo_inalterado', 'Este código de rastreamento já está registrado.'],
      ['motivo_divergente', 'O pedido já foi cancelado com outro motivo.'],
      [
        'reservas_inconsistentes',
        'As reservas do pedido apresentam uma divergência. Verifique a situação antes de tentar novamente.',
      ],
      [
        'lote_inelegivel',
        'Um lote reservado não está apto para a entrega. Verifique a situação do estoque antes de tentar novamente.',
      ],
      [
        'saldo_insuficiente',
        'O saldo dos lotes reservados é insuficiente para a entrega. Verifique o estoque antes de tentar novamente.',
      ],
      ['conflito_concorrencia', 'Outra operação está atualizando este pedido.'],
      ['versao_desatualizada', 'O pedido mudou. Confira os dados atualizados antes de confirmar novamente.'],
    ]);
    if (codigo === 'prazo_encerrado') this.prazoEncerradoServidor.set(true);
    this.erro.set(mensagens.get(String(codigo)) ?? 'O pedido mudou. Consulte a situação atual.');
    this.limparAcao();
    this.recarregar();
    this.titulo()?.nativeElement.focus();
  }

  private limparAcao(): void {
    this.operacaoPostalIncerta.set(false);
    this.servicoPostal.set('');
    this.codigoPostal.set('');
    this.acao.set(null);
    this.conferido.set(false);
    this.motivo.set('');
    this.motivoInvalido.set(false);
  }
}
