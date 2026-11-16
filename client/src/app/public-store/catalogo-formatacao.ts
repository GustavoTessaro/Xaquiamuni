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
