export interface DecimalText {
  text: string;
  coefficient: bigint;
  scale: number;
}

const MAX_ESTOQUE_BASE_MICROUNITS = 999_999_999_999_999_999n;
const MAX_CUSTO_CENTAVOS = 999_999_999_999_999_999n;

export function parseDecimalText(value: string): DecimalText | null {
  const input = value.trim();
  if (!input || input.length > 64 || !/^[\d.,]+$/.test(input)) {
    return null;
  }

  const commaIndex = input.indexOf(',');
  if (commaIndex !== -1 && commaIndex !== input.lastIndexOf(',')) {
    return null;
  }

  let integerText: string;
  let fractionText = '';
  if (commaIndex !== -1) {
    integerText = input.slice(0, commaIndex);
    fractionText = input.slice(commaIndex + 1);
    if (!/^\d+$/.test(fractionText) || !validBrazilianInteger(integerText)) {
      return null;
    }
    integerText = integerText.replace(/\./g, '');
  } else {
    const decimalIndex = input.indexOf('.');
    if (decimalIndex !== -1 && decimalIndex !== input.lastIndexOf('.')) {
      return null;
    }
    integerText = decimalIndex === -1 ? input : input.slice(0, decimalIndex);
    fractionText = decimalIndex === -1 ? '' : input.slice(decimalIndex + 1);
    if (!/^\d*$/.test(integerText) || !/^\d*$/.test(fractionText)) {
      return null;
    }
  }

  if (!integerText && !fractionText) {
    return null;
  }

  const normalizedInteger = (integerText || '0').replace(/^0+(?=\d)/, '');
  const digits = `${normalizedInteger}${fractionText}`;
  const coefficient = BigInt(digits || '0');
  const text = fractionText ? `${normalizedInteger}.${fractionText}` : normalizedInteger;
  return { text, coefficient, scale: fractionText.length };
}

export function decimalTextFromNumber(value: number): string | null {
  if (!Number.isFinite(value) || value < 0) {
    return null;
  }

  const raw = value.toString();
  const exponentIndex = raw.search(/[eE]/);
  if (exponentIndex === -1) {
    return raw;
  }

  const mantissa = raw.slice(0, exponentIndex);
  const exponent = Number(raw.slice(exponentIndex + 1));
  const point = mantissa.indexOf('.');
  const integerLength = point === -1 ? mantissa.length : point;
  const digits = mantissa.replace('.', '');
  const shiftedPoint = integerLength + exponent;
  if (shiftedPoint <= 0) {
    return `0.${'0'.repeat(-shiftedPoint)}${digits}`;
  }
  if (shiftedPoint >= digits.length) {
    return `${digits}${'0'.repeat(shiftedPoint - digits.length)}`;
  }
  return `${digits.slice(0, shiftedPoint)}.${digits.slice(shiftedPoint)}`;
}

export function multiplyDecimalByPowerOfTen(value: string, power: number): string | null {
  const parsed = parseDecimalText(value);
  if (!parsed || !Number.isInteger(power) || power < 0 || power > 9) {
    return null;
  }

  const remainingScale = parsed.scale - power;
  if (remainingScale >= 0) {
    return decimalFromParts(parsed.coefficient, remainingScale);
  }
  return decimalFromParts(parsed.coefficient * 10n ** BigInt(-remainingScale), 0);
}

export function multiplyDecimalByInteger(value: string, integer: string): string | null {
  const parsed = parseDecimalText(value);
  if (!parsed || !/^[1-9]\d*$/.test(integer)) {
    return null;
  }
  return decimalFromParts(parsed.coefficient * BigInt(integer), parsed.scale);
}

export function isPositiveIntegerText(value: string): boolean {
  const parsed = parseDecimalText(value);
  if (!parsed) {
    return false;
  }
  const normalized = removeTrailingZeros(parsed);
  return normalized.coefficient > 0n && normalized.scale === 0;
}

export function isValidBaseQuantity(value: string, isCountUnit: boolean): boolean {
  return isValidBaseQuantityWithMinimum(value, isCountUnit, false);
}

export function isValidNonNegativeBaseQuantity(value: string, isCountUnit: boolean): boolean {
  return isValidBaseQuantityWithMinimum(value, isCountUnit, true);
}

function isValidBaseQuantityWithMinimum(
  value: string,
  isCountUnit: boolean,
  allowZero: boolean,
): boolean {
  const parsed = parseDecimalText(value);
  if (!parsed || (allowZero ? parsed.coefficient < 0n : parsed.coefficient <= 0n)) {
    return false;
  }

  const normalized = removeTrailingZeros(parsed);
  if (normalized.scale > 6 || (isCountUnit && normalized.scale > 0)) {
    return false;
  }
  const scaledMicrounits = normalized.coefficient * 10n ** BigInt(6 - normalized.scale);
  return scaledMicrounits <= MAX_ESTOQUE_BASE_MICROUNITS;
}

