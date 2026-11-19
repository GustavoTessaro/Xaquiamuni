import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { InsumosService } from '../../core/admin-estoque/insumos.service';
import { ProdutosService } from '../../core/admin-produtos/produtos.service';
import { DetalheReceita, ReceitasProducaoService } from '../../core/receitas-producao.service';
import { AdminReceitasComponent } from './admin-receitas.component';

const revisao = { numero: 1, rendimento: 4, criadaEmUtc: '2026-11-19T00:00:00Z' };
const detalhe: DetalheReceita = {
  resumo: {
    id: 'r1',
    nome: 'Receita floral',
    ativa: true,
    produtoId: 'p1',
    produto: 'Sabonete',
    varianteId: 'v1',
    variante: 'Lavanda',
    numeroVersao: 1,
    rendimento: 4,
  },
  token: 't1',
  atual: {
    ...revisao,
    itens: [
      { insumoId: 'i1', nomeInsumo: 'Base original', quantidade: '24.25', unidadeBase: 'Grama' },
    ],
  },
  historico: [revisao],
};

async function montar() {
  const api = {
    listar: vi.fn(() =>
      of([detalhe.resumo, { ...detalhe.resumo, id: 'r2', nome: 'Receita antiga', ativa: false }]),
    ),
    obter: vi.fn(() => of(detalhe)),
    versao: vi.fn(() => of(detalhe.atual)),
    salvar: vi.fn(() => of(detalhe)),
    alterarStatus: vi.fn(() => of({ ...detalhe, resumo: { ...detalhe.resumo, ativa: false } })),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ReceitasProducaoService, useValue: api },
      {
        provide: InsumosService,
        useValue: {
          listar: () =>
            of({
              pagina: 1,
              itensPorPagina: 50,
              totalItens: 2,
              itens: [
                { insumoId: 'i1', nome: 'Base', unidadeBase: 'Grama' },
                { insumoId: 'i2', nome: 'Frasco', unidadeBase: 'Unidade' },
              ],
            }),
        },
      },
      {
        provide: ProdutosService,
        useValue: {
          listar: () =>
            of([
              {
                id: 'p1',
                nome: 'Sabonete',
                variantes: [
                  { id: 'v1', identificacao: 'Lavanda', ativa: true },
                  { id: 'v2', identificacao: 'Aposentada', ativa: false },
                ],
              },
            ]),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(AdminReceitasComponent);
  const root = fixture.nativeElement as HTMLElement;
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function clicar(texto: string) {
    const botao = [...root.querySelectorAll('button')].find((b) =>
      b.textContent.trim().startsWith(texto),
    );
    expect(botao).toBeDefined();
    botao!.click();
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
  return { api, root, preencher, clicar };
}

describe('Receitas de produção', () => {
  it('busca por produto, filtra situação, consulta histórico e envia edição por token', async () => {
    const { api, root, preencher, clicar } = await montar();
    expect(root.querySelector('a[href="/admin/receitas"]')?.getAttribute('aria-current')).toBe(
      'page',
    );
    await preencher('input[type=search]', 'sabOnete');
    await preencher('select', 'ativa');
    expect(root.textContent).toContain('Receita floral');
    expect(root.textContent).not.toContain('Receita antiga');
    await clicar('Abrir receita');
    expect(root.querySelector('select[name=variante]')).toBeNull();
    root.querySelector('details')!.open = true;
    await clicar('Ver versão 1');
    expect(api.versao).toHaveBeenCalledWith('r1', 1);
    expect(root.textContent).toContain('Base original · 24,25 g');
    await preencher('[name=rendimento]', '6');
    await clicar('Salvar receita');
    expect(api.salvar).toHaveBeenCalledWith(
      'r1',
      expect.objectContaining({ rendimento: 6, token: 't1' }),
    );
    await clicar('Inativar receita');
    expect(api.alterarStatus).toHaveBeenCalledWith('r1', false, 't1');
    expect(root.textContent).toContain('Reativar receita');
  });

  it('cadastra com unidade-base, rejeita unidades fracionárias e materiais repetidos', async () => {
    const { api, root, preencher, clicar } = await montar();
    await clicar('Nova receita');
    expect(root.textContent).toContain('não movimenta estoque');
    await clicar('Salvar receita');
    expect(root.querySelector('[role=alert]')?.textContent).toContain('rendimento');
    await preencher('[name=produto]', 'p1');
    expect(root.textContent).not.toContain('Aposentada');
    expect(root.querySelector<HTMLSelectElement>('[name=variante]')?.value).toBe('v1');
    await preencher('[name=nome]', 'Floral');
    await preencher('[name=rendimento]', '4');
    await preencher('[name=material-0]', 'i2');
    expect(root.querySelector('[name=quantidade-0]')?.closest('label')?.textContent).toContain(
      '(un)',
    );
    await preencher('[name=quantidade-0]', '1,5');
    await clicar('Salvar receita');
    expect(api.salvar).not.toHaveBeenCalled();
    await preencher('[name=quantidade-0]', '2');
    await clicar('Adicionar material');
    await preencher('[name=material-1]', 'i2');
    await preencher('[name=quantidade-1]', '1');
    await clicar('Salvar receita');
    expect(root.querySelector('[role=alert]')?.textContent).toContain('apenas uma vez');
    await preencher('[name=material-1]', 'i1');
    await preencher('[name=quantidade-1]', '24,250001');
    await clicar('Salvar receita');
    expect(api.salvar).toHaveBeenCalledWith(null, {
      produtoVarianteId: 'v1',
      nome: 'Floral',
      rendimento: 4,
      token: null,
      itens: [
        { insumoId: 'i2', quantidade: '2', unidadeBase: 'Unidade' },
        { insumoId: 'i1', quantidade: '24.250001', unidadeBase: 'Grama' },
      ],
    });
  });
});
