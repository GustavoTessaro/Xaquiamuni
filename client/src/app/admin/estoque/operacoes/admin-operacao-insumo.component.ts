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
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, finalize } from 'rxjs';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminInsumoDetalhe,
  AdminMovimentoPreciso,
  AdminPagina,
  AdminPosicaoEstoquePrecisa,
  AdminResumoInsumoPreciso,
  AdminPosicaoInsumoPrecisa,
  EstornarMovimentoInsumoRequest,
  InsumosService,
  RegistrarContagemInsumoRequest,
  RegistrarSaidaInsumoRequest,
  ResultadoContagemInsumo,
  TipoSaidaInsumo,
  TipoMovimentoInsumo,
  UnidadeBaseEstoque,
  UnidadeFisicaEstoque,
} from '../../../core/admin-estoque/insumos.service';
import {
  compareDecimalText,
  formatDecimalPtBr,
  formatSignedDecimalPtBr,
  isValidBaseQuantity,
  isValidNonNegativeBaseQuantity,
  parseDecimalText,
  subtractDecimalText,
} from '../../../core/admin-estoque/estoque-decimal';
import {
  EstadoOperacaoEstoquePendente,
  OperacaoEstoquePendente,
  OperacaoPendenteStore,
} from '../../../core/admin-estoque/operacao-pendente.store';
import { AdminWorkspaceComponent } from '../../admin-workspace.component';

type AcaoOperacao = TipoSaidaInsumo | 'Contagem' | 'Estorno';
type EtapaOperacao = 'formulario' | 'revisao' | 'enviando' | 'incerta' | 'conflito' | 'sucesso';
type EstadoPendente = EstadoOperacaoEstoquePendente;

interface OperacaoControls {
  insumoId: FormControl<string>;
  acao: FormControl<AcaoOperacao>;
  quantidade: FormControl<string>;
  loteId: FormControl<string>;
  motivo: FormControl<string>;
  observacao: FormControl<string>;
}

interface ResumoOperacao {
  insumoNome: string;
  acao: AcaoOperacao;
  quantidade: string | null;
  unidade: UnidadeBaseEstoque | null;
  saldoReferencia: string | null;
  diferenca: string | null;
  lote: string | null;
  motivo: string;
  movimentoOriginalId: string | null;
}

type RequisicaoOperacao =
  RegistrarSaidaInsumoRequest | RegistrarContagemInsumoRequest | EstornarMovimentoInsumoRequest;
type OperacaoPendente = OperacaoEstoquePendente<RequisicaoOperacao, ResumoOperacao>;

