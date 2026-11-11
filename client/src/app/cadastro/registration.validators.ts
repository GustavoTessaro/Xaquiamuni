import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

const brazilianStates = new Set([
  'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS',
  'MG', 'PA', 'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC',
  'SP', 'SE', 'TO',
]);

const digitsOnly = (value: string): string => value.replace(/\D/g, '');
const normalizedText = (value: string): string => value.trim().replace(/\s+/g, ' ');

export const brazilianCpfValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const cpf = digitsOnly(String(control.value ?? ''));
  if (cpf.length !== 11 || /^(\d)\1{10}$/.test(cpf)) {
    return { cpf: true };
  }

  const calculateDigit = (slice: string, factor: number): number => {
    const sum = [...slice].reduce((total, digit) => total + Number(digit) * factor--, 0);
    const remainder = sum % 11;
    return remainder < 2 ? 0 : 11 - remainder;
  };

  const firstDigit = calculateDigit(cpf.slice(0, 9), 10);
  const secondDigit = calculateDigit(cpf.slice(0, 10), 11);
  return firstDigit === Number(cpf[9]) && secondDigit === Number(cpf[10])
    ? null
    : { cpf: true };
};

export const brazilianPhoneValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  let phone = digitsOnly(String(control.value ?? ''));
  if (phone.startsWith('55') && (phone.length === 12 || phone.length === 13)) {
    phone = phone.slice(2);
  }
  if (phone.length !== 10 && phone.length !== 11) {
    return { brazilianPhone: true };
  }

  const areaCode = Number(phone.slice(0, 2));
  const subscriberStart = phone[2];
  const validSubscriber = phone.length === 11
    ? subscriberStart === '9'
    : subscriberStart >= '2' && subscriberStart <= '5';
  return areaCode >= 11 && areaCode <= 99 && validSubscriber
    ? null
    : { brazilianPhone: true };
};

export const brazilianUfValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null =>
  brazilianStates.has(String(control.value ?? '').trim().toUpperCase())
    ? null
    : { brazilianUf: true };

export const cepValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null =>
  /^\d{8}$/.test(digitsOnly(String(control.value ?? '')))
    ? null
    : { cep: true };

export const normalizedTextValidator = (maxLength: number): ValidatorFn => (
  control: AbstractControl,
): ValidationErrors | null => {
  const normalized = normalizedText(String(control.value ?? ''));
  if (normalized.length === 0) {
    return { required: true };
  }
  return normalized.length <= maxLength
    ? null
    : { maxlength: { requiredLength: maxLength, actualLength: normalized.length } };
};

export const optionalNormalizedTextValidator = (maxLength: number): ValidatorFn => (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = String(control.value ?? '');
  const normalized = normalizedText(value);
  return normalized.length <= maxLength
    ? null
    : { maxlength: { requiredLength: maxLength, actualLength: normalized.length } };
};

export const brazilianEmailValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const email = String(control.value ?? '').trim();
  if (email.length === 0) {
    return { required: true };
  }
  if (
    email.length > 256 ||
    !/^[^\s@]+@[^\s@.]+(?:\.[^\s@.]+)+$/.test(email)
  ) {
    return { email: true };
  }
  return null;
};

export const passwordsMatchValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const password = control.get('senha')?.value;
  const confirmation = control.get('confirmacaoSenha')?.value;
  return password === confirmation ? null : { passwordsMismatch: true };
};

export const normalizeRegistrationDigits = (value: string): string => digitsOnly(value);
