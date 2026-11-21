import { AbstractControl, ValidationErrors } from '@angular/forms';

export const MAX_PRECO_VARIANTE = 9_999_999_999_999_998;
export const UNIDADES_VENDA = ['UN', 'KIT', 'PAR', 'PCT', 'CX', 'CJ'] as const;

export function nonWhitespace(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { whitespace: true };
}

export function currencyPrecision(
  control: AbstractControl<number | null>,
): ValidationErrors | null {
  const value = control.value;
  if (value === null || !Number.isFinite(value)) {
    return null;
  }
  return Number(value.toFixed(2)) === value ? null : { currencyPrecision: true };
}

export function validSaleUnit(
  control: AbstractControl<string>,
  preservedLegacyUnit?: string,
): ValidationErrors | null {
  const value = control.value.trim().toUpperCase();
  return UNIDADES_VENDA.some((unit) => unit === value) || value === preservedLegacyUnit
    ? null
    : { invalidSaleUnit: true };
}

export function converterMedida(valor: string, unidade: 'G' | 'KG' | 'ML' | 'L'): number | null {
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
