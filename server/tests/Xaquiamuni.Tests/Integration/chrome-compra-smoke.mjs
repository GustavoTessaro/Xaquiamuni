import { createInterface } from 'node:readline';
import { validarJornada } from './chrome-compra-jornada.mjs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const linhas = createInterface({ input: process.stdin, crlfDelay: Infinity });
const mensagens = linhas[Symbol.asyncIterator]();
const dados = JSON.parse((await mensagens.next()).value);
const { origin, pinSpki, chromePath, profile, cliente, admin } = dados;
async function checkpoint(etapa, id = '') {
  console.log(`@checkpoint ${etapa} ${id}`);
  const resposta = await mensagens.next();
  if (resposta.done) throw new Error('Executor encerrou o checkpoint: ' + etapa);
  return JSON.parse(resposta.value);
}
const chrome = spawn(chromePath, [
  '--headless=new', '--remote-debugging-port=0',
  '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--disable-extensions', '--proxy-server=127.0.0.1:1',
  '--ignore-certificate-errors-spki-list=' + pinSpki, 'about:blank',
], { stdio: 'ignore', windowsHide: true });
const closed = new Promise(resolve => { chrome.once('exit', resolve); chrome.once('error', resolve); });
let socket, call;

async function waitFor(check, label) {
  for (let i = 0; i < 100; i++) {
    const result = await check();
    if (result) return result;
    await delay(100);
  }
  throw new Error(label);
}

