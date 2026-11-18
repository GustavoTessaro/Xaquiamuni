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
import {
  EMPTY,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  switchMap,
} from 'rxjs';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminFormaCompraPrecisa,
  AdminInsumoDetalhePreciso,
  AdminMovimentoInsumo,
  AdminPagina,
  AdminPosicaoEstoquePrecisa,
  AdminResumoInsumoPreciso,
  InsumosService,
  RegistrarEntradaInsumoRequest,
  TipoEntradaInsumo,
  UnidadeBaseEstoque,
  UnidadeFisicaEstoque,
} from '../../../core/admin-estoque/insumos.service';
import {
  estimateCostPerBaseUnit,
  formatBrl,
  formatDecimalPtBr,
  isPositiveIntegerText,
  isValidBaseQuantity,
  isValidCostTotal,
  multiplyDecimalByInteger,
  multiplyDecimalByPowerOfTen,
  parseDecimalText,
} from '../../../core/admin-estoque/estoque-decimal';
import {
  EstadoOperacaoEstoquePendente,
  OperacaoEstoquePendente,
  OperacaoPendenteStore,
} from '../../../core/admin-estoque/operacao-pendente.store';
import { AdminWorkspaceComponent } from '../../admin-workspace.component';

type FormaQuantidade = 'Direta' | 'FormaCompra';
type FormaLote = 'Existente' | 'Novo';
type EtapaEntrada = 'formulario' | 'revisao' | 'enviando' | 'incerta' | 'conflito' | 'sucesso';
type EstadoPendente = EstadoOperacaoEstoquePendente;

interface EntradaControls {
  tipo: FormControl<TipoEntradaInsumo>;
  formaQuantidade: FormControl<FormaQuantidade>;
  unidadeOriginal: FormControl<UnidadeFisicaEstoque>;
  formaCompraId: FormControl<string>;
  quantidadeOriginal: FormControl<string>;
  custoTotal: FormControl<string>;
  observacao: FormControl<string>;
  formaLote: FormControl<FormaLote>;
  loteId: FormControl<string>;
  identificacaoNovoLote: FormControl<string>;
  dataEntradaNovoLote: FormControl<string>;
  dataValidadeNovoLote: FormControl<string>;
  fornecedorNovoLote: FormControl<string>;
  novoLoteBloqueado: FormControl<boolean>;
}

interface ResumoEntrada {
  insumoNome: string;
  tipo: TipoEntradaInsumo;
  tipoLabel: string;
  quantidadeOriginal: string;
  unidadeOriginalLabel: string;
  quantidadeBase: string;
  unidadeBase: UnidadeBaseEstoque;
  formaCompraNome: string | null;
  loteLabel: string;
  custoTotal: string | null;
  custoUnitarioEstimado: string | null;
}

type OperacaoPendente = OperacaoEstoquePendente<RegistrarEntradaInsumoRequest, ResumoEntrada>;

interface ConsultaInsumos {
  id: number;
  nome: string;
  pagina: number;
}

const SESSION_STORAGE_KEY = 'xaquiamuni:estoque:entrada:pendente';
const PAGE_SIZE = 20;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  selector: 'app-admin-entrada-insumo',
  styleUrl: './admin-entrada-insumo.component.css',
  templateUrl: './admin-entrada-insumo.component.html',
})
export class AdminEntradaInsumoComponent {
  protected readonly resultados = signal<AdminResumoInsumoPreciso[]>([]);
  protected readonly detalhe = signal<AdminInsumoDetalhePreciso | null>(null);
  protected readonly selecionado = computed(() => this.detalhe()?.resumo ?? null);
  protected readonly retornoInsumoId = computed(
    () =>
      this.detalhe()?.resumo.insumoId ??
      this.chavePendente()?.insumoId ??
      this.route.snapshot.queryParamMap.get('insumoId'),
  );
  protected readonly etapa = signal<EtapaEntrada>('formulario');
  protected readonly buscandoInsumos = signal(false);
  protected readonly carregandoDetalhe = signal(false);
  protected readonly enviando = signal(false);
  protected readonly atualizandoSaldo = signal(false);
  protected readonly erroBusca = signal('');
  protected readonly erroDetalhe = signal('');
  protected readonly erroEntrada = signal('');
  protected readonly erroSaldo = signal('');
  protected readonly erroValidacao = signal('');
  protected readonly totalInsumos = signal(0);
  protected readonly paginaInsumos = signal(1);
  protected readonly chavePendente = signal<OperacaoPendente | null>(null);
  protected readonly resumo = signal<ResumoEntrada | null>(null);
  protected readonly movimento = signal<AdminMovimentoInsumo | null>(null);
  protected readonly chave = signal('');
  protected readonly responsavelId = signal('');
  protected readonly erroRecuperacaoPendente = signal('');
  protected readonly carregandoSessao = signal(true);
  protected readonly busca = new FormControl('', { nonNullable: true });

