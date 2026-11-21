const unidadesVendaAmigaveis: Record<string, string> = {
  UN: 'Unidade',
  KIT: 'Kit',
  PAR: 'Par',
  PCT: 'Pacote',
  CX: 'Caixa',
  CJ: 'Conjunto',
};

export function formatarUnidadeVenda(unidade: string): string {
  return unidadesVendaAmigaveis[unidade.trim().toUpperCase()] ?? unidade;
}
export function precoValido(valor: unknown): valor is string {
  return typeof valor === 'string' && /^[1-9]\d{0,18}$/.test(valor) &&
    BigInt(valor) <= 9_223_372_036_854_775_807n;
}