try {
  const endpoint = await waitFor(async () => {
    try {
      const [port, path] = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).trim().split(/\r?\n/);
      return path && 'ws://127.0.0.1:' + port + path;
    } catch { return null; }
  }, 'Chrome não abriu a porta de depuração');

  socket = new WebSocket(endpoint);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', () => reject(new Error('DevTools indisponível')), { once: true });
  });
  let seq = 0;
  const pending = new Map();
  let externos = 0;
  let postsFinanceiros = 0;
  const respostas = new Map();
  const compras = new Map();
  const falhasPix = new Map();
  call = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = ++seq;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error('Tempo limite do DevTools: ' + method));
    }, 10_000);
    pending.set(id, data => {
      clearTimeout(timer);
      if (data.error) reject(new Error('Falha do DevTools: ' + method));
      else resolve(data.result);
    });
    socket.send(JSON.stringify({ id, method, params, sessionId }));
  });
  socket.addEventListener('message', event => {
    const data = JSON.parse(event.data);
    if (data.method === 'Fetch.requestPaused') {
      const { requestId, request } = data.params;
      const local = new URL(request.url).origin === origin;
      if (!local) externos++;
      if (local && request.method === 'POST' && new URL(request.url).pathname.startsWith('/api/admin/financeiro/'))
        postsFinanceiros++;
      if (local && request.method === 'POST' && new URL(request.url).pathname === '/api/pedidos') {
        const chave = Object.entries(request.headers).find(([nome]) => nome.toLowerCase() === 'idempotency-key')?.[1];
        compras.set(data.sessionId, { chave, corpo: request.postData,
          total: (compras.get(data.sessionId)?.total ?? 0) + 1 });
      }
      const falhaPix = local && request.method === 'GET'
        && new URL(request.url).pathname === falhasPix.get(data.sessionId);
      if (falhaPix) falhasPix.delete(data.sessionId);
      void call(local && !falhaPix ? 'Fetch.continueRequest' : 'Fetch.failRequest',
        local && !falhaPix ? { requestId } : { requestId, errorReason: 'BlockedByClient' }, data.sessionId)
        .catch(() => {});
    } else if (data.method === 'Network.responseReceived') {
      const url = new URL(data.params.response.url);
      if (url.origin === origin) respostas.set(url.pathname, data.params.response.status);
    } else if (pending.has(data.id)) {
      pending.get(data.id)(data);
      pending.delete(data.id);
    }
  });

  async function abrir(path) {
    const { browserContextId } = await call('Target.createBrowserContext');
    const { targetId } = await call('Target.createTarget', { url: 'about:blank', browserContextId });
    const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
    await call('Fetch.enable', { patterns: [{ urlPattern: 'http*' }] }, sessionId);
    await call('Network.enable', {}, sessionId);
    const navegacao = await call('Page.navigate', { url: origin + path }, sessionId);
    if (navegacao.errorText) throw new Error('Falha ao navegar em ' + path + ': ' + navegacao.errorText);
    const ev = async expression => {
      const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId);
      if (r.exceptionDetails) {
        const erro = r.exceptionDetails.exception?.className ?? 'erro JavaScript';
        throw new Error(`Falha ao avaliar verificação no Chrome (${erro}, linha ${r.exceptionDetails.lineNumber + 1})`);
      }
      return r.result.value;
    };
    try {
      await waitFor(() => ev('!!document.querySelector("app-root main h1")'), 'Angular');
    } catch {
      throw new Error('Angular não carregou em ' + path + '. HTTP local: ' + JSON.stringify([...respostas]));
    }
    assert.equal(await ev(`(() => {
      try {
        localStorage.setItem('smoke', 'ok');
        const ok = localStorage.getItem('smoke') === 'ok';
        localStorage.removeItem('smoke');
        return ok && isSecureContext && !!crypto.subtle && typeof crypto.randomUUID === 'function';
      } catch { return false; }
    })()`), true);
    assert.equal(await ev('fetch("/health").then(r => r.status)'), 200);
    return { ev, sessionId, browserContextId,
      falharProximaConsultaPix: path => falhasPix.set(sessionId, path), compra: () => compras.get(sessionId), ir: async destino => {
      const resultado = await call('Page.navigate', { url: origin + destino }, sessionId);
      if (resultado.errorText) throw new Error('Falha de navega??o na jornada');
    } };
  }

  if (dados.seed) {
    await validarJornada({ ...dados, abrir, waitFor, checkpoint, call });
    assert.equal(externos, 0);
  } else {
  const c = await abrir('/login');
  const a = await abrir('/admin/login');
  async function login(browser, credenciais, prefixo) {
    const { Email: email, Senha: senha, Id: id } = credenciais;
    const resultados = await browser.ev(`(async () => {
      const corpo = JSON.stringify(${JSON.stringify({ email, senha })});
      const post = (token) => fetch(${JSON.stringify(prefixo + '/login')}, { method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(token ? { 'X-XSRF-TOKEN': token } : {}) }, body: corpo });
      const semToken = (await post()).status;
      const csrf = await fetch('/api/auth/csrf');
      const { requestToken } = await csrf.json();
      const invalido = (await post('invalido')).status;
      const login = (await post(requestToken)).status;
      const me = await fetch(${JSON.stringify(prefixo + '/me')});
      return [semToken, csrf.status, invalido, login, me.status, me.ok && (await me.json()).userId === ${JSON.stringify(id)}];
    })()`);
    assert.deepEqual(resultados, [400, 200, 400, 204, 200, true]);
  }
  await login(c, cliente, '/api/auth');
  assert.equal(await a.ev('fetch("/api/admin/auth/me").then(r => r.status)'), 401);
  await login(a, admin, '/api/admin/auth');
  assert.equal(await c.ev('fetch("/api/pedidos").then(r => r.status)'), 200);
  assert.equal(await a.ev('fetch("/api/admin/pedidos").then(r => r.status)'), 200);
  assert.equal(await c.ev('fetch("/api/admin/pedidos").then(r => r.status)'), 403);
  assert.equal(await a.ev('fetch("/api/pedidos").then(r => r.status)'), 403);

  if (dados.agenda) {
    assert.equal(await c.ev('fetch("/api/admin/agenda").then(r => r.status)'), 403);
    const botaoAgenda = rotulo => `[...document.querySelectorAll('main button')]
      .find(b => b.textContent.trim() === ${JSON.stringify(rotulo)} && !b.disabled && b.getClientRects().length)`;
    const clicarAgenda = async rotulo => {
      await waitFor(() => a.ev(`(() => {
        const botao = ${botaoAgenda(rotulo)};
        if (!botao) return false;
        botao.click();
        return true;
      })()`), 'Botão da Agenda indisponível: ' + rotulo);
    };
    const preencherAgenda = (nome, valor) => a.ev(`(() => {
      const campo = document.querySelector('main [name="' + ${JSON.stringify(nome)} + '"]');
      campo.value = ${JSON.stringify(valor)};
      campo.dispatchEvent(new Event('input', { bubbles: true }));
    })()`);
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', {
        width, height: 900, mobile: false, deviceScaleFactor: 1,
      }, a.sessionId);
      await a.ir('/admin/agenda');
      await waitFor(() => a.ev(`document.querySelector('main h1')?.textContent.trim() === 'Agenda'`), 'Agenda não abriu');
      await clicarAgenda('Novo compromisso');
      await waitFor(() => a.ev('!!document.querySelector("#agenda-form")'), 'Cadastro não abriu');
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      assert.equal(await a.ev('document.activeElement?.id'), 'agenda-form');
      await preencherAgenda('titulo', 'Compromisso E2E');
      await preencherAgenda('inicio', '09:00');
      await preencherAgenda('fim', '09:30');
      await clicarAgenda('Salvar compromisso');
      await waitFor(async () => {
        const erro = await a.ev(`document.querySelector('main [role="alert"]')?.textContent.trim()`);
        if (erro) throw new Error('Agenda: ' + erro + ' HTTP: ' + respostas.get('/api/admin/agenda'));
        return a.ev(`document.querySelector('main .resumo h3')?.textContent.trim() === 'Compromisso E2E'
        && document.querySelector('main [aria-pressed="true"] .marcador')
        && document.querySelector('main .resumo').textContent.includes('09:00 a 09:30')`);
      }, 'Compromisso não apareceu').catch(async erro => {
        const estado = await a.ev(`({
          texto: document.querySelector('main .agenda')?.innerText.slice(0, 1000),
          campos: [...document.querySelectorAll('main form input')].map(x => [x.name, x.value, x.validity.valid]),
          dataMarcada: document.querySelector('main [aria-pressed="true"]')?.getAttribute('aria-label'),
        })`);
        throw new Error(erro.message + ': ' + JSON.stringify(estado) + ' HTTP: ' + respostas.get('/api/admin/agenda'));
      });
      await clicarAgenda('Editar');
      await waitFor(() => a.ev('!!document.querySelector("#agenda-form")'), 'Edição não abriu');
      await preencherAgenda('titulo', 'Compromisso editado');
      await clicarAgenda('Salvar compromisso');
      await waitFor(() => a.ev(`document.querySelector('main .resumo h3')?.textContent.trim() === 'Compromisso editado'`), 'Edição não persistiu');
      const concorrente = await a.ev(`(async () => {
        const rota = '/api/admin/agenda';
        const item = (await fetch(rota).then(r => r.json())).find(x => x.titulo === 'Compromisso editado');
        const url = rota + '/' + item.id;
        const corpo = { titulo: 'Edição anterior', data: item.data, descricao: null, categoria: null,
          horarioInicial: item.horarioInicial, horarioFinal: item.horarioFinal, versao: item.versao };
        sessionStorage.setItem('xaquiamuni:agenda:v1:' + ${JSON.stringify(admin.Id)}, JSON.stringify({
          operacao: 'editar', metodo: 'PUT', url, id: item.id, corpo, chave: crypto.randomUUID(),
        }));
        return { url, corpo: { ...corpo, titulo: 'Edição concorrente' } };
      })()`);
      await a.ir('/admin/agenda');
      await waitFor(() => a.ev(`[...document.querySelectorAll('main button')].some(b =>
        b.textContent.trim() === 'Reenviar tentativa' && !b.disabled)`), 'Tentativa não consultada na recarga');
      assert.equal(await a.ev(`(async () => {
        const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
        const { url, corpo } = ${JSON.stringify(concorrente)};
        return (await fetch(url, { method: 'PUT', body: JSON.stringify(corpo), headers: {
          'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(),
        } })).status;
      })()`), 200);
      await clicarAgenda('Reenviar tentativa');
      await clicarAgenda('Recarregar compromisso');
      await waitFor(() => a.ev(`document.querySelector('[name="titulo"]')?.value === 'Edição concorrente'`),
        'Versão concorrente não foi carregada');
      await a.ir('/admin/agenda');
      await waitFor(() => a.ev(`document.querySelector('main .resumo h3')?.textContent.trim() === 'Edição concorrente'`),
        'O reenvio alterou a versão concorrente');
      await clicarAgenda('Excluir');
      await clicarAgenda('Confirmar exclusão');
      await waitFor(() => a.ev(`document.querySelector('main')?.textContent.includes('Nenhum compromisso neste dia')
        && !document.querySelector('main [aria-pressed="true"] .marcador')`), 'Exclusão não atualizou a grade');
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      console.log('Agenda em ' + width + ' px: cadastro, edição, conflito no reenvio, recarga e exclusão.');
    }
  }

  if (dados.financeiro) {
    const adminId = JSON.stringify(admin.Id);
    assert.equal(await a.ev(`(async () => {
      const chave = crypto.randomUUID();
      const corpo = { tipo: 'Entrada', categoria: 'Ensaio', descricao: 'Aporte de teste',
        valorCentavos: '12345', data: '2026-11-20', meio: 'Pix' };
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      const resposta = await fetch('/api/admin/financeiro/lancamentos', {
        method: 'POST', body: JSON.stringify(corpo), headers: {
          'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave,
        },
      });
      if (resposta.status !== 201) return resposta.status;
      // Reproduz a sessão que ficou com a tentativa após o commit e sem processar a resposta.
      sessionStorage.setItem('xaquiamuni:financeiro:v1:' + ${adminId}, JSON.stringify({ tipo: 'criar', corpo, chave }));
      return resposta.status;
    })()`), 201);
    await a.ir('/admin/financeiro');
    await waitFor(() => a.ev(`document.querySelector('main')?.textContent.includes('Operação recuperada')
      && document.querySelector('#lancamento-titulo')?.textContent.trim() === 'Aporte de teste'`),
      'O Financeiro não recuperou o lançamento confirmado');
    assert.equal(await a.ev(`sessionStorage.getItem('xaquiamuni:financeiro:v1:' + ${adminId})`), null);
    assert.equal(postsFinanceiros, 1);
    assert.equal(await a.ev('fetch("/api/admin/financeiro/lancamentos").then(r => r.json()).then(r => r.length)'), 1);
    console.log('Financeiro: chave recuperada, detalhe aberto e um único lançamento no SQL descartável.');

    const botaoFinanceiro = rotulo => `[...document.querySelectorAll('main button')]
      .find(b => b.textContent.trim() === ${JSON.stringify(rotulo)} && !b.disabled && b.getClientRects().length)`;
    await a.ev(`(${botaoFinanceiro('Movimentações')}).click()`);
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', {
        width, height: 900, mobile: false, deviceScaleFactor: 1,
      }, a.sessionId);
      await a.ev(`(${botaoFinanceiro('Visão geral')}).click()`);
      await waitFor(() => a.ev(`document.querySelector('app-admin-visao-financeira')
        ?.textContent.includes('R$ 123,45')`), 'Visão geral não exibiu o total real');
      const posicao = await a.ev(`document.querySelector('[aria-labelledby="posicao-financeira-titulo"]').textContent`);
      await a.ev(`(() => {
        const campo = document.querySelector('app-admin-visao-financeira input[name="dataInicial"]');
        campo.value = '2026-11-21';
        campo.dispatchEvent(new Event('input', { bubbles: true }));
        (${botaoFinanceiro('Aplicar período')}).click();
      })()`);
      await waitFor(() => a.ev(`document.querySelector('app-admin-visao-financeira')
        ?.textContent.includes('Nenhuma movimentação no período selecionado')`), 'Período não atualizou o fluxo');
      assert.equal(await a.ev(`document.querySelector('[aria-labelledby="posicao-financeira-titulo"]').textContent`), posicao);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      await a.ev(`(${botaoFinanceiro('Todo o período')}).click()`);
      await waitFor(() => a.ev(`document.querySelector('app-admin-visao-financeira')
        ?.textContent.includes('R$ 123,45')`), 'Fluxo não retornou após abrir o período');
      await a.ev(`(${botaoFinanceiro('Movimentações')}).click()`);
      console.log('Visão geral em ' + width + ' px: total real, filtro de fluxo, posição atual e ausência de rolagem horizontal.');
      await waitFor(() => a.ev(`!!(${botaoFinanceiro('Ver lançamento')})
        && document.querySelector('app-admin-movimentacoes-financeiras').textContent.includes('+ R$ 123,45')`),
        'Movimentação consolidada ausente no Chrome');
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      await a.ev(`(() => {
        const campo = document.querySelector('app-admin-movimentacoes-financeiras select[name="tipo"]');
        campo.value = 'Saida';
        campo.dispatchEvent(new Event('change', { bubbles: true }));
        (${botaoFinanceiro('Aplicar filtros')}).click();
      })()`);
      await waitFor(() => a.ev(`document.querySelector('app-admin-movimentacoes-financeiras')
        .textContent.includes('Nenhuma movimentação para os filtros')`), 'Filtro de saída não aplicado');
      await a.ev(`(${botaoFinanceiro('Limpar filtros')}).click()`);
      await waitFor(() => a.ev(`!!(${botaoFinanceiro('Ver lançamento')})`), 'Lançamento não voltou após limpar filtro');
      await a.ev(`(${botaoFinanceiro('Ver lançamento')}).click()`);
      await waitFor(() => a.ev(`document.querySelector('#lancamento-titulo')?.textContent.trim() === 'Aporte de teste'`),
        'Origem da movimentação não abriu');
      await a.ev(`(${botaoFinanceiro('Movimentações')}).click()`);
      console.log('Movimentações em ' + width + ' px: filtro, centavos, origem e ausência de rolagem horizontal.');
    }
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      await a.ev(`(${botaoFinanceiro('Lançamentos')}).click()`);
      await waitFor(() => a.ev(`!!(${botaoFinanceiro('Novo lançamento')})`), 'Formulário financeiro indisponível');
      await a.ev(`(${botaoFinanceiro('Novo lançamento')}).click()`);
      const descricao = 'Atualização E2E ' + width;
      await a.ev(`(() => {
        for (const [nome, valor] of Object.entries({ categoria: 'Ensaio', descricao: ${JSON.stringify(descricao)}, valor: '10,00' })) {
          const campo = document.querySelector('main [name="' + nome + '"]');
          campo.value = valor;
          campo.dispatchEvent(new Event('input', { bubbles: true }));
        }
      })()`);
      await a.ev(`(${botaoFinanceiro('Confirmar')}).click()`);
      await waitFor(() => a.ev(`document.querySelector('#lancamento-titulo')?.textContent.trim() === ${JSON.stringify(descricao)}`),
        'Lançamento não confirmado');
      await a.ev(`(${botaoFinanceiro('Movimentações')}).click()`);
      await waitFor(() => a.ev(`document.querySelector('app-admin-movimentacoes-financeiras')?.textContent.includes(${JSON.stringify(descricao)})`),
        'Histórico permaneceu desatualizado');
      console.log('Histórico atualizado após lançamento em ' + width + ' px.');
    }
    assert.equal(postsFinanceiros, 3);

    const { comprasTeste, vendaId } = await a.ev(`(async () => {
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      const post = async (url, corpo) => {
        const resposta = await fetch(url, { method: 'POST', body: JSON.stringify(corpo), headers: {
          'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(),
        } });
        if (resposta.status !== 201) return { erro: url + ': ' + resposta.status };
        return resposta.json();
      };
      const comprasTeste = [];
      for (const nome of ['Fornecedor E2E A', 'Fornecedor E2E B']) {
        const fornecedor = await post('/api/admin/fornecedores', { nome });
        const compra = await post('/api/admin/compras-materias-primas', {
          fornecedorId: fornecedor.id, dataCompra: '2026-11-20', versao: null,
          linhas: [{ kit: null, quantidade: 1, valorTotal: 12, componentes: [
            { insumoId: ${JSON.stringify(dados.insumoFinanceiro)}, apresentacaoId: null, quantidade: 1, snapshotId: null },
          ] }],
        });
        comprasTeste.push(compra.resumo?.id ?? compra.erro);
      }
      await post('/api/admin/estoque/produtos-acabados/variantes/' + ${JSON.stringify(dados.varianteFinanceiro)} + '/entradas',
        { tipo: 'EntradaManual', quantidade: 1, condicaoDestino: 'Vendavel', semControleValidade: true, motivo: 'Ensaio' });
      const venda = await post('/api/admin/vendas-externas', { canal: 'Feira', dataVenda: '2026-11-20', comprador: 'Cliente E2E',
        itens: [{ produtoId: ${JSON.stringify(dados.produtoFinanceiro)}, varianteId: ${JSON.stringify(dados.varianteFinanceiro)},
          quantidade: 1, precoUnitarioCentavos: '500', descontoCentavos: '0' }],
        recebimentoInicial: { valorCentavos: '500', data: '2026-11-20', meio: 'Dinheiro' } });
      return { comprasTeste, vendaId: venda.id ?? venda.erro };
    })()`);
    for (const id of [...comprasTeste, vendaId]) assert.match(id, /^[0-9a-f-]{36}$/);
    console.log('Compras e venda externa sintéticas criadas para o ensaio de navegação.');
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      await a.ir('/admin/compras');
      const abrir = `[...document.querySelectorAll('main button')].find(b => b.getAttribute('aria-label')?.includes('Abrir compra de Fornecedor E2E A'))`;
      await waitFor(() => a.ev(`!!(${abrir})`), 'Compra de teste ausente');
      await a.ev(`(${abrir}).click()`);
      await waitFor(() => a.ev(`!!(${botaoFinanceiro('Confirmar recebimento')})`), 'Detalhe de compra indisponível');
      await a.ev(`(${botaoFinanceiro('Confirmar recebimento')}).click()`);
      const linkCompra = `document.querySelector('main a[href*="financeiro?compra="]')`;
      await waitFor(() => a.ev(`!!(${linkCompra})`), 'Link de pagamentos ausente');
      await a.ev(`(${linkCompra}).click()`);
      const compraAberta = (id, fornecedor) => a.ev(`new URL(location.href).searchParams.get('compra') === ${JSON.stringify(id)}
        && document.querySelector('#compra-titulo')?.textContent.trim() === ${JSON.stringify(fornecedor)}`);
      await waitFor(() => compraAberta(comprasTeste[0], 'Fornecedor E2E A'), 'Compra A não abriu pelo link');
      console.log('Link Compras → Financeiro confirmado em ' + width + ' px.');
      await a.ir('/admin/financeiro?compra=' + comprasTeste[0]);
      await waitFor(() => compraAberta(comprasTeste[0], 'Fornecedor E2E A'), 'Compra A não restaurada');
      await waitFor(() => a.ev(`!!(${botaoFinanceiro('Outra compra')})`), 'Seletor de outra compra indisponível');
      await a.ev(`(${botaoFinanceiro('Outra compra')}).click()`);
      await waitFor(() => a.ev(`!location.search && !!(${botaoFinanceiro('Consultar compra')})`), 'URL antiga persistiu');
      await a.ev(`(() => {
        const botao = ${botaoFinanceiro('Consultar compra')};
        const seletor = botao.closest('section').querySelector('select');
        seletor.value = ${JSON.stringify(comprasTeste[1])};
        botao.click();
      })()`).catch(() => { throw new Error('Falha ao selecionar a compra B'); });
      await waitFor(() => compraAberta(comprasTeste[1], 'Fornecedor E2E B'), 'Compra B não selecionada');
      console.log('Troca de compra confirmada em ' + width + ' px.');
      await a.ev('history.back()');
      await waitFor(() => a.ev('!location.search && !document.querySelector("#compra-titulo")'), 'Voltar manteve compra antiga');
      await a.ev('history.forward()');
      await waitFor(() => compraAberta(comprasTeste[1], 'Fornecedor E2E B'), 'Avançar não restaurou compra B');
      console.log('Voltar e Avançar confirmados em ' + width + ' px.');
      await waitFor(() => a.ev(`!!(${botaoFinanceiro('Movimentações')})`), 'Aba Movimentações indisponível após Avançar');
      await a.ev(`(${botaoFinanceiro('Movimentações')}).click()`);
      const rotaVenda = '/admin/vendas-externas/' + vendaId;
      const linkVenda = `document.querySelector('main a[href="' + ${JSON.stringify(rotaVenda)} + '"]')`;
      await waitFor(() => a.ev(`!location.search && !!(${linkVenda})`), 'Link da venda externa ausente');
      await a.ev(`(${linkVenda}).click()`);
      const vendaAberta = () => a.ev(`location.pathname === ${JSON.stringify(rotaVenda)}
        && document.querySelector('main')?.textContent.includes('Cliente E2E')`);
      await waitFor(vendaAberta, 'Venda externa não abriu pelo link');
      await a.ir(rotaVenda);
      await waitFor(vendaAberta, 'Venda externa não abriu após recarregar');
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      console.log('Compras, Voltar/Avançar e detalhe de venda externa em ' + width + ' px.');
    }
    await c.ir('/admin/vendas-externas/' + vendaId);
    await waitFor(() => c.ev(`location.pathname === '/admin/login'`), 'Cliente acessou a rota administrativa de venda');
    await a.ir('/admin/vendas-externas/00000000-0000-4000-8000-000000000001');
    await waitFor(() => a.ev(`document.querySelector('main [role="alert"]')?.textContent.includes('Venda não encontrada')`),
      'Venda inexistente sem mensagem de erro');
  }

  if (dados.varianteAuditoria) {
    const telas = [
      ['/admin/fornecedores', 'Fornecedores', 'Novo fornecedor', 'Salvar fornecedor'],
      ['/admin/compras', 'Compras de matérias-primas', 'Nova compra', 'Salvar rascunho'],
      ['/admin/estoque/insumos', 'Matérias-primas', 'Novo material'],
      ['/admin/receitas', 'Receitas', 'Nova receita'],
      ['/admin/producao', 'Produção', 'Nova produção', 'Consultar disponibilidade'],
      ['/admin/estoque/produtos-acabados/' + dados.varianteAuditoria, 'Sabonete', 'Registrar saída'],
    ];
    const vazios = {
      Fornecedores: 'Nenhum fornecedor cadastrado.',
      'Compras de matérias-primas': 'Nenhuma compra cadastrada.',
      Receitas: 'Nenhuma receita encontrada.',
      Produção: 'Nenhuma ordem nesta situação.',
    };
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', {
        width, height: 900, mobile: false, deviceScaleFactor: 1,
      }, a.sessionId);
      for (const [rota, titulo, acao, formulario] of telas) {
        await a.ir(rota);
        const botao = `[...document.querySelectorAll('main button, main a')]
          .find(e => e.textContent.trim() === ${JSON.stringify(acao)} && !e.matches(':disabled') && e.getClientRects().length)`;
        await waitFor(() => a.ev(`document.querySelector('main h1')?.textContent.trim() === ${JSON.stringify(titulo)}
           && !!(${botao})`), 'Tela ou ação indisponível: ' + titulo);
        if (vazios[titulo]) {
          await waitFor(() => a.ev(`document.querySelector('main')?.textContent.includes(${JSON.stringify(vazios[titulo])})`),
            'Estado vazio indisponível: ' + titulo);
        }
        assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'), titulo + ' excede a largura');
        assert.ok(await a.ev(`(() => { const e = ${botao}; e.focus(); return document.activeElement === e; })()`), acao);
        if (formulario || acao === 'Registrar saída' || acao === 'Nova receita') {
          await a.ev(`(${botao}).click()`);
          await waitFor(() => a.ev(`document.querySelector('main')?.textContent.includes(
            ${JSON.stringify(formulario ?? (acao === 'Registrar saída' ? 'Saldo disponível para saída' : 'Rendimento'))})`),
            'Formulário indisponível: ' + titulo);
          assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'), titulo + ' formulário excede a largura');
          if (titulo === 'Fornecedores') {
            await a.ev(`document.querySelector('form[aria-labelledby="supplier-form-title"] button[type=submit]').click()`);
            await waitFor(() => a.ev(`(() => {
              const campo = document.querySelector('#supplier-name');
              return campo.labels.length > 0 && campo.getAttribute('aria-invalid') === 'true'
                && !!document.getElementById(campo.getAttribute('aria-describedby'));
            })()`), 'Validação do nome do fornecedor sem associação acessível');
          }
        }
      }
      const visivel = selector => a.ev(`!!document.querySelector(${JSON.stringify(selector)})`);
      const larguraValida = () => a.ev('document.documentElement.scrollWidth <= innerWidth');
      const preencher = (selector, valor) => a.ev(`(() => {
        const campo = document.querySelector(${JSON.stringify(selector)});
        campo.value = ${JSON.stringify(valor)};
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      })()`);
      await a.ir('/admin/estoque/produtos-acabados/' + dados.varianteAuditoria);
      const abrirEntrada = `[...document.querySelectorAll('main button')]
        .find(e => e.textContent.trim() === 'Registrar entrada' && e.getClientRects().length)`;
      await waitFor(() => a.ev(`!!(${abrirEntrada})`), 'Entrada de acabados indisponivel');
      await a.ev(`(${abrirEntrada}).click()`);
      await waitFor(() => visivel('#finished-entry-quantity'), 'Formulario de entrada de acabados');
      assert.equal(await visivel('#finished-entry-lot-mode, #finished-entry-fabrication, #finished-entry-expiry'), false);
      assert.ok(await larguraValida(), 'Entrada de acabados excede a largura');
      respostas.delete('/api/admin/estoque/produtos-acabados/variantes/' + dados.varianteAuditoria + '/entradas');
      await preencher('#finished-entry-quantity', '1');
      await preencher('#finished-entry-reason', 'Estoque sintetico do ensaio');
      await a.ev(`document.querySelector('#finished-entry-quantity').form.requestSubmit()`);
      const rotaEntrada = '/api/admin/estoque/produtos-acabados/variantes/' + dados.varianteAuditoria + '/entradas';
      await waitFor(() => respostas.get(rotaEntrada) === 201 &&
        a.ev("document.querySelector('main').textContent.includes('Movimenta\u00e7\u00e3o registrada com sucesso')"),
        'Entrada de acabados sem validade nao confirmada');

      await a.ir('/admin/estoque/insumos/novo');
      await waitFor(() => visivel('#stock-supply-name'), 'Cadastro de materia-prima indisponivel');
      assert.equal(await visivel('[formControlName="controlaLote"], [formControlName="validadeObrigatoria"]'), false);
      assert.ok(await larguraValida(), 'Cadastro de materia-prima excede a largura');

      await a.ir('/admin/estoque/insumos');
      const href = await waitFor(() => a.ev(`[...document.querySelectorAll('main a[href]')]
        .find(a => a.pathname.split('/').length === 6 && a.pathname.startsWith('/admin/estoque/insumos/') && a.pathname.endsWith('/consultas'))
        ?.getAttribute('href')`), 'Consulta de materia-prima indisponivel');
      const material = href.split('/')[4];
      await a.ir('/admin/compras/apresentacoes/' + material);
      await waitFor(() => visivel('#stock-supply-forms-title'), 'Apresentacoes em Compras indisponiveis');
      assert.equal(await visivel('#stock-supply-name'), false);
      assert.ok(await larguraValida(), 'Apresentacoes excedem a largura');
      console.log('Entrada de acabados confirmada e cadastro de materiais e apresentacoes verificados em ' + width + ' px.');
      console.log('Seis telas administrativas verificadas em ' + width + ' px.');
    }
  }

  if (dados.varianteAuditoria) {
    const rotaProdutos = '/api/admin/produtos';
    const clicar = async (rotulo, aria = false) => {
      const expressao = "Array.from(document.querySelectorAll('main button')).find(b => !b.disabled && b.getClientRects().length && "
        + (aria ? "b.getAttribute('aria-label')" : "b.textContent.trim()") + " === " + JSON.stringify(rotulo) + ")";
      await waitFor(() => a.ev('!!(' + expressao + ')'), rotulo);
      await a.ev('(' + expressao + ').click()');
    };
    const preencher = (seletor, valor) => a.ev('(() => { const e = document.querySelector(' + JSON.stringify(seletor)
      + '); e.value = ' + JSON.stringify(valor) + "; e.dispatchEvent(new Event('input', { bubbles: true })); })()");
    const publico = await a.ev('fetch(' + JSON.stringify(rotaProdutos)
      + ').then(r => r.json()).then(ps => ps.find(p => p.variantes.some(v => v.id === '
      + JSON.stringify(dados.varianteAuditoria) + ')))');
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      for (const simples of [true, false]) {
        const nome = (simples ? 'Simples E2E ' : 'Com opcoes E2E ') + width;
        const corpo = {
          nome, categoriaId: publico.categoriaId, simples,
          variantePadrao: { identificacao: simples ? null : 'Branca', unidadeVenda: 'UN', precoVenda: 19 },
        };
        if (!simples) {
          corpo.caracteristicas = [{ referencia: 'cor', nome: 'Cor', valores: [
            { referencia: 'b', nome: 'Branca' }, { referencia: 'v', nome: 'Verde' },
          ] }];
          corpo.variantePadrao.caracteristicas = [{ referenciaCaracteristica: 'cor', referenciaValor: 'b' }];
        }
        const produto = await a.ev("(async () => { const {requestToken} = await fetch('/api/auth/csrf').then(r => r.json());"
          + "return fetch('/api/admin/produtos', { method: 'POST', body: " + JSON.stringify(JSON.stringify(corpo))
          + ", headers: {'Content-Type':'application/json','X-XSRF-TOKEN':requestToken} }).then(r => r.json()); })()");
        assert.ok(produto.id, JSON.stringify(produto));
        await a.ir('/admin/produtos');
        await clicar('Gerenciar op\u00e7\u00f5es de ' + nome, true);
        await clicar('Adicionar op\u00e7\u00e3o');
        await preencher('#managed-variant-name', simples ? 'Lavanda' : 'Verde');
        await preencher('#managed-variant-price', '2350');
        if (!simples) {
          await a.ev("(() => { const o = [...document.querySelectorAll('app-admin-produto-variante-form option')].find(o => o.textContent.trim() === 'Verde');"
            + "o.parentElement.value = o.value; o.parentElement.dispatchEvent(new Event('change', { bubbles: true })); })()");
        }
        assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
        await clicar('Salvar op\u00e7\u00e3o');
        await waitFor(() => a.ev('!document.querySelector("#managed-variant-name")'), 'Salvar opção');
        const salvo = await a.ev('fetch(' + JSON.stringify(rotaProdutos + '/' + produto.id) + ').then(r => r.json())');
        assert.equal(salvo.variantes.length, 2);
        assert.equal(salvo.variantes.find(v => v.identificacao === (simples ? 'Lavanda' : 'Verde')).precoVenda, 23.5);
        if (simples) {
          await a.ir('/admin/produtos');
          await clicar('Editar ' + nome, true);
          await waitFor(() => a.ev('!!document.querySelector("#product-description")'), 'Edição geral');
          assert.equal(await a.ev('!!document.querySelector("#product-mode-simple, #default-variant-unit, #features-title")'), false);
          await preencher('#product-description', 'Descricao editada no ensaio');
          await clicar('Salvar altera\u00e7\u00f5es');
          await waitFor(() => a.ev('!document.querySelector("#product-description")'), 'Salvar dados gerais');
        }
      }
      await a.ir('/produtos/' + publico.id);
      await waitFor(() => a.ev('!!document.querySelector("#produto-opcao")'), 'Seletor público');
      assert.equal(await a.ev('document.querySelector("label[for=produto-opcao]").firstChild.textContent.trim()'), 'Op\u00e7\u00e3o');
      await a.ev("(() => { const s = document.querySelector('#produto-opcao'); s.value = "
        + JSON.stringify(publico.variantes.find(v => v.id !== dados.varianteAuditoria).id)
        + "; s.dispatchEvent(new Event('change', { bubbles: true })); })()");
      assert.match(await a.ev('document.querySelector(".product-detail__price").textContent'), /10,01/);
      assert.match(await a.ev('document.querySelector("[data-testid=product-availability]").textContent'), /esgotada/);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      console.log('Opções, edição geral e loja verificadas em ' + width + ' px.');
      await a.ir('/admin/produtos');
    }
    await clicar('Gerenciar opções de ' + publico.nome, true);
    await clicar('Configurar características');
    await clicar('Peso');
    await clicar('Adicionar valor');
    await preencher('input[id^=measure-value-]', '100');
    await clicar('Adicionar valor');
    const medidas = await a.ev("Array.from(document.querySelectorAll('input[id^=measure-value-]'), e => e.id)");
    assert.equal(medidas.length, 2);
    await preencher('#' + medidas[1], '200');
    await clicar('Salvar alterações');
    await waitFor(() => a.ev('!document.querySelector("#features-title")'), 'Salvar característica');
    for (const variante of publico.variantes) {
      await clicar('Editar opção ' + variante.identificacao, true);
      const valor = variante.id === dados.varianteAuditoria ? '100 g' : '200 g';
      await a.ev("(() => { const o = [...document.querySelectorAll('app-admin-produto-variante-form option')].find(o => o.textContent.trim() === "
        + JSON.stringify(valor) + "); o.parentElement.value = o.value; o.parentElement.dispatchEvent(new Event('change', { bubbles: true })); })()");
      await clicar('Salvar opção');
      await waitFor(() => a.ev('!document.querySelector("#managed-variant-name")'), 'Salvar medida na opção');
    }
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      await a.ir('/produtos/' + publico.id);
      await waitFor(() => a.ev('!!document.querySelector("#produto-opcao")'), 'Seletor de peso');
      assert.equal(await a.ev('document.querySelector("label[for=produto-opcao]").firstChild.textContent.trim()'), 'Peso');
      assert.deepEqual(await a.ev('Array.from(document.querySelector("#produto-opcao").options, o => o.textContent.trim()).sort()'), ['100 g', '200 g']);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      console.log('Loja atualizou o seletor para Peso em ' + width + ' px.');
    }
  }

  if (dados.demonstracao) {
    await c.ir('/produtos');
    await waitFor(() => c.ev(`document.querySelector('main')?.textContent.includes('Sabonete demonstração')
      && document.querySelector('main')?.textContent.includes('Vela demonstração')
      && [...document.querySelectorAll('main img')].filter(i => i.src.includes('/conteudo') && i.naturalWidth > 0).length >= 2`),
      'Catálogo sintético com duas imagens');
    await a.ir('/admin/pedidos');
    await waitFor(() => a.ev("document.querySelector('main h1')?.textContent.trim() === 'Pedidos'"), 'Painel administrativo');
    assert.equal(await a.ev(`fetch('/api/admin/configuracoes/pix-manual').then(async r =>
      r.ok && (await r.json()).instrucoesAdicionais.includes('Não faça transferências.'))`), true);
    const novo = await abrir('/cadastro');
    const registrado = await novo.ev(`(() => {
      const senha = 'E2e!' + crypto.randomUUID() + 'a1';
      const campos = {
        nomeCompleto: 'Cliente sintético', cpf: '52998224725', telefone: '11912345678',
        email: 'demo-' + crypto.randomUUID() + '@example.test', senha, confirmacaoSenha: senha,
        cep: '95000000', uf: 'RS', cidade: 'Caxias do Sul', bairro: 'Bairro de teste',
        logradouro: 'Rua de teste', numero: '100',
      };
      for (const [nome, valor] of Object.entries(campos)) {
        const campo = document.querySelector('[formControlName="' + nome + '"]');
        if (!campo) return false;
        campo.value = valor;
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      }
      document.querySelector('form button[type=submit]').click();
      return true;
    })()`);
    assert.equal(registrado, true);
    await waitFor(() => novo.ev("document.querySelector('main')?.textContent.includes('Cadastro realizado com sucesso')"),
      'Cadastro Angular da demonstração');
    const link = new URL(await checkpoint('email'));
    assert.equal(link.origin, origin);
    await novo.ir(link.pathname + link.search + link.hash);
    await waitFor(() => novo.ev("document.querySelector('main')?.textContent.includes('E-mail confirmado')"),
      'Confirmação pelo e-mail capturado localmente');
    console.log('Demonstração: catálogo com imagens, painel, PIX sintético e confirmação de e-mail local conferidos.');
  }

  const cookies = [];
  for (const { sessionId } of [c, a]) {
    const { cookies: armazenados } = await call('Network.getCookies', { urls: [origin] }, sessionId);
    const sessao = armazenados.find(cookie => cookie.name === '__Host-Xaquiamuni.Auth');
    assert.equal(!!(sessao?.secure && sessao.httpOnly && sessao.sameSite === 'Lax'), true);
    cookies.push(sessao.value);
  }
  assert.equal(cookies[0] !== cookies[1], true);
  assert.equal(await c.ev(`(async () => {
    const { requestToken } = await (await fetch('/api/auth/csrf')).json();
    return (await fetch('/api/auth/logout', { method: 'POST', headers: {'X-XSRF-TOKEN': requestToken} })).status;
  })()`), 204);
  assert.equal(await c.ev('fetch("/api/auth/me").then(r => r.status)'), 401);
  assert.equal(await a.ev('fetch("/api/admin/auth/me").then(r => r.status)'), 200);
  assert.equal(externos, 0);
  console.log('Chrome: Angular, HTTPS/WebCrypto/localStorage, CLIENTE/ADMIN, cookies isolados, CSRF, leituras 200 e acesso administrativo 403; zero chamadas externas.');
  }
} catch (erro) {
  console.error(erro.message);
  process.exitCode = 1;
} finally {
  linhas.close();
  process.stdin.pause();
  if (call && socket?.readyState === WebSocket.OPEN) await call('Browser.close').catch(() => {});
  socket?.close();
  if (await Promise.race([closed.then(() => false), delay(1500, true, { ref: false })])) {
    chrome.kill();
    await Promise.race([closed, delay(3000, undefined, { ref: false })]);
  }
}
