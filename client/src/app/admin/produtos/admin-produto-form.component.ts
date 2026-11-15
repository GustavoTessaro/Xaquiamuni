import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { AdminCategoria } from '../../core/admin-categorias/categorias.service';
import {
  AdminProduto,
  AdminProdutoCadastroRequest,
  AdminProdutoCaracteristicaRequest,
  AdminProdutoEdicaoRequest,
  AdminProdutoVariante,
  AdminProdutoVarianteCaracteristicaRequest,
  AdminProdutoVarianteRequest,
} from '../../core/admin-produtos/produtos.service';
import { BrlCurrencyInputComponent } from './brl-currency-input.component';
import {
  currencyPrecision,
  MAX_PRECO_VARIANTE,
  nonWhitespace,
  UNIDADES_VENDA,
  validSaleUnit,
} from './produto-form.validators';

type ProdutoControls = {
  nome: FormControl<string>;
  descricao: FormControl<string>;
  categoriaId: FormControl<string>;
};

type VarianteControls = {
  identificacao: FormControl<string>;
  unidadeVenda: FormControl<string>;
  precoVenda: FormControl<number | null>;
  peso: FormControl<string>;
  pesoUnidade: FormControl<'G' | 'KG'>;
  volume: FormControl<string>;
  volumeUnidade: FormControl<'ML' | 'L'>;
};

type TipoMedida = 'PesoLiquido' | 'VolumeLiquido' | null;
type UnidadeMedida = 'G' | 'KG' | 'ML' | 'L';

interface ValorDraft {
  id?: string;
  referencia: string;
  nome: FormControl<string>;
  medida: FormControl<string>;
  unidade: FormControl<UnidadeMedida>;
  removido: boolean;
}

interface CaracteristicaDraft {
  id?: string;
  referencia: string;
  nome: FormControl<string>;
  tipoMedidaFisica: TipoMedida;
  valores: ValorDraft[];
  removida: boolean;
}

interface VarianteDraft {
  id?: string;
  identificacao: FormControl<string>;
  identificacaoManual: boolean;
  unidadeVenda: FormControl<string>;
  precoVenda: FormControl<number | null>;
  ativa: FormControl<boolean>;
  padrao: boolean;
  peso: FormControl<string>;
  pesoUnidade: FormControl<'G' | 'KG'>;
  volume: FormControl<string>;
  volumeUnidade: FormControl<'ML' | 'L'>;
  selecoes: Record<string, FormControl<string>>;
}

const UNIDADES: ReadonlyArray<{ codigo: string; nome: string }> = [
  { codigo: 'UN', nome: 'Unidade' },
  { codigo: 'KIT', nome: 'Kit' },
  { codigo: 'PAR', nome: 'Par' },
  { codigo: 'PCT', nome: 'Pacote' },
  { codigo: 'CX', nome: 'Caixa' },
  { codigo: 'CJ', nome: 'Conjunto' },
];

export type ProdutoFormSubmission =
  | { mode: 'create'; request: AdminProdutoCadastroRequest }
  | { mode: 'edit'; request: AdminProdutoEdicaoRequest };

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrlCurrencyInputComponent, ReactiveFormsModule],
  selector: 'app-admin-produto-form',
  templateUrl: './admin-produto-form.component.html',
})
export class AdminProdutoFormComponent {
  readonly categorias = input<AdminCategoria[]>([]);
  readonly produto = input<AdminProduto | null>(null);
  readonly salvando = input(false);
  readonly mensagemErro = input('');
  readonly salvar = output<ProdutoFormSubmission>();
  readonly cancelar = output<void>();

