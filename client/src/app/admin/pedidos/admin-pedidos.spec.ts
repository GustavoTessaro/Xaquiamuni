import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import { AdminAuthService, AdminSession } from '../../core/admin-auth/admin-auth.service';
import type {
  DetalhePedidoAdmin,
  PaginaPedidosAdmin,
  ResumoPedidoAdmin,
} from '../../core/admin-pedidos/admin-pedidos.service';

const id = '20000000-0000-4000-8000-000000000004';
const caminho = '/admin/pedidos';
const api = '/api/admin/pedidos';
const prazo = '2090-11-19T08:30:00+00:00';
const resumo: ResumoPedidoAdmin = {
  id,
  numeroPublico: 'XQ-234567ACDE',
  situacao: 'AguardandoPagamento',
  criadoEmUtc: '2026-11-18T08:30:00+00:00',
  prazoPagamentoEmUtc: prazo,
  totalCentavos: '9223372036854775807',
  nomeCliente: 'Cliente Teste',
};
const detalhe: DetalhePedidoAdmin = {
  cliente: { nome: 'Cliente Teste', email: 'cliente@example.test' },
  pedido: {
    id,
    numeroPublico: resumo.numeroPublico,
    situacao: resumo.situacao,
    criadoEmUtc: resumo.criadoEmUtc,
    prazoPagamentoEmUtc: prazo,
    totalCentavos: resumo.totalCentavos,
    modalidadeEntrega: 'RetiradaLocal',
    itens: [
      {
        produtoId: 'produto',
        produtoVarianteId: 'variante',
        nomeProduto: 'Sabonete',
        nomeVariante: 'Lavanda',
        quantidade: 1,
        precoUnitarioCentavos: resumo.totalCentavos,
        subtotalCentavos: resumo.totalCentavos,
      },
    ],
  },
};

