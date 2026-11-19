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
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  AdminFormaCompra,
  AdminInsumoDetalhe,
  AtualizarInsumoRequest,
  CadastrarInsumoRequest,
  FormaCompraRequest,
  InsumosService,
  TipoInsumoEstoque,
  UnidadeBaseEstoque,
  UnidadeFisicaEstoque,
} from '../../../core/admin-estoque/insumos.service';
import { AdminWorkspaceComponent } from '../../admin-workspace.component';

type InsumoControls = {
  nome: FormControl<string>;
  tipo: FormControl<TipoInsumoEstoque>;
  unidadeBase: FormControl<UnidadeBaseEstoque>;
  estoqueMinimo: FormControl<number | null>;
  controlaLote: FormControl<boolean>;
  validadeObrigatoria: FormControl<boolean>;
};

type FormaCompraControls = {
  nome: FormControl<string>;
  quantidadeEquivalentePorEmbalagem: FormControl<number | null>;
  unidade: FormControl<UnidadeFisicaEstoque>;
};

const MAX_QUANTIDADE_BASE_UI = 999_999_999_999.9999;

const hasDecimalScale = (value: number, scale: number): boolean => {
  if (value === null || !Number.isFinite(value)) {
    return false;
  }

  const [mantissa, exponentText] = value.toString().toLowerCase().split('e');
  const fractionLength = mantissa?.split('.')[1]?.length ?? 0;
  const exponent = exponentText ? Number(exponentText) : 0;
  return Math.max(0, fractionLength - exponent) <= scale;
};

const decimalScale =
  (scale: number): ValidatorFn =>
  (control): ValidationErrors | null => {
    const value = control.value as number | null;
    return value === null || hasDecimalScale(value, scale) ? null : { decimalScale: true };
  };

const nonWhitespace: ValidatorFn = (control): ValidationErrors | null =>
  typeof control.value === 'string' && control.value.trim().length > 0
    ? null
    : { whitespace: true };

const loteConfiguration: ValidatorFn = (control): ValidationErrors | null => {
  const group = control as FormGroup<InsumoControls>;
  return group.controls.validadeObrigatoria.value && !group.controls.controlaLote.value
    ? { validadeSemLote: true }
    : null;
};

const estoqueMinimoConfiguration: ValidatorFn = (control): ValidationErrors | null => {
  const group = control as FormGroup<InsumoControls>;
  const minimo = group.controls.estoqueMinimo.value;
  return group.controls.unidadeBase.value === 'Unidade' &&
    minimo !== null &&
    !Number.isInteger(minimo)
    ? { integerMinimum: true }
    : null;
};

const formaFactor: ValidatorFn = (control): ValidationErrors | null => {
  const group = control as FormGroup<FormaCompraControls>;
  const quantity = group.controls.quantidadeEquivalentePorEmbalagem.value;
  if (quantity === null || !Number.isFinite(quantity) || quantity <= 0) {
    return { positive: true };
  }

  const unidade = group.controls.unidade.value;
  if (unidade === 'Unidade' && !Number.isInteger(quantity)) {
    return { integerUnit: true };
  }
  const factor = unidade === 'Quilograma' || unidade === 'Litro' ? 1000 : 1;
  const quantidadeBase = quantity * factor;
  if (!Number.isFinite(quantidadeBase) || quantidadeBase > MAX_QUANTIDADE_BASE_UI) {
    return { maximum: true };
  }
  const maximumInputScale = factor === 1000 ? 9 : 6;
  return hasDecimalScale(quantity, maximumInputScale) ? null : { decimalScale: true };
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  selector: 'app-admin-insumo-form',
  styleUrl: './admin-insumo-form.component.css',
  templateUrl: './admin-insumo-form.component.html',
})
export class AdminInsumoFormComponent {
  protected readonly insumoId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly editando = this.insumoId !== null;
  protected readonly detalhe = signal<AdminInsumoDetalhe | null>(null);
  protected readonly carregando = signal(false);
  protected readonly salvando = signal(false);
  protected readonly alterandoStatus = signal(false);
  protected readonly erroCarregamento = signal('');
  protected readonly erroFormulario = signal('');
  protected readonly mensagem = signal('');
  protected readonly formaEmEdicao = signal<AdminFormaCompra | null>(null);
  protected readonly mostrandoForma = signal(false);
  protected readonly salvandoForma = signal(false);
  protected readonly erroForma = signal('');
  protected readonly mensagemForma = signal('');
  protected readonly formasEmAtualizacao = signal<ReadonlySet<string>>(new Set());
  protected readonly titulo = computed(() => (this.editando ? 'Detalhe do material' : 'Novo material'));

