import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { InsumosService } from '../../core/admin-estoque/insumos.service';
import {
  ComprasMateriasPrimasService,
  DetalheCompra,
  RecebimentoCompra,
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

const compraId = '09000000-0000-4000-8000-000000000001';
const versao = '09000000-0000-4000-8000-000000000002';
const componenteId = '09000000-0000-4000-8000-000000000003';
const ator = 'admin-teste';
const storageKey = 'xaquiamuni:compras:recebimento:v1:' + ator;
const chaveCriacao = '09000000-0000-4000-8000-000000000010';
const criacaoKey = 'xaquiamuni:compras:criacao:v1:' + ator;
const criacaoOriginal = {
  chave: chaveCriacao,
  fornecedor: 'Ervas do Vale',
  materiais: { [compraId]: 'Alecrim', [componenteId]: 'Guiné' },
  apresentacoes: { [versao]: 'Pacote' },
  corpo: {
    fornecedorId: compraId,
    dataCompra: '2026-11-19',
    versao: null,
    linhas: [
      {
        kit: 'Ervas juntas',
        quantidade: '1',
        valorTotal: '0',
        componentes: [
          { insumoId: compraId, apresentacaoId: versao, quantidade: '2', snapshotId: null },
          {
            insumoId: componenteId,
            apresentacaoId: null,
            quantidade: '7.123456',
            snapshotId: null,
          },
        ],
      },
    ],
  },
};
const rascunho: DetalheCompra = {
  ...detalhe,
  resumo: { ...resumo, id: compraId },
  versao,
  linhas: [
    {
      ...detalhe.linhas[0],
      componentes: [{ ...detalhe.linhas[0].componentes[0], id: componenteId }],
    },
    {
      kit: 'Ervas juntas',
      quantidade: '1',
      valorTotal: '10',
      componentes: [
        {
          ...detalhe.linhas[0].componentes[0],
          id: 'kit-1',
          insumoId: 'i2',
          nomeInsumo: 'Alecrim',
          nomeApresentacao: null,
          quantidade: '3',
          quantidadeBase: '3',
          unidadeBase: 'Grama',
        },
        {
          ...detalhe.linhas[0].componentes[0],
          id: 'kit-2',
          insumoId: 'i3',
          nomeInsumo: 'Guiné',
          nomeApresentacao: null,
          quantidade: '1',
          quantidadeBase: '1',
          unidadeBase: 'Grama',
        },
      ],
    },
  ],
};
const recebida: DetalheCompra = {
  ...rascunho,
  resumo: { ...rascunho.resumo, situacao: 'Recebida' },
  recebidaPorId: ator,
  recebidaEmUtc: '2026-11-19T15:00:00Z',
};
const resultado: RecebimentoCompra = {
  compraId,
  situacao: 'Recebida',
  versao,
  recebidaPorId: ator,
  recebidaEmUtc: '2026-11-19T15:00:00Z',
  entradas: [],
};

beforeEach(() => sessionStorage.clear());
afterEach(() => sessionStorage.clear());

async function montar(
  atual = detalhe,
  consultarCriacao = vi.fn<ComprasMateriasPrimasService['consultarCriacao']>(() =>
    throwError(() => new HttpErrorResponse({ status: 404 })),
  ),
) {
  const compras = {
    listar: vi.fn(() =>
      of([atual.resumo, { ...resumo, id: 'c2', situacao: 'Cancelada', nomeFornecedor: 'Vidros' }]),
    ),
    obter: vi.fn(() => of(atual)),
    consultarCriacao,
    receber: vi.fn(() => of(resultado)),
    salvar: vi.fn(() => of(detalhe)),
    cancelar: vi.fn(() => of(detalhe)),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AdminAuthService, useValue: { getCurrentSession: () => of({ userId: ator }) } },
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
          obterParaEntrada: (id: string) =>
            of({
              resumo: { controlaLote: id !== 'i3', validadeObrigatoria: id === 'i1' },
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
  return { compras, root, clicar, preencher, atualizar };
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

  it('guarda antes de enviar, bloqueia cliques e preserva o pedido após 503', async () => {
    const { compras, root, clicar, preencher, atualizar } = await montar();
    await clicar('Nova compra');
    await preencher('[name=fornecedor]', 'f1');
    await preencher('[name=material-0-0]', 'i1');
    await preencher('[name=qtd-0-0]', '2');
    const resposta = new Subject<DetalheCompra>();
    compras.salvar.mockImplementationOnce(() => {
      expect(sessionStorage.getItem(criacaoKey)).not.toBeNull();
      return resposta;
    });
    const salvar = [...root.querySelectorAll('button')].find(
      (b) => b.textContent.trim() === 'Salvar rascunho',
    )!;
    salvar.click();
    salvar.click();
    resposta.error(new HttpErrorResponse({ status: 503 }));
    await atualizar();
    expect(compras.salvar).toHaveBeenCalledTimes(1);
    const { chave, corpo } = JSON.parse(sessionStorage.getItem(criacaoKey)!);
    expect(compras.salvar).toHaveBeenCalledWith(null, corpo, chave);
    expect(root.textContent).toContain('Criação de compra pendente');
    expect(root.textContent).not.toContain('Nova compra');
  });

  it('localiza rascunho já criado após recarga, sem POST', async () => {
    sessionStorage.setItem(criacaoKey, JSON.stringify(criacaoOriginal));
    const consulta = vi.fn<ComprasMateriasPrimasService['consultarCriacao']>(() => of(rascunho));
    const { compras, root } = await montar(rascunho, consulta);
    expect(consulta).toHaveBeenCalledExactlyOnceWith(chaveCriacao);
    expect(compras.salvar).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Rascunho localizado');
    expect(root.textContent).toContain('Ervas juntas');
    expect(sessionStorage.getItem(criacaoKey)).toBeNull();
  });

  it('após 404, mostra os dados originais e reenvia manualmente o mesmo kit e a mesma chave', async () => {
    sessionStorage.setItem(criacaoKey, JSON.stringify(criacaoOriginal));
    const { compras, root, clicar } = await montar();
    expect(compras.salvar).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Ervas juntas');
    expect(root.textContent).toContain('7,123456');
    expect(root.textContent).toContain('R$ 0,00');
    expect(root.querySelector('[name=fornecedor]')).toBeNull();
    await clicar('Reenviar criação');
    expect(compras.salvar).toHaveBeenCalledExactlyOnceWith(
      null,
      criacaoOriginal.corpo,
      chaveCriacao,
    );
    expect(sessionStorage.getItem(criacaoKey)).toBeNull();
  });

  it('preserva a tentativa sem reenvio quando a consulta falha e exige confirmação para descartá-la', async () => {
    sessionStorage.setItem(criacaoKey, JSON.stringify(criacaoOriginal));
    const consulta = vi.fn<ComprasMateriasPrimasService['consultarCriacao']>(() =>
      throwError(() => new HttpErrorResponse({ status: 0 })),
    );
    const { compras, clicar, root } = await montar(detalhe, consulta);
    expect(root.textContent).not.toContain('Reenviar criação');
    await clicar('Descartar tentativa');
    expect(root.textContent).toContain('pode gerar um rascunho duplicado');
    expect(sessionStorage.getItem(criacaoKey)).not.toBeNull();
    await clicar('Descartar e iniciar outra compra');
    expect(root.querySelector('[name=fornecedor]')).not.toBeNull();
    expect(sessionStorage.getItem(criacaoKey)).toBeNull();
    expect(compras.salvar).not.toHaveBeenCalled();
  });

  it('revisa kit e material, valida datas e evita confirmação duplicada', async () => {
    const { root, compras, clicar, preencher } = await montar(rascunho);
    await clicar('Abrir compra');
    await clicar('Confirmar recebimento');
    expect(root.textContent).toContain('Ervas juntas');
    expect(root.textContent).toContain('Valor do kit: R$ 10,00');
    expect(root.textContent).toMatch(/Alecrim\s+3 g/);
    expect(root.querySelectorAll('input[type=date]')).toHaveLength(2);
    expect(
      root.querySelector('input[type=date][required]')?.closest('label')?.textContent,
    ).toContain('Extrato');
    const botao = () =>
      [...root.querySelectorAll('button')].find(
        (b) => b.textContent.trim() === 'Confirmar e atualizar estoque',
      )!;
    expect(botao().disabled).toBe(true);
    await preencher('input[type=date][required]', '2026-12-20');
    expect(botao().disabled).toBe(false);

    const resposta = new Subject<RecebimentoCompra>();
    compras.receber.mockReturnValue(resposta);
    const form = botao().form!;
    await clicar('Confirmar e atualizar estoque');
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    expect(compras.receber).toHaveBeenCalledTimes(1);
    expect(compras.receber).toHaveBeenCalledWith(
      compraId,
      {
        versao,
        validades: [{ componenteId, dataValidade: '2026-12-20' }],
      },
      expect.stringMatching(/^[0-9a-f-]{36}$/),
    );
    expect(root.textContent).not.toContain('O estoque foi atualizado.');
    resposta.next(resultado);
    resposta.complete();
    await vi.waitFor(() => expect(root.textContent).toContain('O estoque foi atualizado.'));
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(root.textContent).toContain('Responsável: Você');
    expect(root.querySelector('a[href="/admin/estoque/insumos/i1/consultas"]')).not.toBeNull();
    expect(root.textContent).not.toContain('Salvar rascunho');
  });

  it.each(['Recebida', 'Cancelada'] as const)('abre %s somente para consulta', async (situacao) => {
    const { root, compras, clicar } = await montar({
      ...recebida,
      resumo: { ...recebida.resumo, situacao },
    });
    await clicar('Abrir compra');
    expect(root.textContent).toContain(situacao);
    for (const acao of [
      'Salvar rascunho',
      'Cancelar compra',
      'Confirmar recebimento',
      'Confirmar e atualizar estoque',
    ])
      expect(root.textContent).not.toContain(acao);
    expect(compras.receber).not.toHaveBeenCalled();
  });

  it.each([400, 404, 409])(
    'apresenta a resposta %s e bloqueia reenvio de versão desatualizada',
    async (status) => {
      const { root, compras, clicar, preencher } = await montar(rascunho);
      await clicar('Abrir compra');
      await clicar('Confirmar recebimento');
      await preencher('input[type=date][required]', '2026-12-20');
      const mensagem =
        status === 400
          ? 'Validade inválida'
          : status === 404
            ? 'Compra não encontrada'
            : 'Versão desatualizada';
      compras.receber.mockReturnValueOnce(
        throwError(
          () =>
            new HttpErrorResponse({
              status,
              error: { detail: mensagem, errors: { compra: [mensagem] } },
            }),
        ),
      );
      await clicar('Confirmar e atualizar estoque');
      expect(root.querySelector('[role=alert]')?.textContent).toContain(mensagem);
      expect(root.textContent).not.toContain('O estoque foi atualizado.');
      if (status === 409) {
        expect(root.textContent).not.toContain('Reenviar tentativa');
        compras.obter.mockReturnValueOnce(of({ ...rascunho, versao: compraId }));
        await clicar('Conferir situação');
        expect(root.textContent).not.toContain('Reenviar tentativa');
        expect(root.textContent).toContain('Abrir rascunho atualizado');
        expect(sessionStorage.getItem(storageKey)).toBeNull();
      }
    },
  );

  it('persiste a tentativa após falha de rede', async () => {
    const { root, compras, clicar, preencher } = await montar(rascunho);
    await clicar('Abrir compra');
    await clicar('Confirmar recebimento');
    await preencher('input[type=date][required]', '2026-12-20');
    compras.receber.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    await clicar('Confirmar e atualizar estoque');
    const [, corpo, chave] = compras.receber.mock.calls[0] as unknown as [string, object, string];
    expect(JSON.parse(sessionStorage.getItem(storageKey)!)).toEqual({ compraId, corpo, chave });
    expect(root.textContent).toContain('Confira a situação');
    expect(root.textContent).not.toContain('O estoque foi atualizado.');
  });

  it.each([rascunho, recebida])(
    'recupera tentativa consultando o servidor antes de decidir',
    async (atual) => {
      const chave = '09000000-0000-4000-8000-000000000004';
      const corpo = { versao, validades: [{ componenteId, dataValidade: '2026-12-20' }] };
      sessionStorage.setItem(storageKey, JSON.stringify({ compraId, corpo, chave }));
      const { root, compras, clicar } = await montar(atual);
      expect(compras.obter).toHaveBeenCalledWith(compraId);
      expect(compras.receber).not.toHaveBeenCalled();
      if (atual === recebida) {
        expect(root.textContent).toContain('O estoque foi atualizado.');
        expect(sessionStorage.getItem(storageKey)).toBeNull();
      } else {
        expect(root.textContent).toContain('20/12/2026');
        await clicar('Reenviar tentativa');
        expect(compras.receber).toHaveBeenCalledExactlyOnceWith(compraId, corpo, chave);
      }
    },
  );

  it('preserva centavos grandes e rejeita arredondamento implícito', () => {
    expect(exibirReais(decimalEscalado('9999999999999999,99', 2, 16)!)).toBe(
      'R$ 9.999.999.999.999.999,99',
    );
    expect(decimalEscalado('1,004', 2, 16)).toBeNull();
  });
});
