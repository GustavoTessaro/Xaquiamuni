import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  AdminMovimentoFiltro,
  AdminMovimentoPreciso,
  AdminPagina,
  AdminPosicaoEstoquePrecisa,
  AdminPosicaoInsumoPrecisa,
  DirecaoMovimentoInsumo,
  InsumosService,
  TipoMovimentoInsumo,
  UnidadeFisicaEstoque,
} from '../../../core/admin-estoque/insumos.service';
import { formatDecimalPtBr } from '../../../core/admin-estoque/estoque-decimal';
import { AdminWorkspaceComponent } from '../../admin-workspace.component';

const PAGE_SIZE = 20;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, RouterLink],
  selector: 'app-admin-consulta-insumo',
  styleUrl: './admin-consulta-insumo.component.css',
  templateUrl: './admin-consulta-insumo.component.html',
})
export class AdminConsultaInsumoComponent {
  protected readonly insumoId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly posicao = signal<AdminPosicaoInsumoPrecisa | null>(null);
  protected readonly carregandoPosicao = signal(false);
  protected readonly erroPosicao = signal('');
  protected readonly movimentos = signal<AdminPagina<AdminMovimentoPreciso> | null>(null);
  protected readonly carregandoMovimentos = signal(false);
  protected readonly erroMovimentos = signal('');
  protected readonly tipoSelecionado = signal<TipoMovimentoInsumo | ''>('');
  protected readonly loteSelecionado = signal('');
  protected readonly pagina = signal(1);
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil((this.movimentos()?.totalItens ?? 0) / PAGE_SIZE)),
  );
  protected readonly lotes = computed(() =>
    (this.posicao()?.posicoes ?? []).filter(
      (posicao): posicao is AdminPosicaoEstoquePrecisa & { loteId: string } =>
        posicao.loteId !== null,
    ),
  );
  protected readonly tiposMovimento: ReadonlyArray<{
    value: TipoMovimentoInsumo;
    label: string;
  }> = [
    { value: 'EstoqueInicial', label: 'Estoque inicial' },
    { value: 'Aquisicao', label: 'Aquisição' },
    { value: 'SaidaManual', label: 'Saída manual' },
    { value: 'Perda', label: 'Perda' },
    { value: 'Descarte', label: 'Descarte' },
    { value: 'AjusteInventario', label: 'Ajuste de inventário' },
    { value: 'Estorno', label: 'Estorno' },
  ];
  protected readonly fusoLocal =
    Intl.DateTimeFormat().resolvedOptions().timeZone || 'fuso local do dispositivo';

  private readonly insumosService = inject(InsumosService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private idConsultaMovimentos = 0;

  constructor() {
    if (!this.insumoId) {
      this.erroPosicao.set('O identificador do insumo não foi informado.');
      this.erroMovimentos.set('O identificador do insumo não foi informado.');
      return;
    }

    this.carregarPosicao();
    this.carregarMovimentos();
  }

  protected carregarPosicao(): void {
    const id = this.insumoId;
    if (!id || this.carregandoPosicao()) {
      return;
    }

    this.carregandoPosicao.set(true);
    this.erroPosicao.set('');
    this.insumosService
      .consultarPosicao(id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoPosicao.set(false)),
      )
      .subscribe({
        next: (position) => this.posicao.set(position),
        error: (error: unknown) => this.tratarErro(error, this.erroPosicao),
      });
  }

  protected carregarMovimentos(): void {
    const id = this.insumoId;
    if (!id) {
      return;
    }

    const requestId = ++this.idConsultaMovimentos;
    this.carregandoMovimentos.set(true);
    this.erroMovimentos.set('');
    this.insumosService
      .listarMovimentacoes(id, this.filtroAtual())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (requestId === this.idConsultaMovimentos) {
            this.carregandoMovimentos.set(false);
          }
        }),
      )
      .subscribe({
        next: (page) => {
          if (requestId !== this.idConsultaMovimentos) {
            return;
          }
          const totalPages = Math.max(1, Math.ceil(page.totalItens / PAGE_SIZE));
          if (this.pagina() > totalPages) {
            this.pagina.set(totalPages);
            this.carregarMovimentos();
            return;
          }
          this.movimentos.set(page);
          this.pagina.set(page.pagina);
        },
        error: (error: unknown) => {
          if (requestId === this.idConsultaMovimentos) {
            this.tratarErro(error, this.erroMovimentos);
          }
        },
      });
  }

  protected definirTipo(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }
    const value = this.tiposMovimento.find((item) => item.value === select.value)?.value ?? '';
    if (this.tipoSelecionado() === value) {
      return;
    }
    this.tipoSelecionado.set(value);
    this.pagina.set(1);
    this.carregarMovimentos();
  }

  protected definirLote(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }
    const value = this.lotes().some((lote) => lote.loteId === select.value) ? select.value : '';
    if (this.loteSelecionado() === value) {
      return;
    }
    this.loteSelecionado.set(value);
    this.pagina.set(1);
    this.carregarMovimentos();
  }

  protected irParaPagina(page: number): void {
    if (page < 1 || page > this.totalPaginas() || page === this.pagina()) {
      return;
    }
    this.pagina.set(page);
    this.carregarMovimentos();
  }

  protected rotuloTipo(tipo: TipoMovimentoInsumo): string {
    return this.tiposMovimento.find((item) => item.value === tipo)?.label ?? 'Movimento';
  }

  protected rotuloDirecao(direcao: DirecaoMovimentoInsumo): string {
    return direcao === 'Entrada' ? 'Entrada' : 'Saída';
  }

  protected rotuloOrigem(origem: AdminMovimentoPreciso['origem']): string {
    switch (origem) {
      case 'UnidadeFisica':
        return 'Unidade física';
      case 'FormaCompra':
        return 'Forma de compra';
      case 'OperacaoManual':
        return 'Operação manual';
      case 'ContagemFisica':
        return 'Contagem física';
      case 'MovimentoOriginal':
        return 'Movimento original';
    }
  }

  protected rotuloSituacao(posicao: AdminPosicaoEstoquePrecisa): string {
    if (posicao.bloqueado === true) {
      return 'Bloqueado';
    }
    if (!posicao.elegivel && posicao.dataValidade) {
      return 'Vencido';
    }
    if (!posicao.elegivel) {
      return 'Não elegível';
    }
    return 'Válido / elegível';
  }

  protected classeSituacao(posicao: AdminPosicaoEstoquePrecisa): string {
    if (posicao.bloqueado === true || !posicao.elegivel) {
      return 'border-amber-300 bg-amber-50 text-amber-950';
    }
    return 'border-emerald-200 bg-emerald-50 text-emerald-950';
  }

  protected rotuloUnidade(unidade: UnidadeFisicaEstoque): string {
    switch (unidade) {
      case 'Grama':
        return 'g';
      case 'Quilograma':
        return 'kg';
      case 'Mililitro':
        return 'ml';
      case 'Litro':
        return 'L';
      case 'Unidade':
        return 'un.';
    }
  }

  protected formatarQuantidade(value: string, unit: UnidadeFisicaEstoque): string {
    return `${formatDecimalPtBr(value)} ${this.rotuloUnidade(unit)}`;
  }

  protected formatarDataCivil(value: string | null): string {
    if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return 'Não informada';
    }
    const [year, month, day] = value.split('-');
    return `${day}/${month}/${year}`;
  }

  protected formatarInstanteUtc(value: string): string {
    const instant = new Date(value);
    if (Number.isNaN(instant.getTime())) {
      return 'Data indisponível';
    }
    return new Intl.DateTimeFormat('pt-BR', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      timeZoneName: 'short',
    }).format(instant);
  }

  protected tipoClasse(tipo: TipoMovimentoInsumo): string {
    switch (tipo) {
      case 'EstoqueInicial':
      case 'Aquisicao':
        return 'border-emerald-200 bg-emerald-50 text-emerald-950';
      case 'SaidaManual':
      case 'Perda':
      case 'Descarte':
        return 'border-orange-200 bg-orange-50 text-orange-950';
      case 'AjusteInventario':
        return 'border-sky-200 bg-sky-50 text-sky-950';
      case 'Estorno':
        return 'border-violet-200 bg-violet-50 text-violet-950';
    }
  }

  private filtroAtual(): AdminMovimentoFiltro {
    const filter: AdminMovimentoFiltro = {
      pagina: this.pagina(),
      itensPorPagina: PAGE_SIZE,
    };
    const type = this.tipoSelecionado();
    if (type !== '') {
      filter.tipo = type;
    }
    if (this.loteSelecionado()) {
      filter.loteId = this.loteSelecionado();
    }
    return filter;
  }

  private tratarErro(error: unknown, target: typeof this.erroPosicao): void {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      target.set('Você não tem permissão para consultar o estoque deste insumo.');
    } else if (error instanceof HttpErrorResponse && error.status === 404) {
      target.set('Este insumo não foi encontrado.');
    } else if (error instanceof HttpErrorResponse && error.status === 400) {
      target.set('Os filtros informados são inválidos. Revise e tente novamente.');
    } else {
      target.set('Não foi possível carregar esta consulta. Verifique a conexão e tente novamente.');
    }
  }
}
