import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { ProdutosAcabadosService } from '../../core/admin-estoque/produtos-acabados.service';
import {
  DetalheOrdem,
  ConclusaoProducao,
  ConcluirProducao,
  OrdensProducaoService,
  PreviaProducao,
} from '../../core/ordens-producao.service';
import { ReceitasProducaoService } from '../../core/receitas-producao.service';
import { AdminProducaoComponent } from './admin-producao.component';

const ordemId = '03000000-0000-4000-8000-000000000001';
const receitaId = '03000000-0000-4000-8000-000000000002';
const versaoId = '03000000-0000-4000-8000-000000000003';
const chave = '03000000-0000-4000-8000-000000000004';
const storageKey = 'xaquiamuni:producao:tentativa:v1:admin-teste';
const corpo = { receitaId, receitaVersaoId: versaoId, quantidade: 4 };
const material = {
  insumoId: '03000000-0000-4000-8000-000000000005',
  nome: 'Base',
  unidade: 'Grama' as const,
  necessario: '10',
  reservado: '8',
};
const previa: PreviaProducao = {
  receitaVersaoId: versaoId,
  versao: 2,
  materiais: [{ ...material, utilizavel: '18', livre: '10', falta: '0' }],
};
const detalhe: DetalheOrdem = {
  varianteId: 'v1',
  conclusao: null,
  resumo: {
    id: ordemId,
    produtoId: 'p1',
    produto: 'Sabonete',
    variante: 'Lavanda',
    receitaId,
    receita: 'Lavanda artesanal',
    versao: 2,
    quantidade: 4,
    versaoOrdem: versaoId,
    situacao: 'EmAndamento',
    iniciadaEmUtc: '2026-11-19T15:00:00Z',
    encerradaEmUtc: null,
  },
  materiais: [{ ...material, reservaInelegivel: true }],
};
const resultado: ConclusaoProducao = {
  ordemId,
  situacao: 'Concluida',
  versao: chave,
  quantidadeProduzida: 3,
  loteId: 'l1',
  codigoLote: 'PA',
  movimentoEntradaId: 'm1',
  dataFabricacao: '2026-11-19',
  dataValidade: '2026-12-19',
  responsavelId: 'admin-teste',
  concluidaEmUtc: '2026-11-19T16:00:00Z',
  consumos: [
    {
      insumoId: material.insumoId,
      nome: 'Base',
      unidadeBase: 'Grama',
      consumida: '5.123456',
      liberada: '2.876544',
    },
  ],
};
const concluida: DetalheOrdem = {
  ...detalhe,
  resumo: { ...detalhe.resumo, situacao: 'Concluida', versaoOrdem: chave },
  conclusao: resultado,
};
const cancelada: DetalheOrdem = {
  ...detalhe,
  resumo: { ...detalhe.resumo, situacao: 'Cancelada' },
};

beforeEach(() => sessionStorage.clear());
afterEach(() => sessionStorage.clear());