  protected readonly enviado = signal(false);
  protected readonly modoCadastro = signal<'simples' | 'opcoes'>('simples');
  protected readonly confirmarModoSimples = signal(false);
  protected readonly erroConfiguracao = signal('');
  protected readonly caracteristicas = signal<CaracteristicaDraft[]>([]);
  protected readonly variantes = signal<VarianteDraft[]>([]);
  protected readonly unidadesVenda = UNIDADES;
  protected readonly modoAtual = computed(() => {
    const produto = this.produto();
    return produto && (produto.caracteristicas.length > 0 || produto.variantes.length > 1)
      ? 'opcoes'
      : produto
        ? 'simples'
        : this.modoCadastro();
  });
  protected readonly temCategoriaAtiva = computed(() =>
    this.categorias().some((categoria) => categoria.ativa),
  );
  protected readonly categoriasDisponiveis = computed(() => {
    const produtoAtual = this.produto();
    return this.categorias().filter(
      (categoria) => categoria.ativa || categoria.id === produtoAtual?.categoriaId,
    );
  });
  protected readonly produtoForm = new FormGroup<ProdutoControls>({
    nome: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespace, Validators.maxLength(200)],
    }),
    descricao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(4000)],
    }),
    categoriaId: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });
  protected readonly variantePadraoForm = this.criarFormularioVariante({
    identificacao: 'Padrão',
    unidadeVenda: 'UN',
    precoVenda: null,
    peso: '',
    pesoUnidade: 'G',
    volume: '',
    volumeUnidade: 'ML',
  });

  private referenciaSequencial = 0;

  constructor() {
    this.variantePadraoForm.controls.identificacao.disable({ emitEvent: false });
    effect(() => {
      const produto = this.produto();
      this.enviado.set(false);
      this.erroConfiguracao.set('');
      this.confirmarModoSimples.set(false);
      this.modoCadastro.set('simples');
      this.produtoForm.reset({
        nome: produto?.nome ?? '',
        descricao: produto?.descricao ?? '',
        categoriaId: produto?.categoriaId ?? '',
      });
      this.variantePadraoForm.reset({
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: null,
        peso: '',
        pesoUnidade: 'G',
        volume: '',
        volumeUnidade: 'ML',
      });
      this.variantePadraoForm.controls.identificacao.disable({ emitEvent: false });
      this.caracteristicas.set([]);
      this.variantes.set([]);
      this.referenciaSequencial = 0;
      if (produto) {
        this.carregarProduto(produto);
      }
    });
  }

  protected selecionarModoCadastro(modo: 'simples' | 'opcoes', event: Event): void {
    if (this.produto()) {
      this.sincronizarRadiosModo(event);
      return;
    }
    if (modo === 'simples' && this.temDadosIncompativeisComModoSimples()) {
      this.confirmarModoSimples.set(true);
      this.sincronizarRadiosModo(event);
      return;
    }
    if (modo === 'simples') {
      this.prepararDadosSimples(this.variantePrincipal());
      this.caracteristicas.set([]);
      this.variantes.set([]);
      this.modoCadastro.set('simples');
      this.erroConfiguracao.set('');
      this.confirmarModoSimples.set(false);
      this.sincronizarRadiosModo(event);
      return;
    }
    this.confirmarModoSimples.set(false);
    this.modoCadastro.set(modo);
    this.erroConfiguracao.set('');
    if (modo === 'opcoes' && this.variantes().length === 0) {
      const simple = this.variantePadraoForm.getRawValue();
      this.variantes.set([
        this.criarVarianteDraft({
          padrao: true,
          unit: simple.unidadeVenda,
          price: simple.precoVenda,
          weight: simple.peso,
          volume: simple.volume,
        }),
      ]);
    }
    this.sincronizarRadiosModo(event);
  }

  protected cancelarTrocaParaSimples(): void {
    this.confirmarModoSimples.set(false);
  }

  protected confirmarTrocaParaSimples(): void {
    if (!this.confirmarModoSimples()) {
      return;
    }
    this.prepararDadosSimples(this.variantePrincipal());
    this.caracteristicas.set([]);
    this.variantes.set([]);
    this.modoCadastro.set('simples');
    this.erroConfiguracao.set('');
    this.confirmarModoSimples.set(false);
  }

  protected tituloCaracteristica(caracteristica: CaracteristicaDraft): string {
    if (caracteristica.tipoMedidaFisica === 'PesoLiquido') {
      return 'Peso';
    }
    if (caracteristica.tipoMedidaFisica === 'VolumeLiquido') {
      return 'Volume';
    }
    return caracteristica.nome.value.trim() || 'Nova característica';
  }

  protected indiceValorVisivel(caracteristica: CaracteristicaDraft, valor: ValorDraft): number {
    return (
      caracteristica.valores
        .filter((item) => !item.removido)
        .findIndex((item) => item.referencia === valor.referencia) + 1
    );
  }

  protected adicionarCaracteristica(tipo: TipoMedida, nome = ''): void {
    if (tipo && this.temCaracteristicaFisica(tipo)) {
      return;
    }
    const valorNome =
      nome || (tipo === 'PesoLiquido' ? 'Peso' : tipo === 'VolumeLiquido' ? 'Volume' : '');
    const referencia = this.novaReferencia('caracteristica');
    const caracteristica: CaracteristicaDraft = {
      referencia,
      nome: new FormControl(valorNome, {
        nonNullable: true,
        validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
      }),
      tipoMedidaFisica: tipo,
      valores: [],
      removida: false,
    };
    this.caracteristicas.update((items) => [...items, caracteristica]);
    this.variantes.update((items) =>
      items.map((item) => ({
        ...item,
        selecoes: { ...item.selecoes, [referencia]: new FormControl('', { nonNullable: true }) },
      })),
    );
    this.erroConfiguracao.set('');
  }

  protected adicionarValor(caracteristica: CaracteristicaDraft): void {
    const valor: ValorDraft = {
      referencia: this.novaReferencia('valor'),
      nome: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
      }),
      medida: new FormControl('', { nonNullable: true }),
      unidade: new FormControl(caracteristica.tipoMedidaFisica === 'PesoLiquido' ? 'G' : 'ML', {
        nonNullable: true,
      }),
      removido: false,
    };
    this.atualizarCaracteristica(caracteristica.referencia, (item) => ({
      ...item,
      valores: [...item.valores, valor],
    }));
    this.erroConfiguracao.set('');
  }

  protected removerCaracteristica(caracteristica: CaracteristicaDraft): void {
    if (this.caracteristicaEmUso(caracteristica)) {
      return;
    }
    this.atualizarCaracteristica(caracteristica.referencia, (item) => ({
      ...item,
      removida: true,
    }));
    this.erroConfiguracao.set('');
  }

  protected restaurarCaracteristica(caracteristica: CaracteristicaDraft): void {
    this.atualizarCaracteristica(caracteristica.referencia, (item) => ({
      ...item,
      removida: false,
    }));
  }

  protected removerValor(caracteristica: CaracteristicaDraft, valor: ValorDraft): void {
    if (this.valorEmUso(caracteristica, valor)) {
      return;
    }
    this.atualizarCaracteristica(caracteristica.referencia, (item) => ({
      ...item,
      valores: item.valores.map((current) =>
        current.referencia === valor.referencia ? { ...current, removido: true } : current,
      ),
    }));
    this.erroConfiguracao.set('');
  }

  protected adicionarCombinacao(): void {
    this.variantes.update((items) => [
      ...items,
      this.criarVarianteDraft({
        padrao: false,
        selecoes: Object.fromEntries(
          this.caracteristicas()
            .filter((feature) => !feature.removida)
            .map((feature) => [feature.referencia, new FormControl('', { nonNullable: true })]),
        ),
      }),
    ]);
    this.erroConfiguracao.set('');
  }

  protected removerCombinacao(variante: VarianteDraft): void {
    if (variante.id) {
      return;
    }
    this.variantes.update((items) => items.filter((item) => item !== variante));
    if (variante.padrao) {
      const firstActive = this.variantes().find((item) => item.ativa.value);
      if (firstActive) {
        this.definirPadrao(firstActive);
      }
    }
  }

  protected definirPadrao(variante: VarianteDraft): void {
    if (!variante.ativa.value || (this.produto() && !variante.id)) {
      return;
    }
    this.variantes.update((items) => items.map((item) => ({ ...item, padrao: item === variante })));
    this.atualizarIdentificacao(variante);
  }

  protected alterarAtiva(variante: VarianteDraft, ativa: boolean): void {
    if (!ativa && variante.padrao) {
      this.erroConfiguracao.set('A opção principal precisa permanecer ativa.');
      return;
    }
    variante.ativa.setValue(ativa);
    if (ativa) {
      variante.ativa.markAsDirty();
    }
    this.variantes.update((items) => [...items]);
  }

  protected alterarAtivaEvent(variante: VarianteDraft, event: Event): void {
    const target = event.target;
    if (target instanceof HTMLInputElement) {
      this.alterarAtiva(variante, target.checked);
    }
  }

  protected atualizarSelecao(
    variante: VarianteDraft,
    caracteristica: CaracteristicaDraft,
    event: Event,
  ): void {
    const target = event.target;
    if (!(target instanceof HTMLSelectElement)) {
      return;
    }
    variante.selecoes[caracteristica.referencia]?.setValue(target.value);
    this.atualizarIdentificacao(variante);
    this.erroConfiguracao.set('');
    this.variantes.update((items) => [...items]);
  }

  protected atualizarIdentificacaoManual(variante: VarianteDraft, event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }
    variante.identificacaoManual = true;
    variante.identificacao.setValue(target.value);
  }

  protected habilitarIdentificacaoManual(variante: VarianteDraft): void {
    variante.identificacaoManual = true;
    variante.identificacao.markAsTouched();
    this.variantes.update((items) => [...items]);
  }

  protected exibirValor(valor: ValorDraft, caracteristica: CaracteristicaDraft): string {
    if (!caracteristica.tipoMedidaFisica) {
      return valor.nome.value.trim();
    }
    const canonical = this.valorMedidaCanonico(valor, caracteristica.tipoMedidaFisica);
    return canonical === null
      ? 'Informe uma medida'
      : this.formatarMedida(canonical, caracteristica.tipoMedidaFisica);
  }

  protected unidadeLegada(unidade: string): boolean {
    return !UNIDADES_VENDA.some((item) => item === unidade);
  }

  protected temCaracteristicaFisica(tipo: Exclude<TipoMedida, null>): boolean {
    return this.caracteristicas().some((item) => !item.removida && item.tipoMedidaFisica === tipo);
  }

  protected unidadesOptions(unidadeAtual: string): Array<{ codigo: string; nome: string }> {
    return this.unidadeLegada(unidadeAtual)
      ? [...UNIDADES, { codigo: unidadeAtual, nome: `${unidadeAtual} (legada — manter)` }]
      : [...UNIDADES];
  }

  protected unidadeLabel(unidade: string): string {
    return UNIDADES.find((item) => item.codigo === unidade)?.nome ?? `${unidade} (legada)`;
  }

  protected medidaDerivada(variante: VarianteDraft, tipo: TipoMedida): string | null {
    if (!tipo) {
      return null;
    }
    const feature = this.caracteristicas().find(
      (item) => !item.removida && item.tipoMedidaFisica === tipo,
    );
    const valorReferencia = feature && variante.selecoes[feature.referencia]?.value;
    const valor = feature?.valores.find(
      (item) => !item.removido && item.referencia === valorReferencia,
    );
    const medida = valor && this.valorMedidaCanonico(valor, tipo);
    return medida === null || medida === undefined ? null : this.formatarMedida(medida, tipo);
  }

  protected erroCampo(form: AbstractControl, field: string): string {
    const control = form.get(field);
    if (!control?.invalid || (!control.touched && !this.enviado())) {
      return '';
    }
    if (control.hasError('currencyPrecision')) {
      return 'O preço não pode ser representado com precisão de centavos. Informe outro valor.';
    }
    if (control.hasError('invalidSaleUnit')) {
      return 'Selecione uma das seis unidades comerciais disponíveis.';
    }
    if (control.hasError('required') || control.hasError('whitespace')) {
      return field === 'nome'
        ? 'Informe o nome do produto.'
        : field === 'categoriaId'
          ? 'Selecione uma categoria ativa.'
          : field === 'identificacao'
            ? 'Informe a identificação da combinação.'
            : field === 'unidadeVenda'
              ? 'Selecione a unidade de venda.'
              : 'Informe o preço de venda.';
    }
    if (control.hasError('maxlength')) {
      return field === 'nome'
        ? 'O nome deve ter no máximo 200 caracteres.'
        : field === 'descricao'
          ? 'A descrição deve ter no máximo 4.000 caracteres.'
          : 'O texto deve ter no máximo 100 caracteres.';
    }
    if (control.hasError('min')) {
      return 'O preço deve ser maior que zero.';
    }
    if (control.hasError('max')) {
      return 'O preço excede o limite aceito.';
    }
    return 'Revise este campo.';
  }

  protected erroVarianteCampo(
    variante: VarianteDraft,
    field: 'identificacao' | 'unidadeVenda' | 'precoVenda',
  ): string {
    const control = variante[field];
    if (!control.invalid || (!control.touched && !this.enviado())) {
      return '';
    }
    if (control.hasError('currencyPrecision')) {
      return 'O preço precisa ter exatamente a precisão de centavos.';
    }
    if (control.hasError('invalidSaleUnit')) {
      return 'Selecione uma das seis unidades comerciais disponíveis.';
    }
    if (control.hasError('required') || control.hasError('whitespace')) {
      return field === 'identificacao'
        ? 'Informe uma identificação para esta opção.'
        : field === 'unidadeVenda'
          ? 'Selecione a unidade comercial.'
          : 'Informe o preço de venda.';
    }
    if (control.hasError('maxlength')) {
      return 'A identificação deve ter no máximo 100 caracteres.';
    }
    if (control.hasError('min')) {
      return 'O preço deve ser maior que zero.';
    }
    if (control.hasError('max')) {
      return 'O preço excede o limite aceito.';
    }
    return 'Revise este campo.';
  }

  protected medidaVarianteValida(
    variante: VarianteDraft,
    tipo: Exclude<TipoMedida, null>,
  ): boolean {
    return tipo === 'PesoLiquido'
      ? this.medirRawValido(variante.peso.value, variante.pesoUnidade.value)
      : this.medirRawValido(variante.volume.value, variante.volumeUnidade.value);
  }

  protected erroNomeCaracteristica(caracteristica: CaracteristicaDraft): string {
    return caracteristica.nome.invalid && (caracteristica.nome.touched || this.enviado())
      ? 'Informe um nome para esta característica.'
      : '';
  }

  protected erroNomeValor(valor: ValorDraft): string {
    return valor.nome.invalid && (valor.nome.touched || this.enviado())
      ? 'Informe um texto para este valor.'
      : '';
  }

  protected submit(): void {
    if (this.salvando()) {
      return;
    }
    this.enviado.set(true);
    this.produtoForm.markAllAsTouched();
    if (this.produtoForm.invalid) {
      return;
    }
    const { nome, descricao, categoriaId } = this.produtoForm.getRawValue();
    const normalizedDescription = descricao.trim() || null;
    const produtoAtual = this.produto();

    if (this.modoAtual() === 'simples') {
      this.variantePadraoForm.markAllAsTouched();
      if (this.variantePadraoForm.invalid || !this.medidasSimplesValidas()) {
        return;
      }
      const simpleVariant = this.toVariantRequest(this.variantePadraoForm, {
        usarNomePadrao: !produtoAtual,
        varianteExistente: produtoAtual?.variantes[0],
        includeDirectMeasurements: true,
      });
      if (produtoAtual) {
        this.salvar.emit({
          mode: 'edit',
          request: {
            nome: nome.trim(),
            descricao: normalizedDescription,
            categoriaId,
            variantes: [simpleVariant],
          },
        });
      } else {
        this.salvar.emit({
          mode: 'create',
          request: {
            nome: nome.trim(),
            descricao: normalizedDescription,
            categoriaId,
            simples: true,
            variantePadrao: simpleVariant,
            variantes: [],
          },
        });
      }
      return;
    }

    if (!this.validarOpcoes()) {
      return;
    }
    const caracteristicas = this.toCaracteristicasRequest();
    const variants = this.variantes().filter((item) => !item.id || item.ativa.value);
    const variantRequests = this.variantes()
      .filter((item) => !this.isDefaultForCreate(item))
      .map((item) => this.toDraftVariantRequest(item));
    if (produtoAtual) {
      this.salvar.emit({
        mode: 'edit',
        request: {
          nome: nome.trim(),
          descricao: normalizedDescription,
          categoriaId,
          caracteristicas,
          variantes: this.variantes().map((item) => this.toDraftVariantRequest(item, true)),
        },
      });
      return;
    }

    const defaultVariant = variants.find((item) => item.padrao && item.ativa.value);
    if (!defaultVariant) {
      this.erroConfiguracao.set('Escolha uma combinação ativa para apresentar primeiro.');
      return;
    }
    this.salvar.emit({
      mode: 'create',
      request: {
        nome: nome.trim(),
        descricao: normalizedDescription,
        categoriaId,
        simples: false,
        variantePadrao: this.toDraftVariantRequest(defaultVariant),
        variantes: variantRequests,
        caracteristicas,
      },
    });
  }

  protected combinacaoLegivel(variante: VarianteDraft): string {
    return this.rotulosSelecao(variante).join(' · ') || 'Selecione os valores desta combinação';
  }

  protected caracteristicaEmUso(caracteristica: CaracteristicaDraft): boolean {
    return this.variantes().some(
      (variante) => !!variante.selecoes[caracteristica.referencia]?.value,
    );
  }

  protected valorEmUso(caracteristica: CaracteristicaDraft, valor: ValorDraft): boolean {
    return this.variantes().some(
      (variante) => variante.selecoes[caracteristica.referencia]?.value === valor.referencia,
    );
  }

  protected recarregarNome(variante: VarianteDraft): void {
    variante.identificacaoManual = false;
    this.atualizarIdentificacao(variante);
  }

  protected unidadeOptions(variante: VarianteDraft): Array<{ codigo: string; nome: string }> {
    const current = variante.unidadeVenda.value;
    return this.unidadeLegada(current)
      ? [...UNIDADES, { codigo: current, nome: `${current} (legada — manter)` }]
      : [...UNIDADES];
  }

  protected medirRawValido(valor: string, unidade: UnidadeMedida): boolean {
    if (!valor.trim()) {
      return true;
    }
    const canonical = this.converterMedida(valor, unidade);
    return canonical !== null && canonical > 0;
  }

  protected medidaObrigatoriaValida(valor: string, unidade: UnidadeMedida): boolean {
    const canonical = this.converterMedida(valor, unidade);
    return canonical !== null && canonical > 0;
  }

  protected canonicalDaVariante(variante: VarianteDraft, tipo: TipoMedida): number | null {
    if (tipo === 'PesoLiquido') {
      return variante.peso.value.trim()
        ? this.converterMedida(variante.peso.value, variante.pesoUnidade.value)
        : null;
    }
    if (tipo === 'VolumeLiquido') {
      return variante.volume.value.trim()
        ? this.converterMedida(variante.volume.value, variante.volumeUnidade.value)
        : null;
    }
    return null;
  }

  private carregarProduto(produto: AdminProduto): void {
    const variante = produto.variantes.find((item) => item.padrao) ?? produto.variantes[0];
    if (this.modoAtual() === 'simples' && variante) {
      this.variantePadraoForm.reset({
        identificacao: variante.identificacao,
        unidadeVenda: variante.unidadeVenda,
        precoVenda: variante.precoVenda,
        peso: variante.pesoLiquidoGramas?.toString() ?? '',
        pesoUnidade: 'G',
        volume: variante.volumeLiquidoMililitros?.toString() ?? '',
        volumeUnidade: 'ML',
      });
      this.variantePadraoForm.controls.identificacao.disable({ emitEvent: false });
      this.variantePadraoForm.controls.unidadeVenda.setValidators([
        Validators.required,
        (control) => validSaleUnit(control, variante.unidadeVenda),
      ]);
      this.variantePadraoForm.controls.unidadeVenda.updateValueAndValidity({ emitEvent: false });
      return;
    }

    const features = produto.caracteristicas.map((item) => {
      const reference = item.id;
      return {
        id: item.id,
        referencia: reference,
        nome: new FormControl(item.nome, {
          nonNullable: true,
          validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
        }),
        tipoMedidaFisica: item.tipoMedidaFisica,
        removida: false,
        valores: item.valores.map((value) => ({
          id: value.id,
          referencia: value.id,
          nome: new FormControl(value.nome, {
            nonNullable: true,
            validators: item.tipoMedidaFisica
              ? []
              : [Validators.required, nonWhitespace, Validators.maxLength(100)],
          }),
          medida: new FormControl(value.valorMedidaCanonico?.toString() ?? '', {
            nonNullable: true,
          }),
          unidade: new FormControl(item.tipoMedidaFisica === 'PesoLiquido' ? 'G' : 'ML', {
            nonNullable: true,
          }),
          removido: false,
        })),
      } satisfies CaracteristicaDraft;
    });
    const variants = produto.variantes.map((item) => {
      const selecoes: Record<string, FormControl<string>> = {};
      for (const feature of features) {
        const association = item.caracteristicas.find(
          (entry) => entry.caracteristicaId === feature.id,
        );
        selecoes[feature.referencia] = new FormControl(association?.valorId ?? '', {
          nonNullable: true,
        });
      }
      const derivedWeight = item.caracteristicas.some(
        (association) =>
          features.find((feature) => feature.id === association.caracteristicaId)
            ?.tipoMedidaFisica === 'PesoLiquido',
      );
      const derivedVolume = item.caracteristicas.some(
        (association) =>
          features.find((feature) => feature.id === association.caracteristicaId)
            ?.tipoMedidaFisica === 'VolumeLiquido',
      );
      return this.criarVarianteDraft({
        id: item.id,
        identification: item.identificacao,
        identificationManual: true,
        unit: item.unidadeVenda,
        price: item.precoVenda,
        active: item.ativa,
        padrao: item.padrao,
        weight: derivedWeight ? '' : (item.pesoLiquidoGramas?.toString() ?? ''),
        volume: derivedVolume ? '' : (item.volumeLiquidoMililitros?.toString() ?? ''),
        selecoes,
      });
    });
    this.caracteristicas.set(features);
    this.variantes.set(variants);
  }

  private criarFormularioVariante(initial: {
    identificacao: string;
    unidadeVenda: string;
    precoVenda: number | null;
    peso: string;
    pesoUnidade: 'G' | 'KG';
    volume: string;
    volumeUnidade: 'ML' | 'L';
  }): FormGroup<VarianteControls> {
    return new FormGroup<VarianteControls>({
      identificacao: new FormControl(initial.identificacao, {
        nonNullable: true,
        validators: [Validators.maxLength(100)],
      }),
      unidadeVenda: new FormControl(initial.unidadeVenda, {
        nonNullable: true,
        validators: [Validators.required, validSaleUnit],
      }),
      precoVenda: new FormControl<number | null>(initial.precoVenda, {
        validators: [
          Validators.required,
          Validators.min(0.01),
          Validators.max(MAX_PRECO_VARIANTE),
          currencyPrecision,
        ],
      }),
      peso: new FormControl(initial.peso, { nonNullable: true }),
      pesoUnidade: new FormControl(initial.pesoUnidade, { nonNullable: true }),
      volume: new FormControl(initial.volume, { nonNullable: true }),
      volumeUnidade: new FormControl(initial.volumeUnidade, { nonNullable: true }),
    });
  }

  private temDadosIncompativeisComModoSimples(): boolean {
    if (this.caracteristicas().some((item) => !item.removida)) {
      return true;
    }
    return this.variantes().slice(1).some((variante) => {
      const temSelecao = Object.values(variante.selecoes).some((control) => !!control.value.trim());
      return (
        temSelecao ||
        variante.identificacaoManual ||
        variante.precoVenda.value !== null ||
        variante.unidadeVenda.value !== 'UN' ||
        !variante.ativa.value ||
        !!variante.peso.value.trim() ||
        !!variante.volume.value.trim()
      );
    });
  }

  private prepararDadosSimples(variante?: VarianteDraft): void {
    if (!variante) {
      return;
    }
    const pesoDerivado = this.medidaDerivadaCanonica(variante, 'PesoLiquido');
    const volumeDerivado = this.medidaDerivadaCanonica(variante, 'VolumeLiquido');
    this.variantePadraoForm.patchValue({
      unidadeVenda: variante.unidadeVenda.value,
      precoVenda: variante.precoVenda.value,
      peso: pesoDerivado?.toString() ?? variante.peso.value,
      pesoUnidade: pesoDerivado === null ? variante.pesoUnidade.value : 'G',
      volume: volumeDerivado?.toString() ?? variante.volume.value,
      volumeUnidade: volumeDerivado === null ? variante.volumeUnidade.value : 'ML',
    });
  }

  private variantePrincipal(): VarianteDraft | undefined {
    return (
      this.variantes().find((variante) => variante.padrao && variante.ativa.value) ??
      this.variantes()[0]
    );
  }

  private medidaDerivadaCanonica(
    variante: VarianteDraft,
    tipo: Exclude<TipoMedida, null>,
  ): number | null {
    const caracteristica = this.caracteristicas().find(
      (item) => !item.removida && item.tipoMedidaFisica === tipo,
    );
    const referenciaValor = caracteristica && variante.selecoes[caracteristica.referencia]?.value;
    const valor = caracteristica?.valores.find(
      (item) => !item.removido && item.referencia === referenciaValor,
    );
    if (!caracteristica?.tipoMedidaFisica || !valor) {
      return null;
    }
    return this.valorMedidaCanonico(valor, caracteristica.tipoMedidaFisica);
  }

  private sincronizarRadiosModo(event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }
    target
      .closest('fieldset')
      ?.querySelectorAll<HTMLInputElement>('input[name="product-mode"]')
      .forEach((radio) => {
        radio.checked = radio.value === this.modoAtual();
      });
  }

  private criarVarianteDraft(initial: {
    id?: string;
    identification?: string;
    identificationManual?: boolean;
    unit?: string;
    price?: number | null;
    active?: boolean;
    padrao: boolean;
    weight?: string;
    volume?: string;
    selecoes?: Record<string, FormControl<string>>;
  }): VarianteDraft {
    const unit = initial.unit ?? 'UN';
    const result: VarianteDraft = {
      id: initial.id,
      identificacao: new FormControl(initial.identification ?? '', {
        nonNullable: true,
        validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
      }),
      identificacaoManual: initial.identificationManual ?? false,
      unidadeVenda: new FormControl(unit, {
        nonNullable: true,
        validators: [
          Validators.required,
          (control) => validSaleUnit(control, initial.id ? unit : undefined),
        ],
      }),
      precoVenda: new FormControl<number | null>(initial.price ?? null, {
        validators: [
          Validators.required,
          Validators.min(0.01),
          Validators.max(MAX_PRECO_VARIANTE),
          currencyPrecision,
        ],
      }),
      ativa: new FormControl(initial.active ?? true, { nonNullable: true }),
      padrao: initial.padrao,
      peso: new FormControl(initial.weight ?? '', { nonNullable: true }),
      pesoUnidade: new FormControl('G', { nonNullable: true }),
      volume: new FormControl(initial.volume ?? '', { nonNullable: true }),
      volumeUnidade: new FormControl('ML', { nonNullable: true }),
      selecoes: initial.selecoes ?? {},
    };
    return result;
  }

  private validarOpcoes(): boolean {
    const features = this.caracteristicas().filter((item) => !item.removida);
    const variants = this.variantes();
    for (const feature of features) {
      feature.nome.markAsTouched();
      const values = feature.valores.filter((item) => !item.removido);
      if (feature.nome.invalid || values.length === 0) {
        this.erroConfiguracao.set('Informe o nome de cada característica e pelo menos um valor.');
        return false;
      }
      const normalizedValues = new Set<string>();
      for (const value of values) {
        if (feature.tipoMedidaFisica) {
          const canonical = this.valorMedidaCanonico(value, feature.tipoMedidaFisica);
          if (canonical === null || canonical <= 0) {
            this.erroConfiguracao.set(
              'Informe cada peso ou volume com um valor positivo e no máximo três casas decimais canônicas.',
            );
            value.medida.markAsTouched();
            return false;
          }
          const key = canonical.toFixed(3);
          if (normalizedValues.has(key)) {
            this.erroConfiguracao.set('Esta característica contém valores físicos repetidos.');
            return false;
          }
          normalizedValues.add(key);
        } else {
          value.nome.markAsTouched();
          const normalized = this.normalizar(value.nome.value);
          if (!normalized || normalizedValues.has(normalized)) {
            this.erroConfiguracao.set(
              'Preencha os valores e remova textos repetidos (mesmo quando muda maiúsculas ou espaços).',
            );
            return false;
          }
          normalizedValues.add(normalized);
        }
      }
    }
    const names = new Set<string>();
    for (const feature of features) {
      const normalized = this.normalizar(feature.nome.value);
      if (!normalized || names.has(normalized)) {
        this.erroConfiguracao.set(
          'Os nomes das características precisam ser preenchidos e não podem se repetir.',
        );
        return false;
      }
      names.add(normalized);
    }
    if ((features.length === 0 && !this.produto()) || variants.length === 0) {
      this.erroConfiguracao.set('Adicione ao menos uma característica e uma combinação de venda.');
      return false;
    }
    if (variants.filter((item) => item.padrao && item.ativa.value).length !== 1) {
      this.erroConfiguracao.set(
        'Escolha exatamente uma combinação ativa para apresentar primeiro.',
      );
      return false;
    }
    const signatures = new Set<string>();
    for (const variant of variants) {
      variant.identificacao.markAsTouched();
      variant.unidadeVenda.markAsTouched();
      variant.precoVenda.markAsTouched();
      if (
        variant.identificacao.invalid ||
        variant.unidadeVenda.invalid ||
        variant.precoVenda.invalid ||
        !this.medidasVarianteValidas(variant)
      ) {
        this.erroConfiguracao.set('Revise os dados de venda, a identificação e as medidas.');
        return false;
      }
      const selected: string[] = [];
      for (const feature of features) {
        const valueReference = variant.selecoes[feature.referencia]?.value;
        const value = feature.valores.find(
          (item) => !item.removido && item.referencia === valueReference,
        );
        if (value) {
          selected.push(`${feature.referencia}:${value.referencia}`);
        }
      }
      if (selected.length === 0) {
        if (!variant.id) {
          this.erroConfiguracao.set(
            'Escolha pelo menos um valor para cada nova combinação vendida.',
          );
          return false;
        }
        continue;
      }
      const signature = selected.sort().join('|');
      if (signatures.has(signature)) {
        this.erroConfiguracao.set(
          'Há combinações repetidas. Cada combinação de valores só pode ser cadastrada uma vez.',
        );
        return false;
      }
      signatures.add(signature);
    }
    this.erroConfiguracao.set('');
    return true;
  }

  private medidasSimplesValidas(): boolean {
    const form = this.variantePadraoForm;
    const peso = form.controls.peso.value;
    const volume = form.controls.volume.value;
    if (
      !this.medirRawValido(peso, form.controls.pesoUnidade.value as UnidadeMedida) ||
      !this.medirRawValido(volume, form.controls.volumeUnidade.value as UnidadeMedida)
    ) {
      this.erroConfiguracao.set(
        'Peso e volume devem ser positivos e ter até três casas decimais canônicas.',
      );
      return false;
    }
    this.erroConfiguracao.set('');
    return true;
  }

  private medidasVarianteValidas(variante: VarianteDraft): boolean {
    if (
      !this.medirRawValido(variante.peso.value, variante.pesoUnidade.value) ||
      !this.medirRawValido(variante.volume.value, variante.volumeUnidade.value)
    ) {
      return false;
    }
    return true;
  }

  private toVariantRequest(
    form: FormGroup<VarianteControls>,
    options: {
      usarNomePadrao?: boolean;
      varianteExistente?: AdminProdutoVariante;
      includeDirectMeasurements?: boolean;
    } = {},
  ): AdminProdutoVarianteRequest {
    const value = form.getRawValue();
    const unit = value.unidadeVenda.trim().toUpperCase();
    const request: AdminProdutoVarianteRequest = {
      unidadeVenda: unit,
      precoVenda: value.precoVenda!,
    };
    if (options.varianteExistente) {
      request.id = options.varianteExistente.id;
      request.identificacao = options.varianteExistente.identificacao;
      request.atualizarMedidas = true;
    } else if (!options.usarNomePadrao) {
      request.identificacao = value.identificacao.trim();
    }
    if (options.includeDirectMeasurements || options.varianteExistente) {
      const weight = this.converterMedida(value.peso, value.pesoUnidade);
      const volume = this.converterMedida(value.volume, value.volumeUnidade);
      if (weight !== null || options.varianteExistente) {
        request.pesoLiquidoGramas = weight;
      }
      if (volume !== null || options.varianteExistente) {
        request.volumeLiquidoMililitros = volume;
      }
    }
    if (options.varianteExistente) {
      request.ativa = options.varianteExistente.ativa;
      request.padrao = options.varianteExistente.padrao;
    }
    return request;
  }

  private toDraftVariantRequest(
    variante: VarianteDraft,
    forEdit = false,
  ): AdminProdutoVarianteRequest {
    const weight = this.medidaDerivada(variante, 'PesoLiquido')
      ? null
      : this.canonicalDaVariante(variante, 'PesoLiquido');
    const volume = this.medidaDerivada(variante, 'VolumeLiquido')
      ? null
      : this.canonicalDaVariante(variante, 'VolumeLiquido');
    const request: AdminProdutoVarianteRequest = {
      identificacao: variante.identificacao.value.trim(),
      unidadeVenda: variante.unidadeVenda.value.trim().toUpperCase(),
      precoVenda: variante.precoVenda.value!,
      caracteristicas: this.toAssociations(variante),
    };
    if (weight !== null || forEdit) {
      request.pesoLiquidoGramas = weight;
    }
    if (volume !== null || forEdit) {
      request.volumeLiquidoMililitros = volume;
    }
    if (forEdit) {
      request.ativa = variante.ativa.value;
      request.padrao = variante.padrao;
      request.atualizarMedidas = true;
    } else if (!variante.ativa.value) {
      request.ativa = false;
    }
    if (variante.id) {
      request.id = variante.id;
    }
    return request;
  }

  private toAssociations(variante: VarianteDraft): AdminProdutoVarianteCaracteristicaRequest[] {
    const associations: AdminProdutoVarianteCaracteristicaRequest[] = [];
    for (const feature of this.caracteristicas()) {
      if (feature.removida) {
        continue;
      }
      const valueReference = variante.selecoes[feature.referencia]?.value;
      if (!valueReference) {
        continue;
      }
      const value = feature.valores.find(
        (item) => !item.removido && item.referencia === valueReference,
      );
      if (!value) {
        continue;
      }
      associations.push({
        ...(feature.id
          ? { caracteristicaId: feature.id }
          : { referenciaCaracteristica: feature.referencia }),
        ...(value.id ? { valorId: value.id } : { referenciaValor: value.referencia }),
      });
    }
    return associations;
  }

  private toCaracteristicasRequest(): AdminProdutoCaracteristicaRequest[] {
    return this.caracteristicas()
      .filter((feature) => !feature.removida || !!feature.id)
      .map((feature) => ({
        ...(feature.id ? { id: feature.id } : { referencia: feature.referencia }),
        ...(feature.removida
          ? { remover: true }
          : {
              nome: feature.nome.value.trim(),
              tipoMedidaFisica: feature.tipoMedidaFisica,
              valores: feature.valores
                .filter((value) => !value.removido || !!value.id)
                .map((value) => {
                  if (value.removido) {
                    return {
                      ...(value.id ? { id: value.id } : { referencia: value.referencia }),
                      remover: true,
                    };
                  }
                  return {
                    ...(value.id ? { id: value.id } : { referencia: value.referencia }),
                    ...(feature.tipoMedidaFisica
                      ? {
                          valorMedidaCanonico: this.valorMedidaCanonico(
                            value,
                            feature.tipoMedidaFisica,
                          ),
                        }
                      : { nome: value.nome.value.trim() }),
                  };
                }),
            }),
      }));
  }

  private valorMedidaCanonico(valor: ValorDraft, tipo: Exclude<TipoMedida, null>): number | null {
    const unit: UnidadeMedida = tipo === 'PesoLiquido' ? valor.unidade.value : valor.unidade.value;
    return this.converterMedida(valor.medida.value, unit);
  }

  private converterMedida(valor: string, unidade: UnidadeMedida): number | null {
    const trimmed = valor.trim().replace(',', '.');
    if (!trimmed) {
      return null;
    }
    if (!/^(?:\d+)(?:\.\d{1,6})?$/.test(trimmed)) {
      return null;
    }
    const [whole, fraction = ''] = trimmed.split('.');
    const scale = 10n ** BigInt(fraction.length);
    const input = BigInt(whole) * scale + BigInt(fraction || '0');
    const factor = unidade === 'KG' || unidade === 'L' ? 1000n : 1n;
    const canonicalScaled = input * factor * 1000n;
    if (canonicalScaled % scale !== 0n) {
      return null;
    }
    const canonical = canonicalScaled / scale;
    if (canonical <= 0n || canonical > 999_999_999_999n) {
      return null;
    }
    return Number(canonical) / 1000;
  }

  private formatarMedida(valor: number, tipo: Exclude<TipoMedida, null>): string {
    const unit = tipo === 'PesoLiquido' ? 'g' : 'ml';
    return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(valor)} ${unit}`;
  }

  private atualizarIdentificacao(variante: VarianteDraft): void {
    if (variante.identificacaoManual) {
      return;
    }
    const generated = this.rotulosSelecao(variante).join(' · ');
    variante.identificacao.setValue(generated);
    this.variantes.update((items) => [...items]);
  }

  private rotulosSelecao(variante: VarianteDraft): string[] {
    const labels: string[] = [];
    for (const feature of this.caracteristicas()) {
      if (feature.removida) {
        continue;
      }
      const selectedReference = variante.selecoes[feature.referencia]?.value;
      const value = feature.valores.find(
        (item) => !item.removido && item.referencia === selectedReference,
      );
      if (value) {
        labels.push(this.exibirValor(value, feature));
      }
    }
    return labels;
  }

  private normalizar(value: string): string {
    return value.trim().replace(/\s+/g, ' ').toLocaleLowerCase('pt-BR');
  }

  private atualizarCaracteristica(
    referencia: string,
    update: (item: CaracteristicaDraft) => CaracteristicaDraft,
  ): void {
    this.caracteristicas.update((items) =>
      items.map((item) => (item.referencia === referencia ? update(item) : item)),
    );
  }

  private isDefaultForCreate(variante: VarianteDraft): boolean {
    return !this.produto() && variante.padrao;
  }

  private novaReferencia(prefix: string): string {
    this.referenciaSequencial += 1;
    return `${prefix}-${this.referenciaSequencial}`;
  }
}
