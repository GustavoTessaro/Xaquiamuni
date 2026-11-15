import { Component } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrlCurrencyInputComponent } from './brl-currency-input.component';

@Component({
  imports: [BrlCurrencyInputComponent, ReactiveFormsModule],
  standalone: true,
  template: `
    <label for="price-input">Preço</label>
    <app-brl-currency-input inputId="price-input" [formControl]="control" [required]="true" />
  `,
})
class CurrencyInputHostComponent {
  readonly control = new FormControl<number | null>(null);
}

describe('BrlCurrencyInputComponent', () => {
  let fixture: ComponentFixture<CurrencyInputHostComponent>;
  let control: FormControl<number | null>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CurrencyInputHostComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(CurrencyInputHostComponent);
    control = fixture.componentInstance.control;
    fixture.detectChanges();
  });

  it('formats digit entry as Brazilian reais and emits a numeric value', () => {
    enter('1500');

    expect(input().value).toBe('R$ 15,00');
    expect(control.value).toBe(15);
  });

  it('accepts a pasted Brazilian amount and groups thousands', () => {
    enter('R$ 1.500,25');

    expect(input().value).toBe('R$ 1.500,25');
    expect(control.value).toBe(1500.25);
  });

  it('formats existing values, supports editing and clears the control on deletion', () => {
    control.setValue(1500.25);
    fixture.detectChanges();
    expect(input().value).toBe('R$ 1.500,25');

    enter('1999');
    expect(input().value).toBe('R$ 19,99');
    expect(control.value).toBe(19.99);

    enter('');
    expect(input().value).toBe('');
    expect(control.value).toBeNull();
  });

  it('keeps negative existing values visibly negative', () => {
    control.setValue(-5);
    fixture.detectChanges();

    expect(input().value).toBe('R$ -5,00');
    expect(control.value).toBe(-5);
  });

  it('does not round values that cannot be represented to cent precision', () => {
    enter('1000000000000000001');

    expect(control.value).toBeNull();
    expect(control.hasError('currencyPrecision')).toBe(true);
    expect(input().value).toBe('R$ 10.000.000.000.000.000,01');
  });

  it('uses a text decimal input and preserves touched, dirty and disabled states', () => {
    expect(input().type).toBe('text');
    expect(input().getAttribute('inputmode')).toBe('decimal');
    expect(control.pristine).toBe(true);

    enter('1500');
    input().dispatchEvent(new Event('blur', { bubbles: true }));
    fixture.detectChanges();

    expect(control.dirty).toBe(true);
    expect(control.touched).toBe(true);

    control.disable();
    fixture.detectChanges();
    expect(input().disabled).toBe(true);
  });

  function input(): HTMLInputElement {
    return fixture.nativeElement.querySelector('#price-input') as HTMLInputElement;
  }

  function enter(value: string): void {
    input().value = value;
    input().setSelectionRange(value.length, value.length);
    input().dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }
});
