import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { ComprasMateriasPrimasService } from '../../core/compras-materias-primas.service';
import {
  CompraFinanceira,
  FinanceiroService,
  LancamentoFinanceiro,
} from '../../core/financeiro.service';
import { AdminFinanceiroComponent, centavosFinanceiros } from './admin-financeiro.component';

const id = '11111111-1111-4111-8111-111111111111';
const chaveStorage = 'xaquiamuni:financeiro:v1:admin';
const lancamento: LancamentoFinanceiro = {
  id,
  tipo: 'Entrada',
  descricao: 'Aporte',
  categoria: 'Outros',
  valorCentavos: '9223372036854775807',
  data: '2026-11-20',
  meio: 'Pix',
  responsavelId: id,
  criadoEmUtc: '2026-11-20T12:00:00Z',
  canceladoPorId: null,
  canceladoEmUtc: null,
  motivoCancelamento: null,
};
const compra: CompraFinanceira = {
  compraId: id,
  fornecedorId: id,
  nomeFornecedor: 'Fornecedor teste',
  dataCompra: '2026-11-20',
  situacaoCompra: 'Recebida',
  situacaoFinanceira: 'Pendente',
  composicaoValor: 'Conhecido',
  totalCentavos: '10000',
  pagoCentavos: '0',
  saldoCentavos: '10000',
};

