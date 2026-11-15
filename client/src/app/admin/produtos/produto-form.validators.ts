import { AbstractControl, ValidationErrors } from '@angular/forms';

export const MAX_PRECO_VARIANTE = 9_999_999_999_999_998;

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