const SESSION_STORAGE_KEY = 'xaquiamuni:estoque:operacao:pendente';
const PAGE_SIZE = 20;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  selector: 'app-admin-operacao-insumo',
  styleUrl: './admin-operacao-insumo.component.css',
  templateUrl: './admin-operacao-insumo.component.html',
})
export class AdminOperacaoInsumoComponent {
  protected readonly resultados = signal<AdminResumoInsumoPreciso[]>([]);
  protected readonly detalhe = signal<AdminInsumoDetalhe | null>(null);
  protected readonly posicao = signal<AdminPosicaoInsumoPrecisa | null>(null);
  protected readonly movimentoOriginal = signal<AdminMovimentoPreciso | null>(null);
  protected readonly historicoAtualizado = signal<AdminPagina<AdminMovimentoPreciso> | null>(null);
  protected readonly etapa = signal<EtapaOperacao>('formulario');
  protected readonly carregando = signal(false);
  protected readonly carregandoLista = signal(false);
  protected readonly carregandoPosicao = signal(false);
  protected readonly carregandoSessao = signal(true);
  protected readonly enviando = signal(false);
  protected readonly buscandoMovimento = signal(false);
  protected readonly erroAtualizacaoHistorico = signal('');
  protected readonly mensagemRecontagem = signal('');
  protected readonly erro = signal('');
  protected readonly erroLista = signal('');
  protected readonly erroPosicao = signal('');
  protected readonly erroPendente = signal('');
  protected readonly podeRecontar = signal(false);
  protected readonly mensagemSucesso = signal('');
  protected readonly busca = new FormControl('', { nonNullable: true });
  protected readonly pagina = signal(1);
  protected readonly totalInsumos = signal(0);
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.totalInsumos() / PAGE_SIZE)),
  );
  protected readonly operacaoPendente = signal<OperacaoPendente | null>(null);
  protected readonly unidadeBase = computed(
    () => this.detalhe()?.resumo.unidadeBase ?? this.posicao()?.resumo.unidadeBase ?? null,
  );
  protected readonly posicoesLote = computed(() =>
    (this.posicao()?.posicoes ?? []).filter(
      (item): item is AdminPosicaoEstoquePrecisa & { loteId: string } => item.loteId !== null,
    ),
  );
  protected readonly posicaoSelecionada = () => {
    const stock = this.posicao();
    if (!stock) {
      return null;
    }
    const loteId = this.form.controls.loteId.value || null;
    return stock.posicoes.find((item) => item.loteId === loteId) ?? null;
  };
  protected readonly permiteLoteAtual = computed(() => {
    const item = this.detalhe();
    return !!item?.resumo.controlaLote;
  });
  protected readonly tiposAcao: ReadonlyArray<{ value: AcaoOperacao; label: string }> = [
    { value: 'SaidaManual', label: 'Saída manual' },
    { value: 'Perda', label: 'Perda' },
    { value: 'Descarte', label: 'Descarte' },
    { value: 'Contagem', label: 'Contagem física' },
  ];

  protected readonly form = new FormGroup<OperacaoControls>({
    insumoId: new FormControl('', { nonNullable: true }),
    acao: new FormControl<AcaoOperacao>('SaidaManual', { nonNullable: true }),
    quantidade: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    loteId: new FormControl('', { nonNullable: true }),
    motivo: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(300)],
    }),
    observacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });

  private readonly insumosService = inject(InsumosService);
  private readonly operacaoPendenteStore = inject(OperacaoPendenteStore);
  private readonly adminAuthService = inject(AdminAuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private responsavelId = '';

  constructor() {
    this.form.controls.acao.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.erro.set('');
      this.etapa.set('formulario');
    });
    this.form.controls.quantidade.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.mensagemRecontagem.set(''));

    this.adminAuthService
      .getCurrentSession()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => {
          this.carregandoSessao.set(false);
          if (!session) {
            void this.router.navigateByUrl('/admin/login');
            return;
          }
          this.responsavelId = session.userId;
          this.carregarOperacaoPendente(session.userId);
          if (this.erroPendente()) {
            return;
          }
          const pending = this.operacaoPendente();
          if (pending) {
            this.form.controls.insumoId.setValue(pending.insumoId);
            this.form.controls.acao.setValue(pending.resumo.acao);
            return;
          }

          const insumoId =
            this.route.snapshot.paramMap.get('id') ??
            this.route.snapshot.queryParamMap.get('insumoId');
          const action =
            this.route.snapshot.queryParamMap.get('acao') ??
            (this.route.snapshot.paramMap.has('movimentoId') ? 'Estorno' : null);
          const selectedAction = this.tiposAcao.find((item) => item.value === action)?.value;
          if (selectedAction) {
            this.form.controls.acao.setValue(selectedAction);
          }
          if (insumoId) {
            this.selecionarInsumoId(insumoId);
          } else {
            this.carregarInsumos();
          }
        },
        error: () => {
          this.carregandoSessao.set(false);
          this.erroLista.set(
            'Não foi possível confirmar a sessão administrativa. Tente novamente.',
          );
        },
      });
  }

  protected carregarInsumos(): void {
    if (this.carregandoLista()) {
      return;
    }
    this.carregandoLista.set(true);
    this.erroLista.set('');
    this.insumosService
      .listar({ nome: this.busca.value.trim(), pagina: this.pagina(), itensPorPagina: PAGE_SIZE })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoLista.set(false)),
      )
      .subscribe({
        next: (page) => {
          this.resultados.set(page.itens);
          this.totalInsumos.set(page.totalItens);
          this.pagina.set(page.pagina);
        },
        error: (error: unknown) => this.erroLista.set(this.mensagemErro(error)),
      });
  }

  protected pesquisar(): void {
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

  protected selecionarInsumo(insumo: AdminResumoInsumoPreciso): void {
    this.selecionarInsumoId(insumo.insumoId);
  }

  protected selecionarInsumoId(id: string): void {
    if (this.carregando()) {
      return;
    }
    this.form.controls.insumoId.setValue(id);
    this.carregando.set(true);
    this.erro.set('');
    this.insumosService
      .obter(id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (detail) => {
          this.detalhe.set(detail);
          this.carregarPosicao();
          const lotId = this.route.snapshot.queryParamMap.get('loteId');
          if (lotId && detail.resumo.controlaLote) {
            this.form.controls.loteId.setValue(lotId);
          }
          const movementId = this.route.snapshot.paramMap.get('movimentoId');
          if (movementId) {
            this.carregarMovimentoOriginal(id, movementId);
          }
        },
        error: (error: unknown) => this.erro.set(this.mensagemErro(error)),
      });
  }

  protected carregarPosicao(): void {
    const id = this.form.controls.insumoId.value;
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
        next: (stock) => this.posicao.set(stock),
        error: (error: unknown) => this.erroPosicao.set(this.mensagemErro(error)),
      });
  }

  protected solicitarRevisao(): void {
    this.form.markAllAsTouched();
    const error = this.validarOperacao();
    if (error) {
      this.erro.set(error);
      return;
    }
    const summary = this.criarResumo();
    if (!summary) {
      this.erro.set('Revise os dados da operação e tente novamente.');
      return;
    }
    this.erro.set('');
    this.etapa.set('revisao');
  }

  protected voltarParaFormulario(): void {
    this.etapa.set('formulario');
    this.erro.set('');
  }

  protected confirmarOperacao(): void {
    if (this.etapa() !== 'revisao' || this.enviando() || !this.detalhe()) {
      return;
    }
    const item = this.detalhe();
    const summary = this.criarResumo();
    const request = this.criarRequisicao();
    if (!item || !summary || !request || !this.responsavelId) {
      this.etapa.set('formulario');
      this.erro.set('Revise os dados da operação antes de confirmar.');
      return;
    }
    const key = this.operacaoPendenteStore.criarChaveIdempotencia();
    if (!key) {
      this.erro.set('Não foi possível gerar uma chave segura. Recarregue a página.');
      return;
    }
    const pending: OperacaoPendente = {
      atorId: this.responsavelId,
      idempotencyKey: key,
      insumoId: item.resumo.insumoId,
      resumo: summary,
      request,
      state: 'enviando',
    };
    if (!this.salvarOperacao(pending)) {
      this.erro.set(
        'Não foi possível preservar a operação com segurança no navegador; ela não foi enviada.',
      );
      return;
    }
    this.operacaoPendente.set(pending);
    this.enviarOperacao(pending);
  }

  protected tentarNovamente(): void {
    const pending = this.operacaoPendente();
    if (!pending || pending.state !== 'incerta' || this.enviando()) {
      return;
    }
    this.enviarOperacao({ ...pending, state: 'enviando' });
  }

  protected confirmarNovaConferencia(): void {
    if (this.etapa() !== 'conflito' || !this.podeRecontar()) {
      return;
    }
    if (!this.removerOperacao()) {
      this.erro.set(
        'Não foi possível encerrar a tentativa recusada. Reconcilie o estoque antes de continuar.',
      );
      return;
    }
    this.operacaoPendente.set(null);
    this.podeRecontar.set(false);
    this.carregarPosicao();
    this.form.controls.quantidade.setValue('');
    this.form.controls.motivo.setValue('');
    this.form.controls.observacao.setValue('');
    this.etapa.set('formulario');
    this.erro.set('');
    this.mensagemRecontagem.set(
      'A quantidade da tentativa anterior foi descartada. Aguarde a posição atual e faça uma nova conferência física.',
    );
  }

  protected selecionarOutroInsumo(): void {
    this.detalhe.set(null);
    this.posicao.set(null);
    this.movimentoOriginal.set(null);
    this.form.controls.insumoId.setValue('');
    this.form.controls.loteId.setValue('');
    this.form.controls.quantidade.setValue('');
    this.erro.set('');
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { insumoId: null, loteId: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.carregarInsumos();
  }

  protected unidadeLabel(unidade: UnidadeFisicaEstoque | null): string {
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
      default:
        return '';
    }
  }

  protected formatarQuantidade(
    value: string | null,
    unit: UnidadeFisicaEstoque | null = this.unidadeBase(),
  ): string {
    return value === null
      ? 'Não informado'
      : `${formatDecimalPtBr(value)} ${this.unidadeLabel(unit)}`.trim();
  }

  protected diferencaContagem(): string | null {
    const current = this.posicaoSelecionada()?.saldoFisico ?? this.posicao()?.resumo.saldoFisico;
    const observed = this.form.controls.quantidade.value;
    return current === undefined || !observed ? null : subtractDecimalText(observed, current);
  }

  protected diferencaFormatada(): string {
    const difference = this.diferencaContagem();
    return difference === null
      ? 'Informe a quantidade encontrada'
      : formatSignedDecimalPtBr(difference);
  }

  protected formatarDiferenca(value: string | null): string {
    return value === null ? 'Não disponível' : formatSignedDecimalPtBr(value);
  }

  protected resumoAtual(): ResumoOperacao | null {
    return this.criarResumo();
  }

  protected modoEstorno(): boolean {
    return this.route.snapshot.paramMap.has('movimentoId');
  }

  protected rotuloAcao(acao = this.form.controls.acao.value): string {
    switch (acao) {
      case 'SaidaManual':
        return 'Saída manual';
      case 'Perda':
        return 'Perda';
      case 'Descarte':
        return 'Descarte';
      case 'Contagem':
        return 'Contagem física';
      case 'Estorno':
        return 'Estorno';
    }
  }

  protected rotuloMovimento(tipo: TipoMovimentoInsumo): string {
    switch (tipo) {
      case 'EstoqueInicial':
        return 'Estoque inicial';
      case 'Aquisicao':
        return 'Aquisição';
      case 'AjusteInventario':
        return 'Ajuste de inventário';
      case 'Estorno':
        return 'Estorno';
      default:
        return this.rotuloAcao(tipo);
    }
  }

  protected rotuloTipoInsumo(tipo: AdminResumoInsumoPreciso['tipo']): string {
    switch (tipo) {
      case 'MateriaPrima':
        return 'Matéria-prima';
      case 'Embalagem':
        return 'Embalagem';
      case 'Outro':
        return 'Outro';
    }
  }

  protected rotuloSituacaoLote(position: AdminPosicaoEstoquePrecisa): string {
    if (position.bloqueado) {
      return 'Bloqueado';
    }
    return position.elegivel ? 'Elegível' : 'Vencido ou inelegível';
  }

  private carregarMovimentoOriginal(insumoId: string, movimentoId: string): void {
    this.buscandoMovimento.set(true);
    this.insumosService
      .consultarMovimentacao(insumoId, movimentoId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.buscandoMovimento.set(false)),
      )
      .subscribe({
        next: (movement) => {
          this.movimentoOriginal.set(movement);
          if (movement.tipo === 'Estorno') {
            this.erro.set('Um movimento de estorno não pode ser estornado novamente.');
          } else {
            this.form.controls.acao.setValue('Estorno');
          }
        },
        error: (error: unknown) => this.erro.set(this.mensagemErro(error)),
      });
  }

  private enviarOperacao(operation: OperacaoPendente): void {
    if (this.enviando()) {
      return;
    }
    this.enviando.set(true);
    this.etapa.set('enviando');
    this.erro.set('');
    const request$ = this.criarRequisicaoHttp(operation);
    request$
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.enviando.set(false)),
      )
      .subscribe({
        next: (result) => {
          const movement =
            'resultado' in result
              ? result.resultado === 'MovimentoRegistrado'
                ? result.movimento
                : null
              : result;
          this.mensagemSucesso.set(
            movement
              ? `Operação confirmada. Movimento ${movement.id} registrado.`
              : 'Contagem confirmada sem diferença; nenhum movimento foi criado.',
          );
          this.etapa.set('sucesso');
          if (!this.removerOperacao()) {
            this.erro.set(
              'A operação foi confirmada, mas a tentativa não pôde ser removida do armazenamento local. Reconcilie antes de iniciar outra.',
            );
            return;
          }
          this.operacaoPendente.set(null);
          this.atualizarConsultasAposSucesso(operation.insumoId);
        },
        error: (error: unknown) => this.tratarFalhaEnvio(error, operation),
      });
  }

  private criarRequisicaoHttp(
    operation: OperacaoPendente,
  ): Observable<AdminMovimentoPreciso | ResultadoContagemInsumo> {
    const request = operation.request;
    switch (operation.resumo.acao) {
      case 'SaidaManual':
      case 'Perda':
      case 'Descarte':
        if (!('tipo' in request)) {
          throw new Error('A tentativa pendente não contém uma requisição de saída válida.');
        }
        return this.insumosService.registrarSaida(
          operation.insumoId,
          request,
          operation.idempotencyKey,
        );
      case 'Contagem':
        if (!('quantidadeEncontrada' in request)) {
          throw new Error('A tentativa pendente não contém uma requisição de contagem válida.');
        }
        return this.insumosService.registrarContagem(
          operation.insumoId,
          request,
          operation.idempotencyKey,
        );
      case 'Estorno':
        if ('tipo' in request || 'quantidadeEncontrada' in request) {
          throw new Error('A tentativa pendente não contém uma requisição de estorno válida.');
        }
        return this.insumosService.estornarMovimento(
          operation.insumoId,
          operation.resumo.movimentoOriginalId ?? '',
          request,
          operation.idempotencyKey,
        );
    }
  }

  private atualizarConsultasAposSucesso(insumoId: string): void {
    this.posicao.set(null);
    this.historicoAtualizado.set(null);
    this.erroAtualizacaoHistorico.set('');
    this.carregarPosicao();
    this.insumosService
      .listarMovimentacoes(insumoId, { pagina: 1, itensPorPagina: 20 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => this.historicoAtualizado.set(history),
        error: (error: unknown) => this.erroAtualizacaoHistorico.set(this.mensagemErro(error)),
      });
  }

  private tratarFalhaEnvio(error: unknown, operation: OperacaoPendente): void {
    if (error instanceof HttpErrorResponse && [400, 401, 403, 404].includes(error.status)) {
      if (!this.removerOperacao()) {
        const pending = { ...operation, state: 'conflito' as const };
        this.salvarOperacao(pending);
        this.operacaoPendente.set(pending);
        this.etapa.set('conflito');
        this.erro.set(
          'A API rejeitou a operação, mas o navegador não conseguiu limpar a tentativa salva. Reconcilie o estoque antes de continuar.',
        );
        return;
      }
      this.operacaoPendente.set(null);
      this.etapa.set('formulario');
      this.erro.set(this.mensagemErro(error));
      return;
    }

    const conflict = error instanceof HttpErrorResponse && error.status === 409;
    const nextState: EstadoPendente = conflict ? 'conflito' : 'incerta';
    const rawDetail = this.detalheConflito(error);
    const staleCount =
      conflict &&
      operation.resumo.acao === 'Contagem' &&
      /posição mudou desde a leitura da contagem/i.test(rawDetail);
    const pending = {
      ...operation,
      state: nextState,
      erro: staleCount ? rawDetail : undefined,
    };
    if (!this.salvarOperacao(pending)) {
      this.etapa.set('conflito');
      this.erro.set(
        'Não foi possível preservar a chave e os dados da tentativa. Não repita a operação; reconcilie o histórico.',
      );
      return;
    }
    this.operacaoPendente.set(pending);
    this.podeRecontar.set(staleCount);
    this.etapa.set(conflict ? 'conflito' : 'incerta');
    this.erro.set(
      conflict
        ? this.mensagemConflito(error, operation.resumo.acao)
        : 'Não foi possível confirmar o resultado. Reenvie manualmente os mesmos dados usando a mesma chave; não altere o payload.',
    );
  }

  private mensagemConflito(error: unknown, action: AcaoOperacao): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'A operação conflitou com outra alteração. Reconcilie o estoque antes de continuar.';
    }
    const detail =
      typeof error.error === 'string' ? error.error : JSON.stringify(error.error ?? '');
    if (/SaldoInsuficiente|saldo.*(insuficiente|não permite)/i.test(detail)) {
      return 'O saldo atual não permite esta operação. Atualize a posição e revise a quantidade.';
    }
    if (/LoteInelegivel|lote.*inelegível/i.test(detail)) {
      return 'O lote não está elegível para saída manual. Atualize a posição; perdas ou descartes seguem as regras da API.';
    }
    if (/MovimentoJaEstornado|já possui estorno|já foi estornado/i.test(detail)) {
      return 'Este movimento já foi estornado. Atualize o histórico antes de continuar.';
    }
    if (/idempot|chave/i.test(detail)) {
      return 'A chave foi associada a outro conteúdo. A tentativa permanece bloqueada; reconcilie o histórico.';
    }
    if (action === 'Contagem') {
      return 'O saldo mudou desde a conferência. Atualize a posição e faça uma nova contagem antes de enviar.';
    }
    return 'O estoque mudou durante a operação. Atualize a posição e verifique o histórico.';
  }

  private detalheConflito(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return '';
    }
    if (typeof error.error?.detail === 'string') {
      return error.error.detail;
    }
    return typeof error.error === 'string' ? error.error : JSON.stringify(error.error ?? '');
  }

  private carregarOperacaoPendente(actorId: string): void {
    let raw: string | null;
    try {
      raw = this.operacaoPendenteStore.ler(SESSION_STORAGE_KEY);
    } catch (error) {
      if (error instanceof DOMException) {
        this.erroPendente.set(
          'O navegador bloqueou o armazenamento seguro da tentativa pendente. Não registre outra operação nesta sessão.',
        );
        return;
      }
      throw error;
    }
    if (raw === null) {
      return;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch (error) {
      if (error instanceof SyntaxError) {
        this.erroPendente.set(
          'A tentativa anterior não pôde ser recuperada. Reconcilie o estoque antes de registrar outra operação.',
        );
        return;
      }
      throw error;
    }
    if (!this.ehOperacaoPendente(parsed)) {
      this.erroPendente.set(
        'A tentativa pendente está inválida. Reconcilie o estoque antes de registrar outra operação.',
      );
      return;
    }
    this.operacaoPendente.set(parsed);
    if (parsed.atorId !== actorId || parsed.state === 'conflito') {
      this.etapa.set('conflito');
      this.podeRecontar.set(
        parsed.atorId === actorId &&
          parsed.resumo.acao === 'Contagem' &&
          typeof parsed.erro === 'string' &&
          /posição mudou desde a leitura da contagem/i.test(parsed.erro),
      );
      this.erro.set(
        parsed.atorId !== actorId
          ? 'Há uma operação pendente iniciada por outra conta ADMIN. Entre com a conta original para reconciliar.'
          : this.podeRecontar()
            ? 'A posição mudou desde a leitura da contagem. Atualize o saldo e faça uma nova conferência.'
            : 'A API informou um conflito. A chave foi preservada; reconcilie o histórico antes de iniciar outra operação.',
      );
      return;
    }
    const uncertain = { ...parsed, state: 'incerta' as const };
    if (!this.salvarOperacao(uncertain)) {
      this.erroPendente.set(
        'Não foi possível preservar o estado da tentativa pendente. Reconcilie o estoque antes de continuar.',
      );
      return;
    }
    this.operacaoPendente.set(uncertain);
    this.etapa.set('incerta');
    this.erro.set('A operação anterior não foi confirmada. Reenvie exatamente os mesmos dados.');
  }

  private ehOperacaoPendente(value: unknown): value is OperacaoPendente {
    if (typeof value !== 'object' || value === null) {
      return false;
    }
    const candidate = value as Partial<OperacaoPendente>;
    if (
      typeof candidate.atorId === 'string' &&
      typeof candidate.idempotencyKey === 'string' &&
      candidate.idempotencyKey.length > 0 &&
      candidate.idempotencyKey.length <= 128 &&
      typeof candidate.insumoId === 'string' &&
      (candidate.state === 'enviando' ||
        candidate.state === 'incerta' ||
        candidate.state === 'conflito') &&
      typeof candidate.resumo === 'object' &&
      candidate.resumo !== null &&
      typeof candidate.resumo.insumoNome === 'string' &&
      typeof candidate.request === 'object' &&
      candidate.request !== null &&
      (candidate.erro === undefined || typeof candidate.erro === 'string')
    ) {
      const { acao } = candidate.resumo;
      const request = candidate.request;
      if (acao === 'SaidaManual' || acao === 'Perda' || acao === 'Descarte') {
        return (
          'tipo' in request &&
          request.tipo === acao &&
          typeof request.quantidadeOriginal === 'string' &&
          typeof request.unidadeOriginal === 'string' &&
          typeof request.motivo === 'string' &&
          (request.loteId === null || typeof request.loteId === 'string') &&
          (request.observacao === null || typeof request.observacao === 'string')
        );
      }
      if (acao === 'Contagem') {
        return (
          'quantidadeEncontrada' in request &&
          typeof request.quantidadeEncontrada === 'string' &&
          typeof request.unidadeOriginal === 'string' &&
          (request.versaoSaldoEsperada === null ||
            typeof request.versaoSaldoEsperada === 'string') &&
          typeof request.motivo === 'string' &&
          (request.loteId === null || typeof request.loteId === 'string') &&
          (request.observacao === null || typeof request.observacao === 'string')
        );
      }
      if (acao === 'Estorno') {
        return (
          !('tipo' in request) &&
          !('quantidadeEncontrada' in request) &&
          typeof request.motivo === 'string' &&
          (request.observacao === null || typeof request.observacao === 'string') &&
          typeof candidate.resumo.movimentoOriginalId === 'string'
        );
      }
    }
    return false;
  }

  private salvarOperacao(operation: OperacaoPendente): boolean {
    return this.operacaoPendenteStore.salvar(SESSION_STORAGE_KEY, operation);
  }

  private removerOperacao(): boolean {
    return this.operacaoPendenteStore.remover(SESSION_STORAGE_KEY);
  }

  private validarOperacao(): string | null {
    const item = this.detalhe();
    const position = this.posicao();
    const action = this.form.controls.acao.value;
    if (!item || !position) {
      return 'Carregue o insumo e a posição atual antes de registrar a operação.';
    }
    if (
      this.form.controls.motivo.invalid ||
      this.form.controls.observacao.invalid ||
      (action !== 'Estorno' && this.form.controls.quantidade.invalid)
    ) {
      return 'Revise o motivo, a observação e os campos obrigatórios.';
    }
    if (action === 'Estorno') {
      if (!this.movimentoOriginal() || this.movimentoOriginal()?.tipo === 'Estorno') {
        return 'O movimento não está disponível ou não é elegível para estorno.';
      }
      return this.form.controls.motivo.value.trim() ? null : 'Informe o motivo do estorno.';
    }

    const quantity = this.form.controls.quantidade.value.trim();
    const countAction = action === 'Contagem';
    if (
      !(countAction
        ? isValidNonNegativeBaseQuantity(quantity, item.resumo.unidadeBase === 'Unidade')
        : isValidBaseQuantity(quantity, item.resumo.unidadeBase === 'Unidade'))
    ) {
      return countAction
        ? 'Informe uma quantidade física válida, sem sinal negativo e com até seis casas decimais. Unidades devem ser inteiras.'
        : 'Informe uma quantidade positiva válida, sem arredondamento; unidades devem ser inteiras.';
    }
    if (item.resumo.controlaLote && !this.form.controls.loteId.value) {
      return 'Selecione o lote desta operação.';
    }
    const balance = this.posicaoSelecionada();
    if (item.resumo.controlaLote && !balance) {
      return 'A posição do lote não foi carregada. Atualize a posição antes de continuar.';
    }
    if (countAction) {
      if (!this.form.controls.motivo.value.trim()) {
        return 'Informe o motivo da contagem física.';
      }
      return null;
    }
    if (!this.form.controls.motivo.value.trim()) {
      return 'Informe o motivo da saída, perda ou descarte.';
    }
    if (action === 'SaidaManual' && balance && !balance.elegivel) {
      return 'A saída manual exige um lote elegível; use perda ou descarte se aplicável.';
    }
    const maximum =
      action === 'SaidaManual'
        ? (balance?.saldoDisponivel ?? position.resumo.saldoDisponivel)
        : (balance?.saldoFisico ?? position.resumo.saldoFisico);
    if (compareDecimalText(quantity, maximum) === 1) {
      return `A quantidade excede o saldo ${action === 'SaidaManual' ? 'disponível' : 'físico'} desta posição (${formatDecimalPtBr(maximum)} ${this.unidadeLabel(item.resumo.unidadeBase)}).`;
    }
    return null;
  }

  private criarResumo(): ResumoOperacao | null {
    const item = this.detalhe();
    const action = this.form.controls.acao.value;
    if (!item) {
      return null;
    }
    const selected = this.posicaoSelecionada();
    const lotLabel = selected?.identificacaoLote ?? null;
    if (action === 'Estorno') {
      const movement = this.movimentoOriginal();
      return movement
        ? {
            insumoNome: item.resumo.nome,
            acao: action,
            quantidade: null,
            unidade: item.resumo.unidadeBase,
            saldoReferencia: null,
            diferenca: null,
            lote: movement.identificacaoLote,
            motivo: this.form.controls.motivo.value.trim(),
            movimentoOriginalId: movement.id,
          }
        : null;
    }
    const quantity = parseDecimalText(this.form.controls.quantidade.value.trim())?.text;
    if (quantity === undefined) {
      return null;
    }
    const current = selected?.saldoFisico ?? this.posicao()?.resumo.saldoFisico ?? null;
    return {
      insumoNome: item.resumo.nome,
      acao: action,
      quantidade: quantity,
      unidade: item.resumo.unidadeBase,
      saldoReferencia: action === 'Contagem' ? current : null,
      diferenca: action === 'Contagem' && current ? subtractDecimalText(quantity, current) : null,
      lote: lotLabel,
      motivo: this.form.controls.motivo.value.trim(),
      movimentoOriginalId: null,
    };
  }

  private criarRequisicao(): RequisicaoOperacao | null {
    const item = this.detalhe();
    if (!item) {
      return null;
    }
    const action = this.form.controls.acao.value;
    const reason = this.form.controls.motivo.value.trim();
    const observation = this.form.controls.observacao.value.trim() || null;
    if (action === 'Estorno') {
      return this.movimentoOriginal() ? { motivo: reason, observacao: observation } : null;
    }
    const quantity = parseDecimalText(this.form.controls.quantidade.value.trim())?.text;
    if (quantity === undefined) {
      return null;
    }
    const lotId = item.resumo.controlaLote ? this.form.controls.loteId.value || null : null;
    if (action === 'Contagem') {
      return {
        quantidadeEncontrada: quantity,
        unidadeOriginal: item.resumo.unidadeBase,
        loteId: lotId,
        versaoSaldoEsperada: this.posicaoSelecionada()?.versaoSaldo ?? null,
        motivo: reason,
        observacao: observation,
      };
    }
    return {
      tipo: action,
      quantidadeOriginal: quantity,
      unidadeOriginal: item.resumo.unidadeBase,
      loteId: lotId,
      motivo: reason,
      observacao: observation,
    };
  }

  private mensagemErro(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        void this.router.navigateByUrl('/admin/login');
        return 'Sua sessão expirou. Entre novamente.';
      }
      if (error.status === 403) {
        return 'Sua conta não tem autorização ADMIN para esta operação.';
      }
      if (error.status === 404) {
        return 'O insumo ou movimento não foi encontrado.';
      }
      if (error.status === 400) {
        return this.mensagemValidacao(error);
      }
      if (error.status === 0 || error.status >= 500) {
        return 'Não foi possível conectar à API. Verifique a conexão e tente novamente.';
      }
    }
    return 'Não foi possível concluir a solicitação. Tente novamente.';
  }

  private mensagemValidacao(error: HttpErrorResponse): string {
    if (typeof error.error === 'string' && error.error.trim()) {
      return error.error;
    }
    if (typeof error.error?.detail === 'string') {
      return error.error.detail;
    }
    return 'A API rejeitou os dados. Revise quantidade, unidade, lote e motivo.';
  }
}