  protected readonly form = new FormGroup<InsumoControls>(
    {
      nome: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, nonWhitespace, Validators.maxLength(200)],
      }),
      tipo: new FormControl<TipoInsumoEstoque>('MateriaPrima', {
        nonNullable: true,
        validators: [Validators.required],
      }),
      unidadeBase: new FormControl<UnidadeBaseEstoque>('Grama', {
        nonNullable: true,
        validators: [Validators.required],
      }),
      estoqueMinimo: new FormControl<number | null>(null, {
        validators: [Validators.min(0), Validators.max(MAX_QUANTIDADE_BASE_UI), decimalScale(6)],
      }),
      controlaLote: new FormControl(false, { nonNullable: true }),
      validadeObrigatoria: new FormControl({ value: false, disabled: true }, { nonNullable: true }),
    },
    { validators: [loteConfiguration, estoqueMinimoConfiguration] },
  );

  protected readonly formaForm = new FormGroup<FormaCompraControls>(
    {
      nome: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
      }),
      quantidadeEquivalentePorEmbalagem: new FormControl<number | null>(null, {
        validators: [Validators.required, Validators.min(Number.MIN_VALUE)],
      }),
      unidade: new FormControl<UnidadeFisicaEstoque>('Grama', {
        nonNullable: true,
        validators: [Validators.required],
      }),
    },
    { validators: [formaFactor] },
  );

  private readonly insumosService = inject(InsumosService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.form.controls.controlaLote.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((controlaLote) => {
        const validade = this.form.controls.validadeObrigatoria;
        if (controlaLote) {
          validade.enable({ emitEvent: false });
        } else {
          validade.setValue(false, { emitEvent: false });
          validade.disable({ emitEvent: false });
        }
      });

    const navigationMessage = this.router.getCurrentNavigation()?.extras.state?.['mensagem'];
    if (typeof navigationMessage === 'string') {
      this.mensagem.set(navigationMessage);
    }

    if (this.insumoId) {
      this.carregarDetalhe();
    }
  }

  protected carregarDetalhe(): void {
    const id = this.insumoId;
    if (!id || this.carregando()) {
      return;
    }

    this.carregando.set(true);
    this.erroCarregamento.set('');
    this.insumosService
      .obter(id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (detalhe) => {
          this.definirDetalhe(detalhe);
          this.erroCarregamento.set('');
        },
        error: (error: unknown) => this.tratarErroCarregamento(error),
      });
  }

  protected salvarInsumo(): void {
    if (this.salvando()) {
      return;
    }

    this.form.markAllAsTouched();
    this.form.updateValueAndValidity();
    if (this.form.invalid) {
      return;
    }

    const valores = this.form.getRawValue();
    const minimo = valores.estoqueMinimo;
    this.erroFormulario.set('');
    this.mensagem.set('');
    this.salvando.set(true);

    const request: CadastrarInsumoRequest = {
      nome: valores.nome.trim(),
      tipo: valores.tipo,
      unidadeBase: valores.unidadeBase,
      estoqueMinimo: minimo,
      controlaLote: valores.controlaLote,
      validadeObrigatoria: valores.validadeObrigatoria,
    };

    const updateRequest: AtualizarInsumoRequest = {
      nome: request.nome,
      tipo: request.tipo,
      estoqueMinimo: request.estoqueMinimo,
      controlaLote: request.controlaLote,
      validadeObrigatoria: request.validadeObrigatoria,
    };
    const politicaAlterada = this.politicaAlterada(updateRequest);
    const operation = this.insumoId
      ? this.insumosService.editar(this.insumoId, updateRequest)
      : this.insumosService.criar(request);

    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvando.set(false)),
      )
      .subscribe({
        next: (detalhe) => {
          this.erroFormulario.set('');
          if (this.insumoId) {
            this.definirDetalhe(detalhe);
            this.mensagem.set('Insumo atualizado com sucesso.');
          } else {
            void this.router.navigate(['/admin/estoque/insumos', detalhe.resumo.insumoId], {
              state: { mensagem: 'Insumo cadastrado sem movimentar estoque.' },
            });
          }
        },
        error: (error: unknown) => this.tratarErroFormulario(error, politicaAlterada),
      });
  }

  protected alterarStatus(): void {
    const detalhe = this.detalhe();
    if (!this.insumoId || !detalhe || this.alterandoStatus()) {
      return;
    }

    const ativo = !detalhe.resumo.ativo;
    if (
      detalhe.resumo.ativo &&
      !window.confirm(
        'Inativar este insumo impede novas aquisições. O histórico e os saldos existentes serão preservados. Deseja continuar?',
      )
    ) {
      return;
    }

    this.alterandoStatus.set(true);
    this.erroFormulario.set('');
    this.mensagem.set('');
    this.insumosService
      .alterarStatus(this.insumoId, { ativo })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.alterandoStatus.set(false)),
      )
      .subscribe({
        next: (atualizado) => {
          this.definirDetalhe(atualizado);
          this.mensagem.set(
            ativo ? 'Insumo ativado com sucesso.' : 'Insumo inativado com sucesso.',
          );
        },
        error: (error: unknown) => this.tratarErroFormulario(error, false, 'status'),
      });
  }

  protected abrirNovaForma(): void {
    if (this.salvandoForma() || this.mostrandoForma()) {
      return;
    }
    this.formaEmEdicao.set(null);
    this.formaForm.reset({
      nome: '',
      quantidadeEquivalentePorEmbalagem: null,
      unidade: this.detalhe()?.resumo.unidadeBase ?? 'Grama',
    });
    this.erroForma.set('');
    this.mensagemForma.set('');
    this.mostrandoForma.set(true);
  }

  protected editarForma(forma: AdminFormaCompra): void {
    if (this.salvandoForma() || this.mostrandoForma()) {
      return;
    }
    this.formaEmEdicao.set(forma);
    this.formaForm.reset({
      nome: forma.nome,
      quantidadeEquivalentePorEmbalagem: forma.quantidadeEquivalentePorEmbalagem,
      unidade: forma.unidade,
    });
    this.erroForma.set('');
    this.mensagemForma.set('');
    this.mostrandoForma.set(true);
  }

  protected cancelarForma(): void {
    if (this.salvandoForma()) {
      return;
    }
    this.mostrandoForma.set(false);
    this.formaEmEdicao.set(null);
    this.erroForma.set('');
  }

  protected salvarForma(): void {
    if (this.salvandoForma() || !this.insumoId) {
      return;
    }

    this.formaForm.markAllAsTouched();
    this.formaForm.updateValueAndValidity();
    if (this.formaForm.invalid) {
      return;
    }

    const valores = this.formaForm.getRawValue();
    const quantidade = valores.quantidadeEquivalentePorEmbalagem;
    if (quantidade === null) {
      return;
    }

    const request: FormaCompraRequest = {
      nome: valores.nome.trim(),
      quantidadeEquivalentePorEmbalagem: quantidade,
      unidade: valores.unidade,
    };
    const forma = this.formaEmEdicao();
    this.salvandoForma.set(true);
    this.erroForma.set('');
    this.mensagemForma.set('');

    const operation = forma
      ? this.insumosService.editarFormaCompra(this.insumoId, forma.id, request)
      : this.insumosService.criarFormaCompra(this.insumoId, request);

    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvandoForma.set(false)),
      )
      .subscribe({
        next: (atualizada) => {
          this.atualizarForma(atualizada);
          this.mostrandoForma.set(false);
          this.formaEmEdicao.set(null);
          this.mensagemForma.set(
            forma
              ? 'Forma de compra atualizada com sucesso.'
              : 'Forma de compra cadastrada com sucesso.',
          );
        },
        error: (error: unknown) => this.tratarErroForma(error),
      });
  }

  protected alterarStatusForma(forma: AdminFormaCompra): void {
    if (!this.insumoId || this.formasEmAtualizacao().has(forma.id)) {
      return;
    }

    const ativa = !forma.ativa;
    if (
      forma.ativa &&
      !window.confirm(
        `Inativar a forma de compra "${forma.nome}"? O histórico de aquisições será preservado.`,
      )
    ) {
      return;
    }

    this.definirStatusFormaPendente(forma.id, true);
    this.erroForma.set('');
    this.mensagemForma.set('');
    this.insumosService
      .alterarStatusFormaCompra(this.insumoId, forma.id, { ativa })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.definirStatusFormaPendente(forma.id, false)),
      )
      .subscribe({
        next: (atualizada) => {
          this.atualizarForma(atualizada);
          this.mensagemForma.set(
            ativa
              ? 'Forma de compra ativada com sucesso.'
              : 'Forma de compra inativada com sucesso.',
          );
        },
        error: (error: unknown) => this.tratarErroForma(error, 'status'),
      });
  }

  protected unidadeFormaCompra(): UnidadeFisicaEstoque[] {
    const unidadeBase = this.detalhe()?.resumo.unidadeBase ?? this.form.controls.unidadeBase.value;
    switch (unidadeBase) {
      case 'Grama':
        return ['Grama', 'Quilograma'];
      case 'Mililitro':
        return ['Mililitro', 'Litro'];
      case 'Unidade':
        return ['Unidade'];
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

  protected rotuloUnidadeBase(unidade: UnidadeBaseEstoque): string {
    switch (unidade) {
      case 'Grama':
        return 'grama (g)';
      case 'Mililitro':
        return 'mililitro (ml)';
      case 'Unidade':
        return 'unidade (un.)';
    }
  }

  protected rotuloTipo(tipo: TipoInsumoEstoque): string {
    switch (tipo) {
      case 'MateriaPrima':
        return 'Matéria-prima';
      case 'Embalagem':
        return 'Embalagem';
      case 'Outro':
        return 'Outro';
    }
  }

  protected rotuloSituacao(situacao: AdminInsumoDetalhe['resumo']['situacao']): string {
    switch (situacao) {
      case 'SemEstoque':
        return 'Sem estoque';
      case 'AbaixoOuNoMinimo':
        return 'Abaixo ou no mínimo';
      case 'Normal':
        return 'Normal';
      case 'MinimoNaoConfigurado':
        return 'Mínimo não configurado';
    }
  }

  protected formatarQuantidade(valor: number, unidade: UnidadeBaseEstoque): string {
    return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 6 }).format(valor)} ${this.rotuloUnidade(unidade)}`;
  }

  protected equivalenciaForma(): string {
    const detalhe = this.detalhe();
    const quantidade = this.formaForm.controls.quantidadeEquivalentePorEmbalagem.value;
    if (!detalhe || quantidade === null || !Number.isFinite(quantidade) || quantidade <= 0) {
      return '';
    }

    const unidade = this.formaForm.controls.unidade.value;
    const fator = unidade === 'Quilograma' || unidade === 'Litro' ? 1000 : 1;
    const equivalente = quantidade * fator;
    const rotulo = this.rotuloUnidade(detalhe.resumo.unidadeBase);
    return equivalente <= MAX_QUANTIDADE_BASE_UI
      ? `Equivale a ${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 6 }).format(equivalente)} ${rotulo} por embalagem.`
      : 'A equivalência excede a quantidade máxima permitida pelo estoque.';
  }

  protected unidadeFactorStep(unidade: UnidadeFisicaEstoque): string {
    if (unidade === 'Unidade') {
      return '1';
    }
    return unidade === 'Quilograma' || unidade === 'Litro' ? '0.000000001' : '0.000001';
  }

  protected unidadeFactorMin(unidade: UnidadeFisicaEstoque): string {
    return this.unidadeFactorStep(unidade);
  }

  protected errorField(control: AbstractControl, submitted: boolean): boolean {
    return control.invalid && (control.touched || submitted);
  }

  private definirDetalhe(detalhe: AdminInsumoDetalhe): void {
    this.detalhe.set({
      ...detalhe,
      formasCompra: this.ordenarFormas(detalhe.formasCompra),
    });
    const { resumo } = detalhe;
    this.form.setValue({
      nome: resumo.nome,
      tipo: resumo.tipo,
      unidadeBase: resumo.unidadeBase,
      estoqueMinimo: resumo.estoqueMinimo,
      controlaLote: resumo.controlaLote,
      validadeObrigatoria: resumo.validadeObrigatoria,
    });
    this.form.controls.unidadeBase.disable({ emitEvent: false });
    if (resumo.controlaLote) {
      this.form.controls.validadeObrigatoria.enable({ emitEvent: false });
    } else {
      this.form.controls.validadeObrigatoria.disable({ emitEvent: false });
    }
  }

  private politicaAlterada(request: AtualizarInsumoRequest): boolean {
    const atual = this.detalhe()?.resumo;
    return (
      !!atual &&
      (request.controlaLote !== atual.controlaLote ||
        request.validadeObrigatoria !== atual.validadeObrigatoria)
    );
  }

  private tratarErroCarregamento(error: unknown): void {
    if (this.redirecionarSessaoExpirada(error)) {
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      this.erroCarregamento.set('Você não tem permissão para acessar este insumo.');
    } else if (error instanceof HttpErrorResponse && error.status === 404) {
      this.erroCarregamento.set('Este insumo não foi encontrado. Atualize a listagem.');
    } else {
      this.erroCarregamento.set('Não foi possível carregar o insumo. Tente novamente.');
    }
  }

  private tratarErroFormulario(
    error: unknown,
    politicaAlterada: boolean,
    operacao: 'cadastro' | 'status' = 'cadastro',
  ): void {
    if (this.redirecionarSessaoExpirada(error)) {
      return;
    }
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        this.erroFormulario.set(
          'Os dados informados não são válidos. Revise os campos e tente novamente.',
        );
        return;
      }
      if (error.status === 403) {
        this.erroFormulario.set('Você não tem permissão para executar esta operação.');
        return;
      }
      if (error.status === 404) {
        this.erroFormulario.set('Este insumo não está mais disponível. Atualize a listagem.');
        return;
      }
      if (error.status === 409 && politicaAlterada) {
        this.erroFormulario.set(
          'Não é possível alterar o controle por lote ou a exigência de validade porque este insumo já possui lotes, saldos ou movimentações. Os dados digitados foram mantidos.',
        );
        return;
      }
      if (error.status === 409) {
        this.erroFormulario.set(
          operacao === 'status'
            ? 'O status do insumo mudou em outra operação. Atualize os dados e tente novamente.'
            : 'Não foi possível salvar: o cadastro pode ter sido alterado em outra operação. Revise os dados.',
        );
        return;
      }
    }
    this.erroFormulario.set('Não foi possível salvar as alterações. Tente novamente.');
  }

  private tratarErroForma(error: unknown, operacao: 'cadastro' | 'status' = 'cadastro'): void {
    if (this.redirecionarSessaoExpirada(error)) {
      return;
    }
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        this.erroForma.set(
          'Confira o nome, o fator positivo e a unidade compatível com este insumo.',
        );
        return;
      }
      if (error.status === 403) {
        this.erroForma.set('Você não tem permissão para alterar formas de compra.');
        return;
      }
      if (error.status === 404) {
        this.erroForma.set('O insumo ou a forma de compra não foi encontrado. Atualize os dados.');
        return;
      }
      if (error.status === 409) {
        this.erroForma.set(
          operacao === 'status'
            ? 'A forma de compra mudou em outra operação. Atualize os dados e tente novamente.'
            : 'Já existe uma forma com esse nome ou a alteração conflitou com outra operação.',
        );
        return;
      }
    }
    this.erroForma.set('Não foi possível salvar a forma de compra. Tente novamente.');
  }

  private atualizarForma(forma: AdminFormaCompra): void {
    this.detalhe.update((detalhe) => {
      if (!detalhe) {
        return detalhe;
      }
      const existe = detalhe.formasCompra.some((item) => item.id === forma.id);
      const formas = existe
        ? detalhe.formasCompra.map((item) => (item.id === forma.id ? forma : item))
        : [...detalhe.formasCompra, forma];
      return { ...detalhe, formasCompra: this.ordenarFormas(formas) };
    });
  }

  private ordenarFormas(formas: AdminFormaCompra[]): AdminFormaCompra[] {
    return [...formas].sort(
      (a, b) => Number(b.ativa) - Number(a.ativa) || a.nome.localeCompare(b.nome, 'pt-BR'),
    );
  }

  private definirStatusFormaPendente(id: string, pendente: boolean): void {
    this.formasEmAtualizacao.update((ids) => {
      const atualizados = new Set(ids);
      if (pendente) {
        atualizados.add(id);
      } else {
        atualizados.delete(id);
      }
      return atualizados;
    });
  }

  protected formaPendente(id: string): boolean {
    return this.formasEmAtualizacao().has(id);
  }

  private redirecionarSessaoExpirada(error: unknown): boolean {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return true;
    }
    return false;
  }
}
