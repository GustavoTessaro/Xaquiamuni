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
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { AdminCategoria } from '../../core/admin-categorias/categorias.service';
import {
  AdminProduto,
  AdminProdutoCadastroRequest,
  AdminProdutoEdicaoRequest,
  AdminProdutoVarianteRequest,
} from '../../core/admin-produtos/produtos.service';
import { currencyPrecision, MAX_PRECO_VARIANTE, nonWhitespace } from './produto-form.validators';

type ProdutoControls = {
  nome: FormControl<string>;
  descricao: FormControl<string>;
  categoriaId: FormControl<string>;
};

type VarianteControls = {
  identificacao: FormControl<string>;
  unidadeVenda: FormControl<string>;
  precoVenda: FormControl<number | null>;
};

export type ProdutoFormSubmission =
  | { mode: 'create'; request: AdminProdutoCadastroRequest }
  | { mode: 'edit'; request: AdminProdutoEdicaoRequest };

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
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
  });
  protected readonly variantesAdicionais = new FormArray<FormGroup<VarianteControls>>([]);

  constructor() {
    effect(() => {
      const produto = this.produto();
      this.enviado.set(false);
      this.produtoForm.reset({
        nome: produto?.nome ?? '',
        descricao: produto?.descricao ?? '',
        categoriaId: produto?.categoriaId ?? '',
      });
      this.variantePadraoForm.reset({
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: null,
      });
      this.variantesAdicionais.clear();
    });
  }

  protected adicionarVariante(): void {
    this.variantesAdicionais.push(
      this.criarFormularioVariante({
        identificacao: '',
        unidadeVenda: 'UN',
        precoVenda: null,
      }),
    );
  }

  protected removerVariante(index: number): void {
    this.variantesAdicionais.removeAt(index);
  }

  protected erroCampo(form: AbstractControl, field: string): string {
    const control = form.get(field);
    if (!control?.invalid || (!control.touched && !this.enviado())) {
      return '';
    }

    if (control.hasError('required') || control.hasError('whitespace')) {
      return field === 'nome'
        ? 'Informe o nome do produto.'
        : field === 'categoriaId'
          ? 'Selecione uma categoria ativa.'
          : field === 'identificacao'
            ? 'Informe a identificação da variante.'
            : field === 'unidadeVenda'
              ? 'Informe a unidade de venda.'
              : 'Informe o preço de venda.';
    }
    if (control.hasError('maxlength')) {
      return field === 'nome'
        ? 'O nome deve ter no máximo 200 caracteres.'
        : field === 'descricao'
          ? 'A descrição deve ter no máximo 4.000 caracteres.'
          : field === 'identificacao'
            ? 'A identificação deve ter no máximo 100 caracteres.'
            : 'A unidade deve ter no máximo 10 caracteres.';
    }
    if (control.hasError('min')) {
      return 'O preço deve ser maior que zero.';
    }
    if (control.hasError('max')) {
      return 'O preço excede o limite aceito.';
    }
    if (control.hasError('currencyPrecision')) {
      return 'Informe o preço com no máximo duas casas decimais.';
    }
    return 'Revise este campo.';
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
    if (this.produto()) {
      this.salvar.emit({
        mode: 'edit',
        request: { nome: nome.trim(), descricao: normalizedDescription, categoriaId },
      });
      return;
    }

    this.variantePadraoForm.markAllAsTouched();
    this.variantesAdicionais.controls.forEach((variante) => variante.markAllAsTouched());
    if (this.variantePadraoForm.invalid || this.variantesAdicionais.invalid) {
      return;
    }

    const defaultVariant = this.toVariantRequest(this.variantePadraoForm);
    const variants = this.variantesAdicionais.controls.map((variante) =>
      this.toVariantRequest(variante),
    );
    this.salvar.emit({
      mode: 'create',
      request: {
        nome: nome.trim(),
        descricao: normalizedDescription,
        categoriaId,
        variantePadrao: defaultVariant,
        variantes: variants,
      },
    });
  }

  private criarFormularioVariante(initial: {
    identificacao: string;
    unidadeVenda: string;
    precoVenda: number | null;
  }): FormGroup<VarianteControls> {
    return new FormGroup<VarianteControls>({
      identificacao: new FormControl(initial.identificacao, {
        nonNullable: true,
        validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
      }),
      unidadeVenda: new FormControl(initial.unidadeVenda, {
        nonNullable: true,
        validators: [Validators.required, Validators.maxLength(10)],
      }),
      precoVenda: new FormControl<number | null>(initial.precoVenda, {
        validators: [
          Validators.required,
          Validators.min(0.01),
          Validators.max(MAX_PRECO_VARIANTE),
          currencyPrecision,
        ],
      }),
    });
  }

  private toVariantRequest(form: FormGroup<VarianteControls>): AdminProdutoVarianteRequest {
    const { identificacao, unidadeVenda, precoVenda } = form.getRawValue();
    return {
      identificacao: identificacao.trim(),
      unidadeVenda: unidadeVenda.trim().toUpperCase(),
      precoVenda: precoVenda!,
    };
  }
}