describe('AdminFinanceiroComponent', () => {
  let fixture: ComponentFixture<AdminFinanceiroComponent>;
  const api = {
    resumo: vi.fn(),
    movimentacoes: vi.fn(),
    consultarTentativa: vi.fn(),
    lancamentos: vi.fn(),
    lancamento: vi.fn(),
    compra: vi.fn(),
    pagamentos: vi.fn(),
    enviar: vi.fn(),
  };
  const rota = { snapshot: { queryParamMap: convertToParamMap({}) } };
  const sessao = { userId: 'admin' };
  const movimento = { valorCentavos: '2500', meio: 'Pix', data: '2026-11-20' };
  const comandos = {
    criar: {
      tipo: 'criar',
      chave: id,
      corpo: { ...movimento, tipo: 'Entrada', categoria: 'Teste', descricao: 'Aporte' },
    },
    cancelar: { tipo: 'cancelar', chave: id, id, corpo: { motivo: 'Correção' } },
    pagar: { tipo: 'pagar', chave: id, id, corpo: { ...movimento, referencia: null } },
  };

  beforeEach(() => {
    sessionStorage.removeItem(chaveStorage);
    vi.resetAllMocks();
    api.resumo.mockReturnValue(of(null));
    api.movimentacoes.mockReturnValue(
      of({ pagina: 1, tamanhoPagina: 50, temProxima: false, itens: [] }),
    );
    sessao.userId = 'admin';
    api.consultarTentativa.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    rota.snapshot.queryParamMap = convertToParamMap({});
    api.lancamentos.mockReturnValue(of([lancamento]));
    api.lancamento.mockReturnValue(of(lancamento));
    api.compra.mockReturnValue(of(compra));
    api.pagamentos.mockReturnValue(of([]));
    api.enviar.mockReturnValue(of(lancamento));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: rota },
        {
          provide: AdminAuthService,
          useValue: { getCurrentSession: () => of(sessao) },
        },
        { provide: FinanceiroService, useValue: api },
        {
          provide: ComprasMateriasPrimasService,
          useValue: { listar: () => of([{ ...compra, id }]) },
        },
      ],
    });
  });
  afterEach(() => sessionStorage.removeItem(chaveStorage));

  async function render(abrirLancamentos = true) {
    fixture = TestBed.createComponent(AdminFinanceiroComponent);
    await atualizar();
    if (
      abrirLancamentos &&
      !botao('Visão geral')?.disabled &&
      botao('Visão geral')?.getAttribute('aria-current') === 'page'
    ) {
      await clicar('Lançamentos');
    }
  }

  it('abre Visão geral e preserva as outras seções e o estado de Movimentações', async () => {
    await render(false);
    expect(botao('Visão geral')?.getAttribute('aria-current')).toBe('page');
    expect(botao('Lançamentos')).toBeDefined();
    expect(botao('Pagamentos de compras')).toBeDefined();
    expect(api.resumo).toHaveBeenCalledTimes(1);
    expect(api.movimentacoes).not.toHaveBeenCalled();
    await clicar('Movimentações');
    expect(botao('Movimentações')?.getAttribute('aria-current')).toBe('page');
    const historico = fixture.nativeElement.querySelector('app-admin-movimentacoes-financeiras');
    await clicar('Visão geral');
    await clicar('Movimentações');
    expect(fixture.nativeElement.querySelector('app-admin-movimentacoes-financeiras')).toBe(
      historico,
    );
    expect(api.movimentacoes).toHaveBeenCalledTimes(1);
    expect(api.lancamentos).not.toHaveBeenCalled();
    expect(api.enviar).not.toHaveBeenCalled();
  });
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  function botao(rotulo: string) {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (el) => el.textContent?.trim() === rotulo && !el.closest('[hidden]'),
    );
  }
  async function clicar(rotulo: string) {
    expect(botao(rotulo)).toBeDefined();
    botao(rotulo)!.click();
    await atualizar();
  }
  async function campo(nome: string, valor: string) {
    const el = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>(
        '[name="' + nome + '"]',
      ),
    ).find((campo) => !campo.closest('[hidden]'))!;
    el.value = valor;
    el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input'));
    await atualizar();
  }
  function abrirDireto() {
    rota.snapshot.queryParamMap = convertToParamMap({ compra: id });
  }
  function texto() {
    return fixture.nativeElement.textContent as string;
  }
  async function preencher() {
    await clicar('Novo lançamento');
    await campo('categoria', 'Teste');
    await campo('descricao', 'Novo movimento');
  }

  it('mostra centavos grandes sem perda e carrega a próxima página', async () => {
    api.lancamentos.mockReturnValueOnce(
      of(Array.from({ length: 50 }, (_, i) => ({ ...lancamento, id: String(i) }))),
    );
    await render();
    expect(texto()).toContain('R$ 92.233.720.368.547.758,07');
    await clicar('Próxima');
    expect(api.lancamentos).toHaveBeenLastCalledWith(2);
    expect(texto()).toContain('Página 2');
  });

  it.each(['Entrada', 'Saida'])('cria %s usando centavos como string', async (tipo) => {
    await render();
    await preencher();
    await campo('tipo', tipo);
    await campo('valor', '1.234,56');
    await clicar('Confirmar');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({
        tipo: 'criar',
        chave: expect.stringMatching(/^[a-f0-9-]{36}$/),
        corpo: expect.objectContaining({ tipo, valorCentavos: '123456' }),
      }),
    );
    expect(sessionStorage.getItem(chaveStorage)).toBeNull();
  });

  it('rejeita valor inválido', async () => {
    await render();
    await preencher();
    await campo('valor', '1,001');
    await clicar('Confirmar');
    expect(api.enviar).not.toHaveBeenCalled();
    expect(texto()).toContain('duas casas decimais');
  });

  it('exige motivo e confirmação para cancelar', async () => {
    api.enviar.mockReturnValue(
      of({ ...lancamento, canceladoEmUtc: lancamento.criadoEmUtc, motivoCancelamento: 'Correção' }),
    );
    await render();
    await clicar('Ver detalhe');
    await clicar('Cancelar lançamento');
    await campo('motivo', 'Correção');
    expect(botao('Confirmar')!.disabled).toBe(true);
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLInputElement>('[name=confirmacao]')!
      .click();
    await atualizar();
    await clicar('Confirmar');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({ tipo: 'cancelar', id, corpo: { motivo: 'Correção' } }),
    );
    expect(texto()).toContain('Cancelado');
  });

  it('seleciona uma compra existente e mostra a obrigação pendente', async () => {
    await render();
    await clicar('Pagamentos de compras');
    const seletor = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('select'),
    ).find((campo) => !campo.closest('[hidden]'))!;
    seletor.value = id;
    await clicar('Consultar compra');
    expect(texto()).toContain('Pendente');
    expect(texto()).toContain('R$ 100,00');
  });

  it.each([
    ['Parcial', 'Parcialmente paga', '2500', '7500'],
    ['Quitada', 'Quitada', '10000', '0'],
  ] as const)(
    'mostra compra %s e atualiza o histórico',
    async (situacaoFinanceira, rotulo, pagoCentavos, saldoCentavos) => {
      abrirDireto();
      await render();
      await clicar('Registrar pagamento');
      if (situacaoFinanceira === 'Quitada') await clicar('Usar saldo total');
      else await campo('valor', '25,00');
      api.compra.mockReturnValue(
        of({ ...compra, situacaoFinanceira, pagoCentavos, saldoCentavos }),
      );
      api.pagamentos.mockReturnValue(
        of([
          {
            id,
            data: lancamento.data,
            meio: 'Pix',
            valorCentavos: pagoCentavos,
            referencia: 'Recibo',
          },
        ]),
      );
      await clicar('Confirmar');
      expect(api.enviar).toHaveBeenCalledWith(
        expect.objectContaining({
          tipo: 'pagar',
          id,
          corpo: expect.objectContaining({ valorCentavos: pagoCentavos }),
        }),
      );
      expect(texto()).toContain(rotulo);
      expect(texto()).toContain('Recibo');
      expect(!!botao('Registrar pagamento')).toBe(situacaoFinanceira === 'Parcial');
    },
  );

  it('mostra saldo desconhecido como valor a conferir e bloqueia pagamento', async () => {
    abrirDireto();
    api.compra.mockReturnValue(
      of({
        ...compra,
        situacaoFinanceira: 'ValorAConferir',
        totalCentavos: null,
        saldoCentavos: null,
      }),
    );
    await render();
    expect(texto()).toMatch(/Saldo a pagar\s*Valor a conferir/);
    expect(botao('Registrar pagamento')).toBeUndefined();
  });

  it('reconsulta o saldo após conflito e mostra a mensagem do servidor', async () => {
    abrirDireto();
    await render();
    await clicar('Registrar pagamento');
    await clicar('Usar saldo total');
    api.enviar.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { detail: 'Saldo alterado.' } }),
      ),
    );
    api.compra.mockReturnValue(
      of({ ...compra, situacaoFinanceira: 'Parcial', pagoCentavos: '7500', saldoCentavos: '2500' }),
    );
    await clicar('Confirmar');
    expect(texto()).toContain('Saldo alterado.');
    expect(texto()).toMatch(/Saldo a pagar\s*R\$ 25,00/);
    expect(api.compra).toHaveBeenCalledTimes(2);
  });

  it('bloqueia duplo clique, consulta após recarga e reenvia manualmente com a mesma chave e corpo', async () => {
    abrirDireto();
    const resposta = new Subject<LancamentoFinanceiro>();
    api.enviar.mockReturnValueOnce(resposta);
    await render();
    await clicar('Registrar pagamento');
    await campo('valor', '25');
    const confirmar = botao('Confirmar')!;
    confirmar.click();
    confirmar.click();
    expect(api.enviar).toHaveBeenCalledTimes(1);
    resposta.error(new HttpErrorResponse({ status: 0 }));
    const tentativa = api.enviar.mock.calls[0][0];
    fixture.destroy();
    await render();
    expect(api.consultarTentativa).toHaveBeenCalledWith(tentativa.chave);
    expect(api.enviar).toHaveBeenCalledTimes(1);
    await clicar('Reenviar tentativa');
    expect(api.enviar).toHaveBeenLastCalledWith(tentativa);
  });

  it.each(['criar', 'cancelar', 'pagar'] as const)(
    'recupera %s pela chave sem novo POST',
    async (operacao) => {
      sessionStorage.setItem(chaveStorage, JSON.stringify(comandos[operacao]));
      const registro =
        operacao === 'cancelar'
          ? { ...lancamento, canceladoEmUtc: lancamento.criadoEmUtc }
          : lancamento;
      api.lancamento.mockReturnValue(of(registro));
      api.consultarTentativa.mockReturnValue(
        of({
          operacao,
          recursoId: id,
          resultado: operacao === 'pagar' ? { ...movimento, id, compraId: id } : registro,
        }),
      );
      await render();
      expect(api.consultarTentativa).toHaveBeenCalledWith(id);
      if (operacao === 'pagar') expect(api.compra).toHaveBeenCalledWith(id);
      else expect(api.lancamento).toHaveBeenCalledWith(id);
      expect(texto()).toContain('Operação recuperada');
      expect(sessionStorage.getItem(chaveStorage)).toBeNull();
      expect(api.enviar).not.toHaveBeenCalled();
    },
  );

  it.each([503, 0])('bloqueia reenvio quando a consulta falha com %s', async (status) => {
    sessionStorage.setItem(chaveStorage, JSON.stringify(comandos.criar));
    api.consultarTentativa.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
    await render();
    expect(botao('Reenviar tentativa')?.disabled).toBe(true);
    expect(texto()).toContain('Serviço indisponível');
    expect(api.enviar).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(chaveStorage)).not.toBeNull();
  });

  it('ignora a tentativa de outra conta', async () => {
    sessionStorage.setItem(chaveStorage, JSON.stringify(comandos.criar));
    sessao.userId = 'outro-admin';
    await render();
    expect(api.consultarTentativa).not.toHaveBeenCalled();
    expect(api.enviar).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(chaveStorage)).not.toBeNull();
  });

  it('bloqueia reenvio se o corpo original deixa de estar disponível', async () => {
    sessionStorage.setItem(chaveStorage, JSON.stringify(comandos.criar));
    await render();
    sessionStorage.removeItem(chaveStorage);
    await clicar('Reenviar tentativa');
    expect(api.enviar).not.toHaveBeenCalled();
    expect(texto()).toContain('Não foi possível ler a tentativa');
  });
});

describe('centavosFinanceiros', () => {
  it('aceita o limite de bigint SQL sem converter a Number', () => {
    expect(centavosFinanceiros('92.233.720.368.547.758,07')).toBe(9223372036854775807n);
  });
  it.each(['92.233.720.368.547.758,08', '1,001', '-1', '0', '1.23', '1.234.56', 'NaN'])(
    'rejeita %s',
    (valor) => {
      expect(centavosFinanceiros(valor)).toBeNull();
    },
  );
});
