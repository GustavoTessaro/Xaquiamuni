import { TestBed } from '@angular/core/testing';
import { OperacaoPendenteStore } from './operacao-pendente.store';

describe('OperacaoPendenteStore', () => {
  let store: OperacaoPendenteStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    store = TestBed.inject(OperacaoPendenteStore);
  });

  afterEach(() => sessionStorage.clear());

  it('gera fingerprint estável e diferente quando o conteúdo muda sem revelar o payload', async () => {
    const first = await store.calcularFingerprint({
      quantidade: 4,
      motivo: 'Recebimento reservado',
    });
    const same = await store.calcularFingerprint({
      quantidade: 4,
      motivo: 'Recebimento reservado',
    });
    const changed = await store.calcularFingerprint({
      quantidade: 5,
      motivo: 'Recebimento reservado',
    });

    expect(first).toMatch(/^[a-f0-9]{64}$/);
    expect(same).toBe(first);
    expect(changed).not.toBe(first);
    expect(first).not.toContain('Recebimento reservado');
  });

  it('persiste registros genéricos e os remove pela chave', () => {
    const pending = {
      atorId: 'actor-1',
      produtoVarianteId: 'variant-1',
      fingerprint: 'f'.repeat(64),
      idempotencyKey: 'key-1',
    };

    expect(store.salvar('finished-goods-operation', pending)).toBe(true);
    expect(JSON.parse(store.ler('finished-goods-operation')!)).toEqual(pending);
    expect(store.remover('finished-goods-operation')).toBe(true);
    expect(store.ler('finished-goods-operation')).toBeNull();
  });
});
