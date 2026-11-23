import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import {
  AdminPedidosService,
  CorrecaoRastreamentoAdmin,
  statusPedidos,
} from '../../core/admin-pedidos/admin-pedidos.service';

type EstadoHistorico =
  | { tipo: 'carregando' }
  | { tipo: 'pronto'; correcoes: CorrecaoRastreamentoAdmin[] }
  | { tipo: 'erro'; mensagem: string };

@Component({
  selector: 'app-historico-rastreamento',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="space-y-3" aria-labelledby="historico-rastreamento">
      <h2 id="historico-rastreamento" class="text-lg font-semibold">Histórico de rastreamento</h2>
      @let historico = estado();
      @switch (historico.tipo) {
        @case ('carregando') {
          <p role="status">Carregando histórico…</p>
        }
        @case ('erro') {
          <p role="alert">{{ historico.mensagem }}</p>
          <button type="button" class="admin-button min-h-11 focus-visible:outline-2 focus-visible:outline-offset-2"
            (click)="recarregar()">Tentar novamente</button>
        }
        @case ('pronto') {
          <ul class="space-y-3">
            @for (correcao of historico.correcoes; track $index) {
              <li class="rounded-xl border border-slate-200 p-3">
                <dl class="grid gap-2 sm:grid-cols-2">
                  <div><dt class="font-semibold">Código anterior</dt><dd class="wrap-anywhere">{{ correcao.codigoAnterior ?? 'Não informado' }}</dd></div>
                  <div><dt class="font-semibold">Código novo</dt><dd class="wrap-anywhere">{{ correcao.codigoNovo ?? 'Não informado' }}</dd></div>
                  <div><dt class="font-semibold">Data e hora local</dt><dd><time [attr.datetime]="correcao.realizadaEmUtc">
                    {{ correcao.realizadaEmUtc | date:'dd/MM/yyyy HH:mm:ss' }}
                  </time></dd></div>
                  <div><dt class="font-semibold">Responsável (ID)</dt><dd class="wrap-anywhere">{{ correcao.administradorId }}</dd></div>
                </dl>
              </li>
            } @empty {
              <li>Nenhuma correção de rastreamento registrada.</li>
            }
          </ul>
        }
      }
    </section>
  `,
})
export class HistoricoRastreamentoComponent {
  readonly pedidoId = input.required<string>();
  private readonly pedidos = inject(AdminPedidosService);
  private readonly atualizacao = signal(0);
  protected readonly estado = signal<EstadoHistorico>({ tipo: 'carregando' });

  constructor() {
    effect(onCleanup => {
      const id = this.pedidoId();
      this.atualizacao();
      this.estado.set({ tipo: 'carregando' });
      const consulta = this.pedidos.consultarHistoricoRastreamento(id).subscribe({
        next: correcoes => this.estado.set({ tipo: 'pronto', correcoes }),
        error: (falha: unknown) => {
          const status = statusPedidos(falha);
          const mensagem = status === 401 ? 'Sessão expirada. Entre novamente no painel.'
            : status === 403 ? 'Você não tem acesso ao histórico de rastreamento.'
            : status === 404 ? 'Pedido não encontrado.'
            : 'Não foi possível consultar o histórico de rastreamento.';
          this.estado.set({ tipo: 'erro', mensagem });
        },
      });
      onCleanup(() => consulta.unsubscribe());
    });
  }

  protected recarregar(): void {
    this.atualizacao.update(atual => atual + 1);
  }
}
