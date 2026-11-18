import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  EMPTY,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  switchMap,
} from 'rxjs';
import {
  AdminInsumoFiltro,
  AdminResumoInsumoPreciso,
  InsumosService,
  SituacaoEstoqueInsumo,
  UnidadeBaseEstoque,
} from '../../../core/admin-estoque/insumos.service';
import { formatDecimalPtBr } from '../../../core/admin-estoque/estoque-decimal';
import { AdminWorkspaceComponent } from '../../admin-workspace.component';

type FiltroAtivo = 'todos' | 'ativos' | 'inativos';

interface SolicitacaoLista {
  id: number;
  filtro: AdminInsumoFiltro;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  selector: 'app-admin-insumos',
  styleUrl: './admin-insumos.component.css',
  templateUrl: './admin-insumos.component.html',
})
export class AdminInsumosComponent {
  protected readonly insumos = signal<AdminResumoInsumoPreciso[]>([]);
  protected readonly carregando = signal(false);
  protected readonly erroCarregamento = signal('');
  protected readonly busca = new FormControl('', { nonNullable: true });
  protected readonly filtroAtivo = signal<FiltroAtivo>('todos');
  protected readonly filtroSituacao = signal<SituacaoEstoqueInsumo | ''>('');
  protected readonly pagina = signal(1);
  protected readonly totalItens = signal(0);
  protected readonly itensPorPagina = 20;
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.totalItens() / this.itensPorPagina)),
  );
  protected readonly situacoes: ReadonlyArray<{
    valor: SituacaoEstoqueInsumo;
    rotulo: string;
  }> = [
    { valor: 'SemEstoque', rotulo: 'Sem estoque' },
    { valor: 'AbaixoOuNoMinimo', rotulo: 'Abaixo ou no mínimo' },
    { valor: 'Normal', rotulo: 'Normal' },
    { valor: 'MinimoNaoConfigurado', rotulo: 'Mínimo não configurado' },
  ];

  private readonly insumosService = inject(InsumosService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly solicitacoes = new Subject<SolicitacaoLista>();
  private idSolicitacao = 0;

  constructor() {
    this.solicitacoes
      .pipe(
        switchMap(({ id, filtro }) => {
          this.carregando.set(true);
          this.erroCarregamento.set('');
          return this.insumosService.listar(filtro).pipe(
            catchError((error: unknown) => {
              if (!this.redirecionarSessaoExpirada(error)) {
                this.erroCarregamento.set(this.mensagemErroLista(error));
              }
              return EMPTY;
            }),
            finalize(() => {
              if (id === this.idSolicitacao) {
                this.carregando.set(false);
              }
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((pagina) => {
        const totalPaginas = Math.max(1, Math.ceil(pagina.totalItens / this.itensPorPagina));
        if (this.pagina() > totalPaginas) {
          this.pagina.set(totalPaginas);
          this.carregarInsumos();
          return;
        }

        this.insumos.set(pagina.itens);
        this.totalItens.set(pagina.totalItens);
      });

    this.busca.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.pagina.set(1);
        this.carregarInsumos();
      });

    this.carregarInsumos();
  }

  protected carregarInsumos(): void {
    const filtro: AdminInsumoFiltro = {
      pagina: this.pagina(),
      itensPorPagina: this.itensPorPagina,
    };
    const nome = this.busca.value.trim();
    if (nome) {
      filtro.nome = nome;
    }

    const ativo = this.filtroAtivo();
    if (ativo !== 'todos') {
      filtro.ativo = ativo === 'ativos';
    }
    const situacao = this.filtroSituacao();
    if (situacao) {
      filtro.situacao = situacao;
    }

    this.insumos.set([]);
    const id = ++this.idSolicitacao;
    this.solicitacoes.next({ id, filtro });
  }

  protected definirFiltroAtivo(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }

    const value = select.value;
    if (value !== 'todos' && value !== 'ativos' && value !== 'inativos') {
      return;
    }
    if (this.filtroAtivo() === value) {
      return;
    }
    this.filtroAtivo.set(value);
    this.pagina.set(1);
    this.carregarInsumos();
  }

  protected definirFiltroSituacao(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }

    const value = select.value;
    const situacao = this.situacoes.find((item) => item.valor === value)?.valor ?? '';
    if (this.filtroSituacao() === situacao) {
      return;
    }
    this.filtroSituacao.set(situacao);
    this.pagina.set(1);
    this.carregarInsumos();
  }

  protected irParaPagina(pagina: number): void {
    if (pagina < 1 || pagina > this.totalPaginas() || pagina === this.pagina()) {
      return;
    }
    this.pagina.set(pagina);
    this.carregarInsumos();
  }

  protected formatarQuantidade(valor: string, unidade: UnidadeBaseEstoque): string {
    return `${formatDecimalPtBr(valor)} ${this.rotuloUnidade(unidade)}`;
  }

  protected rotuloUnidade(unidade: UnidadeBaseEstoque): string {
    switch (unidade) {
      case 'Grama':
        return 'g';
      case 'Mililitro':
        return 'ml';
      case 'Unidade':
        return 'un.';
    }
  }

  protected rotuloTipo(tipo: AdminResumoInsumoPreciso['tipo']): string {
    switch (tipo) {
      case 'MateriaPrima':
        return 'Matéria-prima';
      case 'Embalagem':
        return 'Embalagem';
      case 'Outro':
        return 'Outro';
    }
  }

  protected rotuloSituacao(situacao: SituacaoEstoqueInsumo): string {
    return (
      this.situacoes.find((item) => item.valor === situacao)?.rotulo ?? 'Situação desconhecida'
    );
  }

  protected classeSituacao(situacao: SituacaoEstoqueInsumo): string {
    switch (situacao) {
      case 'SemEstoque':
        return 'border-red-200 bg-red-50 text-red-900';
      case 'AbaixoOuNoMinimo':
        return 'border-amber-200 bg-amber-50 text-amber-950';
      case 'Normal':
        return 'border-emerald-200 bg-emerald-50 text-emerald-950';
      case 'MinimoNaoConfigurado':
        return 'border-slate-300 bg-slate-100 text-slate-800';
    }
  }

  private mensagemErroLista(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return 'Você não tem permissão para consultar os insumos.';
    }
    if (error instanceof HttpErrorResponse && error.status === 400) {
      return 'Os filtros informados são inválidos. Revise-os e tente novamente.';
    }
    return 'Não foi possível carregar os insumos. Tente novamente.';
  }

  private redirecionarSessaoExpirada(error: unknown): boolean {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return true;
    }
    return false;
  }
}
