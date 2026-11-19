import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import {
  DetalheOrdem,
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
  insumoId: 'i1',
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
  resumo: {
    id: ordemId,
    produtoId: 'p1',
    produto: 'Sabonete',
    variante: 'Lavanda',
    receitaId,
    receita: 'Lavanda artesanal',
    versao: 2,
    quantidade: 4,
    situacao: 'EmAndamento',
    iniciadaEmUtc: '2026-11-19T15:00:00Z',
    encerradaEmUtc: null,
  },
  materiais: [{ ...material, reservaInelegivel: true }],
};
const cancelada: DetalheOrdem = {
  ...detalhe,
  resumo: { ...detalhe.resumo, situacao: 'Cancelada' },
};

beforeEach(() => sessionStorage.clear());
afterEach(() => sessionStorage.clear());

async function montar(atual = detalhe, localizada = false) {
  const api = {
    listar: vi.fn(() => of([atual.resumo])),
    obter: vi.fn(() => of(atual)),
    tentativa: vi.fn(() =>
      localizada ? of({ ordemId }) : throwError(() => new HttpErrorResponse({ status: 404 })),
    ),
    prever: vi.fn(() => of(previa)),
    iniciar: vi.fn(() => of({ ordemId })),
    cancelar: vi.fn(() => of({ ordemId })),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: AdminAuthService,
        useValue: { getCurrentSession: () => of({ userId: 'admin-teste' }) },
      },
      { provide: OrdensProducaoService, useValue: api },
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
  return { api, root, clicar, preencher, botao, atualizar };
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
    expect(root.querySelector('a[href="/admin/estoque/insumos/i1/consultas"]')).not.toBeNull();
    expect(root.textContent).not.toContain('Concluir produção');
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
});
