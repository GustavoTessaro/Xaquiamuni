export function hojeCivil(): string {
  const agora = new Date();
  return civil(agora.getFullYear(), agora.getMonth() + 1, agora.getDate());
}

function civil(ano: number, mes: number, dia: number): string {
  return [
    String(ano).padStart(4, '0'),
    String(mes).padStart(2, '0'),
    String(dia).padStart(2, '0'),
  ].join('-');
}

function dataUtc(ano: number, mes: number, dia: number): Date {
  const data = new Date(0);
  data.setUTCFullYear(ano, mes - 1, dia);
  return data;
}

export function mesAgenda(referencia: string, deslocamento = 0) {
  const [ano, mes] = referencia.split('-').map(Number);
  const primeiro = dataUtc(ano, mes + deslocamento, 1);
  const ultimo = dataUtc(ano, mes + deslocamento + 1, 0);
  const atual = primeiro.getUTCFullYear();
  if (atual < 1 || atual > 9999) return null;
  const mesAtual = primeiro.getUTCMonth() + 1;
  const quantidade = ultimo.getUTCDate();
  return {
    inicio: civil(atual, mesAtual, 1),
    fim: civil(atual, mesAtual, quantidade),
    titulo: new Intl.DateTimeFormat('pt-BR', {
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    }).format(primeiro),
    espacos: Array.from({ length: (primeiro.getUTCDay() + 6) % 7 }, (_, i) => i),
    dias: Array.from({ length: quantidade }, (_, i) => ({
      numero: i + 1,
      data: civil(atual, mesAtual, i + 1),
    })),
  };
}

export function rotuloDia(data: string): string {
  return data.split('-').reverse().join('/');
}
