import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { TemaAdminService } from './tema-admin.service';

const chave = 'xaquiamuni:admin:tema';

describe('TemaAdminService', () => {
  beforeEach(() => localStorage.removeItem(chave));
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(chave);
  });

  it.each([null, 'invalido', 'light', 'dark'])('restaura a preferência %s', (valor) => {
    if (valor !== null) localStorage.setItem(chave, valor);
    expect(TestBed.inject(TemaAdminService).tema()).toBe(valor === 'dark' ? 'dark' : 'light');
  });

  it('inicializa em claro quando o navegador bloqueia a leitura', () => {
    vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
      throw new DOMException('Armazenamento bloqueado', 'SecurityError');
    });
    expect(TestBed.inject(TemaAdminService).tema()).toBe('light');
  });

  it('mantém a alternância em memória quando a gravação falha', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota excedida', 'QuotaExceededError');
    });
    const tema = TestBed.inject(TemaAdminService);
    tema.alternar();
    expect(tema.tema()).toBe('dark');
    expect(localStorage.getItem(chave)).toBeNull();
    tema.alternar();
    expect(tema.tema()).toBe('light');
  });
});
