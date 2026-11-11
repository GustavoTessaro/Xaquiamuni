import { FormControl, FormGroup } from '@angular/forms';
import {
  brazilianCpfValidator,
  brazilianEmailValidator,
  brazilianPhoneValidator,
  brazilianUfValidator,
  cepValidator,
  identityPasswordValidator,
  normalizedTextValidator,
  normalizeRegistrationDigits,
  optionalNormalizedTextValidator,
  passwordsMatchValidator,
  registrationPasswordRequirements,
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

  it('matches the configured Identity password rules', () => {
    expect(identityPasswordValidator(new FormControl('shortA1!'))).not.toBeNull();
    expect(identityPasswordValidator(new FormControl('Senha Forte 123!'))).toBeNull();
    expect(identityPasswordValidator(new FormControl(`A${'a'.repeat(9)}1!`))).toBeNull();
    expect(identityPasswordValidator(new FormControl(`A${'a'.repeat(253)}1!`))).toBeNull();
    expect(identityPasswordValidator(new FormControl(`A${'a'.repeat(254)}1!`))).toEqual({
      passwordMaxLength: true,
    });
  });

  it('evaluates supplementary Unicode characters as UTF-16 code units like Identity', () => {
    const supplementaryUppercase = String.fromCodePoint(0x10400);
    const supplementaryLowercase = String.fromCodePoint(0x10428);
    const supplementaryDigit = String.fromCodePoint(0x1d7d9);
    const password = `${supplementaryUppercase}${supplementaryLowercase}${supplementaryDigit}`;
    const requirementMet = (id: string): boolean =>
      registrationPasswordRequirements.find((requirement) => requirement.id === id)!.isMet(password);

    expect(requirementMet('uppercase')).toBe(false);
    expect(requirementMet('lowercase')).toBe(false);
    expect(requirementMet('digit')).toBe(false);
    expect(requirementMet('nonAlphanumeric')).toBe(true);
    expect(identityPasswordValidator(new FormControl(password))).toEqual({
      passwordMinLength: true,
      passwordUppercase: true,
      passwordLowercase: true,
      passwordDigit: true,
    });
  });

  it('measures supplementary characters by their UTF-16 length for Identity limits', () => {
    const supplementaryLetter = String.fromCodePoint(0x10400);
    const supplementarySymbol = String.fromCodePoint(0x1f680);
    const minLength = registrationPasswordRequirements.find(
      (requirement) => requirement.id === 'minLength',
    )!;
    const maxLength = registrationPasswordRequirements.find(
      (requirement) => requirement.id === 'maxLength',
    )!;

    expect(minLength.isMet(supplementaryLetter.repeat(5))).toBe(false);
    expect(minLength.isMet(supplementaryLetter.repeat(6))).toBe(true);
    expect(maxLength.isMet(supplementarySymbol.repeat(128))).toBe(true);
    expect(maxLength.isMet(supplementarySymbol.repeat(129))).toBe(false);
  });

  it('reports each password requirement without exposing the password', () => {
    const incompletePassword = 'curta';
    const failedRequirements = registrationPasswordRequirements
      .filter((requirement) => !requirement.isMet(incompletePassword))
      .map((requirement) => requirement.id);

    expect(failedRequirements).toEqual([
      'minLength',
      'uppercase',
      'digit',
      'nonAlphanumeric',
    ]);
    expect(identityPasswordValidator(new FormControl('Senha Forte 123!'))).toBeNull();
  });
});
