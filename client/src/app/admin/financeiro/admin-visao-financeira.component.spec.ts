import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { FinanceiroService, ResumoFinanceiro } from '../../core/financeiro.service';
import { AdminVisaoFinanceiraComponent } from './admin-visao-financeira.component';

const fluxo = {
  dataInicial: null,
  dataFinal: null,
  entradasRecebidasCentavos: '9223372036854775808',
  saidasPagasCentavos: '100',
  movimentacaoLiquidaCentavos: '9223372036854775708',
};
const posicaoAtual = { aReceberCentavos: '10', aPagarCentavos: '105', comprasValorAConferir: 2 };

describe('AdminVisaoFinanceiraComponent', () => {
  const resumo = vi.fn();
  let fixture: ComponentFixture<AdminVisaoFinanceiraComponent>;

  beforeEach(() => {
    resumo.mockReset().mockReturnValue(of({ fluxoPeriodo: fluxo, posicaoAtual }));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: FinanceiroService, useValue: { resumo } }],
    });
  });
  afterEach(() => vi.useRealTimers());

  async function render() {
    fixture = TestBed.createComponent(AdminVisaoFinanceiraComponent);
    await atualizar();
  }
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  function texto(seletor = 'section') {
    return (fixture.nativeElement as HTMLElement).querySelector(seletor)?.textContent ?? '';
  }
  async function clicar(rotulo: string) {
    const botao = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === rotulo)!;
    botao.click();
    await atualizar();
  }
  async function preencher(nome: string, valor: string) {
    const campo = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '[name="' + nome + '"]',
    )!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  }

  it('exibe indicadores positivos com centavos acima de long.MaxValue e destaca valor a conferir', async () => {
    await render();
    expect(texto()).toContain('R$ 92.233.720.368.547.758,08');
    expect(texto()).toContain('R$ 92.233.720.368.547.757,08');
    expect(texto()).toContain('Não representa lucro nem saldo bancário');
    expect(texto('[aria-labelledby="posicao-financeira-titulo"]')).toMatch(/valor a conferir:\s*2/);
    expect(texto()).toContain('o total a pagar não inclui esses valores');
  });

  it('mostra alertas do Dashboard a partir dos agregados e aponta para filtros exatos', async () => {
    const vazio = { quantidade: 0, valorCentavos: '0' };
    const valor = { quantidade: 1, valorCentavos: '1990' };
    resumo.mockReturnValue(of({ fluxoPeriodo: fluxo, posicaoAtual: { ...posicaoAtual,
      contasEmAberto: { dataOperacional: '2026-11-21',
        aPagar: { ...valor, vencidas: valor, venceHoje: vazio, vencendoEmBreve: vazio, proximosTresDias: vazio },
        aReceber: { ...valor, vencidas: vazio, venceHoje: valor, vencendoEmBreve: valor, proximosTresDias: vazio },
      },
    } }));
    fixture = TestBed.createComponent(AdminVisaoFinanceiraComponent);
    fixture.componentRef.setInput('compacto', true);
    await atualizar();
    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('.alertas-contas a')];
    expect(links.map(link => new URL(link.href).searchParams.get('classificacao'))).toEqual(['Vencidas', 'VenceHoje']);
    expect(links.map(link => new URL(link.href).searchParams.get('direcao'))).toEqual(['APagar', 'AReceber']);
    expect(links[0].textContent).toContain('R$ 19,90');
  });

  it('envia os limites civis de mês atual e anterior, incluindo virada de ano', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 0, 15, 23, 45));
    await render();
    await clicar('Mês atual');
    expect(resumo).toHaveBeenLastCalledWith({ dataInicial: '2026-01-01', dataFinal: '2026-01-31' });
    await clicar('Mês anterior');
    expect(resumo).toHaveBeenLastCalledWith({ dataInicial: '2025-12-01', dataFinal: '2025-12-31' });
    await clicar('Todo o período');
    expect(resumo).toHaveBeenLastCalledWith({ dataInicial: '', dataFinal: '' });
  });

  it('valida o intervalo e atualiza apenas o período apresentado, com líquido negativo exato', async () => {
    await render();
    const depois = texto('[aria-labelledby="posicao-financeira-titulo"]');
    await preencher('dataInicial', '2026-10-02');
    await preencher('dataFinal', '2026-10-01');
    await clicar('Aplicar período');
    expect(resumo).toHaveBeenCalledTimes(1);
    expect(texto('[role=alert]')).toContain('intervalo válido');

    resumo.mockReturnValueOnce(
      of({
        fluxoPeriodo: {
          ...fluxo,
          dataInicial: '2026-10-01',
          dataFinal: '2026-10-01',
          movimentacaoLiquidaCentavos: '-1',
        },
        posicaoAtual,
      }),
    );
    await preencher('dataInicial', '2026-10-01');
    await clicar('Aplicar período');
    expect(resumo).toHaveBeenLastCalledWith({ dataInicial: '2026-10-01', dataFinal: '2026-10-01' });
    expect(texto()).toMatch(/01\/10\/2026\s*a\s*01\/10\/2026/);
    expect(texto()).toContain('− R$ 0,01');
    expect(texto('[aria-labelledby="posicao-financeira-titulo"]')).toBe(depois);
  });

  it('mostra carregamento e ausência de movimentações com a posição atual', async () => {
    const resultado = new Subject<ResumoFinanceiro>();
    resumo.mockReturnValueOnce(resultado);
    await render();
    expect(texto('[role=status]')).toContain('Carregando');
    resultado.next({
      fluxoPeriodo: {
        ...fluxo,
        entradasRecebidasCentavos: '0',
        saidasPagasCentavos: '0',
        movimentacaoLiquidaCentavos: '0',
      },
      posicaoAtual,
    });
    resultado.complete();
    await atualizar();
    expect(texto('[role=status]')).toContain('Nenhuma movimentação');
    expect(texto()).toContain('R$ 1,05');
  });

  it('mostra erro da API e oferece nova consulta', async () => {
    resumo.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));
    await render();
    expect(texto('[role=alert]')).toContain('Tente novamente');
    await clicar('Tentar novamente');
    expect(texto()).toContain('Entradas recebidas');
  });
});
