import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  ViewChild,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  EMPTY,
  catchError,
  finalize,
  forkJoin,
  from,
  Observable,
  of,
  Subscription,
  switchMap,
  tap,
} from 'rxjs';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import { AdminProduto, ProdutosService } from '../../../core/admin-produtos/produtos.service';
import {
  LoteProdutoAcabado,
  MovimentoProdutoAcabado,
  PosicaoProdutoAcabado,
  ProdutosAcabadosService,
  SituacaoEstoqueProdutoAcabado,
  TipoMovimentoProdutoAcabado,
  CondicaoEstoqueProdutoAcabado,
  ContagemProdutoAcabadoRequest,
} from '../../../core/admin-estoque/produtos-acabados.service';
import { AdminWorkspaceComponent } from '../../admin-workspace.component';
import { OperacaoPendenteStore } from '../../../core/admin-estoque/operacao-pendente.store';

const PAGE_SIZE = 20;
type AcaoMovimentacao =
  'Entrada' | 'Saida' | 'Perda' | 'Descarte' | 'Transferencia' | 'Contagem' | 'Estorno';

interface OperacaoPendenteProdutoAcabado {
  versao: 1;
  atorId: string;
  varianteId: string;
  acao: string;
  fingerprint: string;
  idempotencyKey: string;
  estado: 'enviando' | 'incerta' | 'conflito';
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  selector: 'app-admin-produtos-acabados',
  styleUrl: './admin-produtos-acabados.component.css',
  templateUrl: './admin-produtos-acabados.component.html',
})
export class AdminProdutosAcabadosComponent {
  protected readonly produtos = signal<AdminProduto[]>([]);
  protected readonly varianteId = signal(
    inject(ActivatedRoute).snapshot.paramMap.get('varianteId') ?? '',
  );
  protected readonly posicao = signal<PosicaoProdutoAcabado | null>(null);
  protected readonly politica = signal<{
    produtoVarianteId: string;
    estoqueMinimo: number | null;
    prazoValidadePadraoDias: number | null;
  } | null>(null);
  protected readonly movimentos = signal<MovimentoProdutoAcabado[]>([]);
  protected readonly totalMovimentos = signal(0);
  protected readonly pagina = signal(1);
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.totalMovimentos() / PAGE_SIZE)),
  );
  protected readonly carregandoProdutos = signal(false);
  protected readonly carregando = signal(false);
  protected readonly erroProdutos = signal('');
  protected readonly erroPosicao = signal('');
  protected readonly erroPolitica = signal('');
  private consultaVariante?: Subscription;
  protected readonly carregandoHistorico = signal(false);
  protected readonly salvandoPolitica = signal(false);
  protected readonly erro = signal('');
  protected readonly erroHistorico = signal('');
  protected readonly mensagemPolitica = signal('');
  protected readonly estoqueMinimo = new FormControl<number | null>(null, [
    Validators.min(0),
    Validators.pattern(/^\d+$/),
  ]);
  protected readonly prazoValidade = new FormControl<number | null>(null, [
    Validators.min(0),
    Validators.pattern(/^\d+$/),
  ]);
  protected readonly tipoMovimento = signal<TipoMovimentoProdutoAcabado | ''>('');
  protected readonly loteMovimento = signal('');
  protected readonly acaoMovimentacao = signal<AcaoMovimentacao | null>(null);
  protected readonly enviandoMovimentacao = signal(false);
  protected readonly sucessoMovimentacao = signal('');
  protected readonly erroMovimentacao = signal('');
  protected readonly entradaForm = new FormGroup({
    tipo: new FormControl<'EstoqueInicial' | 'EntradaManual'>('EntradaManual', {
      nonNullable: true,
    }),
    loteId: new FormControl('', { nonNullable: true }),
    novoLote: new FormControl(true, { nonNullable: true }),
    quantidade: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.pattern(/^\d+$/),
    ]),
    condicaoDestino: new FormControl<CondicaoEstoqueProdutoAcabado>('Vendavel', {
      nonNullable: true,
    }),
    dataFabricacao: new FormControl('', { nonNullable: true }),
    dataValidade: new FormControl('', { nonNullable: true }),
    motivo: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    observacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });
  protected readonly operacaoForm = new FormGroup({
    tipo: new FormControl<'SaidaManual' | 'Perda' | 'Descarte'>('SaidaManual', {
      nonNullable: true,
    }),
    loteId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    quantidade: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.pattern(/^\d+$/),
    ]),
    condicaoOrigem: new FormControl<CondicaoEstoqueProdutoAcabado>('Vendavel', {
      nonNullable: true,
    }),
    motivo: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    observacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });
  protected readonly transferenciaForm = new FormGroup({
    loteId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    quantidade: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.pattern(/^\d+$/),
    ]),
    condicaoOrigem: new FormControl<CondicaoEstoqueProdutoAcabado>('Vendavel', {
      nonNullable: true,
    }),
    condicaoDestino: new FormControl<CondicaoEstoqueProdutoAcabado>('Quarentena', {
      nonNullable: true,
    }),
    motivo: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
    observacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });
  protected readonly contagemForm = new FormGroup({
    loteId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    vendavel: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(2147483647),
      Validators.pattern(/^\d+$/),
    ]),
    danificada: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(2147483647),
      Validators.pattern(/^\d+$/),
    ]),
    quarentena: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(2147483647),
      Validators.pattern(/^\d+$/),
    ]),
    motivo: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(120)],
    }),
  });
  protected readonly estornoForm = new FormGroup({
    motivo: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(120)],
    }),
  });
  protected readonly movimentoEstorno = signal<MovimentoProdutoAcabado | null>(null);
  protected readonly mostrandoResumoContagem = signal(false);
  protected readonly contagemVersaoCapturada = signal<string | null>(null);
  protected readonly contagemLoteCapturado = signal<string | null>(null);
  protected readonly contagemLoteSnapshot = signal<LoteProdutoAcabado | null>(null);
  protected readonly contagemConcluidaLoteId = signal<string | null>(null);
  protected readonly conflitoContagemVersao = signal(false);
  protected readonly atorId = signal('');

  protected readonly tiposMovimento: ReadonlyArray<{
    value: TipoMovimentoProdutoAcabado;
    label: string;
  }> = [
    { value: 'EstoqueInicial', label: 'Estoque inicial' },
    { value: 'EntradaManual', label: 'Entrada manual' },
    { value: 'SaidaManual', label: 'Saída manual' },
    { value: 'Perda', label: 'Perda' },
    { value: 'Descarte', label: 'Descarte' },
    { value: 'AjusteInventario', label: 'Ajuste de inventário' },
    { value: 'TransferenciaCondicao', label: 'Mudança de condição' },
    { value: 'Estorno', label: 'Correção de movimentação' },
  ];

  protected readonly produtoSelecionado = computed(() => {
    const selectedVariant = this.varianteId();
    return (
      this.produtos().find((produto) =>
        produto.variantes.some((variante) => variante.id === selectedVariant),
      ) ?? null
    );
  });
  protected readonly varianteSelecionada = computed(
    () =>
      this.produtoSelecionado()?.variantes.find((variante) => variante.id === this.varianteId()) ??
      null,
  );
  protected readonly totalVendavel = computed(() =>
    (this.posicao()?.lotes ?? []).reduce((sum, lote) => sum + lote.quantidadeVendavel, 0),
  );
  protected readonly totalDanificada = computed(() =>
    (this.posicao()?.lotes ?? []).reduce((sum, lote) => sum + lote.quantidadeDanificada, 0),
  );
  protected readonly totalQuarentena = computed(() =>
    (this.posicao()?.lotes ?? []).reduce((sum, lote) => sum + lote.quantidadeQuarentena, 0),
  );

  private readonly produtosService = inject(ProdutosService);
  private readonly produtosAcabadosService = inject(ProdutosAcabadosService);
  private readonly adminAuthService = inject(AdminAuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly operacaoPendenteStore = inject(OperacaoPendenteStore);
  private focoInicialContagemPendente = false;
  private campoVendavelContagem?: ElementRef<HTMLInputElement>;

  @ViewChild('policyMinimumInput')
  private campoEstoqueMinimo?: ElementRef<HTMLInputElement>;

  @ViewChild('policyExpiryInput')
  private campoPrazoValidade?: ElementRef<HTMLInputElement>;

  @ViewChild('countVendavelInput')
  private set countVendavelInput(element: ElementRef<HTMLInputElement> | undefined) {
    this.campoVendavelContagem = element;
    if (element && this.focoInicialContagemPendente) {
      this.focoInicialContagemPendente = false;
      element.nativeElement.focus({ preventScroll: true });
    }
  }

  @ViewChild('countSummary')
  private set countSummary(element: ElementRef<HTMLElement> | undefined) {
    element?.nativeElement.focus();
  }

  @ViewChild('countErrorAlert')
  private set countErrorAlert(element: ElementRef<HTMLElement> | undefined) {
    element?.nativeElement.focus({ preventScroll: true });
  }

  @ViewChild('countDanificadaInput')
  private campoDanificadaContagem?: ElementRef<HTMLInputElement>;

  @ViewChild('countQuarentenaInput')
  private campoQuarentenaContagem?: ElementRef<HTMLInputElement>;

  @ViewChild('countReasonInput')
  private campoMotivoContagem?: ElementRef<HTMLInputElement>;

  constructor() {
    this.carregarProdutos();
    this.adminAuthService
      .getCurrentSession()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => {
          if (!session) {
            void this.router.navigateByUrl('/admin/login');
            return;
          }
          this.atorId.set(session.userId);
        },
        error: (error: unknown) => {
          this.erroMovimentacao.set(
            this.mensagemErro(error, 'Não foi possível confirmar a sessão administrativa.'),
          );
        },
      });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const selected = params.get('varianteId') ?? '';
      if (selected === this.varianteId()) {
        return;
      }
      this.consultaVariante?.unsubscribe();
      this.erro.set('');
      this.erroPosicao.set('');
      this.erroPolitica.set('');
      this.varianteId.set(selected);
      this.limparContagem();
      this.contagemConcluidaLoteId.set(null);
      this.posicao.set(null);
      this.politica.set(null);
      this.movimentos.set([]);
      if (!this.carregandoProdutos() && !this.erroProdutos()) {
        this.consultarVarianteDoCatalogo();
      }
    });
  }

  protected carregarProdutos(): void {
    if (this.carregandoProdutos()) {
      return;
    }
    this.carregandoProdutos.set(true);
    this.erroProdutos.set('');
    this.produtosService
      .listar()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoProdutos.set(false)),
      )
      .subscribe({
        next: (produtos) => {
          this.produtos.set(produtos);
          this.consultarVarianteDoCatalogo();
        },
        error: () => {
          this.erroProdutos.set('Não foi possível carregar os produtos.');
        },
      });
  }

  private consultarVarianteDoCatalogo(): void {
    if (!this.varianteId()) {
      return;
    }
    if (this.varianteSelecionada()) {
      this.carregarVariante();
    } else {
      this.erro.set('A variante selecionada não foi encontrada.');
    }
  }

  protected selecionarVariante(varianteId: string): void {
    void this.router.navigate(['/admin/estoque/produtos-acabados', varianteId]);
  }

  private recarregarVarianteAposOperacao(): void {
    this.consultaVariante?.unsubscribe();
    this.carregarVariante();
  }

  protected carregarVariante(consulta: 'ambas' | 'posicao' | 'politica' = 'ambas'): void {
    const variant = this.varianteId();
    if (!variant || this.carregando()) {
      return;
    }

    this.carregando.set(true);
    this.erro.set('');
    this.mensagemPolitica.set('');
    const consultarPosicao = consulta !== 'politica';
    const consultarPolitica = consulta !== 'posicao';
    if (consultarPosicao) {
      this.erroPosicao.set('');
    }
    if (consultarPolitica) {
      this.erroPolitica.set('');
    }

    this.consultaVariante = forkJoin({
      posicao: consultarPosicao
        ? this.produtosAcabadosService.consultarPosicao(variant).pipe(
            catchError(() => {
              this.erroPosicao.set('Não foi possível consultar o estoque desta variante.');
              return of(null);
            }),
          )
        : of(null),
      politica: consultarPolitica
        ? this.produtosAcabadosService.consultarPolitica(variant).pipe(
            catchError(() => {
              this.erroPolitica.set('Não foi possível consultar a política de estoque.');
              return of(null);
            }),
          )
        : of(null),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe(({ posicao, politica }) => {
        if (politica) {
          this.politica.set(politica);
          this.estoqueMinimo.setValue(politica.estoqueMinimo);
          this.prazoValidade.setValue(politica.prazoValidadePadraoDias);
        }
        if (posicao) {
          this.posicao.set(posicao);
          this.pagina.set(1);
          this.carregarHistorico();
        }
      });
  }

  protected abrirMovimentacao(acao: AcaoMovimentacao): void {
    if (this.enviandoMovimentacao()) {
      return;
    }
    if (this.acaoMovimentacao() === 'Contagem') {
      this.erroMovimentacao.set(
        'Cancele ou conclua a contagem física antes de iniciar outra movimentação.',
      );
      return;
    }
    this.acaoMovimentacao.set(acao);
    this.sucessoMovimentacao.set('');
    this.erroMovimentacao.set('');
    this.contagemConcluidaLoteId.set(null);
    if (acao === 'Saida') {
      this.operacaoForm.controls.tipo.setValue('SaidaManual');
    } else if (acao === 'Perda') {
      this.operacaoForm.controls.tipo.setValue('Perda');
    } else if (acao === 'Descarte') {
      this.operacaoForm.controls.tipo.setValue('Descarte');
    }
  }

  protected fecharMovimentacao(): void {
    if (!this.enviandoMovimentacao()) {
      this.acaoMovimentacao.set(null);
      this.erroMovimentacao.set('');
    }
  }

  protected selecionarTipoOperacao(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }
    const tipo = select.value;
    if (tipo === 'SaidaManual' || tipo === 'Perda' || tipo === 'Descarte') {
      this.operacaoForm.controls.tipo.setValue(tipo);
    }
  }

  protected registrarEntrada(): void {
    const varianteId = this.varianteId();
    if (!varianteId || this.entradaForm.invalid || this.enviandoMovimentacao()) {
      this.entradaForm.markAllAsTouched();
      return;
    }

    const value = this.entradaForm.getRawValue();
    if (
      !this.validarDataCivil(value.dataFabricacao) ||
      (value.dataValidade && !this.validarDataCivil(value.dataValidade)) ||
      (value.dataValidade && value.dataFabricacao && value.dataValidade < value.dataFabricacao)
    ) {
      this.erroMovimentacao.set(
        'Informe datas civis válidas e mantenha a validade igual ou posterior à fabricação.',
      );
      return;
    }

    if (!value.novoLote && !value.loteId) {
      this.erroMovimentacao.set('Selecione um lote existente ou escolha criar um novo lote.');
      return;
    }
    if (value.novoLote && !value.dataFabricacao) {
      this.erroMovimentacao.set('A data de fabricação é obrigatória para criar um novo lote.');
      return;
    }

    const request = {
      tipo: value.tipo,
      quantidade: value.quantidade!,
      condicaoDestino: value.condicaoDestino,
      loteId: value.novoLote ? null : value.loteId,
      dataFabricacao: value.novoLote ? value.dataFabricacao : null,
      dataValidade: value.novoLote && value.dataValidade ? value.dataValidade : null,
      motivo: value.motivo.trim(),
      observacao: value.observacao.trim() || null,
    };
    this.executarMovimentacao('entrada', request, (idempotencyKey) =>
      this.produtosAcabadosService.registrarEntrada(varianteId, request, idempotencyKey),
    );
  }

  protected registrarOperacao(): void {
    const varianteId = this.varianteId();
    if (!varianteId || this.operacaoForm.invalid || this.enviandoMovimentacao()) {
      this.operacaoForm.markAllAsTouched();
      return;
    }

    const value = this.operacaoForm.getRawValue();
    const request = {
      tipo: value.tipo,
      loteId: value.loteId,
      quantidade: value.quantidade!,
      condicaoOrigem: value.condicaoOrigem,
      motivo: value.motivo.trim(),
      observacao: value.observacao.trim() || null,
    };
    this.executarMovimentacao('operacao', request, (idempotencyKey) =>
      this.produtosAcabadosService.registrarSaida(varianteId, request, idempotencyKey),
    );
  }

  protected registrarTransferencia(): void {
    const varianteId = this.varianteId();
    if (!varianteId || this.transferenciaForm.invalid || this.enviandoMovimentacao()) {
      this.transferenciaForm.markAllAsTouched();
      return;
    }

    const value = this.transferenciaForm.getRawValue();
    if (value.condicaoOrigem === value.condicaoDestino) {
      this.erroMovimentacao.set('Origem e destino devem ser condições diferentes.');
      return;
    }
    const permitidas =
      (value.condicaoOrigem === 'Vendavel' && value.condicaoDestino === 'Quarentena') ||
      (value.condicaoOrigem === 'Quarentena' && value.condicaoDestino === 'Vendavel') ||
      (value.condicaoOrigem === 'Vendavel' && value.condicaoDestino === 'Danificada') ||
      (value.condicaoOrigem === 'Danificada' && value.condicaoDestino === 'Vendavel');
    if (!permitidas) {
      this.erroMovimentacao.set(
        'Esta mudança de condição não é permitida. Confira as condições escolhidas.',
      );
      return;
    }

    const request = {
      loteId: value.loteId,
      quantidade: value.quantidade!,
      condicaoOrigem: value.condicaoOrigem,
      condicaoDestino: value.condicaoDestino,
      motivo: value.motivo.trim(),
      observacao: value.observacao.trim() || null,
    };
    this.executarMovimentacao('transferencia', request, (idempotencyKey) =>
      this.produtosAcabadosService.transferir(varianteId, request, idempotencyKey),
    );
  }

  protected abrirContagem(lote: LoteProdutoAcabado): void {
    if (this.enviandoMovimentacao()) {
      return;
    }
    const loteEmContagem = this.contagemLoteCapturado();
    if (this.acaoMovimentacao() === 'Contagem' && loteEmContagem === lote.loteId) {
      this.campoVendavelContagem?.nativeElement.focus({ preventScroll: true });
      return;
    }
    if (this.acaoMovimentacao() === 'Contagem' && loteEmContagem !== lote.loteId) {
      this.erroMovimentacao.set(
        'Cancele a conferência aberta antes de iniciar a contagem de outro lote.',
      );
      return;
    }
    if (this.acaoMovimentacao()) {
      this.erroMovimentacao.set('Feche a movimentação atual antes de iniciar uma contagem física.');
      return;
    }
    this.focoInicialContagemPendente = true;
    this.acaoMovimentacao.set('Contagem');
    this.mostrandoResumoContagem.set(false);
    this.erroMovimentacao.set('');
    this.sucessoMovimentacao.set('');
    this.conflitoContagemVersao.set(false);
    this.contagemConcluidaLoteId.set(null);
    this.contagemVersaoCapturada.set(lote.versaoSaldo);
    this.contagemLoteCapturado.set(lote.loteId);
    this.contagemLoteSnapshot.set(lote);
    this.contagemForm.reset({
      loteId: lote.loteId,
      vendavel: null,
      danificada: null,
      quarentena: null,
      motivo: '',
    });
  }

  protected revisarContagem(): void {
    this.erroMovimentacao.set('');
    if (this.contagemForm.invalid) {
      this.contagemForm.markAllAsTouched();
      this.focarPrimeiroCampoContagemInvalido();
      return;
    }
    const values = this.contagemForm.getRawValue();
    const total = (values.vendavel ?? 0) + (values.danificada ?? 0) + (values.quarentena ?? 0);
    if (!Number.isSafeInteger(total) || total > 2147483647) {
      this.erroMovimentacao.set('A soma das quantidades excede o limite suportado.');
      return;
    }
    this.erroMovimentacao.set('');
    this.mostrandoResumoContagem.set(true);
  }

  protected voltarAEditarContagem(): void {
    this.mostrandoResumoContagem.set(false);
    this.campoVendavelContagem?.nativeElement.focus({ preventScroll: true });
  }

  protected cancelarContagem(): void {
    if (this.enviandoMovimentacao()) {
      return;
    }
    this.limparContagem();
  }

  protected erroContagemVisivel(
    campo: 'vendavel' | 'danificada' | 'quarentena' | 'motivo',
  ): boolean {
    const control = this.contagemForm.controls[campo];
    return control.touched && control.invalid;
  }

  protected mensagemErroContagem(
    campo: 'vendavel' | 'danificada' | 'quarentena' | 'motivo',
  ): string {
    const control = this.contagemForm.controls[campo];
    if (control.hasError('required')) {
      return campo === 'motivo'
        ? 'Informe o motivo da contagem.'
        : 'Informe a quantidade encontrada. Digite 0 se não houver unidades.';
    }
    if (control.hasError('max')) {
      return 'A quantidade não pode exceder 2.147.483.647 unidades.';
    }
    if (control.hasError('min') || control.hasError('pattern')) {
      return 'Informe um número inteiro não negativo.';
    }
    if (control.hasError('maxlength')) {
      return 'O motivo deve ter no máximo 120 caracteres.';
    }
    return 'Revise este campo.';
  }

  protected confirmarContagem(): void {
    const varianteId = this.varianteId();
    if (
      !varianteId ||
      !this.mostrandoResumoContagem() ||
      this.contagemForm.invalid ||
      this.enviandoMovimentacao()
    ) {
      return;
    }
    const value = this.contagemForm.getRawValue();
    const versaoCapturada = this.contagemVersaoCapturada();
    const loteCapturado = this.contagemLoteCapturado();
    if (!versaoCapturada || !loteCapturado || loteCapturado !== value.loteId) {
      this.erroMovimentacao.set('A posição não possui uma versão válida para conferência.');
      return;
    }
    const request: ContagemProdutoAcabadoRequest = {
      loteId: value.loteId,
      quantidadeVendavel: value.vendavel!,
      quantidadeDanificada: value.danificada!,
      quantidadeQuarentena: value.quarentena!,
      motivo: value.motivo.trim(),
      versaoSaldoEsperada: versaoCapturada,
    };
    this.executarMovimentacao('contagem', request, (idempotencyKey) =>
      this.produtosAcabadosService.registrarContagem(varianteId, request, idempotencyKey),
    );
  }

  protected abrirEstorno(movimento: MovimentoProdutoAcabado): void {
    if (
      movimento.tipo === 'Estorno' ||
      this.movimentoJaEstornado(movimento) ||
      this.enviandoMovimentacao()
    ) {
      return;
    }
    this.acaoMovimentacao.set('Estorno');
    this.movimentoEstorno.set(movimento);
    this.estornoForm.reset({ motivo: '' });
    this.erroMovimentacao.set('');
    this.sucessoMovimentacao.set('');
  }

  protected confirmarEstorno(): void {
    const varianteId = this.varianteId();
    const movimento = this.movimentoEstorno();
    if (!varianteId || !movimento || this.estornoForm.invalid || this.enviandoMovimentacao()) {
      this.estornoForm.markAllAsTouched();
      return;
    }
    const request = {
      motivo: this.estornoForm.controls.motivo.value.trim(),
      observacao: null,
    };
    this.executarMovimentacao('estorno', { movimentoId: movimento.id, ...request }, (key) =>
      this.produtosAcabadosService.estornarMovimento(varianteId, movimento.id, request, key),
    );
  }

  protected loteDaContagem(): LoteProdutoAcabado | null {
    const lote = this.contagemLoteSnapshot();
    return lote?.loteId === this.contagemLoteCapturado() ? lote : null;
  }

  protected loteEmContagem(loteId: string): boolean {
    return this.acaoMovimentacao() === 'Contagem' && this.contagemLoteCapturado() === loteId;
  }

  protected diferencaCondicao(atual: number, encontrada: number | null): number {
    return (encontrada ?? atual) - atual;
  }

  protected movimentoJaEstornado(movimento: MovimentoProdutoAcabado): boolean {
    return this.movimentos().some(
      (item) => item.tipo === 'Estorno' && item.movimentoOriginalId === movimento.id,
    );
  }

  protected formatarDiferenca(valor: number): string {
    return valor === 0 ? '0 un.' : `${valor > 0 ? '+' : ''}${valor} un.`;
  }

  protected recarregarContagemParaNovoSnapshot(): void {
    const varianteId = this.varianteId();
    const loteId = this.contagemLoteCapturado();
    if (!varianteId || !loteId) {
      return;
    }

    this.erroMovimentacao.set('');
    this.produtosAcabadosService
      .consultarLote(varianteId, loteId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lote) => {
          if (!lote.versaoSaldo || lote.loteId !== loteId) {
            this.erroMovimentacao.set(
              'Não foi possível obter uma nova versão do lote. Os valores informados foram mantidos.',
            );
            return;
          }
          this.posicao.update((posicao) =>
            posicao
              ? {
                  ...posicao,
                  lotes: posicao.lotes.map((item) => (item.loteId === lote.loteId ? lote : item)),
                }
              : posicao,
          );
          this.contagemVersaoCapturada.set(lote.versaoSaldo);
          this.contagemLoteCapturado.set(lote.loteId);
          this.contagemLoteSnapshot.set(lote);
          this.mostrandoResumoContagem.set(false);
          this.conflitoContagemVersao.set(false);
          this.erroMovimentacao.set('');
          this.recarregarVarianteAposOperacao();
        },
        error: (error: unknown) => {
          this.erroMovimentacao.set(
            this.mensagemErro(error, 'Não foi possível recarregar o lote.'),
          );
        },
      });
  }

  protected saldoDaCondicao(loteId: string, condicao: CondicaoEstoqueProdutoAcabado): number {
    const lote = this.posicao()?.lotes.find((item) => item.loteId === loteId);
    if (!lote) {
      return 0;
    }
    switch (condicao) {
      case 'Vendavel':
        return lote.quantidadeVendavel;
      case 'Danificada':
        return lote.quantidadeDanificada;
      case 'Quarentena':
        return lote.quantidadeQuarentena;
    }
  }

  private limparContagem(): void {
    if (this.acaoMovimentacao() === 'Contagem') {
      this.acaoMovimentacao.set(null);
    }
    this.erroMovimentacao.set('');
    this.sucessoMovimentacao.set('');
    this.mostrandoResumoContagem.set(false);
    this.conflitoContagemVersao.set(false);
    this.contagemVersaoCapturada.set(null);
    this.contagemLoteCapturado.set(null);
    this.contagemLoteSnapshot.set(null);
    this.contagemForm.reset({
      loteId: '',
      vendavel: null,
      danificada: null,
      quarentena: null,
      motivo: '',
    });
  }

  private focarPrimeiroCampoContagemInvalido(): void {
    const campos = [
      [this.contagemForm.controls.vendavel, this.campoVendavelContagem],
      [this.contagemForm.controls.danificada, this.campoDanificadaContagem],
      [this.contagemForm.controls.quarentena, this.campoQuarentenaContagem],
      [this.contagemForm.controls.motivo, this.campoMotivoContagem],
    ] as const;
    campos.find(([control]) => control.invalid)?.[1]?.nativeElement.focus({ preventScroll: true });
  }

  private executarMovimentacao<T>(
    acao: string,
    payload: T,
    request: (idempotencyKey: string) => Observable<unknown>,
  ): void {
    if (this.enviandoMovimentacao()) {
      return;
    }
    this.enviandoMovimentacao.set(true);
    this.erroMovimentacao.set('');
    this.sucessoMovimentacao.set('');
    from(this.prepararOperacaoPendente(acao, payload))
      .pipe(
        switchMap((pending) => {
          if (!pending) {
            return EMPTY;
          }
          return request(pending.operacao.idempotencyKey).pipe(
            tap(() => {
              this.sucessoMovimentacao.set('Movimentação registrada com sucesso.');
              this.limparOperacaoConfirmada(pending.storageKey);
              if (acao === 'contagem') {
                this.contagemConcluidaLoteId.set(this.contagemLoteCapturado());
                this.limparContagem();
              }
              this.recarregarVarianteAposOperacao();
            }),
            catchError((error: unknown) => {
              this.registrarFalhaMovimentacao(error, acao, pending);
              return EMPTY;
            }),
          );
        }),
        catchError((error: unknown) => {
          this.erroMovimentacao.set(
            error instanceof DOMException
              ? 'O navegador bloqueou a operação segura. Confira o histórico antes de tentar novamente.'
              : 'Não foi possível concluir a operação com segurança. Confira o histórico antes de tentar novamente.',
          );
          return EMPTY;
        }),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.enviandoMovimentacao.set(false)),
      )
      .subscribe();
  }

  private async prepararOperacaoPendente(
    acao: string,
    payload: unknown,
  ): Promise<{ operacao: OperacaoPendenteProdutoAcabado; storageKey: string } | null> {
    const atorId = this.atorId();
    const varianteId = this.varianteId();
    if (!atorId || !varianteId) {
      this.erroMovimentacao.set('Confirme a sessão e a variante antes de registrar a operação.');
      return null;
    }

    const fingerprint = await this.operacaoPendenteStore.calcularFingerprint(payload);
    const storageKey = this.chaveArmazenamentoOperacao(atorId, varianteId, acao, fingerprint);
    let raw: string | null;
    try {
      raw = this.operacaoPendenteStore.ler(storageKey);
    } catch (error) {
      if (error instanceof DOMException) {
        this.erroMovimentacao.set(
          'O navegador bloqueou o armazenamento seguro da operação; ela não foi enviada.',
        );
        return null;
      }
      throw error;
    }

    if (raw !== null) {
      let parsed: unknown;
      try {
        parsed = JSON.parse(raw);
      } catch (error) {
        if (error instanceof SyntaxError) {
          this.erroMovimentacao.set(
            'Não foi possível ler a tentativa anterior salva no navegador. Confira o histórico antes de continuar.',
          );
          return null;
        }
        throw error;
      }
      if (
        !this.ehOperacaoPendenteProdutoAcabado(parsed) ||
        parsed.atorId !== atorId ||
        parsed.varianteId !== varianteId ||
        parsed.acao !== acao ||
        parsed.fingerprint !== fingerprint
      ) {
        this.erroMovimentacao.set(
          'A tentativa anterior não corresponde a esta operação. Confira o histórico antes de continuar; nada foi enviado.',
        );
        return null;
      }
      const operacao = { ...parsed, estado: 'enviando' as const };
      if (!this.operacaoPendenteStore.salvar(storageKey, operacao)) {
        this.erroMovimentacao.set(
          'Não foi possível preservar a tentativa no navegador; ela não foi enviada.',
        );
        return null;
      }
      return { operacao, storageKey };
    }

    const key = this.operacaoPendenteStore.criarChaveIdempotencia();
    if (!key) {
      this.erroMovimentacao.set(
        'Não foi possível preparar a operação com segurança; ela não foi enviada.',
      );
      return null;
    }
    const operacao: OperacaoPendenteProdutoAcabado = {
      versao: 1,
      atorId,
      varianteId,
      acao,
      fingerprint,
      idempotencyKey: key,
      estado: 'enviando',
    };
    if (!this.operacaoPendenteStore.salvar(storageKey, operacao)) {
      this.erroMovimentacao.set(
        'Não foi possível preservar a operação com segurança; ela não foi enviada.',
      );
      return null;
    }
    return { operacao, storageKey };
  }

  private chaveArmazenamentoOperacao(
    atorId: string,
    varianteId: string,
    acao: string,
    fingerprint: string,
  ): string {
    return [
      'xaquiamuni:estoque:produtos-acabados:pendente',
      encodeURIComponent(atorId),
      encodeURIComponent(varianteId),
      encodeURIComponent(acao),
      fingerprint,
    ].join(':');
  }

  private ehOperacaoPendenteProdutoAcabado(
    value: unknown,
  ): value is OperacaoPendenteProdutoAcabado {
    if (typeof value !== 'object' || value === null) {
      return false;
    }
    const candidate = value as Partial<OperacaoPendenteProdutoAcabado>;
    return (
      candidate.versao === 1 &&
      typeof candidate.atorId === 'string' &&
      typeof candidate.varianteId === 'string' &&
      typeof candidate.acao === 'string' &&
      typeof candidate.fingerprint === 'string' &&
      /^[a-f0-9]{64}$/.test(candidate.fingerprint) &&
      typeof candidate.idempotencyKey === 'string' &&
      candidate.idempotencyKey.length > 0 &&
      candidate.idempotencyKey.length <= 128 &&
      (candidate.estado === 'enviando' ||
        candidate.estado === 'incerta' ||
        candidate.estado === 'conflito')
    );
  }

  private registrarFalhaMovimentacao(
    error: unknown,
    acao: string,
    pending: { operacao: OperacaoPendenteProdutoAcabado; storageKey: string },
  ): void {
    if (error instanceof HttpErrorResponse && error.status === 409) {
      this.operacaoPendenteStore.salvar(pending.storageKey, {
        ...pending.operacao,
        estado: 'conflito',
      });
      this.erroMovimentacao.set(this.mensagemErroMovimentacao(error, acao));
      return;
    }

    if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
      this.operacaoPendenteStore.remover(pending.storageKey);
      this.erroMovimentacao.set(this.mensagemErroMovimentacao(error, acao));
      return;
    }

    if (!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500) {
      const incerta = { ...pending.operacao, estado: 'incerta' as const };
      const armazenada = this.operacaoPendenteStore.salvar(pending.storageKey, incerta);
      this.erroMovimentacao.set(
        armazenada
          ? 'Não foi possível confirmar o resultado. Confira o histórico antes de decidir; se tentar novamente, use exatamente os mesmos dados.'
          : 'Não foi possível confirmar o resultado nem salvar a tentativa no navegador. Confira a posição e o histórico antes de iniciar outra operação.',
      );
      return;
    }
    this.erroMovimentacao.set(this.mensagemErroMovimentacao(error, acao));
  }

  private limparOperacaoConfirmada(storageKey: string): void {
    if (!this.operacaoPendenteStore.remover(storageKey)) {
      this.sucessoMovimentacao.set(
        'Movimentação registrada. Não foi possível limpar a tentativa salva no navegador; confira o histórico antes de repetir qualquer operação.',
      );
    }
  }

  private validarDataCivil(value: string): boolean {
    if (!value) {
      return true;
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return false;
    }
    const date = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === value;
  }

  private mensagemErroMovimentacao(error: unknown, acao: string): string {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 0:
          return 'Falha de conexão. A tentativa foi preservada; reenvie exatamente os mesmos dados.';
        case 400:
          return 'Quantidade ou dados inválidos. Confira os campos e tente novamente.';
        case 401:
          void this.router.navigateByUrl('/admin/login');
          return 'Sua sessão expirou.';
        case 403:
          return 'Você não tem permissão para executar esta movimentação.';
        case 404:
          return 'Variante ou lote não encontrado.';
        case 409: {
          const detail = typeof error.error?.detail === 'string' ? error.error.detail : '';
          if (/idempot|chave/i.test(detail)) {
            return 'Esta tentativa está associada a dados diferentes. Não tente novamente; confira o histórico antes de iniciar outra operação.';
          }
          if (acao === 'contagem') {
            this.conflitoContagemVersao.set(true);
            return 'O estoque mudou durante a conferência. Atualize os dados antes de conferir novamente; os valores digitados serão mantidos.';
          }
          if (/já possui estorno|já foi estornado/i.test(detail)) {
            return 'Esta movimentação já foi corrigida.';
          }
          if (/movimentos posteriores/i.test(detail)) {
            return 'Há movimentações mais recentes; confira o histórico antes de tentar corrigir esta movimentação.';
          }
          if (/saldo atual não permite|saldo insuficiente/i.test(detail)) {
            return 'O saldo atual não permite esta correção. Atualize a posição e confira o histórico.';
          }
          return 'O estoque foi alterado durante a operação. Atualize a posição e confira o histórico antes de tentar novamente.';
        }
      }
    }
    return 'Não foi possível registrar a movimentação.';
  }

  protected salvarPolitica(): void {
    const variant = this.varianteId();
    if (!variant || !this.politica() || this.erroPolitica() || this.carregando()) {
      return;
    }
    if (this.estoqueMinimo.invalid || this.prazoValidade.invalid) {
      this.estoqueMinimo.markAsTouched();
      this.prazoValidade.markAsTouched();
      const campo = this.estoqueMinimo.invalid ? this.campoEstoqueMinimo : this.campoPrazoValidade;
      afterNextRender(() => campo?.nativeElement.focus(), { injector: this.injector });
      return;
    }

    this.salvandoPolitica.set(true);
    this.mensagemPolitica.set('');
    this.erro.set('');
    this.produtosAcabadosService
      .configurarPolitica(variant, {
        estoqueMinimo: this.estoqueMinimo.value,
        prazoValidadePadraoDias: this.prazoValidade.value,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvandoPolitica.set(false)),
      )
      .subscribe({
        next: (politica) => {
          this.politica.set(politica);
          this.estoqueMinimo.setValue(politica.estoqueMinimo);
          this.prazoValidade.setValue(politica.prazoValidadePadraoDias);
          this.mensagemPolitica.set('Política de estoque salva.');
        },
        error: (error: unknown) => {
          this.erro.set(this.mensagemErro(error, 'Não foi possível salvar a política.'));
        },
      });
  }

  protected carregarHistorico(): void {
    const variant = this.varianteId();
    if (!variant) {
      return;
    }

    this.carregandoHistorico.set(true);
    this.erroHistorico.set('');
    this.produtosAcabadosService
      .listarMovimentacoes(variant, {
        tipo: this.tipoMovimento() || undefined,
        loteId: this.loteMovimento() || undefined,
        pagina: this.pagina(),
        itensPorPagina: PAGE_SIZE,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoHistorico.set(false)),
      )
      .subscribe({
        next: (page) => {
          const totalPages = Math.max(1, Math.ceil(page.totalItens / PAGE_SIZE));
          if (this.pagina() > totalPages) {
            this.pagina.set(totalPages);
            this.carregarHistorico();
            return;
          }
          this.movimentos.set(page.itens);
          this.totalMovimentos.set(page.totalItens);
          this.pagina.set(page.pagina);
        },
        error: (error: unknown) => {
          this.erroHistorico.set(
            this.mensagemErro(error, 'Não foi possível carregar o histórico.'),
          );
        },
      });
  }

  protected definirTipo(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }
    const value = this.tiposMovimento.find((item) => item.value === select.value)?.value ?? '';
    this.tipoMovimento.set(value);
    this.pagina.set(1);
    this.carregarHistorico();
  }

  protected definirLote(event: Event): void {
    const select = event.currentTarget;
    if (!(select instanceof HTMLSelectElement)) {
      return;
    }
    const loteId = this.posicao()?.lotes.some((lote) => lote.loteId === select.value)
      ? select.value
      : '';
    this.loteMovimento.set(loteId);
    this.pagina.set(1);
    this.carregarHistorico();
  }

  protected irParaPagina(page: number): void {
    if (page < 1 || page > this.totalPaginas() || page === this.pagina()) {
      return;
    }
    this.pagina.set(page);
    this.carregarHistorico();
  }

  protected rotuloTipo(tipo: TipoMovimentoProdutoAcabado): string {
    return this.tiposMovimento.find((item) => item.value === tipo)?.label ?? 'Movimento';
  }

  protected rotuloCondicao(condicao: 'Vendavel' | 'Danificada' | 'Quarentena'): string {
    switch (condicao) {
      case 'Vendavel':
        return 'Vendável';
      case 'Danificada':
        return 'Danificada';
      case 'Quarentena':
        return 'Quarentena';
    }
  }

  protected rotuloSituacao(situacao: SituacaoEstoqueProdutoAcabado): string {
    switch (situacao) {
      case 'SemEstoque':
        return 'Sem saldo disponível para venda';
      case 'AbaixoOuNoMinimo':
        return 'Abaixo ou no mínimo';
      case 'Normal':
        return 'Normal';
      case 'MinimoNaoConfigurado':
        return 'Mínimo não configurado';
    }
  }

  protected rotuloValidade(lote: LoteProdutoAcabado): string {
    if (!lote.valido) {
      return 'Vencido';
    }
    return 'Válido';
  }

  protected formatarDataCivil(value: string): string {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      return 'Não informada';
    }
    const [year, month, day] = value.split('-');
    return `${day}/${month}/${year}`;
  }

  protected formatarInstanteUtc(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return 'Data indisponível';
    }
    return new Intl.DateTimeFormat('pt-BR', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      timeStyle: 'short',
    }).format(date);
  }

  private mensagemErro(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 400:
          return 'Os dados informados são inválidos.';
        case 401:
          void this.router.navigateByUrl('/admin/login');
          return 'Sua sessão expirou.';
        case 403:
          return 'Você não tem permissão para acessar o estoque de produtos acabados.';
        case 404:
          return 'O recurso solicitado não foi encontrado.';
        case 409:
          return 'A operação conflitou com o estado atual do estoque.';
      }
    }
    return fallback;
  }
}
