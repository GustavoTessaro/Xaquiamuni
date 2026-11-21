import { Component, DestroyRef, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, catchError, map, of, switchMap } from 'rxjs';
import {
  ContaEmAberto, DirecaoConta, FiltroVencimento, FinanceiroService, OrigemConta,
  PaginaContasEmAberto,
} from '../../core/financeiro.service';
import { exibirReais } from '../compras/compra-decimais';

const direcoes = ['APagar', 'AReceber'] as const;
const classificacoes = ['Todas', 'Vencidas', 'VenceHoje', 'VencendoEmBreve', 'ProximosTresDias'] as const;
const origens = ['Manual', 'Compra', 'VendaExterna'] as const;

function opcao<T extends string>(lista: readonly T[], valor: string | null): T | undefined {
  return lista.find(item => item === valor);
}

@Component({
  selector: 'app-admin-contas-em-aberto',
  imports: [FormsModule],
  templateUrl: './admin-contas-em-aberto.component.html',
  styleUrl: './admin-financeiro.component.css',
})
export class AdminContasEmAbertoComponent {
  private readonly api = inject(FinanceiroService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly consulta = new Subject<number>();
  readonly abrir = output<ContaEmAberto>();

  protected direcao: DirecaoConta | '' = '';
  protected classificacao: FiltroVencimento = 'Todas';
  protected origem: OrigemConta | '' = '';
  protected readonly resultado = signal<PaginaContasEmAberto | null>(null);
  protected readonly carregando = signal(false);
  protected readonly erro = signal(false);
  protected readonly moeda = (centavos: string) => exibirReais(BigInt(centavos));
  protected readonly rotulosOrigem = { Manual: 'Lançamento manual', Compra: 'Compra', VendaExterna: 'Venda externa' };
  protected readonly rotulosSituacao = {
    EmAberto: 'Em aberto', Vencida: 'Vencida', VenceHoje: 'Vence hoje', VencendoEmBreve: 'Vencendo em breve',
  };

  constructor() {
    this.consulta.pipe(
      switchMap(pagina => this.api.contasEmAberto({
        direcao: this.direcao || undefined, classificacao: this.classificacao,
        origem: this.origem || undefined,
      }, pagina).pipe(
        map(resultado => ({ resultado, erro: false })),
        catchError(() => of({ resultado: null, erro: true })),
      )),
      takeUntilDestroyed(this.destroy),
    ).subscribe(({ resultado, erro }) => {
      this.resultado.set(resultado);
      this.erro.set(erro);
      this.carregando.set(false);
    });

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params => {
      this.direcao = opcao(direcoes, params.get('direcao')) ?? '';
      this.classificacao = opcao(classificacoes, params.get('classificacao')) ?? 'Todas';
      this.origem = opcao(origens, params.get('origem')) ?? '';
      this.carregar();
    });
  }

  protected filtrar(): void {
    const queryParams = { secao: 'contas', direcao: this.direcao || null,
      classificacao: this.classificacao === 'Todas' ? null : this.classificacao, origem: this.origem || null };
    const atual = this.route.snapshot.queryParamMap;
    if (Object.entries(queryParams).every(([nome, valor]) => atual.get(nome) === valor)) {
      this.carregar();
      return;
    }
    void this.router.navigate([], { relativeTo: this.route, queryParamsHandling: 'merge', queryParams });
  }

  protected carregar(pagina = 1): void {
    this.carregando.set(true);
    this.erro.set(false);
    this.consulta.next(pagina);
  }
}
