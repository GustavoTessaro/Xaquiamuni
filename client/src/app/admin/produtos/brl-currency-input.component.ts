import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  forwardRef,
  inject,
  input,
} from '@angular/core';
import {
  AbstractControl,
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator,
} from '@angular/forms';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => BrlCurrencyInputComponent),
      multi: true,
    },
    {
      provide: NG_VALIDATORS,
      useExisting: forwardRef(() => BrlCurrencyInputComponent),
      multi: true,
    },
  ],
  selector: 'app-brl-currency-input',
  template: `
    <input
      [id]="inputId()"
      [value]="displayValue"
      [disabled]="disabled"
      [required]="required()"
      [attr.aria-invalid]="invalid() ? 'true' : null"
      [attr.aria-describedby]="describedBy()"
      type="text"
      inputmode="decimal"
      autocomplete="off"
      placeholder="R$ 0,00"
      (input)="handleInput($event)"
      (blur)="handleBlur()"
      class="min-h-11 w-full rounded-xl border border-slate-300 bg-white px-3 text-slate-900 shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-800 disabled:cursor-not-allowed disabled:bg-slate-100"
    />
  `,
})
export class BrlCurrencyInputComponent implements ControlValueAccessor, Validator {
  private readonly changeDetectorRef = inject(ChangeDetectorRef);
  readonly inputId = input.required<string>();
  readonly invalid = input(false);
  readonly describedBy = input<string | null>(null);
  readonly required = input(false);

  protected displayValue = '';
  protected disabled = false;
  private precisionError = false;
  private onChange: (value: number | null) => void = () => {};
  private onTouched: () => void = () => {};
  private onValidatorChange: () => void = () => {};

  writeValue(value: unknown): void {
    if (value === null || value === undefined) {
      this.displayValue = '';
      this.setPrecisionError(false);
      this.changeDetectorRef.markForCheck();
      return;
    }

    if (typeof value !== 'number' || !Number.isFinite(value)) {
      this.displayValue = '';
      this.setPrecisionError(true);
      this.changeDetectorRef.markForCheck();
      return;
    }

    const decimal = value.toFixed(2);
    const hasCentPrecision = Number(decimal) === value;
    this.displayValue =
      value >= 0 && hasCentPrecision
        ? this.formatDigits(decimal.replace(/\D/g, ''))
        : `R$ ${hasCentPrecision ? decimal.replace('.', ',') : value.toString().replace('.', ',')}`;
    this.setPrecisionError(!hasCentPrecision);
    this.changeDetectorRef.markForCheck();
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  registerOnValidatorChange(fn: () => void): void {
    this.onValidatorChange = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
    this.changeDetectorRef.markForCheck();
  }

  validate(_control: AbstractControl): ValidationErrors | null {
    return this.precisionError ? { currencyPrecision: true } : null;
  }

  protected handleInput(event: Event): void {
    const inputElement = event.currentTarget as HTMLInputElement;
    const rawValue = inputElement.value;
    const caret = inputElement.selectionStart ?? rawValue.length;
    const digitsBeforeCaret = rawValue.slice(0, caret).replace(/\D/g, '').length;
    const digits = rawValue.replace(/\D/g, '').replace(/^0+(?=\d)/, '');
    const formatted = digits ? this.formatDigits(digits) : '';
    const parsed = this.parseDigits(digits);

    this.displayValue = formatted;
    inputElement.value = formatted;
    this.setPrecisionError(parsed.precisionError);
    this.onChange(parsed.value);

    const leadingPadding = Math.max(0, 3 - digits.length);
    const targetDigit = Math.min(digitsBeforeCaret + leadingPadding, this.countDigits(formatted));
    const targetPosition = this.positionAfterDigits(formatted, targetDigit);
    inputElement.setSelectionRange(targetPosition, targetPosition);
  }

  protected handleBlur(): void {
    this.onTouched();
  }

  private parseDigits(digits: string): { value: number | null; precisionError: boolean } {
    if (!digits) {
      return { value: null, precisionError: false };
    }

    const normalized = digits.replace(/^0+(?=\d)/, '');
    const whole = normalized.length > 2 ? normalized.slice(0, -2) : '0';
    const fraction = normalized.slice(-2).padStart(2, '0');
    const decimal = `${whole}.${fraction}`;
    const value = Number(decimal);
    const precisionError = !Number.isFinite(value) || value.toFixed(2) !== decimal;

    return { value: precisionError ? null : value, precisionError };
  }

  private formatDigits(digits: string): string {
    const normalized = digits.replace(/^0+(?=\d)/, '');
    const whole = normalized.length > 2 ? normalized.slice(0, -2) : '0';
    const fraction = normalized.slice(-2).padStart(2, '0');
    const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    return `R$ ${grouped},${fraction}`;
  }

  private countDigits(value: string): number {
    return value.replace(/\D/g, '').length;
  }

  private positionAfterDigits(value: string, digitCount: number): number {
    if (digitCount === 0) {
      return 0;
    }

    let seen = 0;
    for (let index = 0; index < value.length; index += 1) {
      if (/\d/.test(value[index])) {
        seen += 1;
        if (seen === digitCount) {
          return index + 1;
        }
      }
    }

    return value.length;
  }

  private setPrecisionError(value: boolean): void {
    if (this.precisionError === value) {
      return;
    }
    this.precisionError = value;
    this.onValidatorChange();
  }
}
