import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  FiltroMovimentacoes,
  FinanceiroService,
  MeioFinanceiro,
  MovimentacaoFinanceira,
  PaginaMovimentacoes,
} from '../../core/financeiro.service';
import { exibirReais } from '../compras/compra-decimais';
import { dataCivilValida } from '../compras/recebimento-pendente';

const semFiltros: FiltroMovimentacoes = { dataInicial: '', dataFinal: '', tipo: '', origem: '' };

@Component({
  selector: 'app-admin-movimentacoes-financeiras',
  imports: [FormsModule, RouterLink],
  templateUrl: './admin-movimentacoes-financeiras.component.html',
  styleUrl: './admin-financeiro.component.css',
})
export class AdminMovimentacoesFinanceirasComponent {
  private readonly api = inject(FinanceiroService);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  readonly recurso = output<{ secao: 'lancamentos' | 'compras'; id: string }>();

  protected filtros = { ...semFiltros };
  protected readonly resultado = signal<PaginaMovimentacoes | null>(null);
  protected readonly carregando = signal(false);
  protected readonly erro = signal('');
  protected readonly erroFiltro = signal('');
  protected readonly temFiltros = signal(false);
  private aplicados = { ...semFiltros };
  private paginaSolicitada = 1;

  protected readonly origens = {
    PedidoSite: 'Pedidos do site',
    VendaExterna: 'Vendas externas',
    PagamentoCompra: 'Pagamentos de compras',
    LancamentoManual: 'Lançamentos manuais',
  };
  protected readonly codigosOrigem = Object.keys(this.origens) as (keyof typeof this.origens)[];

  constructor() {
    this.carregar(1);
  }

  protected aplicar(): void {
    const { dataInicial, dataFinal } = this.filtros;
    if (
      (dataInicial && !dataCivilValida(dataInicial)) ||
      (dataFinal && !dataCivilValida(dataFinal)) ||
      (dataInicial && dataFinal && dataInicial > dataFinal)
    ) {
      this.erroFiltro.set(
        'Informe um intervalo válido. A data inicial deve ser anterior ou igual à data final.',
      );
      return;
    }
    this.erroFiltro.set('');
    this.aplicados = { ...this.filtros };
    this.temFiltros.set(Object.values(this.aplicados).some(Boolean));
    this.carregar(1);
  }

  protected limpar(): void {
    this.filtros = { ...semFiltros };
    this.aplicar();
  }

  protected carregar(pagina = this.paginaSolicitada): void {
    if (this.carregando()) return;
    this.paginaSolicitada = pagina;
    this.carregando.set(true);
    this.erro.set('');
    this.api
      .movimentacoes(this.aplicados, pagina)
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (resultado) => this.resultado.set(resultado),
        error: (falha) => {
          this.resultado.set(null);
          this.erro.set(this.mensagem(falha));
        },
      });
  }

  private mensagem(erro: unknown): string {
    if (erro instanceof HttpErrorResponse) {
      if (erro.status === 401) {
        void this.router.navigateByUrl('/admin/login');
        return 'Sua sessão expirou.';
      }
      if (erro.status === 403) return 'Acesso negado. Confira sua sessão administrativa.';
      if (erro.status === 404)
        return 'A consulta de movimentações está indisponível neste ambiente.';
      if (erro.status === 503 || erro.status === 0)
        return 'Serviço indisponível ou conexão interrompida.';
      if (erro.status === 400) return 'Confira o período e os filtros informados.';
    }
    return 'Não foi possível carregar as movimentações.';
  }

  protected valor(item: MovimentacaoFinanceira): string {
    return (item.tipo === 'Entrada' ? '+ ' : '− ') + exibirReais(BigInt(item.valorCentavos));
  }

  protected data(dia: string): string {
    return dia.split('-').reverse().join('/');
  }

  protected meio(valor: MeioFinanceiro): string {
    return valor === 'Cartao' ? 'Cartão' : valor === 'Transferencia' ? 'Transferência' : valor;
  }
}
