import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, Observable, catchError, map, of, startWith, switchMap, timeout } from 'rxjs';
import { DashboardAdminService, OperacaoDashboard } from '../../core/dashboard-admin.service';
import { PeriodoFinanceiro } from '../../core/financeiro.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { AdminVisaoFinanceiraComponent } from '../financeiro/admin-visao-financeira.component';

const pedidos = [
  { chave: 'pixNoPrazo', titulo: 'Aguardando PIX no prazo', fluxo: 'AguardandoPagamento' },
  { chave: 'pixVencido', titulo: 'PIX vencido ainda em aberto', fluxo: 'AguardandoPagamento' },
  { chave: 'aPreparar', titulo: 'Pedidos a preparar', fluxo: 'PagamentoConfirmado' },
  { chave: 'emPreparacao', titulo: 'Pedidos em preparação', fluxo: 'EmPreparacao' },
  { chave: 'prontosParaRetirada', titulo: 'Prontos para retirada', fluxo: 'ProntoParaRetirada' },
] as const satisfies readonly { chave: keyof OperacaoDashboard; titulo: string; fluxo: string }[];

type Estado<T> = { tipo: 'carregando' | 'erro' } | { tipo: 'dados'; dados: T };

@Component({
  selector: 'app-admin-dashboard',
  imports: [AdminWorkspaceComponent, AdminVisaoFinanceiraComponent, RouterLink],
  templateUrl: './admin-dashboard.component.html',
  styleUrl: './admin-dashboard.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardComponent {
  private readonly api = inject(DashboardAdminService);
  private readonly carga = new BehaviorSubject(0);
  protected readonly revisao = signal(0);
  protected readonly pedidos = pedidos;
  protected readonly periodoInicial = this.api.periodo;
  protected readonly operacao = this.consultar(() => this.api.operacao());
  protected readonly estoque = this.consultar(() => this.api.estoque());
  protected readonly agenda = this.consultar(() => this.api.agenda());
  protected readonly carregando = computed(() =>
    this.operacao().tipo === 'carregando' || this.estoque().tipo === 'carregando' ||
    this.agenda().tipo === 'carregando');
  protected readonly totalPedidos = computed(() => {
    const atual = this.operacao();
    return atual.tipo === 'dados' ? pedidos.reduce((soma, { chave }) => soma + atual.dados[chave], 0) : 0;
  });

  protected atualizar(): void {
    if (this.carregando()) return;
    this.revisao.update(n => n + 1);
    this.carga.next(this.revisao());
  }

  protected guardarPeriodo(periodo: PeriodoFinanceiro): void {
    this.api.periodo = periodo;
  }

  private consultar<T>(carregar: () => Observable<T>) {
    return toSignal(this.carga.pipe(switchMap(() => carregar().pipe(
      timeout(10000),
      map((dados): Estado<T> => ({ tipo: 'dados', dados })),
      catchError(() => of<Estado<T>>({ tipo: 'erro' })),
      startWith<Estado<T>>({ tipo: 'carregando' }),
    ))), { requireSync: true });
  }
}
