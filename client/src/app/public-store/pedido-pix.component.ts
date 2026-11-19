import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  PagamentoPixPedido,
  PagamentoPixService,
  statusPix,
  tiposChavePix,
} from '../core/pagamento-pix.service';
import { formatarCentavos } from './carrinho.service';

@Component({
  selector: 'app-pedido-pix',
  imports: [RouterLink],
  templateUrl: './pedido-pix.component.html',
  styleUrl: './compra.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PedidoPixComponent {
  readonly pedidoId = input.required<string>();
  readonly aguardandoPagamento = input.required<boolean>();
  private readonly api = inject(PagamentoPixService);
  private readonly tentativa = signal(0);
  private readonly resposta = signal<PagamentoPixPedido | null>(null);
  private readonly vencido = signal(false);
  protected readonly pix = computed(() => {
    const pagamento = this.resposta();
    return this.aguardandoPagamento() && !this.vencido() && pagamento?.situacao === 'disponivel'
      ? pagamento.pix
      : null;
  });
  protected readonly pagamento = this.resposta.asReadonly();
  protected readonly carregando = signal(true);
  protected readonly falha = signal<number | null>(null);
  protected readonly copia = signal<'pendente' | 'sucesso' | 'falha' | null>(null);
  protected readonly tipos = tiposChavePix;
  protected readonly reais = formatarCentavos;

  constructor() {
    effect((limpar) => {
      const id = this.pedidoId();
      this.tentativa();
      this.carregando.set(true);
      this.falha.set(null);
      this.resposta.set(null);
      this.vencido.set(false);
      this.copia.set(null);
      let timer: ReturnType<typeof setTimeout> | undefined;
      const consulta = this.api.consultarPedido(id).subscribe({
        next: (pagamento) => {
          this.resposta.set(pagamento);
          const restante = Date.parse(pagamento.prazoPagamentoEmUtc) - Date.now();
          this.vencido.set(!(restante > 0));
          if (pagamento.situacao === 'disponivel' && restante > 0) {
            timer = setTimeout(() => this.vencido.set(true), restante);
          }
          this.carregando.set(false);
        },
        error: (erro: unknown) => {
          this.falha.set(statusPix(erro));
          this.carregando.set(false);
        },
      });
      limpar(() => {
        consulta.unsubscribe();
        clearTimeout(timer);
      });
    });
  }

  protected consultarNovamente() {
    this.tentativa.update((valor) => valor + 1);
  }

  protected async copiar() {
    const pix = this.pix();
    const pagamento = this.resposta();
    if (
      !pix ||
      !pagamento ||
      Date.now() >= Date.parse(pagamento.prazoPagamentoEmUtc) ||
      this.copia() === 'pendente'
    )
      return;
    this.copia.set('pendente');
    try {
      await navigator.clipboard.writeText(pix.chavePix);
      if (this.resposta() === pagamento) this.copia.set('sucesso');
    } catch {
      if (this.resposta() === pagamento) this.copia.set('falha');
    }
  }
}