  protected readonly form = new FormGroup<EntradaControls>({
    tipo: new FormControl<TipoEntradaInsumo>('Aquisicao', { nonNullable: true }),
    formaQuantidade: new FormControl<FormaQuantidade>('Direta', { nonNullable: true }),
    unidadeOriginal: new FormControl<UnidadeFisicaEstoque>('Grama', { nonNullable: true }),
    formaCompraId: new FormControl('', { nonNullable: true }),
    quantidadeOriginal: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    custoTotal: new FormControl('', { nonNullable: true }),
    observacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
    formaLote: new FormControl<FormaLote>('Novo', { nonNullable: true }),
    loteId: new FormControl('', { nonNullable: true }),
    identificacaoNovoLote: new FormControl('', { nonNullable: true }),
    dataEntradaNovoLote: new FormControl('', { nonNullable: true }),
    dataValidadeNovoLote: new FormControl('', { nonNullable: true }),
    fornecedorNovoLote: new FormControl('', { nonNullable: true }),
    novoLoteBloqueado: new FormControl(false, { nonNullable: true }),
  });

  protected readonly totalPaginasInsumos = computed(() =>
    Math.max(1, Math.ceil(this.totalInsumos() / PAGE_SIZE)),
  );
  protected readonly formasCompraAtivas = computed(() =>
    (this.detalhe()?.formasCompra ?? []).filter((forma) => forma.ativa),
  );
  protected readonly lotes = computed(() =>
    (this.detalhe()?.posicoes ?? []).filter(
      (posicao): posicao is AdminPosicaoEstoquePrecisa & { loteId: string } =>
        posicao.loteId !== null,
    ),
  );
  protected readonly selecionadoIneligivel = computed(() => {
    const id = this.form.controls.loteId.value;
    return this.lotes().find((lote) => lote.loteId === id && !lote.elegivel) ?? null;
  });

  private readonly insumosService = inject(InsumosService);
  private readonly operacaoPendenteStore = inject(OperacaoPendenteStore);
  private readonly adminAuthService = inject(AdminAuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly consultasInsumos = new Subject<ConsultaInsumos>();
  private idConsultaInsumos = 0;

  constructor() {
    this.consultasInsumos
      .pipe(
        switchMap(({ id, nome, pagina }) => {
          this.buscandoInsumos.set(true);
          this.erroBusca.set('');
          return this.insumosService
            .listar({ nome, ativo: true, pagina, itensPorPagina: PAGE_SIZE })
            .pipe(
              catchError((error: unknown) => {
                if (!this.redirecionarSessaoExpirada(error)) {
                  this.erroBusca.set(this.mensagemErroBusca(error));
                }
                return EMPTY;
              }),
              finalize(() => {
                if (id === this.idConsultaInsumos) {
                  this.buscandoInsumos.set(false);
                }
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((pagina) => this.definirResultados(pagina));

    this.busca.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.paginaInsumos.set(1);
        this.carregarInsumosAtivos();
      });

    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.erroValidacao.set('');
    });

    this.form.controls.tipo.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((tipo) => {
        if (tipo === 'Aquisicao') {
          this.form.controls.novoLoteBloqueado.setValue(false, { emitEvent: false });
        }
      });

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
          this.responsavelId.set(session.userId);
          this.carregarOperacaoPendente(session.userId);
          if (this.erroRecuperacaoPendente()) {
            return;
          }
          const pending = this.chavePendente();
          if (pending) {
            this.carregarDetalhe(pending.insumoId, true);
            return;
          }
          const insumoId = this.route.snapshot.queryParamMap.get('insumoId');
          if (insumoId) {
            this.carregarDetalhe(insumoId);
          } else {
            this.carregarInsumosAtivos();
          }
        },
        error: () => {
          this.carregandoSessao.set(false);
          this.erroBusca.set(
            'Não foi possível confirmar a sessão administrativa. Tente novamente.',
          );
        },
      });
  }

  protected carregarInsumosAtivos(): void {
    const id = ++this.idConsultaInsumos;
    this.resultados.set([]);
    this.consultasInsumos.next({
      id,
      nome: this.busca.value.trim(),
      pagina: this.paginaInsumos(),
    });
  }

