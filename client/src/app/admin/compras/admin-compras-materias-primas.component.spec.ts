import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { InsumosService } from '../../core/admin-estoque/insumos.service';
import {
  ComprasMateriasPrimasService,
  DetalheCompra,
  ResumoCompra,
} from '../../core/compras-materias-primas.service';
import { FornecedoresService } from '../../core/fornecedores.service';
import { AdminComprasMateriasPrimasComponent } from './admin-compras-materias-primas.component';
import { decimalEscalado, exibirReais } from './compra-decimais';

const resumo: ResumoCompra = {
  id: 'c1',
  fornecedorId: 'f1',
  nomeFornecedor: 'Ervas do Vale',
  dataCompra: '2026-11-19',
  situacao: 'Rascunho',
  total: '25.81',
};
const detalhe: DetalheCompra = {
  resumo,
  versao: 'v1',
  linhas: [
    {
      kit: null,
      quantidade: '2',
      valorTotal: '25.81',
      componentes: [
        {
          id: 's1',
          insumoId: 'i1',
          nomeInsumo: 'Extrato',
          apresentacaoId: 'a1',
          nomeApresentacao: 'Frasco',
          fator: '100',
          quantidade: '2',
          quantidadeBase: '200',
          unidadeBase: 'Mililitro',
        },
      ],
    },
  ],
};

async function montar() {
  const compras = {
    listar: vi.fn(() =>
      of([resumo, { ...resumo, id: 'c2', situacao: 'Cancelada', nomeFornecedor: 'Vidros' }]),
    ),
    obter: vi.fn(() => of(detalhe)),
    salvar: vi.fn(() => of(detalhe)),
    cancelar: vi.fn(() => of(detalhe)),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ComprasMateriasPrimasService, useValue: compras },
      {
        provide: FornecedoresService,
        useValue: {
          listar: () =>
            of([
              { id: 'f1', nome: resumo.nomeFornecedor, ativo: true },
              { id: 'f2', nome: 'Fora do catálogo', ativo: false },
            ]),
        },
      },
      {
        provide: InsumosService,
        useValue: {
          listar: () =>
            of({
              pagina: 1,
              itensPorPagina: 50,
              totalItens: 2,
              itens: [
                { insumoId: 'i1', nome: 'Extrato', unidadeBase: 'Mililitro' },
                { insumoId: 'i2', nome: 'Alecrim', unidadeBase: 'Grama' },
              ],
            }),
          obterParaEntrada: () =>
            of({
              formasCompra: [
                {
                  id: 'a1',
                  nome: 'Frasco',
                  quantidadeEquivalentePorEmbalagemDecimal: '100',
                  ativa: true,
                },
              ],
            }),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(AdminComprasMateriasPrimasComponent);
  const root = fixture.nativeElement as HTMLElement;
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function clicar(texto: string) {
    const botao = [...root.querySelectorAll('button')].find((b) => b.textContent.trim() === texto);
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
  return { compras, root, clicar, preencher };
}

describe('Compras de matérias-primas', () => {
  it('filtra listagem, abre detalhe e confirma cancelamento por versão', async () => {
    const { root, compras, clicar, preencher } = await montar();
    expect(root.querySelector('a[href="/admin/compras"]')?.getAttribute('aria-current')).toBe(
      'page',
    );
    expect(root.textContent).toContain('R$ 25,81');
    await preencher('input[type=search]', 'vale');
    expect(root.textContent).not.toContain('Vidros');
    await preencher('select', 'Cancelada');
    expect(root.textContent).toContain('Nenhuma compra encontrada.');
    await preencher('select', 'Rascunho');
    await clicar('Abrir compra');
    expect(compras.obter).toHaveBeenCalledWith('c1');
    expect(root.textContent).toContain('2 × 100 ml = 200 ml');
    await clicar('Cancelar compra');
    expect(compras.cancelar).not.toHaveBeenCalled();
    await clicar('Confirmar cancelamento');
    expect(compras.cancelar).toHaveBeenCalledWith('c1', 'v1');
    expect(root.textContent).toContain('Compra cancelada.');
  });

  it('valida o rascunho e envia apresentação e kit com custo único ou desconhecido', async () => {
    const { root, compras, clicar, preencher } = await montar();
    await clicar('Nova compra');
    expect(root.textContent).toContain('Salvar o rascunho não atualiza o estoque.');
    expect(root.textContent).not.toContain('Fora do catálogo');
    await clicar('Salvar rascunho');
    expect(compras.salvar).not.toHaveBeenCalled();
    expect(root.querySelector('[role=alert]')?.textContent).toContain('fornecedor');
    await preencher('[name=fornecedor]', 'f1');
    await preencher('[name=material-0-0]', 'i1');
    await preencher('[name=forma-0-0]', 'a1');
    await preencher('[name=qtd-0-0]', '2');
    await preencher('[name=valor-0]', '0');
    expect(root.textContent).toContain('2 × 100 ml = 200 ml');

    await clicar('Adicionar kit misto');
    await preencher('[name=kit-1]', 'Ervas');
    await preencher('[name=material-1-0]', 'i1');
    await preencher('[name=qtd-1-0]', '10');
    await preencher('[name=material-1-1]', 'i2');
    await preencher('[name=qtd-1-1]', '7,5');
    expect(root.textContent).toContain('Total da compra: Pendente');
    await preencher('[name=valor-1]', '19,90');
    expect(root.textContent).toContain('R$ 19,90');
    await clicar('Salvar rascunho');
    expect(compras.salvar).toHaveBeenCalledWith(
      null,
      expect.objectContaining({
        linhas: [
          expect.objectContaining({ quantidade: '2', valorTotal: '0' }),
          expect.objectContaining({
            kit: 'Ervas',
            valorTotal: '19.9',
            componentes: [
              expect.objectContaining({ quantidade: '10' }),
              expect.objectContaining({ quantidade: '7.5' }),
            ],
          }),
        ],
      }),
      expect.any(String),
    );
    expect(root.textContent).toContain('Rascunho salvo.');
  });

  it('preserva centavos grandes e rejeita arredondamento implícito', () => {
    expect(exibirReais(decimalEscalado('9999999999999999,99', 2, 16)!)).toBe(
      'R$ 9.999.999.999.999.999,99',
    );
    expect(decimalEscalado('1,004', 2, 16)).toBeNull();
  });
});
