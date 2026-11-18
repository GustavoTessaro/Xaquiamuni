import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { CarrinhoService, formatarCentavos } from './carrinho.service';
import { ProdutoCatalogoDetalhe } from './catalogo-publico.service';

const chave = 'xaquiamuni:carrinho:v1';
const imagem = {
  id: 'foto-1',
  conteudoUrl: '/api/catalogo/produtos/produto-1/imagens/foto-1/conteudo',
  ordemExibicao: 1,
  principal: true,
  opcaoIds: [],
};
const opcao = {
  id: 'variante-1',
  identificacao: 'Lavanda',
  unidadeVenda: 'UN',
  precoVenda: 0.1,
  principal: true,
  pesoLiquidoGramas: null,
  volumeLiquidoMililitros: null,
  caracteristicas: [],
  disponivel: true,
};
const produto: ProdutoCatalogoDetalhe = {
  id: 'produto-1',
  nome: 'Sabonete',
  descricao: null,
  categoriaId: 'categoria-1',
  categoriaNome: 'Sabonetes',
  requerSelecaoDeOpcao: true,
  opcaoPrincipalId: opcao.id,
  fotoPrincipal: imagem,
  galeria: [imagem],
  caracteristicas: [],
  disponivel: true,
  opcoes: [opcao, { ...opcao, id: 'variante-2', identificacao: 'Capim-limão', precoVenda: 0.2 }],
};

describe('CarrinhoService', () => {
  beforeEach(() => localStorage.removeItem(chave));
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(chave);
  });

  it('agrupa a mesma variante, separa variantes do produto e soma centavos inteiros', () => {
    const carrinho = TestBed.inject(CarrinhoService);
    expect(carrinho.adicionar(produto, 'variante-1')).toBe('ok');
    expect(carrinho.adicionar(produto, 'variante-1')).toBe('ok');
    expect(carrinho.adicionar(produto, 'variante-2')).toBe('ok');
    expect(carrinho.itens().map((item) => [item.varianteId, item.quantidade])).toEqual([
      ['variante-1', 2],
      ['variante-2', 1],
    ]);
    expect(carrinho.itens()[0]).toMatchObject({
      produtoId: produto.id,
      imagem: imagem.conteudoUrl,
    });
    expect(carrinho.quantidadeTotal()).toBe(3);
    expect(carrinho.subtotalCentavos()).toBe(40);
    expect(formatarCentavos(40).replace(/\s+/g, ' ')).toBe('R$ 0,40');
  });

  it('valida quantidades, recalcula, remove e esvazia', () => {
    const carrinho = TestBed.inject(CarrinhoService);
    carrinho.adicionar(produto, opcao.id);
    for (const valor of [0, -1, 1.5, Infinity, Number.MAX_SAFE_INTEGER]) {
      expect(carrinho.alterarQuantidade(opcao.id, valor)).toBe('invalido');
    }
    expect(carrinho.alterarQuantidade(opcao.id, 3)).toBe('ok');
    expect(carrinho.subtotalCentavos()).toBe(30);
    carrinho.remover(opcao.id);
    expect(carrinho.itens()).toEqual([]);
    carrinho.adicionar(produto, opcao.id);
    carrinho.esvaziar();
    expect(carrinho.quantidadeTotal()).toBe(0);
    expect(JSON.parse(localStorage.getItem(chave)!)).toEqual([]);
  });

  it('restaura o carrinho sem persistir disponibilidade nem dados do cliente', () => {
    const carrinho = TestBed.inject(CarrinhoService);
    carrinho.adicionar(
      { ...produto, email: 'privado@teste.invalid' } as ProdutoCatalogoDetalhe,
      opcao.id,
    );
    carrinho.alterarQuantidade(opcao.id, 2);
    const gravado = localStorage.getItem(chave)!;
    expect(gravado).not.toContain('disponivel');
    expect(gravado).not.toContain('privado');

    TestBed.resetTestingModule();
    const restaurado = TestBed.inject(CarrinhoService);
    expect(restaurado.quantidadeTotal()).toBe(2);
    expect(restaurado.subtotalCentavos()).toBe(20);
    expect(restaurado.itens()[0]?.disponivel).toBeNull();
  });

  it('ignora JSON corrompido, registros inválidos e duplicados e rejeita URL externa', () => {
    localStorage.setItem(chave, '{inválido');
    expect(TestBed.inject(CarrinhoService).itens()).toEqual([]);
    TestBed.resetTestingModule();
    const base = {
      produtoId: produto.id,
      varianteId: opcao.id,
      nome: produto.nome,
      variante: opcao.identificacao,
      imagem: 'https://outro.invalid/foto',
      precoCentavos: 10,
      quantidade: 1,
    };
    localStorage.setItem(
      chave,
      JSON.stringify([
        null,
        { ...base, quantidade: -2 },
        { ...base, produtoId: '../privado' },
        base,
        { ...base, quantidade: 2 },
        { ...base, varianteId: 'variante-2', precoCentavos: 1.2 },
      ]),
    );
    const restaurado = TestBed.inject(CarrinhoService);
    expect(restaurado.itens()).toEqual([{ ...base, imagem: null, disponivel: null }]);
  });

  it('recusa disponibilidade desconhecida, opção esgotada e sinaliza mudanças para itens existentes', () => {
    const carrinho = TestBed.inject(CarrinhoService);
    expect(carrinho.adicionar(produto, 'ausente')).toBe('indisponivel');
    carrinho.adicionar(produto, opcao.id);
    carrinho.alterarQuantidade(opcao.id, 2);
    const esgotado = { ...produto, opcoes: [{ ...opcao, disponivel: false }] };
    carrinho.atualizarDisponibilidade(esgotado);
    expect(carrinho.adicionar(esgotado, opcao.id)).toBe('indisponivel');
    expect(carrinho.itens()[0]?.disponivel).toBe(false);
    expect(carrinho.alterarQuantidade(opcao.id, 3)).toBe('indisponivel');
    expect(carrinho.alterarQuantidade(opcao.id, 1)).toBe('ok');
    carrinho.sinalizarDisponibilidade(produto.id, null);
    expect(carrinho.alterarQuantidade(opcao.id, 2)).toBe('indisponivel');
  });

  it('mantém o uso em memória quando o navegador impede a gravação', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('armazenamento indisponível');
    });
    const carrinho = TestBed.inject(CarrinhoService);
    expect(carrinho.adicionar(produto, opcao.id)).toBe('ok');
    expect(carrinho.quantidadeTotal()).toBe(1);
    expect(carrinho.persistenciaIndisponivel()).toBe(true);
  });
});