async function montar(atual = detalhe, localizada = false, prazo: number | null = 30) {
  const api = {
    listar: vi.fn(() => of([atual.resumo])),
    obter: vi.fn(() => of(atual)),
    tentativa: vi.fn(() =>
      localizada ? of({ ordemId }) : throwError(() => new HttpErrorResponse({ status: 404 })),
    ),
    prever: vi.fn(() => of(previa)),
    iniciar: vi.fn(() => of({ ordemId })),
    cancelar: vi.fn(() => of({ ordemId })),
    concluir: vi.fn<OrdensProducaoService['concluir']>(() => of(resultado)),
  };
  const consultarPolitica = vi.fn(() => of({ prazoValidadePadraoDias: prazo }));
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: AdminAuthService,
        useValue: { getCurrentSession: () => of({ userId: 'admin-teste' }) },
      },
      { provide: OrdensProducaoService, useValue: api },
      { provide: ProdutosAcabadosService, useValue: { consultarPolitica } },
      {
        provide: ReceitasProducaoService,
        useValue: {
          listar: () =>
            of([
              {
                id: receitaId,
                nome: 'Lavanda artesanal',
                produto: 'Sabonete',
                variante: 'Lavanda',
                ativa: true,
              },
              { id: 'inativa', nome: 'Receita antiga', ativa: false },
            ]),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(AdminProducaoComponent);
  const root = fixture.nativeElement as HTMLElement;
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  const botao = (texto: string) =>
    [...root.querySelectorAll('button')].find((b) => b.textContent.trim() === texto)!;
  async function clicar(texto: string) {
    botao(texto).click();
    await atualizar();
  }
  async function preencher(seletor: string, valor: string) {
    const campo = root.querySelector<HTMLInputElement | HTMLSelectElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(
      new Event(campo instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
    await atualizar();
  }
  await atualizar();
  return { api, root, clicar, preencher, botao, atualizar, consultarPolitica, fixture };
}

describe('Administração de produção', () => {
  it('exibe falta comprometida e inicia com a versão da prévia, sem duplo envio', async () => {
    const { root, api, clicar, preencher, botao, atualizar } = await montar();
    expect(root.querySelector('a[href="/admin/producao"]')?.getAttribute('aria-current')).toBe(
      'page',
    );
    await preencher('select', 'Cancelada');
    expect(root.textContent).toContain('Nenhuma ordem');
    await clicar('Nova produção');
    expect(root.textContent).not.toContain('Receita antiga');
    await clicar('Consultar disponibilidade');
    expect(api.prever).not.toHaveBeenCalled();
    await preencher('[name=receita]', receitaId);
    await preencher('[name=quantidade]', '4');
    api.prever.mockReturnValueOnce(
      of({ ...previa, materiais: [{ ...material, utilizavel: '12', livre: '4', falta: '6' }] }),
    );
    await clicar('Consultar disponibilidade');
    expect(root.textContent).toContain('Saldo comprometido por outras ordens');
    expect(botao('Iniciar produção').disabled).toBe(true);
    await clicar('Consultar disponibilidade');
    const resposta = new Subject<{ ordemId: string }>();
    api.iniciar.mockReturnValueOnce(resposta);
    await clicar('Iniciar produção');
    await clicar('Iniciar produção');
    expect(api.iniciar).toHaveBeenCalledExactlyOnceWith(
      corpo,
      expect.stringMatching(/^[0-9a-f-]{36}$/),
    );
    resposta.next({ ordemId });
    resposta.complete();
    await atualizar();
    expect(root.textContent).toContain('Materiais reservados');
    expect(root.textContent).toContain('lote vencido ou bloqueado');
    expect(root.textContent).toContain('procure suporte antes de continuar');
    expect(
      root.querySelector(`a[href="/admin/estoque/insumos/${material.insumoId}/consultas"]`),
    ).not.toBeNull();
    expect(root.textContent).toContain('Concluir produção');
  });

  it('exige confirmação e cancela com chave, exibindo a ordem atualizada', async () => {
    const { root, api, clicar } = await montar();
    await clicar('Abrir ordem');
    await clicar('Cancelar produção');
    expect(api.cancelar).not.toHaveBeenCalled();
    api.obter.mockReturnValueOnce(of(cancelada));
    await clicar('Confirmar cancelamento');
    expect(api.cancelar).toHaveBeenCalledExactlyOnceWith(ordemId, expect.any(String));
    expect(root.textContent).toContain('Reservas liberadas');
    expect(root.textContent).not.toContain('Cancelar produção');
  });

  it('recarrega início pendente e reencontra a ordem sem novo POST', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify({ tipo: 'inicio', chave, corpo }));
    const { root, api } = await montar(detalhe, true);
    expect(api.tentativa).toHaveBeenCalledWith(chave);
    expect(api.obter).toHaveBeenCalledWith(ordemId);
    expect(api.iniciar).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(root.textContent).toContain('já foi localizada');
  });

  it('reenvia início somente após consulta e preserva corpo e chave', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify({ tipo: 'inicio', chave, corpo }));
    const { api, clicar } = await montar();
    expect(api.tentativa).toHaveBeenCalledWith(chave);
    expect(api.iniciar).not.toHaveBeenCalled();
    await clicar('Reenviar tentativa');
    expect(api.iniciar).toHaveBeenCalledExactlyOnceWith(corpo, chave);
  });

  it('recupera cancelamento em andamento e preserva a chave no reenvio manual', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify({ tipo: 'cancelamento', chave, ordemId }));
    const { api, clicar } = await montar();
    expect(api.obter).toHaveBeenCalledWith(ordemId);
    expect(api.cancelar).not.toHaveBeenCalled();
    api.obter.mockReturnValueOnce(of(cancelada));
    await clicar('Reenviar tentativa');
    expect(api.cancelar).toHaveBeenCalledExactlyOnceWith(ordemId, chave);
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('confere consumo exato e sobras e atualiza a ordem após uma única conclusão', async () => {
    const { api, root, clicar, preencher, atualizar, botao, consultarPolitica } = await montar();
    await clicar('Abrir ordem');
    await clicar('Concluir produção');
    expect(consultarPolitica).toHaveBeenCalledWith('v1');
    expect(root.querySelector<HTMLInputElement>('[name=consumo-0]')?.value).toBe('8');
    expect(root.querySelector('label')?.textContent).toContain('Quantidade realmente produzida');
    expect(botao('Confirmar conclusão').disabled).toBe(true);
    await preencher('[name=produzida]', '3');
    await preencher('[name=consumo-0]', '5,123456');
    expect(root.textContent).toContain('2,876544 g');
    expect(root.textContent).toContain('prazo padrão de 30 dias');
    (root.querySelector('[name=conferido]') as HTMLInputElement).click();
    await atualizar();
    const resposta = new Subject<ConclusaoProducao>();
    api.concluir.mockReturnValueOnce(resposta);
    await clicar('Confirmar conclusão');
    await clicar('Confirmar conclusão');
    expect(api.concluir).toHaveBeenCalledExactlyOnceWith(
      ordemId,
      {
        versao: versaoId,
        quantidadeProduzida: 3,
        dataFabricacao: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
        dataValidade: null,
        consumos: [{ insumoId: material.insumoId, unidadeBase: 'Grama', quantidade: '5.123456' }],
      },
      expect.stringMatching(/^[0-9a-f-]{36}$/),
    );
    api.obter.mockReturnValueOnce(of(concluida));
    resposta.next(resultado);
    resposta.complete();
    await atualizar();
    expect(root.textContent).toContain('Produzido: 3 unidades');
    expect(root.textContent).toContain('Liberado: 2,876544 g');
    expect(root.textContent).toContain('Responsável: Você');
    expect(botao('Concluir produção')).toBeUndefined();
    expect(botao('Cancelar produção')).toBeUndefined();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('exige validade manual quando não há prazo e rejeita excesso e precisão inválida', async () => {
    const { api, root, clicar, preencher, atualizar } = await montar(detalhe, false, null);
    await clicar('Abrir ordem');
    await clicar('Concluir produção');
    expect(root.querySelector<HTMLInputElement>('[name=validade]')?.required).toBe(true);
    await preencher('[name=produzida]', '3');
    const declarar = async () => {
      (root.querySelector('[name=conferido]') as HTMLInputElement).click();
      await atualizar();
    };
    await declarar();
    await clicar('Confirmar conclusão');
    expect(api.concluir).not.toHaveBeenCalled();
    await preencher('[name=validade]', '2026-12-19');
    for (const invalida of ['9', '0', '1.0000001']) {
      await preencher('[name=consumo-0]', invalida);
      expect(root.querySelector('[name=consumo-0]')?.getAttribute('aria-invalid')).toBe('true');
      await declarar();
      await clicar('Confirmar conclusão');
      expect(api.concluir).not.toHaveBeenCalled();
    }
    await preencher('[name=consumo-0]', '8');
    await declarar();
    api.obter.mockReturnValueOnce(of(concluida));
    await clicar('Confirmar conclusão');
    expect(api.concluir.mock.calls[0]?.[1] satisfies ConcluirProducao | undefined).toMatchObject({
      dataValidade: '2026-12-19',
      consumos: [{ quantidade: '8' }],
    });
  });

  const tentativaConclusao = {
    tipo: 'conclusao',
    chave,
    ordemId,
    corpo: {
      versao: versaoId,
      quantidadeProduzida: 3,
      dataFabricacao: null,
      dataValidade: null,
      consumos: [{ insumoId: material.insumoId, unidadeBase: 'Grama', quantidade: '5.123456' }],
    },
  };

  it('consulta o estado e libera nova conferência após rejeição definitiva', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(tentativaConclusao));
    const { api, clicar, botao, root } = await montar();
    api.concluir.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'Uma posição reservada está inelegível.', rejeicaoDefinitiva: true },
          }),
      ),
    );
    await clicar('Reenviar tentativa');
    expect(api.obter).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[role=alert]')?.textContent).toContain('está inelegível');
    expect(botao('Reenviar tentativa')).toBeUndefined();
    expect(botao('Concluir produção')).toBeDefined();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('preserva a tentativa em falhas de rede e reenvia o payload exato após consulta', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(tentativaConclusao));
    const { api, clicar, root } = await montar();
    expect(api.obter).toHaveBeenCalledWith(ordemId);
    expect(api.concluir).not.toHaveBeenCalled();
    api.concluir.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    await clicar('Reenviar tentativa');
    expect(api.concluir).toHaveBeenCalledExactlyOnceWith(ordemId, tentativaConclusao.corpo, chave);
    expect(JSON.parse(sessionStorage.getItem(storageKey)!)).toEqual(tentativaConclusao);
    expect(root.querySelector('[role=alert]')?.textContent).toContain('Confira a situação');
    await clicar('Conferir situação');
    api.obter.mockReturnValueOnce(of(concluida));
    await clicar('Reenviar tentativa');
    expect(api.concluir).toHaveBeenLastCalledWith(ordemId, tentativaConclusao.corpo, chave);
  });

  it.each([
    concluida,
    cancelada,
    { ...detalhe, resumo: { ...detalhe.resumo, versaoOrdem: chave } },
  ])('consulta antes do reenvio e bloqueia tentativa incompatível (%s)', async (ordem) => {
    sessionStorage.setItem(storageKey, JSON.stringify(tentativaConclusao));
    const { api, botao, root } = await montar(ordem);
    expect(api.concluir).not.toHaveBeenCalled();
    expect(botao('Reenviar tentativa')).toBeUndefined();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    if (ordem === concluida) expect(root.textContent).toContain('Produzido: 3 unidades');
    else expect(root.textContent).toContain('O reenvio foi bloqueado');
  });
});
