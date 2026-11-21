import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AdminProdutoCaracteristica,
  AdminProdutoVariante,
  AdminProdutoVarianteRequest,
} from '../../core/admin-produtos/produtos.service';
import { BrlCurrencyInputComponent } from './brl-currency-input.component';
import {
  converterMedida,
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
  peso: FormControl<string>;
  volume: FormControl<string>;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrlCurrencyInputComponent, ReactiveFormsModule],
  selector: 'app-admin-produto-variante-form',
  templateUrl: './admin-produto-variante-form.component.html',
})
export class AdminProdutoVarianteFormComponent {
  readonly variante = input<AdminProdutoVariante | null>(null);
  readonly caracteristicas = input<AdminProdutoCaracteristica[]>([]);
  readonly salvando = input(false);
  readonly mensagemErro = input('');
  readonly salvar = output<AdminProdutoVarianteRequest>();
  readonly cancelar = output<void>();
  protected readonly enviado = signal(false);
  protected readonly camposMedida = ['peso', 'volume'] as const;
  protected readonly unidadesVenda = UNIDADES_VENDA;
  protected readonly selecoes = signal<Record<string, string>>({});
  protected readonly medidasVinculadas = computed(() => {
    const selecoes = this.selecoes();
    const valor = (tipo: 'PesoLiquido' | 'VolumeLiquido') => {
      const caracteristica = this.caracteristicas().find((item) => item.tipoMedidaFisica === tipo);
      return caracteristica?.valores.find((item) => item.id === selecoes[caracteristica.id])
        ?.valorMedidaCanonico ?? null;
    };
    return { peso: valor('PesoLiquido'), volume: valor('VolumeLiquido') };
  });
  protected readonly form = new FormGroup<VarianteControls>({
    identificacao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
    }),
    peso: new FormControl('', { nonNullable: true }),
    volume: new FormControl('', { nonNullable: true }),
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
        peso: variante?.pesoLiquidoGramas?.toString() ?? '',
        volume: variante?.volumeLiquidoMililitros?.toString() ?? '',
      });
      this.selecoes.set(Object.fromEntries((variante?.caracteristicas ?? []).map((item) =>
        [item.caracteristicaId, item.valorId])));
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

  protected selecionar(id: string, event: Event): void {
    const valor = (event.target as HTMLSelectElement).value;
    this.selecoes.update((selecoes) => ({ ...selecoes, [id]: valor }));
  }

  protected medidaInvalida(campo: 'peso' | 'volume'): boolean {
    const valor = this.form.controls[campo].value;
    return this.medidasVinculadas()[campo] === null && !!valor.trim() &&
      converterMedida(valor, campo === 'peso' ? 'G' : 'ML') === null;
  }

  protected submit(): void {
    if (this.salvando()) {
      return;
    }
    this.enviado.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid || this.medidaInvalida('peso') || this.medidaInvalida('volume')) {
      return;
    }
    const { identificacao, unidadeVenda, precoVenda } = this.form.getRawValue();
    if (precoVenda === null) {
      return;
    }
    const peso = this.medidasVinculadas().peso ?? converterMedida(this.form.controls.peso.value, 'G');
    const volume = this.medidasVinculadas().volume ?? converterMedida(this.form.controls.volume.value, 'ML');
    const variante = this.variante();
    const caracteristicas = this.caracteristicas().flatMap(({ id }) => {
      const valorId = this.selecoes()[id];
      return valorId ? [{ caracteristicaId: id, valorId }] : [];
    });
    const anteriores = variante?.caracteristicas ?? [];
    const mudouCombinacao = caracteristicas.length !== anteriores.length ||
      anteriores.some(({ caracteristicaId, valorId }) => this.selecoes()[caracteristicaId] !== valorId);
    const mudouMedidas = peso !== (variante?.pesoLiquidoGramas ?? null) ||
      volume !== (variante?.volumeLiquidoMililitros ?? null);
    this.salvar.emit({
      identificacao: identificacao.trim(),
      unidadeVenda: unidadeVenda.trim().toUpperCase(),
      precoVenda,
      ...(mudouCombinacao ? { caracteristicas } : {}),
      ...(mudouMedidas || (mudouCombinacao && (peso !== null || volume !== null))
        ? { pesoLiquidoGramas: peso, volumeLiquidoMililitros: volume, atualizarMedidas: true }
        : {}),
    });
  }
}
