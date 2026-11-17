import { Injectable } from '@angular/core';

export type EstadoOperacaoEstoquePendente = 'enviando' | 'incerta' | 'conflito';

export interface OperacaoEstoquePendente<TRequest, TResumo> {
  atorId: string;
  idempotencyKey: string;
  insumoId: string;
  resumo: TResumo;
  request: TRequest;
  state: EstadoOperacaoEstoquePendente;
  erro?: string;
}

@Injectable({ providedIn: 'root' })
export class OperacaoPendenteStore {
  criarChaveIdempotencia(): string | null {
    if (typeof crypto === 'undefined') {
      return null;
    }
    if (typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    if (typeof crypto.getRandomValues !== 'function') {
      return null;
    }

    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  ler(chave: string): string | null {
    return sessionStorage.getItem(chave);
  }

  async calcularFingerprint(value: unknown): Promise<string> {
    const serialized = JSON.stringify(value);
    if (serialized === undefined) {
      throw new TypeError('O conteúdo da operação não pode ser serializado.');
    }

    const webCrypto = globalThis.crypto;
    if (!webCrypto?.subtle) {
      throw new Error('O navegador não oferece fingerprint criptográfico.');
    }

    const digest = await webCrypto.subtle.digest('SHA-256', new TextEncoder().encode(serialized));
    return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join(
      '',
    );
  }

  salvar<T>(chave: string, operation: T): boolean {
    try {
      const serialized = JSON.stringify(operation);
      if (serialized === undefined) {
        throw new TypeError('A operação pendente não pode ser serializada.');
      }
      sessionStorage.setItem(chave, serialized);
      return true;
    } catch (error) {
      if (error instanceof DOMException) {
        return false;
      }
      throw error;
    }
  }

  remover(chave: string): boolean {
    try {
      sessionStorage.removeItem(chave);
      return true;
    } catch (error) {
      if (error instanceof DOMException) {
        return false;
      }
      throw error;
    }
  }
}