  protected selecionarInsumo(insumo: AdminResumoInsumoPreciso): void {
    this.carregarDetalhe(insumo.insumoId);
  }

  protected carregarDetalhe(insumoId: string, permitirInativo = false): void {
    if (this.carregandoDetalhe()) {
      return;
    }

    this.carregandoDetalhe.set(true);
    this.erroDetalhe.set('');
    this.insumosService
      .obterParaEntrada(insumoId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoDetalhe.set(false)),
      )
      .subscribe({
        next: (detalhe) => {
          if (!permitirInativo && !detalhe.resumo.ativo) {
            this.detalhe.set(null);
            this.erroDetalhe.set('Insumos inativos não podem receber novas entradas.');
            return;
          }
          this.detalhe.set(detalhe);
          this.form.controls.unidadeOriginal.setValue(detalhe.resumo.unidadeBase);
          this.form.controls.formaCompraId.setValue('');
          this.form.controls.loteId.setValue('');
          this.atualizarQueryInsumo(insumoId);
        },
        error: (error: unknown) => this.tratarErroDetalhe(error),
      });
  }

  protected avancarBusca(pagina: number): void {
    if (pagina < 1 || pagina > this.totalPaginasInsumos() || pagina === this.paginaInsumos()) {
      return;
    }
    this.paginaInsumos.set(pagina);
    this.carregarInsumosAtivos();
  }

  protected selecionarOutraMateriaPrima(): void {
    if (this.chavePendente()) {
      return;
    }
    this.detalhe.set(null);
    this.movimento.set(null);
    this.resumo.set(null);
    this.etapa.set('formulario');
    this.erroEntrada.set('');
    this.erroSaldo.set('');
    this.erroDetalhe.set('');
    this.form.reset({
      tipo: 'Aquisicao',
      formaQuantidade: 'Direta',
      unidadeOriginal: 'Grama',
      formaCompraId: '',
      quantidadeOriginal: '',
      custoTotal: '',
      observacao: '',
      formaLote: 'Novo',
      loteId: '',
      identificacaoNovoLote: '',
      dataEntradaNovoLote: '',
      dataValidadeNovoLote: '',
      fornecedorNovoLote: '',
      novoLoteBloqueado: false,
    });
    this.busca.setValue('', { emitEvent: false });
    this.paginaInsumos.set(1);
    this.carregarInsumosAtivos();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { insumoId: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected voltarParaFormulario(): void {
    this.etapa.set('formulario');
    this.resumo.set(null);
    this.erroEntrada.set('');
  }

  protected solicitarRevisao(): void {
    this.form.markAllAsTouched();
    const validation = this.validarEntrada();
    if (validation) {
      this.erroValidacao.set(validation);
      return;
    }

    const operation = this.criarResumoEntrada();
    if (!operation) {
      this.erroValidacao.set('Revise os dados da entrada e tente novamente.');
      return;
    }
    this.resumo.set(operation.resumo);
    this.etapa.set('revisao');
    this.erroEntrada.set('');
  }

  protected confirmarEntrada(): void {
    if (this.etapa() !== 'revisao' || this.enviando() || !this.detalhe()) {
      return;
    }
    const operation = this.criarResumoEntrada();
    const actorId = this.responsavelId();
    if (!operation || !actorId) {
      this.etapa.set('formulario');
      this.erroValidacao.set('Revise os dados da entrada antes de confirmar.');
      return;
    }

    const idempotencyKey = this.criarChaveIdempotencia();
    if (!idempotencyKey) {
      this.erroEntrada.set(
        'Não foi possível gerar uma chave segura para a operação. Recarregue a página.',
      );
      return;
    }

    const pending: OperacaoPendente = {
      atorId: actorId,
      idempotencyKey,
      insumoId: this.detalhe()!.resumo.insumoId,
      resumo: operation.resumo,
      request: operation.request,
      state: 'enviando',
    };
    if (!this.salvarOperacaoPendente(pending)) {
      this.erroEntrada.set(
        'Não foi possível preservar a operação com segurança no navegador; a entrada não foi enviada.',
      );
      return;
    }

    this.resumo.set(pending.resumo);
    this.chave.set(pending.idempotencyKey);
    this.chavePendente.set(pending);
    this.enviarOperacaoPendente(pending);
  }

  protected repetirOperacaoPendente(): void {
    const pending = this.chavePendente();
    if (!pending || this.enviando() || pending.state === 'conflito') {
      return;
    }
    const retry = { ...pending, state: 'enviando' as const };
    if (!this.salvarOperacaoPendente(retry)) {
      this.erroEntrada.set('Não foi possível preservar a tentativa para reenvio.');
      return;
    }
    this.chavePendente.set(retry);
    this.enviarOperacaoPendente(retry);
  }

  protected escolherLote(lote: AdminPosicaoEstoquePrecisa): void {
    if (!lote.loteId || (this.form.controls.tipo.value === 'Aquisicao' && !lote.elegivel)) {
      return;
    }
    this.form.controls.loteId.setValue(lote.loteId);
  }

  protected unidadesDiretas(): UnidadeFisicaEstoque[] {
    const base = this.detalhe()?.resumo.unidadeBase;
    switch (base) {
      case 'Grama':
        return ['Grama', 'Quilograma'];
      case 'Mililitro':
        return ['Mililitro', 'Litro'];
      case 'Unidade':
        return ['Unidade'];
      default:
        return [];
    }
  }

  protected rotuloUnidade(unidade: UnidadeFisicaEstoque | UnidadeBaseEstoque): string {
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

  protected rotuloTipo(tipo: TipoEntradaInsumo): string {
    return tipo === 'EstoqueInicial' ? 'Estoque inicial' : 'Aquisição';
  }

  protected rotuloSituacaoLote(posicao: AdminPosicaoEstoquePrecisa): string {
    if (posicao.bloqueado) {
      return 'Bloqueado';
    }
    return posicao.elegivel ? 'Elegível' : 'Vencido ou inelegível';
  }

  protected formatarDataCivil(value: string | null): string {
    if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return 'Não informada';
    }
    const [year, month, day] = value.split('-');
    return `${day}/${month}/${year}`;
  }

  protected equivalenciaForma(forma: AdminFormaCompraPrecisa): string {
    const decimal = forma.quantidadeEquivalentePorEmbalagemDecimal;
    return decimal
      ? `${formatDecimalPtBr(decimal)} ${this.rotuloUnidade(forma.unidade)} por embalagem`
      : 'Equivalência indisponível';
  }

  protected quantidadeBasePreview(): string {
    const calculation = this.calcularQuantidadeBase();
    const unit = this.detalhe()?.resumo.unidadeBase;
    if (!calculation || !unit) {
      return '';
    }
    return `${formatDecimalPtBr(calculation)} ${this.rotuloUnidade(unit)}`;
  }

  protected custoFormatado(): string {
    const value = this.form.controls.custoTotal.value;
    return value.trim() ? formatBrl(value) : 'Desconhecido';
  }

  protected custoUnitarioPreview(): string | null {
    const cost = this.form.controls.custoTotal.value.trim();
    const quantity = this.calcularQuantidadeBase();
    return cost && quantity ? estimateCostPerBaseUnit(cost, quantity) : null;
  }

  protected exibirErroCampo(controlName: keyof EntradaControls): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && control.touched;
  }

  protected lotePendente(loteId: string): boolean {
    return this.form.controls.loteId.value === loteId;
  }

  protected saldoAtualizadoLabel(): string {
    const summary = this.selecionado();
    if (!summary) {
      return '';
    }
    return `${formatDecimalPtBr(summary.saldoFisico)} ${this.rotuloUnidade(summary.unidadeBase)}`;
  }

  protected formatarSaldo(value: string, unit: UnidadeBaseEstoque): string {
    return `${formatDecimalPtBr(value)} ${this.rotuloUnidade(unit)}`;
  }

  protected recarregarDetalhe(): void {
    const id = this.detalhe()?.resumo.insumoId ?? this.route.snapshot.queryParamMap.get('insumoId');
    if (id) {
      this.carregarDetalhe(id, !!this.chavePendente());
    }
  }

  protected recarregarSaldo(): void {
    const id = this.detalhe()?.resumo.insumoId;
    if (id) {
      this.atualizarSaldoDepoisDaEntrada(id);
    }
  }

  private carregarOperacaoPendente(actorId: string): void {
    let raw: string | null;
    try {
      raw = this.operacaoPendenteStore.ler(SESSION_STORAGE_KEY);
    } catch (error) {
      if (error instanceof DOMException) {
        this.erroRecuperacaoPendente.set(
          'O navegador bloqueou o armazenamento seguro da tentativa pendente. Não registre outra entrada nesta sessão.',
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
        this.erroRecuperacaoPendente.set(
          'Não foi possível recuperar uma tentativa de entrada pendente. Reconcilie o estoque antes de registrar outra.',
        );
        return;
      }
      throw error;
    }
    if (!this.ehOperacaoPendente(parsed)) {
      this.erroRecuperacaoPendente.set(
        'A tentativa pendente está inválida. Reconcilie o estoque antes de registrar outra entrada.',
      );
      return;
    }

    this.chavePendente.set(parsed);
    this.chave.set(parsed.idempotencyKey);
    this.resumo.set(parsed.resumo);
    if (parsed.atorId !== actorId) {
      this.etapa.set('conflito');
      this.erroEntrada.set(
        'Há uma entrada pendente iniciada por outra conta ADMIN. Entre com a conta original e reconcilie a operação antes de iniciar outra.',
      );
      return;
    }
    if (parsed.state === 'conflito') {
      this.etapa.set('conflito');
      this.erroEntrada.set(
        'A API informou conflito de idempotência. A chave foi preservada; reconcilie o histórico antes de iniciar outra entrada.',
      );
      return;
    }

    const incerta = { ...parsed, state: 'incerta' as const };
    this.chavePendente.set(incerta);
    if (!this.salvarOperacaoPendente(incerta)) {
      this.erroRecuperacaoPendente.set(
        'Não foi possível preservar o estado da tentativa pendente. Não registre outra entrada até reconciliar o estoque.',
      );
      this.etapa.set('conflito');
      return;
    }
    this.etapa.set('incerta');
    this.erroEntrada.set(
      'O resultado da tentativa anterior não foi confirmado. Reenvie os mesmos dados usando a mesma chave.',
    );
  }

  private ehOperacaoPendente(value: unknown): value is OperacaoPendente {
    if (typeof value !== 'object' || value === null) {
      return false;
    }
    const candidate = value as Partial<OperacaoPendente>;
    return (
      typeof candidate.atorId === 'string' &&
      typeof candidate.idempotencyKey === 'string' &&
      candidate.idempotencyKey.length > 0 &&
      typeof candidate.insumoId === 'string' &&
      (candidate.state === 'enviando' ||
        candidate.state === 'incerta' ||
        candidate.state === 'conflito') &&
      typeof candidate.resumo === 'object' &&
      candidate.resumo !== null &&
      typeof candidate.request === 'object' &&
      candidate.request !== null &&
      typeof candidate.request.quantidadeOriginal === 'string'
    );
  }

  private salvarOperacaoPendente(operation: OperacaoPendente): boolean {
    return this.operacaoPendenteStore.salvar(SESSION_STORAGE_KEY, operation);
  }

  private removerOperacaoPendente(): boolean {
    if (!this.operacaoPendenteStore.remover(SESSION_STORAGE_KEY)) {
      return false;
    }
    this.chavePendente.set(null);
    this.chave.set('');
    return true;
  }

  private definirResultados(pagina: AdminPagina<AdminResumoInsumoPreciso>): void {
    const totalPages = Math.max(1, Math.ceil(pagina.totalItens / PAGE_SIZE));
    if (this.paginaInsumos() > totalPages) {
      this.paginaInsumos.set(totalPages);
      this.carregarInsumosAtivos();
      return;
    }
    this.resultados.set(pagina.itens);
    this.totalInsumos.set(pagina.totalItens);
    this.paginaInsumos.set(pagina.pagina);
  }

  private atualizarQueryInsumo(insumoId: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { insumoId },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private validarEntrada(): string | null {
    const detalhe = this.detalhe();
    if (!detalhe) {
      return 'Selecione um insumo ativo.';
    }
    if (!detalhe.resumo.ativo) {
      return 'Insumos inativos não podem receber novas entradas.';
    }

    const original = parseDecimalText(this.form.controls.quantidadeOriginal.value);
    if (!original || original.coefficient <= 0n) {
      return 'Informe uma quantidade positiva válida.';
    }
    if (
      this.form.controls.formaQuantidade.value === 'FormaCompra' &&
      !isPositiveIntegerText(original.text)
    ) {
      return 'A quantidade de embalagens deve ser um número inteiro positivo.';
    }
    const baseQuantity = this.calcularQuantidadeBase();
    if (
      !baseQuantity ||
      !isValidBaseQuantity(baseQuantity, detalhe.resumo.unidadeBase === 'Unidade')
    ) {
      return 'A quantidade convertida excede a precisão, o limite ou a unidade inteira permitida pelo estoque.';
    }

    const cost = this.form.controls.custoTotal.value.trim();
    if (cost && !isValidCostTotal(cost)) {
      return 'O custo total deve ser não negativo, ter até duas casas decimais e respeitar o limite do backend.';
    }
    if (this.form.controls.observacao.value.length > 500) {
      return 'A observação deve ter no máximo 500 caracteres.';
    }

    if (detalhe.resumo.controlaLote) {
      if (this.form.controls.formaLote.value === 'Existente') {
        const lote = this.lotes().find((item) => item.loteId === this.form.controls.loteId.value);
        if (!lote) {
          return 'Selecione um lote pertencente a este insumo.';
        }
        if (this.form.controls.tipo.value === 'Aquisicao' && !lote.elegivel) {
          return 'Aquisições exigem lote elegível. Escolha um lote elegível ou registre estoque inicial quando permitido.';
        }
      } else {
        const identificacao = this.form.controls.identificacaoNovoLote.value.trim();
        if (!identificacao || identificacao.length > 100) {
          return 'Informe a identificação do novo lote, com até 100 caracteres.';
        }
        if (!isCivilDate(this.form.controls.dataEntradaNovoLote.value)) {
          return 'Informe a data de entrada do novo lote.';
        }
        const validity = this.form.controls.dataValidadeNovoLote.value;
        if (detalhe.resumo.validadeObrigatoria && !isCivilDate(validity)) {
          return 'Este insumo exige uma data de validade para cada novo lote.';
        }
        if (validity && !isCivilDate(validity)) {
          return 'Informe uma data de validade válida.';
        }
        if (this.form.controls.fornecedorNovoLote.value.trim().length > 200) {
          return 'O fornecedor deve ter no máximo 200 caracteres.';
        }
        if (
          this.form.controls.tipo.value === 'Aquisicao' &&
          this.form.controls.novoLoteBloqueado.value
        ) {
          return 'Uma aquisição não pode abrir um lote bloqueado.';
        }
      }
    }
    return null;
  }

  private calcularQuantidadeBase(): string | null {
    const detail = this.detalhe();
    const original = parseDecimalText(this.form.controls.quantidadeOriginal.value);
    if (!detail || !original || original.coefficient <= 0n) {
      return null;
    }

    if (this.form.controls.formaQuantidade.value === 'Direta') {
      const unit = this.form.controls.unidadeOriginal.value;
      if (!this.unidadesDiretas().includes(unit)) {
        return null;
      }
      const multiplier = unit === 'Quilograma' || unit === 'Litro' ? 3 : 0;
      return multiplyDecimalByPowerOfTen(original.text, multiplier);
    }

    const forma = this.formasCompraAtivas().find(
      (item) => item.id === this.form.controls.formaCompraId.value,
    );
    const factorText = forma?.quantidadeEquivalentePorEmbalagemDecimal ?? null;
    if (!forma || !factorText || !isPositiveIntegerText(original.text)) {
      return null;
    }
    const normalizedCount = removeDecimalZeroes(original);
    return multiplyDecimalByInteger(factorText, normalizedCount);
  }

  private criarResumoEntrada(): {
    resumo: ResumoEntrada;
    request: RegistrarEntradaInsumoRequest;
  } | null {
    const detail = this.detalhe();
    const baseQuantity = this.calcularQuantidadeBase();
    const original = parseDecimalText(this.form.controls.quantidadeOriginal.value);
    if (!detail || !baseQuantity || !original) {
      return null;
    }

    const formaQuantidade = this.form.controls.formaQuantidade.value;
    const selectedForm =
      formaQuantidade === 'FormaCompra'
        ? (this.formasCompraAtivas().find(
            (item) => item.id === this.form.controls.formaCompraId.value,
          ) ?? null)
        : null;
    const unit = formaQuantidade === 'Direta' ? this.form.controls.unidadeOriginal.value : null;
    const formaLote = this.form.controls.formaLote.value;
    const existingLot =
      detail.resumo.controlaLote && formaLote === 'Existente'
        ? (this.lotes().find((item) => item.loteId === this.form.controls.loteId.value) ?? null)
        : null;
    const isNewLot = detail.resumo.controlaLote && formaLote === 'Novo';
    const cost = this.form.controls.custoTotal.value.trim();
    const request: RegistrarEntradaInsumoRequest = {
      tipo: this.form.controls.tipo.value,
      quantidadeOriginal: original.text,
      unidadeOriginal: unit,
      formaCompraId: selectedForm?.id ?? null,
      loteId: existingLot?.loteId ?? null,
      identificacaoNovoLote: isNewLot
        ? this.form.controls.identificacaoNovoLote.value.trim()
        : null,
      dataEntradaNovoLote: isNewLot ? this.form.controls.dataEntradaNovoLote.value : null,
      dataValidadeNovoLote:
        isNewLot && this.form.controls.dataValidadeNovoLote.value
          ? this.form.controls.dataValidadeNovoLote.value
          : null,
      fornecedorNovoLote: isNewLot
        ? this.form.controls.fornecedorNovoLote.value.trim() || null
        : null,
      novoLoteBloqueado:
        isNewLot && this.form.controls.tipo.value === 'EstoqueInicial'
          ? this.form.controls.novoLoteBloqueado.value
          : false,
      custoTotal: cost ? (parseDecimalText(cost)?.text ?? null) : null,
      observacao: this.form.controls.observacao.value.trim() || null,
    };
    const lotLabel = existingLot
      ? `${existingLot.identificacaoLote ?? 'Lote'} (${this.rotuloSituacaoLote(existingLot)})`
      : isNewLot
        ? this.form.controls.identificacaoNovoLote.value.trim()
        : 'Não se aplica';
    const formaFactor = selectedForm?.quantidadeEquivalentePorEmbalagemDecimal ?? null;
    const originalLabel = selectedForm
      ? `${formatDecimalPtBr(original.text)} embalagem(ns)`
      : `${formatDecimalPtBr(original.text)} ${this.rotuloUnidade(unit!)}`;

    return {
      request,
      resumo: {
        insumoNome: detail.resumo.nome,
        tipo: request.tipo,
        tipoLabel: this.rotuloTipo(request.tipo),
        quantidadeOriginal: originalLabel,
        unidadeOriginalLabel: selectedForm ? selectedForm.nome : this.rotuloUnidade(unit!),
        quantidadeBase: formatDecimalPtBr(baseQuantity),
        unidadeBase: detail.resumo.unidadeBase,
        formaCompraNome:
          selectedForm && formaFactor
            ? `${selectedForm.nome} (${formatDecimalPtBr(formaFactor)} ${this.rotuloUnidade(selectedForm.unidade)}/embalagem)`
            : null,
        loteLabel: lotLabel,
        custoTotal: cost ? formatBrl(cost) : null,
        custoUnitarioEstimado: cost ? estimateCostPerBaseUnit(cost, baseQuantity) : null,
      },
    };
  }

  private enviarOperacaoPendente(operation: OperacaoPendente): void {
    this.enviando.set(true);
    this.etapa.set('enviando');
    this.erroEntrada.set('');
    this.insumosService
      .registrarEntrada(operation.insumoId, operation.request, operation.idempotencyKey)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.enviando.set(false)),
      )
      .subscribe({
        next: (movement) => {
          const cleared = this.removerOperacaoPendente();
          this.movimento.set(movement);
          this.resumo.set(operation.resumo);
          this.etapa.set('sucesso');
          this.erroEntrada.set(
            cleared
              ? ''
              : 'A entrada foi confirmada, mas o navegador não conseguiu limpar o marcador local. Reconcilie esta confirmação antes de registrar outra entrada.',
          );
          this.atualizarSaldoDepoisDaEntrada(operation.insumoId);
        },
        error: (error: unknown) => this.tratarErroEntrada(error, operation),
      });
  }

  private tratarErroEntrada(error: unknown, operation: OperacaoPendente): void {
    if (error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500)) {
      const uncertain = { ...operation, state: 'incerta' as const };
      this.chavePendente.set(uncertain);
      this.resumo.set(uncertain.resumo);
      const saved = this.salvarOperacaoPendente(uncertain);
      this.etapa.set(saved ? 'incerta' : 'conflito');
      if (!saved) {
        this.erroRecuperacaoPendente.set(
          'O resultado não foi confirmado e o navegador não conseguiu preservar a tentativa. Reconcilie o estoque antes de registrar outra entrada.',
        );
        this.erroEntrada.set(this.erroRecuperacaoPendente());
        return;
      }
      this.erroEntrada.set(
        'Não foi possível confirmar se a entrada foi registrada. Os dados e a chave foram preservados; tente novamente sem alterar a operação.',
      );
      return;
    }

    if (
      error instanceof HttpErrorResponse &&
      error.status === 409 &&
      this.ehConflitoIdempotencia(error)
    ) {
      const conflict = { ...operation, state: 'conflito' as const };
      this.salvarOperacaoPendente(conflict);
      this.chavePendente.set(conflict);
      this.resumo.set(conflict.resumo);
      this.etapa.set('conflito');
      this.erroEntrada.set(
        'A API recusou a chave porque ela está associada a outro conteúdo. A chave foi preservada; reconcilie o histórico antes de iniciar outra entrada.',
      );
      return;
    }

    if (!this.removerOperacaoPendente()) {
      const conflict = { ...operation, state: 'conflito' as const };
      this.chavePendente.set(conflict);
      this.resumo.set(conflict.resumo);
      this.etapa.set('conflito');
      this.erroEntrada.set(
        'A API rejeitou a operação, mas o navegador não conseguiu limpar a tentativa preservada. Reconcilie o estoque antes de registrar outra entrada.',
      );
      return;
    }
    this.etapa.set('formulario');
    this.resumo.set(null);
    this.erroEntrada.set(this.mensagemErroEntrada(error));
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
    }
  }

  private atualizarSaldoDepoisDaEntrada(insumoId: string): void {
    this.atualizandoSaldo.set(true);
    this.erroSaldo.set('');
    this.insumosService
      .obterParaEntrada(insumoId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.atualizandoSaldo.set(false)),
      )
      .subscribe({
        next: (detail) => this.detalhe.set(detail),
        error: () => {
          this.erroSaldo.set(
            'A entrada foi confirmada, mas não foi possível atualizar o saldo. Atualize a consulta.',
          );
        },
      });
  }

  private tratarErroDetalhe(error: unknown): void {
    if (this.redirecionarSessaoExpirada(error)) {
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      this.erroDetalhe.set('Você não tem permissão para consultar este insumo.');
    } else if (error instanceof HttpErrorResponse && error.status === 404) {
      this.erroDetalhe.set('Este insumo não foi encontrado. Selecione outro insumo ativo.');
    } else {
      this.erroDetalhe.set('Não foi possível carregar os dados do insumo. Tente novamente.');
    }
  }

  private mensagemErroBusca(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return 'Você não tem permissão para consultar os insumos.';
    }
    if (error instanceof HttpErrorResponse && error.status === 400) {
      return 'A busca retornou filtros inválidos. Revise e tente novamente.';
    }
    return 'Não foi possível carregar os insumos ativos. Tente novamente.';
  }

  private mensagemErroEntrada(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        return 'A API rejeitou os dados da entrada. Revise quantidade, unidade, custo e dados do lote.';
      }
      if (error.status === 403) {
        return 'A operação foi bloqueada por autorização ou antifalsificação. Os dados foram mantidos.';
      }
      if (error.status === 404) {
        return 'O insumo ou lote não foi encontrado. Atualize os dados antes de tentar novamente.';
      }
      if (error.status === 409) {
        const detail = this.detalheErroApi(error).toLowerCase();
        if (detail.includes('inelegível') || detail.includes('elegível')) {
          return 'O lote não está elegível para aquisição. Verifique a validade e o bloqueio ou selecione outro lote.';
        }
        if (detail.includes('estoque inicial')) {
          return 'O estoque inicial só pode abrir uma posição sem saldo ou histórico. Para uma posição existente, registre uma aquisição.';
        }
        if (detail.includes('inativo')) {
          return 'O insumo está inativo e não pode receber novas entradas.';
        }
        return 'A operação conflita com o estado atual do estoque. Atualize o insumo e revise os dados.';
      }
    }
    return 'Não foi possível registrar a entrada. Os dados digitados foram preservados.';
  }

  private ehConflitoIdempotencia(error: HttpErrorResponse): boolean {
    return this.detalheErroApi(error).toLowerCase().includes('idempotência');
  }

  private detalheErroApi(error: HttpErrorResponse): string {
    if (typeof error.error === 'string') {
      return error.error;
    }
    if (typeof error.error === 'object' && error.error !== null) {
      const detail = (error.error as { detail?: unknown }).detail;
      return typeof detail === 'string' ? detail : '';
    }
    return '';
  }

  private redirecionarSessaoExpirada(error: unknown): boolean {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return true;
    }
    return false;
  }

  private criarChaveIdempotencia(): string | null {
    return this.operacaoPendenteStore.criarChaveIdempotencia();
  }
}

function isCivilDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }
  const [year, month, day] = value.split('-').map(Number);
  if (!year || !month || !day || month > 12) {
    return false;
  }
  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const daysInMonth = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
  return day <= (daysInMonth ?? 0);
}

function removeDecimalZeroes(value: NonNullable<ReturnType<typeof parseDecimalText>>): string {
  let coefficient = value.coefficient;
  let scale = value.scale;
  while (scale > 0 && coefficient % 10n === 0n) {
    coefficient /= 10n;
    scale -= 1;
  }
  return coefficient.toString();
}
