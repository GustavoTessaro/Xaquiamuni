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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
  protected readonly mensagem = signal('');
  protected readonly erroExclusao = signal('');
  protected readonly excluindo = signal(false);
  protected readonly exclusao = signal<{
    id: string; nome: string; ativa: boolean; versao: string; chave: string;
  } | null>(null);
  protected readonly listaOcultaAlvoExclusao = computed(() => {
    const alvo = this.exclusao();
    return alvo !== null && !this.insumos().some(item => item.insumoId === alvo.id);
  });
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
    const parametros = inject(ActivatedRoute).snapshot.queryParamMap;
    this.busca.setValue(parametros.get('nome') ?? '', { emitEvent: false });
    const ativo = parametros.get('ativo');
    if (ativo === 'ativos' || ativo === 'inativos') this.filtroAtivo.set(ativo);
    const situacao = this.situacoes.find(item => item.valor === parametros.get('situacao'));
    if (situacao) this.filtroSituacao.set(situacao.valor);
    const pagina = Number(parametros.get('pagina'));
    if (Number.isSafeInteger(pagina) && pagina > 0) this.pagina.set(pagina);
    const mensagem = this.router.getCurrentNavigation()?.extras.state?.['mensagem'];
    if (typeof mensagem === 'string') this.mensagem.set(mensagem);
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

  protected parametrosDeRetorno() {
    return { nome: this.busca.value || null, ativo: this.filtroAtivo() === 'todos' ? null : this.filtroAtivo(),
      situacao: this.filtroSituacao() || null, pagina: this.pagina() > 1 ? this.pagina() : null };
  }

  protected prepararExclusao(insumo: AdminResumoInsumoPreciso): void {
    if (this.excluindo()) return;
    const anterior = this.exclusao();
    if (anterior) {
      if (anterior.id === insumo.insumoId) this.confirmarExclusao(anterior);
      return;
    }
    this.excluindo.set(true);
    this.erroExclusao.set('');
    this.insumosService.obter(insumo.insumoId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: atual => {
        this.excluindo.set(false);
        if (!atual.versao || !window.confirm(
          `Excluir a matéria-prima “${atual.resumo.nome}”? A exclusão é permanente e remove as formas de compra elegíveis.`,
        )) return;
        this.confirmarExclusao({
          id: insumo.insumoId, nome: atual.resumo.nome, ativa: atual.resumo.ativo,
          versao: atual.versao, chave: crypto.randomUUID(),
        });
      },
      error: erro => {
        this.excluindo.set(false);
        if (this.redirecionarSessaoExpirada(erro)) return;
        this.erroExclusao.set('Não foi possível carregar a matéria-prima para exclusão. Confira a lista atualizada.');
        this.carregarInsumos();
      },
    });
  }

  protected confirmarExclusao(tentativa: NonNullable<ReturnType<typeof this.exclusao>>): void {
    if (this.excluindo()) return;
    this.exclusao.set(tentativa);
    this.excluindo.set(true);
    this.erroExclusao.set('');
    this.insumosService.excluir(tentativa.id, null, tentativa.versao, tentativa.chave)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.excluindo.set(false)))
      .subscribe({
        next: () => {
          this.exclusao.set(null);
          this.insumos.update(lista => lista.filter(item => item.insumoId !== tentativa.id));
          this.totalItens.update(total => Math.max(0, total - 1));
          this.mensagem.set('Matéria-prima excluída.');
          this.carregarInsumos();
        },
        error: erro => {
          if (!(erro instanceof HttpErrorResponse) || erro.status === 0 || erro.status === 408 || erro.status >= 500) {
            this.erroExclusao.set('Resultado da exclusão desconhecido. Recupere a tentativa antes de continuar.');
            return;
          }
          this.exclusao.set(null);
          if (this.redirecionarSessaoExpirada(erro)) return;
          const codigo = erro.error?.codigo;
          const desatualizado = codigo === 'versao_desatualizada' || codigo === 'conflito_concorrencia';
          const detalhe = typeof erro.error?.detail === 'string' ? erro.error.detail : 'Não foi possível excluir a matéria-prima.';
          this.erroExclusao.set(desatualizado ? 'O cadastro foi alterado. Confira a lista e confirme novamente.'
            : tentativa.ativa && typeof codigo === 'string' && (codigo.startsWith('insumo_com_') || codigo === 'insumo_historico_inconclusivo')
              ? `${detalhe} Você pode inativar o cadastro.` : detalhe);
          if (desatualizado || erro.status === 404) this.carregarInsumos();
        },
      });
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
