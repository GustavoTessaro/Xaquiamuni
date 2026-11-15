import { ChangeDetectionStrategy, Component, effect, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AdminProdutoVariante,
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

type VarianteControls = {
  identificacao: FormControl<string>;
  unidadeVenda: FormControl<string>;
  precoVenda: FormControl<number | null>;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrlCurrencyInputComponent, ReactiveFormsModule],
  selector: 'app-admin-produto-variante-form',
  templateUrl: './admin-produto-variante-form.component.html',
})
export class AdminProdutoVarianteFormComponent {
  readonly variante = input<AdminProdutoVariante | null>(null);
  readonly salvando = input(false);
  readonly mensagemErro = input('');
  readonly salvar = output<AdminProdutoVarianteRequest>();
  readonly cancelar = output<void>();
  protected readonly enviado = signal(false);
  protected readonly unidadesVenda = UNIDADES_VENDA;
  protected readonly form = new FormGroup<VarianteControls>({
    identificacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
    }),
    unidadeVenda: new FormControl('UN', {
      nonNullable: true,
      validators: [Validators.required, validSaleUnit],
    }),
    precoVenda: new FormControl<number | null>(null, {
      validators: [
        Validators.required,
        Validators.min(0.01),
        Validators.max(MAX_PRECO_VARIANTE),
        currencyPrecision,
      ],
    }),
  });

  constructor() {
    effect(() => {
      const variante = this.variante();
      this.enviado.set(false);
      this.form.reset({
        identificacao: variante?.identificacao ?? '',
        unidadeVenda: variante?.unidadeVenda ?? 'UN',
        precoVenda: variante?.precoVenda ?? null,
      });
      this.form.controls.unidadeVenda.setValidators([
        Validators.required,
        (control) => validSaleUnit(control, variante?.unidadeVenda),
      ]);
      this.form.controls.unidadeVenda.updateValueAndValidity({ emitEvent: false });
    });
  }

  protected erroCampo(field: keyof VarianteControls): string {
    const control = this.form.controls[field];
    if (!control.invalid || (!control.touched && !this.enviado())) {
      return '';
    }
    if (control.hasError('currencyPrecision')) {
      return 'O preço não pode ser representado com precisão de centavos. Informe outro valor.';
    }
    if (control.hasError('invalidSaleUnit')) {
      return 'Selecione uma das seis unidades comerciais disponíveis.';
    }
    if (control.hasError('required') || control.hasError('whitespace')) {
      return field === 'identificacao'
        ? 'Informe uma identificação para a opção.'
        : field === 'unidadeVenda'
          ? 'Selecione a unidade de venda.'
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

  protected unidadeLegada(unidade: string): boolean {
    return !this.unidadesVenda.some((item) => item === unidade);
  }

  protected submit(): void {
    if (this.salvando()) {
      return;
    }
    this.enviado.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    const { identificacao, unidadeVenda, precoVenda } = this.form.getRawValue();
    if (precoVenda === null) {
      return;
    }
    this.salvar.emit({
      identificacao: identificacao.trim(),
      unidadeVenda: unidadeVenda.trim().toUpperCase(),
      precoVenda,
    });
  }
}
