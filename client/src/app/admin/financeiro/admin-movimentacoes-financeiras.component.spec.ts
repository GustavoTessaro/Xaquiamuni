import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { FinanceiroService, MovimentacaoFinanceira } from '../../core/financeiro.service';
import { AdminMovimentacoesFinanceirasComponent } from './admin-movimentacoes-financeiras.component';

const itens: MovimentacaoFinanceira[] = [
  {
    eventoId: 'p',
    recursoId: 'pedido-id',
    data: '2026-11-01',
    origem: 'PedidoSite',
    tipo: 'Entrada',
    naturezaData: 'ConfirmacaoUtc',
    valorCentavos: '9223372036854775807',
    descricao: 'Pedido confirmado',
    meio: 'Pix',
  },
  {
    eventoId: 'v',
    recursoId: 'venda-id',
    data: '2026-11-01',
    origem: 'VendaExterna',
    tipo: 'Entrada',
    naturezaData: 'Efetiva',
    valorCentavos: '100',
    descricao: 'Recebimento',
    meio: 'Dinheiro',
  },
  {
    eventoId: 'p2',
    recursoId: 'compra-id',
    data: '2026-11-01',
    origem: 'PagamentoCompra',
    tipo: 'Saida',
    naturezaData: 'Efetiva',
    valorCentavos: '101',
    descricao: 'Pagamento',
    meio: 'Transferencia',
  },
  {
    eventoId: 'l',
    recursoId: 'lancamento-id',
    data: '2026-10-31',
    origem: 'LancamentoManual',
    tipo: 'Saida',
    naturezaData: 'Efetiva',
    valorCentavos: '1',
    descricao: 'Ajuste',
    meio: null,
  },
];

describe('AdminMovimentacoesFinanceirasComponent', () => {
  let fixture: ComponentFixture<AdminMovimentacoesFinanceirasComponent>;
  const movimentacoes = vi.fn();
  const pagina = { pagina: 1, tamanhoPagina: 50, temProxima: true, itens };

  beforeEach(() => {
    movimentacoes.mockReset().mockReturnValue(of(pagina));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: FinanceiroService, useValue: { movimentacoes } }],
    });
  });

  async function render() {
    fixture = TestBed.createComponent(AdminMovimentacoesFinanceirasComponent);
    await atualizar();
  }
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  function texto() {
    return fixture.nativeElement.textContent as string;
  }
  function botao(nome: string) {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === nome,
    )!;
  }
  async function clicar(nome: string) {
    botao(nome).click();
    await atualizar();
  }
  async function preencher(nome: string, valor: string) {
    const campo = fixture.nativeElement.querySelector('[name="' + nome + '"]');
    campo.value = valor;
    campo.dispatchEvent(new Event(campo.tagName === 'SELECT' ? 'change' : 'input'));
    await atualizar();
  }

  it('mostra as quatro origens, dados civis e centavos exatos com sinal', async () => {
    await render();
    for (const parte of [
      'Pedidos do site',
      'Vendas externas',
      'Pagamentos de compras',
      'Lançamentos manuais',
      '+ R$ 92.233.720.368.547.758,07',
      '− R$ 0,01',
      'Confirmação do PIX (UTC)',
      'Data efetiva',
      '01/11/2026',
      '31/10/2026',
      'Transferência',
    ])
      expect(texto()).toContain(parte);
    expect(fixture.nativeElement.querySelector('time').getAttribute('datetime')).toBe('2026-11-01');
  });

  it('envia filtros ao backend, pagina com os filtros aplicados e desativa Próxima na última página', async () => {
    await render();
    const filtros = {
      dataInicial: '2026-11-01',
      dataFinal: '2026-11-01',
      tipo: 'Saida',
      origem: 'PagamentoCompra',
    };
    for (const [campo, valor] of Object.entries(filtros)) await preencher(campo, valor);
    await clicar('Aplicar filtros');
    expect(movimentacoes).toHaveBeenLastCalledWith(filtros, 1);
    movimentacoes.mockReturnValueOnce(of({ ...pagina, pagina: 2, temProxima: false }));
    await clicar('Próxima');
    expect(movimentacoes).toHaveBeenLastCalledWith(filtros, 2);
    expect(botao('Próxima').disabled).toBe(true);
    await clicar('Anterior');
    expect(movimentacoes).toHaveBeenLastCalledWith(filtros, 1);
  });

  it('rejeita intervalo invertido e diferencia lista vazia de filtro sem resultado', async () => {
    movimentacoes.mockReturnValue(of({ ...pagina, itens: [], temProxima: false }));
    await render();
    expect(texto()).toContain('Nenhuma movimentação nesta página');
    await preencher('dataInicial', '2026-11-02');
    await preencher('dataFinal', '2026-11-01');
    await clicar('Aplicar filtros');
    expect(movimentacoes).toHaveBeenCalledTimes(1);
    expect(texto()).toContain('intervalo válido');
    await preencher('dataFinal', '2026-11-02');
    await clicar('Aplicar filtros');
    expect(texto()).toContain('Nenhuma movimentação para os filtros');
  });

  it('abre os destinos dos eventos, incluindo a venda externa', async () => {
    await render();
    const acao = vi.fn();
    fixture.componentInstance.recurso.subscribe(acao);
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(
      '/admin/pedidos/pedido-id',
    );
    expect(
      fixture.nativeElement.querySelector('a[href="/admin/vendas-externas/venda-id"]'),
    ).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('a')).toHaveLength(2);
    await clicar('Ver compra');
    await clicar('Ver lançamento');
    expect(acao.mock.calls).toEqual([
      [{ secao: 'compras', id: 'compra-id' }],
      [{ secao: 'lancamentos', id: 'lancamento-id' }],
    ]);
  });

  it.each([403, 404, 503, 0])('oferece nova consulta após erro %s', async (status) => {
    movimentacoes.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status })));
    await render();
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
    await clicar('Tentar novamente');
    expect(texto()).toContain('Pedido confirmado');
  });

  it('redireciona ao login após 401', async () => {
    const router = TestBed.inject(Router);
    const navegar = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    movimentacoes.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 401 })));
    await render();
    expect(navegar).toHaveBeenCalledWith('/admin/login');
  });
});
