import { FormControl, FormGroup } from '@angular/forms';
import {
  brazilianCpfValidator,
  brazilianEmailValidator,
  brazilianPhoneValidator,
  brazilianUfValidator,
  cepValidator,
  normalizedTextValidator,
  normalizeRegistrationDigits,
  optionalNormalizedTextValidator,
  passwordsMatchValidator,
} from './registration.validators';

describe('registration validators', () => {
  it('accepts a valid CPF with or without presentation punctuation', () => {
    expect(brazilianCpfValidator(new FormControl('529.982.247-25'))).toBeNull();
    expect(brazilianCpfValidator(new FormControl('52998224725'))).toBeNull();
  });

  it('rejects invalid and repeated-digit CPFs', () => {
    expect(brazilianCpfValidator(new FormControl('529.982.247-24'))).toEqual({ cpf: true });
    expect(brazilianCpfValidator(new FormControl('111.111.111-11'))).toEqual({ cpf: true });
  });

  it('validates Brazilian mobile and landline numbers', () => {
    expect(brazilianPhoneValidator(new FormControl('(11) 91234-5678'))).toBeNull();
    expect(brazilianPhoneValidator(new FormControl('+55 (11) 3234-5678'))).toBeNull();
    expect(brazilianPhoneValidator(new FormControl('(00) 11234-5678'))).toEqual({
      brazilianPhone: true,
    });
  });

  it('checks email, state and postal code formats', () => {
    expect(brazilianEmailValidator(new FormControl('ana@example.com'))).toBeNull();
    expect(brazilianEmailValidator(new FormControl('ana@example'))).toEqual({ email: true });
    expect(brazilianUfValidator(new FormControl('rs'))).toBeNull();
    expect(brazilianUfValidator(new FormControl('XX'))).toEqual({ brazilianUf: true });
    expect(cepValidator(new FormControl('95000-000'))).toBeNull();
    expect(cepValidator(new FormControl('9500-000'))).toEqual({ cep: true });
  });

  it('normalizes masked CPF, telephone and postal-code values to digits', () => {
    expect(normalizeRegistrationDigits('529.982.247-25')).toBe('52998224725');
    expect(normalizeRegistrationDigits('+55 (11) 91234-5678')).toBe('5511912345678');
    expect(normalizeRegistrationDigits('95000-000')).toBe('95000000');
  });

  it('enforces required text and backend length limits while allowing an empty complement', () => {
    expect(normalizedTextValidator(5)(new FormControl('  '))).toEqual({ required: true });
    expect(normalizedTextValidator(5)(new FormControl(' Ana  Silva '))).toEqual({
      maxlength: { requiredLength: 5, actualLength: 9 },
    });
    expect(optionalNormalizedTextValidator(100)(new FormControl('  '))).toBeNull();
  });

  it('requires matching password confirmation', () => {
    const passwords = new FormGroup({
      senha: new FormControl('Senha segura'),
      confirmacaoSenha: new FormControl('Outra senha'),
    }, { validators: [passwordsMatchValidator] });

    expect(passwords.errors).toEqual({ passwordsMismatch: true });
    passwords.controls.confirmacaoSenha.setValue('Senha segura');
    expect(passwords.errors).toBeNull();
  });
});