export function compareDecimalText(left: string, right: string): number | null {
  const leftParsed = parseDecimalText(left);
  const rightParsed = parseDecimalText(right);
  if (!leftParsed || !rightParsed) {
    return null;
  }

  const scale = Math.max(leftParsed.scale, rightParsed.scale);
  const leftCoefficient = leftParsed.coefficient * 10n ** BigInt(scale - leftParsed.scale);
  const rightCoefficient = rightParsed.coefficient * 10n ** BigInt(scale - rightParsed.scale);
  return leftCoefficient < rightCoefficient ? -1 : leftCoefficient > rightCoefficient ? 1 : 0;
}

export function subtractDecimalText(left: string, right: string): string | null {
  const leftParsed = parseDecimalText(left);
  const rightParsed = parseDecimalText(right);
  if (!leftParsed || !rightParsed) {
    return null;
  }

  const scale = Math.max(leftParsed.scale, rightParsed.scale);
  const leftCoefficient = leftParsed.coefficient * 10n ** BigInt(scale - leftParsed.scale);
  const rightCoefficient = rightParsed.coefficient * 10n ** BigInt(scale - rightParsed.scale);
  const difference = leftCoefficient - rightCoefficient;
  const absolute = difference < 0n ? -difference : difference;
  const decimal = decimalFromParts(absolute, scale);
  return difference < 0n ? `-${decimal}` : decimal;
}

export function formatSignedDecimalPtBr(value: string): string {
  if (!value.startsWith('-')) {
    return formatDecimalPtBr(value);
  }
  const formatted = formatDecimalPtBr(value.slice(1));
  return formatted === value.slice(1) ? value : `-${formatted}`;
}

export function isValidCostTotal(value: string): boolean {
  const parsed = parseDecimalText(value);
  if (!parsed) {
    return false;
  }
  const normalized = removeTrailingZeros(parsed);
  if (normalized.scale > 2) {
    return false;
  }
  return normalized.coefficient * 10n ** BigInt(2 - normalized.scale) <= MAX_CUSTO_CENTAVOS;
}

export function formatDecimalPtBr(value: string, trimFraction = true): string {
  const parsed = parseDecimalText(value);
  if (!parsed) {
    return value;
  }
  const normalized = trimFraction ? removeTrailingZeros(parsed) : parsed;
  const plain = decimalFromParts(normalized.coefficient, normalized.scale);
  const [whole = '0', fraction] = plain.split('.');
  const groupedWhole = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return fraction ? `${groupedWhole},${fraction}` : groupedWhole;
}

export function formatBrl(value: string): string {
  const parsed = parseDecimalText(value);
  if (!parsed || !isValidCostTotal(value)) {
    return '';
  }
  const normalized = removeTrailingZeros(parsed);
  const cents = normalized.coefficient * 10n ** BigInt(2 - normalized.scale);
  const fixed = decimalFromParts(cents, 2);
  return `R$ ${formatDecimalPtBr(fixed, false)}`;
}

export function estimateCostPerBaseUnit(costTotal: string, quantityBase: string): string | null {
  const cost = parseDecimalText(costTotal);
  const quantity = parseDecimalText(quantityBase);
  if (!cost || !quantity || quantity.coefficient <= 0n || !isValidCostTotal(costTotal)) {
    return null;
  }

  const normalizedCost = removeTrailingZeros(cost);
  const normalizedQuantity = removeTrailingZeros(quantity);
  const cents = normalizedCost.coefficient * 10n ** BigInt(2 - normalizedCost.scale);
  const numerator = cents * 10n ** BigInt(normalizedQuantity.scale + 12);
  const denominator = normalizedQuantity.coefficient;
  const roundedAtScale14 = (numerator * 2n + denominator) / (denominator * 2n);
  const formatted = formatDecimalPtBr(decimalFromParts(roundedAtScale14, 14));
  const [whole, fraction = ''] = formatted.split(',');
  return `R$ ${whole},${fraction.padEnd(2, '0')}`;
}

function validBrazilianInteger(value: string): boolean {
  if (!value) {
    return true;
  }
  if (!value.includes('.')) {
    return /^\d+$/.test(value);
  }
  return /^(?:\d{1,3})(?:\.\d{3})+$/.test(value);
}

function removeTrailingZeros(value: DecimalText): DecimalText {
  let coefficient = value.coefficient;
  let scale = value.scale;
  while (scale > 0 && coefficient % 10n === 0n) {
    coefficient /= 10n;
    scale -= 1;
  }
  return { text: decimalFromParts(coefficient, scale), coefficient, scale };
}

function decimalFromParts(coefficient: bigint, scale: number): string {
  const digits = coefficient.toString().padStart(scale + 1, '0');
  if (scale === 0) {
    return digits;
  }
  const split = digits.length - scale;
  return `${digits.slice(0, split)}.${digits.slice(split)}`;
}