describe('Pedidos administrativos', () => {
  let http: HttpTestingController;
  let sessao: AdminSession | null;

  beforeEach(() => {
    sessao = { userId: 'admin' };
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AdminAuthService, useValue: { getCurrentSession: () => of(sessao) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.match(req => req.method === 'GET' && req.url.endsWith('/comprovante-pix'))
      .filter(req => !req.cancelled)
      .forEach(req => req.flush(null, { status: 404, statusText: 'Ausente' }));
    http.verify();
  });

  function clicar(harness: RouterTestingHarness, texto: string): void {
    const botao = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === texto,
    );
    expect(botao).toBeDefined();
    botao!.click();
    harness.detectChanges();
  }

  function lista(pagina: number, situacao?: string) {
    return http.expectOne(
      (req) =>
        req.method === 'GET' &&
        req.url === api &&
        req.params.get('pagina') === String(pagina) &&
        req.params.get('situacao') === (situacao ?? null),
    );
  }

  function pagina(
    p: number,
    itens: ResumoPedidoAdmin[] = [resumo],
    totalPaginas = 2,
  ): PaginaPedidosAdmin {
    return { itens, pagina: p, itensPorPagina: 20, totalItens: itens.length, totalPaginas };
  }

  async function abrirPedido(dados: DetalhePedidoAdmin = detalhe): Promise<RouterTestingHarness> {
    const harness = await RouterTestingHarness.create(`${caminho}/${id}`);
    expect(harness.routeNativeElement!.querySelector('[role=status]')?.textContent).toContain(
      'Carregando pedido',
    );
    http.expectOne({ method: 'GET', url: `${api}/${id}` }).flush(dados, {
      headers: { ETag: '"AAAAAAAAAAE="' },
    });
    harness.detectChanges();
    return harness;
  }

  function enviarComando(caminhoComando: string) {
    http.expectOne({ method: 'GET', url: '/api/auth/csrf' }).flush({ requestToken: 'csrf-teste' });
    const post = http.expectOne({ method: 'POST', url: `${api}/${id}/${caminhoComando}` });
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-teste');
    return post;
  }

  function abrirCancelamento(harness: RouterTestingHarness, motivo?: string) {
    clicar(harness, 'Cancelar pedido');
    const campo = harness.routeNativeElement!.querySelector<HTMLTextAreaElement>('textarea')!;
    if (motivo !== undefined) {
      campo.value = motivo;
      campo.dispatchEvent(new Event('input'));
      harness.detectChanges();
    }
    return campo;
  }

  function conferirPagamento(harness: RouterTestingHarness) {
    clicar(harness, 'Confirmar pagamento');
    const declarar =
      harness.routeNativeElement!.querySelector<HTMLInputElement>('input[type=checkbox]')!;
    declarar.click();
    harness.detectChanges();
  }

  it('oferece a navegação no painel e protege as duas rotas com o guard', async () => {
    const harness = await RouterTestingHarness.create('/admin');
    for (const secao of ['operacao', 'estoque', 'agenda']) {
      http.expectOne('/api/admin/dashboard/' + secao).flush(null, { status: 503, statusText: 'Offline' });
    }
    http.expectOne('/api/admin/financeiro/resumo').flush(null, { status: 503, statusText: 'Offline' });
    const link = harness.routeNativeElement!.querySelector<HTMLAnchorElement>(
      'a[href="/admin/pedidos"]',
    )!;
    expect(link.textContent?.trim()).toBe('Pedidos');
    link.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    lista(1).flush(pagina(1));
    harness.detectChanges();
    const linkPedido = harness.routeNativeElement!.querySelector(`a[href="${caminho}/${id}"]`);
    expect(linkPedido?.textContent).toContain(resumo.numeroPublico);
    expect(linkPedido?.textContent).not.toContain(id);
    expect(
      harness.routeNativeElement!.querySelector('a[aria-current="page"]')?.textContent?.trim(),
    ).toBe('Pedidos');
    sessao = null;
    await harness.navigateByUrl(`${caminho}/${id}`);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    await harness.navigateByUrl(caminho);
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    http.expectNone(`${api}/${id}`);
  });

  it.each(['RetiradaLocal', 'EntregaPropria'] as const)('oculta o histórico na modalidade %s', async modalidadeEntrega => {
    const harness = await abrirPedido({ ...detalhe, pedido: { ...detalhe.pedido, modalidadeEntrega } });
    expect(harness.routeNativeElement!.querySelector('app-historico-rastreamento')).toBeNull();
    http.expectNone(`${api}/${id}/historico-rastreamento`);
  });

  it('consulta o histórico novamente após confirmar a correção, separando os ETags operacionais', async () => {
    const postal: DetalhePedidoAdmin = { ...detalhe, pedido: { ...detalhe.pedido, modalidadeEntrega: 'Correios', situacao: 'Postado' } };
    const rotaHistorico = `${api}/${id}/historico-rastreamento`;
    const harness = await abrirPedido(postal);
    http.expectOne(rotaHistorico).flush([], { headers: { ETag: '"ignorar"' } });
    harness.detectChanges();
    clicar(harness, 'Corrigir rastreamento');
    const campo = harness.routeNativeElement!.querySelector<HTMLInputElement>('form input[maxlength="32"]')!;
    campo.value = 'AB123456789BR';
    campo.dispatchEvent(new Event('input'));
    harness.routeNativeElement!.querySelector<HTMLInputElement>('form input[type="checkbox"]')!.click();
    harness.detectChanges();
    clicar(harness, 'Salvar código');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf-teste' });
    const correcao = http.expectOne({ method: 'PUT', url: `${api}/${id}/rastreamento-postal` });
    expect(correcao.request.headers.get('If-Match')).toBe('"AAAAAAAAAAE="');
    correcao.flush({ id, situacao: 'Postado', repetida: false });
    harness.detectChanges();
    http.expectOne(`${api}/${id}`).flush(postal, { headers: { ETag: '"AAAAAAAAAAI="' } });
    harness.detectChanges();
    http.expectOne(rotaHistorico).flush([{
      codigoAnterior: null, codigoNovo: 'AB123456789BR', administradorId: 'admin', realizadaEmUtc: '2026-11-23T10:30:00Z',
    }]);
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('app-historico-rastreamento')?.textContent).toContain('AB123456789BR');
  });

  it('devolve o foco ao botão da ação ao fechar por Escape', async () => {
    const harness = await abrirPedido();
    clicar(harness, 'Confirmar pagamento');
    await harness.fixture.whenStable();
    const campo =
      harness.routeNativeElement!.querySelector<HTMLInputElement>('input[type=checkbox]')!;
    expect(document.activeElement).toBe(campo);
    campo.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(document.activeElement).toBe(
      harness.routeNativeElement!.querySelector('[data-acao="pagamento"]'),
    );
    http.expectNone('/api/auth/csrf');
  });

  it('lista e pagina, conserva o filtro na rota e distingue carregamento de vazio', async () => {
    const harness = await RouterTestingHarness.create(caminho);
    expect(harness.routeNativeElement!.querySelector('[role=status]')?.textContent).toContain(
      'Carregando pedidos',
    );
    lista(1).flush(pagina(1));
    harness.detectChanges();
    const view = harness.routeNativeElement!;
    expect(view.textContent).toContain('Cliente Teste');
    expect(view.textContent).toContain(resumo.numeroPublico);
    expect(view.textContent).not.toContain(id);
    expect(view.textContent).toContain('92.233.720.368.547.758,07');
    clicar(harness, 'Próxima');
    await harness.fixture.whenStable();
    lista(2).flush(pagina(2));
    harness.detectChanges();
    expect(view.textContent).toContain('Página 2 de 2');

    const select = view.querySelector<HTMLSelectElement>('select')!;
    expect(view.querySelector(`label[for="${select.id}"]`)?.textContent).toContain(
      'Filtrar por situação',
    );
    expect(select.options[0].textContent).toBe('Todos');
    select.value = 'Cancelado';
    select.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    lista(1, 'Cancelado').flush(pagina(1, [], 0));
    harness.detectChanges();
    expect(view.textContent).toContain('Nenhum pedido encontrado.');
    expect(TestBed.inject(Router).url).toBe('/admin/pedidos?situacao=Cancelado');
  });

  it('restaura o filtro selecionado ao voltar do detalhe', async () => {
    const harness = await RouterTestingHarness.create(
      `${caminho}/${id}?situacao=AguardandoPagamento&pagina=2`,
    );
    http.expectOne({ method: 'GET', url: `${api}/${id}` }).flush(detalhe);
    harness.detectChanges();
    harness.routeNativeElement!.querySelector<HTMLAnchorElement>('main a')!.click();
    await harness.fixture.whenStable();
    lista(2, 'AguardandoPagamento').flush(pagina(2));
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector<HTMLSelectElement>('select')?.value).toBe(
      'AguardandoPagamento',
    );
  });

  it('distingue erro de lista vazia e permite repetir a consulta', async () => {
    const harness = await RouterTestingHarness.create(caminho);
    lista(1).flush(null, { status: 500, statusText: 'Erro' });
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain(
      'Não foi possível',
    );
    expect(harness.routeNativeElement!.textContent).not.toContain('Nenhum pedido');
    clicar(harness, 'Tentar novamente');
    lista(1).flush(pagina(1, [], 0));
    harness.detectChanges();
    expect(harness.routeNativeElement!.textContent).toContain('Nenhum pedido');
  });

  it.each([false, true])(
    'exige conferência, envia uma vez e visualiza confirmação, repetida=%s',
    async (repetida) => {
      const harness = await abrirPedido();
      const view = harness.routeNativeElement!;
      expect(view.textContent).toContain('cliente@example.test');
      expect(view.textContent).toContain('Retirada na loja');
      expect(view.textContent).toContain('Sabonete · Lavanda');
      expect(view.textContent).toContain('92.233.720.368.547.758,07');
      expect(view.querySelector(`time[datetime="${prazo}"]`)).not.toBeNull();
      clicar(harness, 'Confirmar pagamento');
      await harness.fixture.whenStable();
      const check = view.querySelector<HTMLInputElement>('input[type=checkbox]')!;
      const form = view.querySelector('form')!;
      expect(check.checked).toBe(false);
      expect(document.activeElement).toBe(check);
      expect(form.querySelector<HTMLButtonElement>('button[type=submit]')?.disabled).toBe(true);
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectNone('/api/auth/csrf');

      check.click();
      harness.detectChanges();
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      harness.detectChanges();
      expect(form.getAttribute('aria-busy')).toBe('true');
      expect(form.querySelector('fieldset')?.disabled).toBe(true);
      const post = enviarComando('confirmar-pagamento');
      expect(post.request.body).toEqual({ recebimentoConferido: true });
      post.flush({ id, situacao: 'PagamentoConfirmado', repetida });
      harness.detectChanges();
      expect(view.querySelector('[role=status]')?.textContent).toContain(
        repetida ? 'O pagamento já estava confirmado.' : 'Pagamento confirmado.',
      );
      expect(view.querySelector('form')).toBeNull();
      expect(view.textContent).not.toContain('Cancelar pedido');
      await harness.navigateByUrl(caminho);
      lista(1).flush(pagina(1, [{ ...resumo, situacao: 'PagamentoConfirmado' }]));
      harness.detectChanges();
      expect(harness.routeNativeElement!.textContent).toContain('Pagamento confirmado');
    },
  );

  it.each([false, true])(
    'exige motivo acessível e informa cancelamento, repetido=%s',
    async (repetida) => {
      const harness = await abrirPedido();
      const campo = abrirCancelamento(harness, '  ');
      expect(campo.maxLength).toBe(500);
      clicar(harness, 'Confirmar cancelamento');
      expect(campo.getAttribute('aria-invalid')).toBe('true');
      expect(campo.getAttribute('aria-describedby')).toContain('pedido-motivo-erro');
      expect(document.activeElement).toBe(campo);
      http.expectNone('/api/auth/csrf');
      campo.value = '  Solicitado pela cliente  ';
      campo.dispatchEvent(new Event('input'));
      clicar(harness, 'Confirmar cancelamento');
      const post = enviarComando('cancelar');
      expect(post.request.body).toEqual({ motivo: 'Solicitado pela cliente' });
      post.flush({ id, situacao: 'Cancelado', repetida });
      harness.detectChanges();
      expect(harness.routeNativeElement!.querySelector('[role=status]')?.textContent).toContain(
        repetida ? 'O pedido já estava cancelado com esse motivo.' : 'Pedido cancelado.',
      );
    },
  );

  it.each([
    ['motivo_divergente', 'outro motivo', 'Cancelado'],
    ['prazo_encerrado', 'prazo de pagamento terminou', 'AguardandoPagamento'],
    ['estado_incompativel', 'não permite', 'Expirado'],
  ] as const)('reconsulta dados após conflito %s', async (codigo, mensagem, situacao) => {
    const harness = await abrirPedido();
    abrirCancelamento(harness, 'teste');
    clicar(harness, 'Confirmar cancelamento');
    enviarComando('cancelar').flush({ codigo }, { status: 409, statusText: 'Conflict' });
    http
      .expectOne({ method: 'GET', url: `${api}/${id}` })
      .flush({ ...detalhe, pedido: { ...detalhe.pedido, situacao } });
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain(
      mensagem,
    );
    expect(harness.routeNativeElement!.querySelector('form')).toBeNull();
    expect(harness.routeNativeElement!.textContent).not.toContain('Confirmar pagamento');
  });

  it.each([
    [401, 'Sua sessão expirou.'],
    [403, 'Você não tem acesso'],
    [404, 'Pedido não encontrado.'],
  ] as const)('trata %s e remove os dados do detalhe', async (status, mensagem) => {
    const harness = await abrirPedido();
    conferirPagamento(harness);
    clicar(harness, 'Registrar confirmação');
    enviarComando('confirmar-pagamento').flush(null, { status, statusText: 'Recusado' });
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain(
      mensagem,
    );
    expect(harness.routeNativeElement!.textContent).not.toContain('cliente@example.test');
    expect(harness.routeNativeElement!.querySelector('form')).toBeNull();
  });

  it('não oferece ações quando o prazo já terminou', async () => {
    const harness = await abrirPedido({
      ...detalhe,
      pedido: { ...detalhe.pedido, prazoPagamentoEmUtc: '2020-01-01T00:00:00Z' },
    });
    expect(harness.routeNativeElement!.textContent).toContain('O prazo de pagamento terminou');
    expect(harness.routeNativeElement!.textContent).not.toContain('Confirmar pagamento');
    expect(harness.routeNativeElement!.textContent).not.toContain('Cancelar pedido');
  });

  const etapas = [
    {
      origem: 'PagamentoConfirmado',
      acao: 'Iniciar preparação',
      botao: 'Confirmar início',
      rota: 'iniciar-preparacao',
      destino: 'EmPreparacao',
      repeticao: 'já havia sido iniciada',
    },
    {
      origem: 'EmPreparacao',
      acao: 'Liberar para retirada',
      botao: 'Confirmar liberação',
      rota: 'liberar-retirada',
      destino: 'ProntoParaRetirada',
      repeticao: 'já estava pronto',
    },
    {
      origem: 'ProntoParaRetirada',
      acao: 'Confirmar entrega',
      botao: 'Registrar entrega',
      rota: 'confirmar-entrega',
      destino: 'Entregue',
      repeticao: 'já estava registrada',
    },
  ] as const;

  function comSituacao(situacao: DetalhePedidoAdmin['pedido']['situacao']): DetalhePedidoAdmin {
    return { ...detalhe, pedido: { ...detalhe.pedido, situacao } };
  }

  function declararEntrega(harness: RouterTestingHarness): void {
    const caixa =
      harness.routeNativeElement!.querySelector<HTMLInputElement>('input[type=checkbox]')!;
    caixa.click();
    harness.detectChanges();
  }

  it('registra saída e recebimento com versões diferentes e proteção contra duplo envio', async () => {
    const dados = comSituacao('EmPreparacao');
    dados.pedido = {
      ...dados.pedido, modalidadeEntrega: 'EntregaPropria', subtotalProdutosCentavos: '250', freteCentavos: '1250',
      totalCentavos: '1500', entrega: { destino: {
        logradouro: 'Rua das Flores', numero: '8', bairro: 'Centro', cidade: 'Vacaria', uf: 'RS',
        cep: '95200000', codigoIbge: '4322509', complemento: null,
      }, cidadeId: null, enderecoRetirada: null, instrucoesRetirada: null },
    };
    const harness = await abrirPedido(dados);
    expect(harness.routeNativeElement!.textContent).toContain('Rua das Flores');
    expect(harness.routeNativeElement!.textContent).not.toContain('Liberar para retirada');
    const executar = (acao: string, rota: string, versao: string) => {
      clicar(harness, acao);
      const form = harness.routeNativeElement!.querySelector('[aria-labelledby="pedido-acoes"] form')!;
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectNone('/api/auth/csrf');
      declararEntrega(harness);
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      const post = enviarComando(rota);
      expect(post.request.body).toEqual({ confirmado: true });
      expect(post.request.headers.get('If-Match')).toBe(versao);
      expect(post.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/i);
      return post;
    };
    const saida = executar('Iniciar entrega própria', 'iniciar-entrega-propria', '"AAAAAAAAAAE="');
    saida.flush({ id, situacao: 'EmEntrega', repetida: false });
    http.expectOne({ method: 'GET', url: `${api}/${id}` }).flush({
      ...dados, pedido: { ...dados.pedido, situacao: 'EmEntrega' },
    }, { headers: { ETag: '"AAAAAAAAAAI="' } });
    harness.detectChanges();
    const recebimento = executar('Confirmar recebimento', 'confirmar-recebimento', '"AAAAAAAAAAI="');
    expect(recebimento.request.headers.get('Idempotency-Key')).not.toBe(saida.request.headers.get('Idempotency-Key'));
    recebimento.flush({ codigo: 'versao_desatualizada' }, { status: 409, statusText: 'Conflict' });
    http.expectOne({ method: 'GET', url: `${api}/${id}` }).flush({
      ...dados, pedido: { ...dados.pedido, situacao: 'Entregue' },
    });
    harness.detectChanges();
    expect(harness.routeNativeElement!.textContent).toContain('Entregue');
    expect(harness.routeNativeElement!.textContent).not.toContain('Confirmar recebimento');
  });

  it.each(etapas.flatMap((etapa) => [false, true].map((repetida) => ({ ...etapa, repetida }))))(
    '$acao solicita confirmação, atualiza pela API e trata repetição $repetida',
    async ({ origem, acao, botao, rota, destino, repeticao, repetida }) => {
      const harness = await abrirPedido(comSituacao(origem));
      const area = harness.routeNativeElement!.querySelector('[aria-labelledby="pedido-acoes"]')!;
      expect([...area.querySelectorAll('button')].map((b) => b.textContent?.trim())).toEqual([
        acao,
      ]);
      clicar(harness, acao);
      await harness.fixture.whenStable();
      const formulario = area.querySelector('form')!;
      const submit = formulario.querySelector<HTMLButtonElement>('button[type=submit]')!;
      expect(submit.textContent?.trim()).toBe(botao);
      http.expectNone('/api/auth/csrf');

      if (rota === 'confirmar-entrega') {
        const caixa = formulario.querySelector<HTMLInputElement>('input[type=checkbox]')!;
        expect(caixa.checked).toBe(false);
        expect(document.activeElement).toBe(caixa);
        expect(submit.disabled).toBe(true);
        expect(formulario.textContent).toContain('baixa física definitiva');
        expect(caixa.closest('label')?.textContent).toContain(
          'entregues presencialmente ao cliente',
        );
        formulario.dispatchEvent(new Event('submit', { cancelable: true }));
        http.expectNone('/api/auth/csrf');
        declararEntrega(harness);
      } else {
        expect(document.activeElement).toBe(submit);
        if (rota === 'liberar-retirada') {
          expect(formulario.textContent).toContain('A entrega presencial é confirmada depois.');
        }
      }

      formulario.dispatchEvent(new Event('submit', { cancelable: true }));
      formulario.dispatchEvent(new Event('submit', { cancelable: true }));
      harness.detectChanges();
      expect(formulario.getAttribute('aria-busy')).toBe('true');
      expect(formulario.querySelector('fieldset')?.disabled).toBe(true);
      const post = enviarComando(rota);
      expect(post.request.body).toEqual(
        rota === 'confirmar-entrega' ? { entregaPresencialConfirmada: true } : {},
      );
      post.flush({ id, situacao: destino, repetida });
      const recarga = http.expectOne({ method: 'GET', url: `${api}/${id}` });
      harness.detectChanges();
      expect(harness.routeNativeElement!.textContent).toContain('Carregando pedido');
      recarga.flush(comSituacao(destino));
      harness.detectChanges();
      const estado =
        destino === 'Entregue'
          ? 'Entregue'
          : destino === 'EmPreparacao'
            ? 'Em preparação'
            : 'Pronto para retirada';
      expect(harness.routeNativeElement!.querySelector('dl')?.textContent).toContain(estado);
      const mensagem = harness.routeNativeElement!.querySelector('[role=status]')?.textContent;
      if (repetida) expect(mensagem).toContain(repeticao);
      else expect(mensagem).toBeTruthy();
      expect(harness.routeNativeElement!.querySelector('form')).toBeNull();
    },
  );

  it.each(['Cancelado', 'Expirado', 'Entregue'] as const)(
    'omite ações incompatíveis em %s',
    async (situacao) => {
      const harness = await abrirPedido(comSituacao(situacao));
      expect(
        harness.routeNativeElement!.querySelector('[aria-labelledby="pedido-acoes"] button'),
      ).toBeNull();
    },
  );

  it.each([
    ['estado_incompativel', 'não permite'],
    ['reservas_inconsistentes', 'reservas do pedido apresentam uma divergência'],
    ['lote_inelegivel', 'lote reservado não está apto'],
    ['saldo_insuficiente', 'saldo dos lotes reservados é insuficiente'],
    ['conflito_concorrencia', 'Outra operação está atualizando'],
  ] as const)('recarrega após conflito de entrega %s', async (codigo, mensagem) => {
    const harness = await abrirPedido(comSituacao('ProntoParaRetirada'));
    clicar(harness, 'Confirmar entrega');
    declararEntrega(harness);
    clicar(harness, 'Registrar entrega');
    enviarComando('confirmar-entrega').flush({ codigo }, { status: 409, statusText: 'Conflict' });
    http.expectOne({ method: 'GET', url: `${api}/${id}` }).flush(comSituacao('ProntoParaRetirada'));
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')?.textContent).toContain(
      mensagem,
    );
    expect(harness.routeNativeElement!.querySelector('form')).toBeNull();
    clicar(harness, 'Confirmar entrega');
    expect(
      harness.routeNativeElement!.querySelector<HTMLInputElement>('input[type=checkbox]')?.checked,
    ).toBe(false);
    http.expectNone('/api/auth/csrf');
  });

  it.each([401, 403, 404])('limpa o detalhe após erro %s na operação', async (status) => {
    const harness = await abrirPedido(comSituacao('EmPreparacao'));
    clicar(harness, 'Liberar para retirada');
    clicar(harness, 'Confirmar liberação');
    enviarComando('liberar-retirada').flush(null, { status, statusText: 'Recusado' });
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('[role=alert]')).not.toBeNull();
    expect(harness.routeNativeElement!.querySelector('form')).toBeNull();
    expect(harness.routeNativeElement!.textContent).not.toContain('cliente@example.test');
  });
});
