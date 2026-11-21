import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  FinanceiroService,
  PeriodoFinanceiro,
  ResumoFinanceiro,
} from '../../core/financeiro.service';
import { exibirReais } from '../compras/compra-decimais';
import { dataCivilValida } from '../compras/recebimento-pendente';

const todoPeriodo: PeriodoFinanceiro = { dataInicial: '', dataFinal: '' };

function dataCivil(data: Date): string {
  return [
    String(data.getFullYear()).padStart(4, '0'),
    String(data.getMonth() + 1).padStart(2, '0'),
    String(data.getDate()).padStart(2, '0'),
  ].join('-');
}

@Component({
  selector: 'app-admin-visao-financeira',
  imports: [FormsModule, RouterLink],
  templateUrl: './admin-visao-financeira.component.html',
  styleUrl: './admin-financeiro.component.css',
})
export class AdminVisaoFinanceiraComponent {
  private readonly api = inject(FinanceiroService);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private aplicado = { ...todoPeriodo };
  protected periodo = { ...todoPeriodo };
  protected readonly resumo = signal<ResumoFinanceiro | null>(null);
  protected readonly carregando = signal(false);
  protected readonly erro = signal('');
  protected readonly erroPeriodo = signal('');

  readonly periodoInicial = input<PeriodoFinanceiro>(todoPeriodo);
  readonly periodoAplicado = output<PeriodoFinanceiro>();
  readonly ativa = input(true);
  readonly compacto = input(false);
  protected readonly direcoes = [
    { chave: 'aPagar', filtro: 'APagar', titulo: 'A pagar' },
    { chave: 'aReceber', filtro: 'AReceber', titulo: 'A receber' },
  ] as const;
  protected readonly alertas = [
    { chave: 'vencidas', filtro: 'Vencidas', titulo: 'Vencidas', classe: 'conta-atrasada' },
    { chave: 'venceHoje', filtro: 'VenceHoje', titulo: 'Vencem hoje', classe: 'conta-hoje' },
    { chave: 'proximosTresDias', filtro: 'ProximosTresDias', titulo: 'Próximos três dias', classe: 'conta-proxima' },
  ] as const;
  readonly revisao = input(0);
  private revisaoConsultada = -1;

  constructor() {
    effect(() => {
      if (!this.ativa() || this.carregando() || this.revisaoConsultada === this.revisao()) return;
      if (this.revisaoConsultada === -1) {
        this.periodo = { ...untracked(this.periodoInicial) };
        this.aplicado = { ...this.periodo };
      }
      this.revisaoConsultada = this.revisao();
      untracked(() => this.carregar());
    });
  }

  protected atalho(tipo: 'atual' | 'anterior' | 'todo'): void {
    if (tipo === 'todo') this.periodo = { ...todoPeriodo };
    else {
      const agora = new Date();
      const ano = agora.getFullYear();
      const mes = agora.getMonth() - (tipo === 'anterior' ? 1 : 0);
      this.periodo = {
        dataInicial: dataCivil(new Date(ano, mes, 1)),
        dataFinal: dataCivil(new Date(ano, mes + 1, 0)),
      };
    }
    this.aplicar();
  }

  protected aplicar(): void {
    if (this.carregando()) return;
    const { dataInicial, dataFinal } = this.periodo;
    if (
      (dataInicial && !dataCivilValida(dataInicial)) ||
      (dataFinal && !dataCivilValida(dataFinal)) ||
      (dataInicial && dataFinal && dataInicial > dataFinal)
    ) {
      this.erroPeriodo.set(
        'Informe um intervalo válido. A data inicial deve ser anterior ou igual à data final.',
      );
      return;
    }
    this.erroPeriodo.set('');
    this.aplicado = { ...this.periodo };
    this.periodoAplicado.emit(this.aplicado);
    this.carregar();
  }

  protected carregar(): void {
    if (this.carregando()) return;
    this.carregando.set(true);
    this.erro.set('');
    this.resumo.set(null);
    this.api
      .resumo(this.aplicado)
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (resumo) => this.resumo.set(resumo),
        error: (erro: unknown) => {
          if (erro instanceof HttpErrorResponse && erro.status === 401) {
            this.erro.set('Sua sessão expirou.');
            void this.router.navigateByUrl('/admin/login');
          } else if (erro instanceof HttpErrorResponse && erro.status === 403) {
            this.erro.set('Acesso negado. Confira sua sessão administrativa.');
          } else {
            this.erro.set('Não foi possível carregar os indicadores financeiros. Tente novamente.');
          }
        },
      });
  }

  protected moeda(centavos: string): string {
    const valor = BigInt(centavos);
    return valor < 0n ? '− ' + exibirReais(-valor) : exibirReais(valor);
  }

  protected civil(data: string): string {
    return data.split('-').reverse().join('/');
  }
}
