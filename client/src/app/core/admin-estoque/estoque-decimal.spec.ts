import {
  decimalTextFromNumber,
  compareDecimalText,
  estimateCostPerBaseUnit,
  formatBrl,
  formatDecimalPtBr,
  formatSignedDecimalPtBr,
  isPositiveIntegerText,
  isValidBaseQuantity,
  isValidCostTotal,
  isValidNonNegativeBaseQuantity,
  multiplyDecimalByInteger,
  multiplyDecimalByPowerOfTen,
  parseDecimalText,
  subtractDecimalText,
} from './estoque-decimal';

describe('stock decimal text utilities', () => {
  it('parses Brazilian grouped values and expands JSON numbers without numeric arithmetic', () => {
    expect(parseDecimalText('1.500,25')?.text).toBe('1500.25');
    expect(parseDecimalText('0,000001')?.text).toBe('0.000001');
    expect(decimalTextFromNumber(0.000001)).toBe('0.000001');
    expect(decimalTextFromNumber(1e-7)).toBe('0.0000001');
    expect(parseDecimalText('1.000,25.')).toBeNull();
    expect(parseDecimalText('1,2.3')).toBeNull();
  });

  it('converts physical units and purchase quantities with integer decimal arithmetic', () => {
    expect(multiplyDecimalByPowerOfTen('2', 3)).toBe('2000');
    expect(multiplyDecimalByPowerOfTen('1,25', 3)).toBe('1250');
    expect(multiplyDecimalByPowerOfTen('2,5', 3)).toBe('2500');
    expect(multiplyDecimalByInteger('500,25', '3')).toBe('1500.75');
    expect(multiplyDecimalByInteger('12', '2')).toBe('24');
    expect(formatDecimalPtBr('1500.75')).toBe('1.500,75');
  });

  it('checks base quantity precision, limits, and indivisible units without rounding', () => {
    expect(isValidBaseQuantity('0.000001', false)).toBe(true);
    expect(isValidBaseQuantity('0.0000001', false)).toBe(false);
    expect(isValidBaseQuantity('24.0', true)).toBe(true);
    expect(isValidBaseQuantity('24.5', true)).toBe(false);
    expect(isValidBaseQuantity('999999999999.999999', false)).toBe(true);
    expect(isValidBaseQuantity('1000000000000', false)).toBe(false);
    expect(isPositiveIntegerText('3,0')).toBe(true);
    expect(isPositiveIntegerText('3,1')).toBe(false);
  });

  it('compares and subtracts quantities exactly for physical counts, including zero and negative differences', () => {
    expect(isValidNonNegativeBaseQuantity('0', false)).toBe(true);
    expect(isValidNonNegativeBaseQuantity('0,5', false)).toBe(true);
    expect(isValidNonNegativeBaseQuantity('0,5', true)).toBe(false);
    expect(compareDecimalText('1.000001', '1')).toBe(1);
    expect(compareDecimalText('1.000000', '1')).toBe(0);
    expect(subtractDecimalText('0.000001', '1')).toBe('-0.999999');
    expect(subtractDecimalText('1.000001', '1')).toBe('0.000001');
    expect(formatSignedDecimalPtBr('-0.999999')).toBe('-0,999999');
  });

  it('distinguishes unknown and zero costs and derives unit cost with exact integer division', () => {
    expect(formatBrl('0')).toBe('R$ 0,00');
    expect(formatBrl('1500,25')).toBe('R$ 1.500,25');
    expect(isValidCostTotal('0')).toBe(true);
    expect(isValidCostTotal('1,20')).toBe(true);
    expect(isValidCostTotal('1,001')).toBe(false);
    expect(isValidCostTotal('10000000000000000')).toBe(false);
    expect(estimateCostPerBaseUnit('10,00', '2000')).toBe('R$ 0,005');
    expect(estimateCostPerBaseUnit('0', '2000')).toBe('R$ 0,00');
    expect(estimateCostPerBaseUnit('10,00', '0')).toBeNull();
  });
});
