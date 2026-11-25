import { validarCorreios } from './chrome-correios.mjs';
import { validarModoAdmin } from './chrome-modo-admin.mjs';
import { createInterface } from 'node:readline';
import { validarJornada } from './chrome-compra-jornada.mjs';
import { validarPromocoes } from './chrome-promocoes.mjs';
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
  const quitacoesSemResposta = new Map();
  const respostasEmRecuperacao = new Map();
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
  const confirmacoes = new Map();
  socket.addEventListener('message', event => {
    const data = JSON.parse(event.data);
    if (data.method === 'Page.javascriptDialogOpening' && confirmacoes.has(data.sessionId)) {
      const confirmacao = confirmacoes.get(data.sessionId);
      confirmacao.mensagem = data.params.message;
      void call('Page.handleJavaScriptDialog', { accept: confirmacao.aceitar }, data.sessionId)
        .then(() => { confirmacao.processada = true; }).catch(() => {});
    } else if (data.method === 'Fetch.requestPaused') {
      const { requestId, request } = data.params;
      const local = new URL(request.url).origin === origin;
      const comprovante = respostasEmRecuperacao.get(data.sessionId);
      if (local && comprovante && request.method === (comprovante.metodo ?? 'POST') && new URL(request.url).pathname === comprovante.rota) {
        if (data.params.responseStatusCode === undefined) {
          const headers = Object.fromEntries(Object.entries(request.headers).map(([nome, valor]) => [nome.toLowerCase(), valor]));
          comprovante.envios.push({ chave: headers['idempotency-key'], versao: headers['x-comprovante-versao'], ...(comprovante.capturarCorpo && { corpo: request.postData }) });
          void call('Fetch.continueRequest', { requestId, interceptResponse: true }, data.sessionId)
            .catch(erro => { comprovante.erro = erro.message; });
        } else {
          comprovante.status.push(data.params.responseStatusCode);
          const primeira = comprovante.status.length === 1;
          if (!primeira) respostasEmRecuperacao.delete(data.sessionId);
          void call(primeira ? 'Fetch.failRequest' : 'Fetch.continueResponse',
            primeira ? { requestId, errorReason: 'ConnectionClosed' } : { requestId }, data.sessionId)
            .catch(erro => { comprovante.erro = erro.message; });
        }
        return;
      }
      const perda = quitacoesSemResposta.get(data.sessionId);
      if (local && perda && request.method === 'POST' && new URL(request.url).pathname === perda.rota) {
        if (data.params.responseStatusCode === undefined) {
          postsFinanceiros++;
          perda.chave = Object.entries(request.headers).find(([nome]) => nome.toLowerCase() === 'idempotency-key')?.[1];
          perda.corpo = JSON.parse(request.postData);
          void call('Fetch.continueRequest', { requestId, interceptResponse: true }, data.sessionId)
            .catch(erro => { perda.erro = erro.message; });
        } else {
          perda.status = data.params.responseStatusCode;
          quitacoesSemResposta.delete(data.sessionId);
          void call('Fetch.failRequest', { requestId, errorReason: 'ConnectionClosed' }, data.sessionId)
            .catch(erro => { perda.erro = erro.message; });
        }
        return;
      }
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

  async function abrir(path, seletorPronto = "app-root main h1") {
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
      await waitFor(() => ev(`!!document.querySelector(${JSON.stringify(seletorPronto)})`), 'Angular');
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
      perderResposta: (rota, metodo = 'POST') => {
        const observacao = { rota, metodo, envios: [], status: [], capturarCorpo: true };
        respostasEmRecuperacao.set(sessionId, observacao);
        return observacao;
      },
      perderRespostaComprovante: rota => {
        const observacao = { rota, envios: [], status: [] };
        respostasEmRecuperacao.set(sessionId, observacao);
        return observacao;
      },
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

  if (dados.exclusaoCategorias) {
    await call('Page.enable', {}, a.sessionId);
    await a.ir('/admin/categorias');
    await waitFor(() => a.ev("!!document.querySelector('.admin-shell .admin-theme-toggle')"), 'shell de categorias');
    const botao = nome => "[...document.querySelectorAll('main button')].find(b => !b.disabled && "
      + "(b.textContent.trim() === " + JSON.stringify(nome) + " || b.getAttribute('aria-label') === " + JSON.stringify(nome) + "))";
    const acionar = async nome => {
      const expr = botao(nome);
      await waitFor(() => a.ev('!!(' + expr + ')'), nome);
      await a.ev('(' + expr + ').click()');
    };
    const contem = texto => waitFor(() => a.ev("document.querySelector('main')?.innerText.includes(" + JSON.stringify(texto) + ")"), texto);
    const confirmar = async (aceitar, nome, acao = 'Excluir categoria') => {
      const confirmacao = { aceitar };
      confirmacoes.set(a.sessionId, confirmacao);
      try {
        await a.ev("setTimeout(() => (" + botao(acao) + ").click(), 0)");
        await waitFor(() => confirmacao.processada, 'confirmação nativa de exclusão');
        assert.ok(confirmacao.mensagem.includes(nome) && confirmacao.mensagem.includes('permanente'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
    };
    const api = (path, method, body, versao) => a.ev(`(async () => {
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      const r = await fetch(${JSON.stringify(path)}, {
        method: ${JSON.stringify(method)}, headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken,
          ...( ${JSON.stringify(versao ?? null)} ? { 'If-Match': '"' + ${JSON.stringify(versao ?? null)} + '"' } : {}) },
        body: JSON.stringify(${JSON.stringify(body)})
      });
      return [r.status, await r.json().catch(() => null)];
    })()`);
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      if (width === 375) {
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
        assert.equal(await a.ev("document.querySelector('.admin-shell').dataset.adminTheme"), 'dark');
      } else {
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
        assert.equal(await a.ev("document.querySelector('.admin-shell').dataset.adminTheme"), 'light');
      }
      const nome = 'Categoria descartavel ' + width;
      await acionar('Nova categoria');
      await waitFor(() => a.ev("!!document.querySelector('#category-name')"), 'formulário da categoria');
      await a.ev(`(() => { const campo = document.querySelector('#category-name'); campo.value = ${JSON.stringify(nome)};
        campo.dispatchEvent(new Event('input', { bubbles: true })); })()`);
      await acionar('Salvar categoria');
      await contem('Categoria criada com sucesso.');
      await confirmar(false, nome, 'Excluir ' + nome);
      assert.equal(await a.ev("!!document.querySelector('app-categoria-form')"), false);
      await acionar('Editar ' + nome);
      await waitFor(() => a.ev('!!(' + botao('Excluir categoria') + ')'), 'ação de exclusão');
      const exclusaoBotao = '(' + botao('Excluir categoria') + ')';
      assert.ok(await a.ev(exclusaoBotao + ".closest('app-categoria-form section') !== null"));
      await a.ev(exclusaoBotao + '.focus()');
      await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9, modifiers: 8 }, a.sessionId);
      await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9, modifiers: 8 }, a.sessionId);
      assert.equal(await a.ev("document.activeElement.matches(':focus-visible')"), true);
      await confirmar(false, nome);
      await contem(nome);
      assert.equal(await a.ev("document.documentElement.scrollWidth <= innerWidth"), true);
      await acionar('Cancelar');
      await confirmar(true, nome, 'Excluir ' + nome);
      await contem('Categoria excluída com sucesso.');
      await a.ir('/admin/categorias');
      await waitFor(() => a.ev("!document.querySelector('app-categoria-form') && !!document.querySelector('main #category-search')"), 'listagem após recarregar');
      const lista = await a.ev("fetch('/api/admin/categorias').then(r => r.json())");
      assert.equal(lista.some(item => item.nome === nome), false);
    }
    const rota = '/api/admin/categorias';
    const [criada, categoria] = await api(rota, 'POST', { nome: 'Categoria com vinculo', ordemExibicao: 0 });
    assert.equal(criada, 201);
    await a.ir('/admin/categorias');
    await acionar('Editar Categoria com vinculo');
    await waitFor(() => a.ev('!!(' + botao('Excluir categoria') + ')'), 'versão carregada');
    const [editada] = await api(rota + '/' + categoria.id, 'PUT', { nome: categoria.nome, ordemExibicao: 1 }, categoria.versao);
    assert.equal(editada, 200);
    await confirmar(true, categoria.nome);
    await acionar('Recarregar categoria');
    const [criado, produto] = await api('/api/admin/produtos', 'POST', {
      nome: 'Produto bloqueante', categoriaId: categoria.id, simples: true,
      variantePadrao: { identificacao: null, unidadeVenda: 'UN', precoVenda: 19 },
    });
    assert.equal(criado, 201);
    await confirmar(true, categoria.nome);
    await contem('possui produtos vinculados');
    assert.equal(await a.ev(`fetch('/api/admin/produtos/${produto.id}').then(r => r.status)`), 200);
    await acionar('Editar Categoria com produto publicado');
    await waitFor(() => a.ev("document.querySelector('#category-name')?.value === 'Categoria com produto publicado'"), 'categoria publicada carregada');
    await confirmar(true, 'Categoria com produto publicado');
    await contem('possui produtos vinculados');
    console.log('Exclusão de categorias em 375 e 1280 px, versões antigas e produtos publicados e despublicados validados.');
  }

  if (dados.insumoFornecedor) {
    const rota = '/admin/fornecedores';
    const base = '/api/admin/fornecedores';
    const compras = '/api/admin/compras-materias-primas';
    const botao = nome => "[...document.querySelectorAll('main button')].find(b => !b.disabled && (b.textContent.trim() === "
      + JSON.stringify(nome) + " || b.getAttribute('aria-label') === " + JSON.stringify(nome) + "))";
    const clicar = async nome => {
      const expr = botao(nome);
      await waitFor(() => a.ev('!!(' + expr + ')'), nome);
      await a.ev('(' + expr + ').click()');
    };
    const texto = esperado => waitFor(() => a.ev("document.querySelector('main')?.innerText.includes("
      + JSON.stringify(esperado) + ")"), esperado);
    const ler = path => a.ev(`fetch(${JSON.stringify(path)}).then(r=>r.json())`);
    const api = (path, method, body) => a.ev(`(async () => {
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      const r = await fetch(${JSON.stringify(path)}, { method: ${JSON.stringify(method)},
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID() },
        body: JSON.stringify(${JSON.stringify(body)}) });
      return [r.status, await r.json().catch(() => null)];
    })()`);
    async function confirmar(aceitar, nome) {
      const confirmacao = { aceitar };
      confirmacoes.set(a.sessionId, confirmacao);
      try {
        await a.ev("setTimeout(() => (" + botao('Excluir fornecedor') + ").click(), 0)");
        await waitFor(() => confirmacao.processada, 'confirmação nativa de exclusão');
        assert.ok(confirmacao.mensagem.includes(nome) && confirmacao.mensagem.includes('permanente'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
    }
    await call('Page.enable', {}, a.sessionId);
    await a.ir(rota);
    await texto('Novo fornecedor');
    for (const [width, modo] of [[375, 'dark'], [375, 'light'], [1280, 'dark'], [1280, 'light']]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      if (await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") !== modo)
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
      const nome = 'Fornecedor descartável ' + width + ' ' + modo;
      await clicar('Novo fornecedor');
      await a.ev(`(() => {
        const campo = document.querySelector('#supplier-name');
        campo.value = ${JSON.stringify(nome)}; campo.dispatchEvent(new Event('input', { bubbles: true }));
      })()`);
      await clicar('Salvar fornecedor');
      await texto('Fornecedor cadastrado.');
      const primeiro = (await ler(base)).find(f => f.nome === nome);
      await clicar('Editar ' + nome);
      await a.ev("(" + botao('Cancelar') + ").focus()");
      await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
      await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
      assert.ok(await a.ev("document.activeElement.textContent.trim() === 'Excluir fornecedor' && document.activeElement.matches(':focus-visible')"));
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      await confirmar(false, nome);
      assert.ok((await ler(base)).some(f => f.id === primeiro.id));
      await confirmar(true, nome);
      await texto('Fornecedor excluído.');
      await a.ir(rota);
      await texto('Novo fornecedor');
      assert.ok(!(await ler(base)).some(f => f.id === primeiro.id));
      const [status, novo] = await api(base, 'POST', { nome });
      assert.equal(status, 201);
      assert.notEqual(novo.id, primeiro.id);
      await a.ir(rota);
      await texto(nome);
    }
    const [statusA, fornecedorA] = await api(base, 'POST', { nome: 'Fornecedor com histórico A' });
    const [statusB, fornecedorB] = await api(base, 'POST', { nome: 'Fornecedor com histórico B' });
    assert.equal(statusA, 201);
    assert.equal(statusB, 201);
    const entrada = {
      fornecedorId: fornecedorA.id, dataCompra: '2026-11-23',
      linhas: [{ quantidade: 1, valorTotal: 10, componentes: [{ insumoId: dados.insumoFornecedor, quantidade: 1 }] }],
    };
    const [statusCompra, compra] = await api(compras, 'POST', entrada);
    assert.equal(statusCompra, 201);
    await a.ir(rota);
    await clicar('Editar ' + fornecedorA.nome);
    await confirmar(true, fornecedorA.nome);
    await texto('Há compras vinculadas');
    const compraUrl = compras + '/' + compra.resumo.id;
    assert.equal((await ler(compraUrl)).resumo.fornecedorId, fornecedorA.id);
    const [statusTroca] = await api(compraUrl, 'PUT', { ...entrada, fornecedorId: fornecedorB.id, versao: compra.versao });
    assert.equal(statusTroca, 200);
    await confirmar(true, fornecedorA.nome);
    await texto('já foi utilizado');
    await clicar('Inativar fornecedor');
    await clicar('Confirmar inativação');
    await texto('Fornecedor inativado.');
    await clicar('Editar ' + fornecedorB.nome);
    await confirmar(true, fornecedorB.nome);
    await texto('Há compras vinculadas');
    assert.equal((await ler(compraUrl)).resumo.fornecedorId, fornecedorB.id);
    assert.equal(await a.ev(`fetch('${base}?ativo=true').then(r=>r.json()).then(x=>x.some(f=>f.id==='${fornecedorA.id}'))`), false);
    await a.ir('/admin/compras');
    await clicar('Nova compra');
    await waitFor(() => a.ev(`[...document.querySelectorAll('select[name=fornecedor] option')].some(o=>o.value==='${fornecedorB.id}')`),
      'fornecedor ativo disponível no formulário de compra');
    assert.equal(await a.ev(`[...document.querySelectorAll('select[name=fornecedor] option')].some(o=>o.value==='${fornecedorA.id}')`), false);
    console.log('Fornecedores: exclusão, troca em rascunho e inativação verificadas em Chrome.');
  }

  if (dados.exclusaoInsumo) {
    await call('Page.enable', {}, a.sessionId);
    const base = '/api/admin/insumos';
    const tela = '/admin/estoque/insumos';
    const comprarEm = '/api/admin/compras-materias-primas';
    const pedir = (rota, method = 'GET', dados = undefined) => a.ev(`(async () => {
      const {requestToken} = await fetch('/api/auth/csrf').then(r => r.json());
      const r = await fetch(${JSON.stringify(rota)}, {method:${JSON.stringify(method)},
        headers:{'Content-Type':'application/json','X-XSRF-TOKEN':requestToken,'Idempotency-Key':crypto.randomUUID()},
        body:${dados === undefined ? 'undefined' : JSON.stringify(JSON.stringify(dados))}});
      const texto = await r.text();
      return {status:r.status, body:texto && r.headers.get('Content-Type')?.includes('json') ? JSON.parse(texto) : null};
    })()`);
    const criar = async nome => {
      const r = await pedir(base, 'POST', { nome, tipo: 'MateriaPrima', unidadeBase: 'Grama',
        controlaLote: false, validadeObrigatoria: false });
      assert.equal(r.status, 201, JSON.stringify(r));
      return r.body.resumo.insumoId;
    };
    const apresentar = async (id, nome) => {
      const r = await pedir(base + '/' + id + '/formas-compra', 'POST', {
        nome, quantidadeEquivalentePorEmbalagem: 10, unidade: 'Grama'
      });
      assert.equal(r.status, 201, JSON.stringify(r));
      return r.body.id;
    };
    const botao = rotulo => "[...document.querySelectorAll('main button')].find(b => !b.disabled && "
      + "(b.textContent.trim() === " + JSON.stringify(rotulo) + " || b.getAttribute('aria-label') === " + JSON.stringify(rotulo) + "))";
    const texto = palavra => waitFor(() => a.ev("document.querySelector('main')?.innerText.includes("
      + JSON.stringify(palavra) + ')'), palavra);
    const confirmar = async (rotulo, nome, aceitar) => {
      const dialogo = { aceitar };
      confirmacoes.set(a.sessionId, dialogo);
      try {
        await waitFor(() => a.ev('!!(' + botao(rotulo) + ')'), rotulo);
        await a.ev('setTimeout(() => (' + botao(rotulo) + ').click(), 0)');
        await waitFor(() => dialogo.processada, 'confirmação de ' + nome);
        assert.ok(dialogo.mensagem.includes(nome));
        if (rotulo.startsWith('Excluir')) assert.ok(dialogo.mensagem.includes('permanente'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
    };
    const detalhe = id => pedir(base + '/' + id);
    const editar = id => a.ir(tela + '/' + id);
    const editarFormas = id => a.ir('/admin/compras/apresentacoes/' + id);
    const inicial = await criar('Material descartável');
    await apresentar(inicial, 'Caixa descartável');
    await apresentar(inicial, 'Saco preservado');
    await editar(inicial);
    await waitFor(() => a.ev('!!(' + botao('Excluir insumo') + ')'), 'ficha do insumo');
    for (const [width, modo] of [[375, 'dark'], [375, 'light'], [1280, 'dark'], [1280, 'light']]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      if (await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") !== modo)
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
      await waitFor(async () => await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") === modo, modo);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
    }
    await a.ev("(" + botao('Excluir insumo') + ").focus()");
    await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
    await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
    assert.ok(await a.ev("document.activeElement.matches(':focus-visible')"));
    await editarFormas(inicial);
    await confirmar('Excluir forma de compra Caixa descartável', 'Caixa descartável', false);
    assert.equal((await detalhe(inicial)).body.formasCompra.length, 2);
    await confirmar('Excluir forma de compra Caixa descartável', 'Caixa descartável', true);
    await texto('Forma de compra excluída.');
    await editarFormas(inicial);
    assert.deepEqual((await detalhe(inicial)).body.formasCompra.map(f => f.nome), ['Saco preservado']);
    await apresentar(inicial, 'Outra embalagem');
    await editar(inicial);
    await confirmar('Excluir insumo', 'Material descartável', false);
    assert.equal((await detalhe(inicial)).body.formasCompra.length, 2);
    await confirmar('Excluir insumo', 'Material descartável', true);
    await texto('Insumo excluído.');
    assert.equal((await detalhe(inicial)).status, 404);
    assert.notEqual(await criar('Material descartável'), inicial);

    const comercial = await criar('Material comprado');
    const forma = await apresentar(comercial, 'Pacote da compra');
    const compra = { fornecedorId: dados.exclusaoInsumo.fornecedor, dataCompra: '2026-11-17',
      linhas: [{ quantidade: 1, valorTotal: 20, componentes: [{ insumoId: comercial, apresentacaoId: forma, quantidade: 1 }] }] };
    const registro = await pedir(comprarEm, 'POST', compra);
    assert.equal(registro.status, 201, JSON.stringify(registro));
    await editarFormas(comercial);
    await confirmar('Excluir forma de compra Pacote da compra', 'Pacote da compra', true);
    await texto('A apresentação possui histórico');
    await editar(comercial);
    await confirmar('Excluir insumo', 'Material comprado', true);
    await texto('O material possui histórico de compras');
    await editarFormas(comercial);
    await confirmar('Inativar forma de compra Pacote da compra', 'Pacote da compra', true);
    await texto('Forma de compra inativada');
    assert.equal((await pedir(comprarEm, 'POST', compra)).status, 400);
    assert.equal((await detalhe(comercial)).body.formasCompra[0].ativa, false);

    const estoque = await criar('Material com estoque');
    const entrada = await pedir(base + '/' + estoque + '/entradas', 'POST',
      { tipo: 'EstoqueInicial', quantidadeOriginal: 2, unidadeOriginal: 'Grama' });
    assert.equal(entrada.status, 201, JSON.stringify(entrada));
    const saida = await pedir(base + '/' + estoque + '/saidas', 'POST',
      { tipo: 'SaidaManual', quantidadeOriginal: 2, unidadeOriginal: 'Grama', motivo: 'Uso de teste' });
    assert.ok([200, 201].includes(saida.status), JSON.stringify(saida));
    assert.equal((await detalhe(estoque)).body.resumo.saldoFisico, 0);
    await editar(estoque);
    await confirmar('Excluir insumo', 'Material com estoque', true);
    await texto('O material possui histórico de estoque');
    assert.equal((await pedir(base + '/' + estoque + '/movimentacoes')).body.totalItens, 2);

    const receitaInsumo = await criar('Material da receita');
    const receita = await pedir('/api/admin/receitas', 'POST', {
      produtoVarianteId: dados.exclusaoInsumo.variante, nome: 'Receita de validação', rendimento: 1,
      itens: [{ insumoId: receitaInsumo, quantidade: '1', unidadeBase: 'Grama' }]
    });
    assert.equal(receita.status, 201, JSON.stringify(receita));
    const receitaId = receita.body.resumo.id;
    await editar(receitaInsumo);
    await confirmar('Excluir insumo', 'Material da receita', true);
    await texto('O material consta em uma versão de receita');
    assert.equal((await pedir('/api/admin/receitas/' + receitaId)).status, 200);
    assert.equal((await pedir(base + '/' + receitaInsumo + '/entradas', 'POST',
      { tipo: 'EstoqueInicial', quantidadeOriginal: 2, unidadeOriginal: 'Grama' })).status, 201);
    const previa = await pedir('/api/admin/producao/previa?receitaId=' + receitaId + '&quantidade=1');
    assert.equal(previa.status, 200, JSON.stringify(previa));
    const ordem = await pedir('/api/admin/producao', 'POST',
      { receitaId, receitaVersaoId: previa.body.receitaVersaoId, quantidade: 1 });
    assert.equal(ordem.status, 201, JSON.stringify(ordem));
    console.log('Insumo, formas, compras, estoque, receitas, produção e inativação verificados no Chrome.');
  }

  if (dados.exclusaoReceita) {
    await call('Page.enable', {}, a.sessionId);
    const base = '/api/admin/receitas';
    const pedir = (rota, method = 'GET', corpo, versao) => a.ev(`(async () => {
      const {requestToken} = await fetch('/api/auth/csrf').then(r => r.json());
      const r = await fetch(${JSON.stringify(rota)}, { method: ${JSON.stringify(method)},
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(),
          ...${JSON.stringify(versao ? { 'If-Match': versao } : {})} },
        body: ${corpo === undefined ? 'undefined' : JSON.stringify(JSON.stringify(corpo))} });
      const texto = await r.text();
      return { status: r.status, etag: r.headers.get('ETag'), body: texto && r.headers.get('Content-Type')?.includes('json') ? JSON.parse(texto) : null };
    })()`);
    const obter = async (rota, status, metodo, corpo, versao) => {
      const r = await pedir(rota, metodo, corpo, versao);
      assert.equal(r.status, status, JSON.stringify(r));
      return r;
    };
    const botao = rotulo => "[...document.querySelectorAll('main button')].find(b => !b.disabled && "
      + "(b.textContent.trim() === " + JSON.stringify(rotulo) + " || b.getAttribute('aria-label') === " + JSON.stringify(rotulo) + "))";
    const clicar = async rotulo => {
      await waitFor(() => a.ev('!!(' + botao(rotulo) + ')'), rotulo);
      await a.ev('(' + botao(rotulo) + ').click()');
    };
    const texto = mensagem => waitFor(() => a.ev("document.querySelector('main')?.innerText.includes("
      + JSON.stringify(mensagem) + ')'), mensagem);
    const confirmar = async (rotulo, alvo, aceitar) => {
      const dialogo = { aceitar };
      confirmacoes.set(a.sessionId, dialogo);
      try {
        await waitFor(() => a.ev('!!(' + botao(rotulo) + ')'), rotulo);
        await a.ev('setTimeout(() => (' + botao(rotulo) + ').click(), 0)');
        await waitFor(() => dialogo.processada, 'confirmação de ' + alvo);
        assert.ok(dialogo.mensagem.includes(alvo) && dialogo.mensagem.includes('permanente'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
    };
    const abrir = async nome => {
      await a.ir('/admin/receitas');
      await clicar('Abrir receita ' + nome);
      await texto('Editar receita');
    };
    const historico = () => a.ev("document.querySelector('main details').open = true");
    const insumoUrl = '/api/admin/insumos';
    const material = (await obter(insumoUrl, 201, 'POST',
      { nome: 'Material da receita', tipo: 'MateriaPrima', unidadeBase: 'Grama' })).body.resumo.insumoId;
    const fichaMaterial = insumoUrl + '/' + material;
    const criar = (nome, produtoVarianteId) => ({
      nome, produtoVarianteId, rendimento: 1,
      itens: [{ insumoId: material, quantidade: '1', unidadeBase: 'Grama' }]
    });
    const teste = criar('Receita descartável', dados.exclusaoReceita.extra);
    const inicial = (await obter(base, 201, 'POST', teste)).body;
    const rota = base + '/' + inicial.resumo.id;
    await obter(rota, 200, 'PUT', { ...teste, token: inicial.token, rendimento: 2 });
    await abrir(teste.nome);
    for (const [width, modo] of [[375, 'dark'], [375, 'light'], [1280, 'dark'], [1280, 'light']]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      if (await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") !== modo)
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
      await waitFor(async () => await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") === modo, modo);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
    }
    await a.ev('(' + botao('Excluir receita') + ').focus()');
    await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
    await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
    assert.ok(await a.ev("document.activeElement.matches(':focus-visible')"));
    await confirmar('Excluir receita', teste.nome, false);
    assert.equal((await obter(rota, 200)).body.historico.length, 2);
    await historico();
    await confirmar('Excluir versão 1', 'versão 1', true);
    await texto('Versão excluída.');
    assert.deepEqual((await obter(rota, 200)).body.historico.map(v => v.numero), [2]);
    const variante = '/api/admin/produtos/' + inicial.resumo.produtoId;
    const fichaVariante = variante + '/variantes/' + teste.produtoVarianteId;
    await obter(fichaMaterial, 409, 'DELETE', undefined, (await obter(fichaMaterial, 200)).etag);
    await obter(fichaVariante, 409, 'DELETE', undefined, (await obter(variante, 200)).etag);
    await confirmar('Excluir receita', teste.nome, true);
    await texto('Receita excluída.');
    await a.ir('/admin/receitas');
    await texto('Nenhuma receita encontrada.');
    await obter(rota, 404);
    await obter(fichaMaterial, 200);
    await obter(fichaVariante, 204, 'DELETE', undefined, (await obter(variante, 200)).etag);
    await obter(fichaMaterial, 204, 'DELETE', undefined, (await obter(fichaMaterial, 200)).etag);

    const outro = (await obter(insumoUrl, 201, 'POST',
      { nome: 'Material do histórico', tipo: 'MateriaPrima', unidadeBase: 'Grama' })).body.resumo.insumoId;
    const usada = { ...criar('Receita em produção', dados.exclusaoReceita.variante),
      itens: [{ insumoId: outro, quantidade: '1', unidadeBase: 'Grama' }] };
    const registrada = (await obter(base, 201, 'POST', usada)).body;
    const emUso = base + '/' + registrada.resumo.id;
    await obter(insumoUrl + '/' + outro + '/entradas', 201, 'POST',
      { tipo: 'EstoqueInicial', quantidadeOriginal: 2, unidadeOriginal: 'Grama', custoTotal: 10 });
    const producao = '/api/admin/producao';
    const ordem = (await obter(producao, 201, 'POST', { receitaId: registrada.resumo.id,
      receitaVersaoId: registrada.atual.id, quantidade: 1 })).body;
    await abrir(usada.nome);
    await historico();
    await confirmar('Excluir versão 1', 'versão 1', true);
    await texto('Esta versão foi utilizada em produção');
    await confirmar('Excluir receita', usada.nome, true);
    await texto('Uma versão desta receita foi utilizada em produção');
    await obter(emUso, 200, 'PUT', { ...usada, token: registrada.token, rendimento: 2 });
    await abrir(usada.nome);
    await historico();
    await confirmar('Excluir versão 1', 'versão 1', true);
    await texto('Esta versão foi utilizada em produção');
    const fichaOrdem = producao + '/' + ordem.ordemId;
    const aberta = (await obter(fichaOrdem, 200)).body;
    const conclusao = (await obter(fichaOrdem + '/concluir', 200, 'POST', {
      versao: aberta.resumo.versaoOrdem, quantidadeProduzida: 1, semControleValidade: true,
      consumos: [{ insumoId: outro, unidadeBase: 'Grama', quantidade: '1' }]
    })).body;
    await confirmar('Excluir receita', usada.nome, true);
    await texto('Uma versão desta receita foi utilizada em produção');
    const encerrada = (await obter(fichaOrdem, 200)).body;
    assert.equal(encerrada.resumo.situacao, 'Concluida');
    assert.deepEqual(encerrada.conclusao, conclusao);
  }

  if (dados.exclusaoProduto) {
    await call('Page.enable', {}, a.sessionId);
    const base = '/api/admin/produtos';
    const pedir = (path, method, body, headers = {}) => a.ev(`(async () => {
      const {requestToken} = await fetch('/api/auth/csrf').then(r => r.json());
      const r = await fetch(${JSON.stringify(path)}, { method: ${JSON.stringify(method)},
        headers: {'Content-Type':'application/json', 'X-XSRF-TOKEN':requestToken, ...${JSON.stringify(headers)}},
        body: ${body === undefined ? 'undefined' : JSON.stringify(JSON.stringify(body))} });
      const texto = await r.text();
      return {status:r.status, tag:r.headers.get('ETag'), body:texto &&
        r.headers.get('Content-Type')?.includes('json') ? JSON.parse(texto) : null};
    })()`);
    const criar = async (nome, categoriaId = dados.exclusaoProduto) => {
      const r = await pedir(base, 'POST', { nome, categoriaId, simples: true,
        variantePadrao: { unidadeVenda: 'UN', precoVenda: 19 } });
      assert.equal(r.status, 201, JSON.stringify(r));
      assert.equal(r.body.variantes.filter(v => v.padrao).length, 1);
      return r.body;
    };
    const botao = rotulo => "[...document.querySelectorAll('main button')].find(b => !b.disabled && "
      + "(b.textContent.trim() === " + JSON.stringify(rotulo) + " || b.getAttribute('aria-label') === " + JSON.stringify(rotulo) + "))";
    const editar = async produto => {
      await a.ir('/admin/produtos');
      const expr = botao('Editar ' + produto.nome);
      await waitFor(() => a.ev('!!(' + expr + ')'), produto.nome);
      await a.ev('(' + expr + ').click()');
      await waitFor(() => a.ev('!!(' + botao('Excluir produto') + ')'), 'ação Excluir produto');
    };
    const confirmar = async (produto, aceitar, acao = 'Excluir produto') => {
      const decisao = { aceitar };
      confirmacoes.set(a.sessionId, decisao);
      try {
        await a.ev("setTimeout(() => (" + botao(acao) + ").click(), 0)");
        await waitFor(() => decisao.processada, 'confirmação de exclusão');
        assert.ok(decisao.mensagem.includes(produto.nome) && decisao.mensagem.includes('permanente') &&
          decisao.mensagem.includes('variantes'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
    };
    const texto = (frase, browser = a) =>
      waitFor(() => browser.ev("document.querySelector('main')?.innerText.includes(" + JSON.stringify(frase) + ")"), frase);

    for (const [width, modo] of [[375, 'dark'], [375, 'light'], [1280, 'dark'], [1280, 'light']]) {
      const produto = await criar('Produto descartável ' + width + ' ' + modo);
      const rota = base + '/' + produto.id;
      const caracteristicas = await pedir(rota, 'PUT',
        { caracteristicas: [{ nome: 'Aroma', valores: [{ nome: 'Lavanda' }, { nome: 'Alecrim' }] }] });
      assert.equal(caracteristicas.status, 200, JSON.stringify(caracteristicas));
      const aroma = caracteristicas.body.caracteristicas[0];
      const variante = await pedir(rota + '/variantes', 'POST', { identificacao: 'Alecrim', unidadeVenda: 'UN', precoVenda: 22,
        caracteristicas: [{ caracteristicaId: aroma.id, valorId: aroma.valores.find(v => v.nome === 'Alecrim').id }] });
      assert.equal(variante.status, 200, JSON.stringify(variante));
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      await editar(produto);
      if (await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") !== modo)
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
      assert.equal(await a.ev("document.querySelector('.admin-shell').dataset.adminTheme"), modo);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      assert.ok(await a.ev("(" + botao('Excluir produto') + ").closest('app-admin-produto-form section') !== null"));
      await a.ev("(" + botao('Cancelar') + ").focus()");
      await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
      await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
      assert.ok(await a.ev("document.activeElement.matches(':focus-visible')"));
      await confirmar(produto, false);
      assert.equal((await pedir(rota, 'GET')).body.variantes.length, 2);
      await a.ev("(" + botao('Cancelar') + ").click()");
      const excluirNaLista = 'Excluir ' + produto.nome;
      await waitFor(() => a.ev('!!(' + botao(excluirNaLista) + ')'), 'ação de exclusão na lista');
      await confirmar(produto, true, excluirNaLista);
      await texto('Produto excluído.');
      await a.ir('/admin/produtos');
      assert.equal((await pedir(rota, 'GET')).status, 404);
      assert.equal((await pedir('/api/admin/categorias/' + dados.exclusaoProduto, 'GET')).status, 200);
    }

    const foto = (produto, nome = 'foto.png', varianteId = null) => a.ev(`(async () => {
      const {requestToken} = await fetch('/api/auth/csrf').then(r => r.json());
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 96;
      const ctx = canvas.getContext('2d');
      ctx.fillStyle = '#277c51'; ctx.fillRect(0, 0, 96, 96);
      const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
      const form = new FormData(); form.append('arquivo', blob, ${JSON.stringify(nome)});
      if (${JSON.stringify(varianteId)}) form.append('varianteIds', ${JSON.stringify(varianteId)});
      const r = await fetch(${JSON.stringify(base + '/' + produto.id + '/imagens')}, {
        method:'POST', headers:{'X-XSRF-TOKEN':requestToken}, body:form });
      return [r.status, await r.json()];
    })()`);
    const vizinho = await criar('Produto preservado');
    const [vizinhoStatus] = await foto(vizinho);
    assert.equal(vizinhoStatus, 201);
    const [segundaFoto] = await foto(vizinho, 'removivel.png');
    assert.equal(segundaFoto, 201);
    const galeria = '/admin/produtos/' + vizinho.id + '/fotos';
    const rotaImagens = base + '/' + vizinho.id + '/imagens';
    await a.ir(galeria);
    const removerFoto = botao('Remover foto removivel.png');
    await waitFor(() => a.ev('!!(' + removerFoto + ')'), 'ação de remoção individual');
    for (const [width, modo] of [[375, 'dark'], [375, 'light'], [1280, 'dark'], [1280, 'light']]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      if (await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") !== modo)
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
      assert.equal(await a.ev("document.querySelector('.admin-shell').dataset.adminTheme"), modo);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
    }
    for (const aceitar of [false, true]) {
      const decisao = { aceitar };
      confirmacoes.set(a.sessionId, decisao);
      try {
        await a.ev("setTimeout(() => (" + removerFoto + ").click(), 0)");
        await waitFor(() => decisao.processada, 'confirmação da foto');
        assert.ok(decisao.mensagem.includes('removivel.png') && decisao.mensagem.includes('permanentemente'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
      if (!aceitar) assert.equal((await pedir(rotaImagens, 'GET')).body.length, 2);
    }
    await texto('A foto foi removida da galeria.');
    await a.ir(galeria);
    assert.equal((await pedir(rotaImagens, 'GET')).body.length, 1);
    assert.equal((await pedir(base + '/' + vizinho.id + '/publicacao', 'PATCH', { publicado: true })).status, 200);
    const publicado = await criar('Produto publicado descartável');
    const rotaPublicada = base + '/' + publicado.id;
    const opcoesPublicadas = await pedir(rotaPublicada, 'PUT',
      { caracteristicas: [{ nome: 'Aroma', valores: [{ nome: 'Lavanda' }, { nome: 'Alecrim' }] }] });
    assert.equal(opcoesPublicadas.status, 200, JSON.stringify(opcoesPublicadas));
    const aromaPublico = opcoesPublicadas.body.caracteristicas[0];
    const novaOpcao = await pedir(rotaPublicada + '/variantes', 'POST',
      { identificacao: 'Alecrim', unidadeVenda: 'UN', precoVenda: 22,
        caracteristicas: [{ caracteristicaId: aromaPublico.id, valorId: aromaPublico.valores.find(v => v.nome === 'Alecrim').id }] });
    assert.equal(novaOpcao.status, 200, JSON.stringify(novaOpcao));
    const [statusFoto, fotoCriada] = await foto(publicado);
    assert.equal(statusFoto, 201, JSON.stringify(fotoCriada));
    assert.equal((await foto(publicado, 'detalhe.png', novaOpcao.body.variantes.find(v => !v.padrao).id))[0], 201);
    assert.equal((await pedir(rotaPublicada + '/publicacao', 'PATCH', { publicado: true })).status, 200);
    const consulta = '/api/catalogo/produtos/' + publicado.id;
    const noCatalogo = () => c.ev(`fetch('/api/catalogo/produtos').then(r=>r.json()).then(p=>p.itens.some(i=>i.id===${JSON.stringify(publicado.id)}))`);
    assert.equal(await c.ev(`fetch(${JSON.stringify(consulta)}).then(r=>r.status)`), 200);
    assert.ok(await noCatalogo());
    // Sem estoque, a seleção antiga é semeada para testar a revalidação da página real.
    await c.ev(`localStorage.setItem('xaquiamuni:carrinho:v1', ${JSON.stringify(JSON.stringify([{
      produtoId: publicado.id, varianteId: publicado.variantes[0].id, nome: publicado.nome,
      variante: publicado.variantes[0].identificacao, imagem: null, precoCentavos: '1900', quantidade: 1,
    }]))})`);
    await editar(publicado);
    await confirmar(publicado, true);
    await texto('Produto excluído.');
    assert.equal((await pedir(rotaPublicada + '/imagens', 'GET')).status, 404);
    assert.equal(await c.ev(`fetch(${JSON.stringify(consulta)}).then(r=>r.status)`), 404);
    assert.equal(await noCatalogo(), false);
    await c.ir('/carrinho');
    await texto('Opção indisponível.', c);
    assert.equal((await pedir(base + '/' + vizinho.id + '/imagens', 'GET')).body.length, 1);

    const bloqueado = await criar('Produto com histórico');
    const rotaBloqueada = base + '/' + bloqueado.id;
    const adicao = await pedir(rotaBloqueada + '/variantes', 'POST',
      { identificacao: 'Adicional', unidadeVenda: 'UN', precoVenda: 20 });
    assert.equal(adicao.status, 200, JSON.stringify(adicao));
    const extra = adicao.body.variantes.find(v => !v.padrao);
    assert.equal((await foto(bloqueado, 'historico.png'))[0], 201);
    const entrada = await pedir('/api/admin/estoque/produtos-acabados/variantes/' + extra.id + '/entradas', 'POST',
      { tipo: 'EntradaManual', quantidade: 1, condicaoDestino: 'Vendavel', semControleValidade: true, motivo: 'Ensaio' },
      { 'Idempotency-Key': await a.ev('crypto.randomUUID()') });
    assert.equal(entrada.status, 201, JSON.stringify(entrada));
    await editar(bloqueado);
    await confirmar(bloqueado, true);
    await texto('A variante possui registros de estoque');
    assert.equal((await pedir(rotaBloqueada, 'GET')).body.variantes.length, 2);
    assert.equal((await pedir(rotaBloqueada + '/imagens', 'GET')).body.length, 1);

    const antigo = await criar('Produto recadastrado');
    const anterior = base + '/' + antigo.id;
    const chave = await a.ev('crypto.randomUUID()');
    const etag = (await pedir(anterior, 'GET')).tag;
    const headers = { 'Idempotency-Key': chave, 'If-Match': etag };
    assert.equal((await pedir(anterior, 'DELETE', undefined, headers)).status, 204);
    const novo = await criar(antigo.nome);
    assert.notEqual(novo.id, antigo.id);
    assert.notEqual(novo.variantes[0].id, antigo.variantes[0].id);
    assert.equal((await pedir(anterior, 'DELETE', undefined, headers)).status, 204);
    const novaRota = base + '/' + novo.id;
    const novaTag = (await pedir(novaRota, 'GET')).tag;
    assert.equal((await pedir(novaRota, 'DELETE', undefined, { ...headers, 'If-Match': novaTag })).status, 409);
    assert.equal((await pedir(novaRota, 'GET')).status, 200);
    const categorias = '/api/admin/categorias';
    const categoria = await pedir(categorias, 'POST', { nome: 'Categoria da exclusão encadeada', ordemExibicao: 43 });
    assert.equal(categoria.status, 201, JSON.stringify(categoria));
    const ramo = await criar('Produto da exclusão encadeada', categoria.body.id);
    const rotaRamo = base + '/' + ramo.id;
    const rotaCategoria = categorias + '/' + categoria.body.id;
    const chaveNova = () => a.ev('crypto.randomUUID()');
    const apagar = async rota => pedir(rota, 'DELETE', undefined, {
      'Idempotency-Key': await chaveNova(), 'If-Match': (await pedir(rota, 'GET')).tag,
    });
    assert.equal((await apagar(rotaCategoria)).status, 409);
    const varianteRamo = ramo.variantes[0].id;
    const rotaPolitica = '/api/admin/estoque/produtos-acabados/variantes/' + varianteRamo + '/politica';
    assert.equal((await pedir(rotaPolitica, 'PUT', { estoqueMinimo: 2 })).status, 200);
    assert.equal((await apagar(rotaRamo)).status, 409);
    await a.ir('/admin/estoque/produtos-acabados/' + varianteRamo);
    const redefinir = botao('Redefinir política');
    await waitFor(() => a.ev('!!(' + redefinir + ')'), 'redefinição de política');
    await a.ev('(' + redefinir + ').closest("details").open = true');
    for (const aceitar of [false, true]) {
      const dialogo = { aceitar };
      confirmacoes.set(a.sessionId, dialogo);
      try {
        await a.ev('setTimeout(() => (' + redefinir + ').click(), 0)');
        await waitFor(() => dialogo.processada, 'confirmação de redefinição');
        assert.ok(dialogo.mensagem.includes('registros de estoque serão preservados'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
      if (!aceitar) assert.equal((await pedir(rotaPolitica, 'GET')).body.estoqueMinimo, 2);
    }
    await texto('Política de estoque redefinida.');
    assert.equal((await apagar(rotaRamo)).status, 204);
    assert.equal((await apagar(rotaCategoria)).status, 204);
    assert.equal((await pedir(rotaCategoria, 'GET')).status, 404);

    const rotaTema = '/api/admin/loja/tema';
    await call('Network.enable', {}, c.sessionId);
    await call('Network.setCacheDisabled', { cacheDisabled: true }, c.sessionId);
    for (const tema of ['natal', 'black-friday', 'pascoa', 'ano-novo', 'dia-das-maes', 'original']) {
      const estadoTema = await pedir(rotaTema, 'GET');
      assert.equal((await pedir(rotaTema, 'PUT', { chave: tema, revisaoEsperada: estadoTema.body.publicacao.revisao },
        { 'Idempotency-Key': await chaveNova() })).status, 200);
      for (const width of [375, 1280]) {
        await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, c.sessionId);
        await c.ir('/produtos/' + vizinho.id);
        await waitFor(() => c.ev(`document.querySelector('app-store-layout')?.dataset.theme === ${JSON.stringify(tema)}
          && [...document.querySelectorAll('main img')].some(img => img.complete && img.naturalWidth > 0)`), 'foto no tema ' + tema);
        const imagem = await c.ev("document.querySelector('main img').currentSrc");
        assert.ok(await c.ev('document.documentElement.scrollWidth <= innerWidth'));
        await call('Network.setBlockedURLs', { urls: [imagem] }, c.sessionId);
        try {
          await c.ir('/produtos/' + vizinho.id);
          await texto('Imagem indisponível', c);
          assert.ok(await c.ev("document.documentElement.scrollWidth <= innerWidth && ![...document.querySelectorAll('main img')].some(img => img.complete && img.naturalWidth === 0)"));
        } finally {
          await call('Network.setBlockedURLs', { urls: [] }, c.sessionId);
        }
      }
    }
    console.log('Exclusão do produto, histórico, catálogo, imagens, carrinho obsoleto e recadastro verificados no Chrome.');
  }

  if (dados.exclusaoVariante) {
    const { Id: id, Nome: nome, CategoriaId: categoria, imagem, opcao } = dados.exclusaoVariante;
    await call('Page.enable', {}, a.sessionId);
    const base = '/api/admin/produtos';
    const tela = '/admin/produtos';
    const botao = (rotulo, aria = false) => "[...document.querySelectorAll('main button')].find(b => !b.disabled && "
      + (aria ? "b.getAttribute('aria-label')" : 'b.textContent.trim()') + ' === ' + JSON.stringify(rotulo) + ')';
    const clicar = async (rotulo, aria = false) => {
      const expr = botao(rotulo, aria);
      await waitFor(() => a.ev('!!(' + expr + ')'), rotulo);
      await a.ev('(' + expr + ').click()');
    };
    const texto = async (mensagem, etapa = mensagem) => {
      try {
        await waitFor(() => a.ev("document.querySelector('main')?.innerText.includes("
          + JSON.stringify(mensagem) + ')'), etapa);
      } catch {
        const alerta = await a.ev("document.querySelector('main [role=alert]')?.textContent.trim()");
        throw new Error(etapa + (alerta ? ': ' + alerta : ''));
      }
    };
    const ler = path => a.ev(`fetch(${JSON.stringify(path)}).then(r=>r.json())`);
    const criar = (path, body) => a.ev(`(async () => {
      const {requestToken} = await fetch('/api/auth/csrf').then(r=>r.json());
      const r = await fetch(${JSON.stringify(path)}, { method: 'POST',
        headers: {'Content-Type':'application/json','X-XSRF-TOKEN':requestToken,'Idempotency-Key':crypto.randomUUID()},
        body: JSON.stringify(${JSON.stringify(body)}) });
      return [r.status, await r.json()];
    })()`);
    const editar = async (produto, variante) => {
      await a.ir(tela);
      await clicar('Gerenciar opções de ' + produto, true);
      await clicar('Editar opção ' + variante, true);
    };
    const confirmar = async (aceitar, identificacao) => {
      const confirmacao = { aceitar };
      confirmacoes.set(a.sessionId, confirmacao);
      try {
        await a.ev("setTimeout(() => (" + botao('Excluir variante') + ").click(), 0)");
        await waitFor(() => confirmacao.processada, 'confirmação nativa de variante');
        assert.ok(confirmacao.mensagem.includes(identificacao) && confirmacao.mensagem.includes('permanente'));
      } finally {
        confirmacoes.delete(a.sessionId);
      }
    };
    for (const [width, modo] of [[375, 'dark'], [1280, 'light']]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      const produto = 'Produto descartável ' + width + ' ' + modo;
      const [status, criado] = await criar(base, { nome: produto, categoriaId: categoria, simples: true,
        variantePadrao: { unidadeVenda: 'UN', precoVenda: 19 } });
      assert.equal(status, 201, JSON.stringify(criado));
      const [adicionada, resposta] = await criar(base + '/' + criado.id + '/variantes',
        { identificacao: 'Opção de teste', unidadeVenda: 'UN', precoVenda: 19 });
      assert.equal(adicionada, 200, JSON.stringify(resposta));
      await editar(produto, 'Opção de teste');
      if (await a.ev("document.querySelector('.admin-shell').dataset.adminTheme") !== modo)
        await a.ev("document.querySelector('.admin-theme-toggle').click()");
      assert.equal(await a.ev("document.querySelector('.admin-shell').dataset.adminTheme"), modo);
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      await a.ev("(" + botao('Cancelar') + ").focus()");
      await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
      await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
      assert.ok(await a.ev("document.activeElement.matches(':focus-visible')"));
      await confirmar(false, 'Opção de teste');
      assert.equal((await ler(base + '/' + criado.id)).variantes.length, 2);
      await confirmar(true, 'Opção de teste');
      await texto('Variante excluída.', 'Excluir opção criada em ' + width + ' px');
      await a.ir(tela);
      const final = await ler(base + '/' + criado.id);
      assert.equal(final.variantes.length, 1);
      await editar(produto, final.variantes[0].identificacao);
      await confirmar(true, produto);
      await texto('O produto precisa manter pelo menos uma variante.');
    }
    const anterior = await ler(base + '/' + id);
    const extra = anterior.variantes.find(v => v.id === opcao);
    await c.ev(`localStorage.setItem('xaquiamuni:carrinho:v1', ${JSON.stringify(JSON.stringify([
      { produtoId: id, varianteId: opcao, nome, variante: extra.identificacao, imagem: null,
        precoCentavos: '1900', quantidade: 1 },
    ]))})`);
    await editar(nome, extra.identificacao);
    await confirmar(true, extra.identificacao);
    await texto('Variante excluída.', 'Excluir opção com imagem compartilhada');
    const imagens = await ler(base + '/' + id + '/imagens');
    assert.deepEqual(imagens.find(i => i.id === imagem).varianteIds, [anterior.variantes.find(v => v.padrao).id]);
    assert.equal((await ler(base + '/' + id)).publicado, true);
    await c.ir('/carrinho');
    await waitFor(() => c.ev("document.querySelector('main')?.textContent.includes('Opção indisponível')"), 'carrinho revalidado');
    assert.equal(await c.ev("fetch('/api/pedidos').then(r=>r.json()).then(p=>p.totalItens)"), 0);

    const [statusOperacional, operacional] = await criar(base + '/' + id + '/variantes',
      { identificacao: 'Com estoque', unidadeVenda: 'UN', precoVenda: 20 });
    assert.equal(statusOperacional, 200);
    const idOperacional = operacional.variantes.find(v => v.identificacao === 'Com estoque').id;
    const [statusEntrada, respostaEntrada] = await criar('/api/admin/estoque/produtos-acabados/variantes/' + idOperacional + '/entradas',
      { tipo: 'EntradaManual', quantidade: 1, condicaoDestino: 'Vendavel', semControleValidade: true, motivo: 'Ensaio' });
    assert.equal(statusEntrada, 201, JSON.stringify(respostaEntrada));
    await editar(nome, 'Com estoque');
    await confirmar(true, 'Com estoque');
    await texto('A variante possui registros de estoque');
    assert.ok((await ler(base + '/' + id)).variantes.some(v => v.id === idOperacional));
    console.log('Variante adicional, última variante, histórico de estoque, carrinho e imagem compartilhada verificadas no Chrome.');
  }

  if (!dados.exclusaoCategorias && !dados.insumoFornecedor && !dados.exclusaoVariante && !dados.exclusaoProduto && process.env.XAQUIAMUNI_CHROME_PROMOCOES_FRETE === '1') {
    await a.ir('/admin/promocoes-frete');
    const botao = texto => waitFor(() => a.ev(`(() => {
      const alvo = [...document.querySelectorAll('main button')]
        .find(b => b.textContent.trim() === ${JSON.stringify(texto)} && !b.disabled);
      if (!alvo) return false;
      alvo.click();
      return true;
    })()`), texto);
    const preencher = (nome, valor) => a.ev(`(() => {
      const campo = document.querySelector('main [name="' + ${JSON.stringify(nome)} + '"]');
      campo.value = ${JSON.stringify(valor)};
      campo.dispatchEvent(new Event(campo.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    })()`);
    const aparece = texto => waitFor(() => a.ev(
      `document.querySelector('main')?.textContent.includes(${JSON.stringify(texto)})`), texto);
    const largura = width => call('Emulation.setDeviceMetricsOverride', {
      width, height: 900, mobile: false, deviceScaleFactor: 1,
    }, a.sessionId);
    await largura(375);
    await botao('Nova campanha');
    await preencher('nome', 'Campanha de frete descartável');
    await preencher('valor', '12,50');
    await a.ev("document.querySelector('main form input[type=checkbox]').click()");
    await botao('Salvar campanha');
    await aparece('Campanha salva.');
    await aparece('12,50%');
    await botao('Ativar');
    await aparece('Campanha ativada.');
    await botao('Editar');
    await waitFor(() => a.ev("!!document.querySelector('main input[name=nome]')"), 'Formulário de edição');
    await preencher('tipo', 'FreteGratis');
    await botao('Salvar campanha');
    await aparece('Integral');
    await botao('Inativar');
    await aparece('Campanha inativada.');

    for (const width of [375, 1280]) {
      await largura(width);
      for (const tema of ['light', 'dark']) {
        const temaAtual = () => a.ev("document.querySelector('.admin-shell')?.dataset.adminTheme");
        if (await temaAtual() !== tema) {
          await a.ev(`[...document.querySelectorAll('header button')].find(b =>
            b.getAttribute('aria-label')?.includes('Ativar modo')).click()`);
          await waitFor(async () => await temaAtual() === tema, 'Modo ' + tema);
        }
        assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'), 'Lista em ' + tema + ' / ' + width);
        await botao('Editar');
        await waitFor(() => a.ev("!!document.querySelector('main input[name=nome]')"), 'Formulário');
        assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'), 'Formulário em ' + tema + ' / ' + width);
        await a.ev("document.querySelector('main input[name=nome]').focus()");
        await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
        await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
        assert.ok(await a.ev("document.activeElement.matches('main select[name=tipo]:focus-visible')"));
        await botao('Cancelar');
      }
    }
    console.log('Campanha de frete criada, editada, ativada e inativada no Chrome, em 375/1280 px e nos dois modos ADMIN.');
  }

  async function validarConta() {
    const conta = await abrir('/');
    const rodape = '.store-footer__account';
    const linksVisitante = `!!document.querySelector('${rodape} a[href^="/login"]')
      && !!document.querySelector('${rodape} a[href="/cadastro"]')
      && !document.querySelector('${rodape} button')`;
    await waitFor(() => conta.ev(linksVisitante), 'Rodapé de visitante');
    await conta.ir('/minha-conta');
    await waitFor(() => conta.ev("location.pathname === '/login' && new URLSearchParams(location.search).get('destino') === '/minha-conta'"),
      'Rota protegida e destino seguro');
    await conta.ev(`(() => {
      for (const [nome, valor] of Object.entries(${JSON.stringify({email: cliente.Email, senha: cliente.Senha})})) {
        const campo = document.querySelector('[formControlName="' + nome + '"]');
        campo.value = valor;
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      }
      document.querySelector('form').requestSubmit();
    })()`);
    await waitFor(() => conta.ev("location.pathname === '/minha-conta' && !!document.querySelector('.conta-dados')"), 'Retorno após login');
    await waitFor(() => conta.ev(`!!document.querySelector('${rodape} a[href="/minha-conta"]')
      && !!document.querySelector('.store-account__link[href="/minha-conta"]')
      && !document.querySelector('${rodape} a[href="/cadastro"]')`), 'Cabeçalho e rodapé autenticados');
    for (const rota of ['/pedidos', '/cotacoes-correios']) {
      await conta.ev(`document.querySelector('.conta-atalhos a[href="${rota}"]').click()`);
      await waitFor(() => conta.ev(`location.pathname === ${JSON.stringify(rota)}`), 'Atalho ' + rota);
      await conta.ev(`document.querySelector('${rodape} a[href="/minha-conta"]').click()`);
      await waitFor(() => conta.ev("!!document.querySelector('.conta-dados')"), 'Volta pelo rodapé');
    }
    const administradorNaVitrine = await abrir('/admin/login');
    await login(administradorNaVitrine, admin, '/api/admin/auth');
    await administradorNaVitrine.ir('/');
    await waitFor(() => administradorNaVitrine.ev(linksVisitante), 'ADMIN como visitante');
    await administradorNaVitrine.ir('/minha-conta');
    await waitFor(() => administradorNaVitrine.ev("location.pathname === '/login'"), 'ADMIN sem acesso à conta de cliente');

    await call('Page.bringToFront', {}, conta.sessionId);
    const publicarEmail = habilitado => a.ev(`(async () => {
      const rota = '/api/admin/loja/atendimento/contato';
      const { revisao } = await fetch(rota).then(r => r.json());
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      return (await fetch(rota + '/email', { method: 'PUT', headers: {
        'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(),
      }, body: JSON.stringify({ endereco: 'contato@example.test', habilitado: ${habilitado}, revisao }) })).status;
    })()`);
    assert.equal(await publicarEmail(true), 200);
    const contatoVisivel = `!!document.querySelector('#conta-contato a[href="mailto:contato@example.test"]')
      && !!document.querySelector('footer a[href="mailto:contato@example.test"]')`;
    await conta.ev("document.dispatchEvent(new Event('visibilitychange'))");
    await waitFor(() => conta.ev(contatoVisivel), 'Canal comercial na conta e no rodapé');
    assert.equal(await publicarEmail(false), 200);
    await conta.ev("document.dispatchEvent(new Event('visibilitychange'))");
    await waitFor(() => conta.ev(`!(${contatoVisivel}) && !document.querySelector('.conta-atalhos a[href="#conta-contato"]')
      && !!document.querySelector('footer a[href="/produtos"]')`), 'Canal desabilitado e navegação preservada');
    const publicar = chave => a.ev(`(async () => {
      const rota = '/api/admin/loja/tema';
      const { publicacao } = await fetch(rota).then(r => r.json());
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      return (await fetch(rota, { method: 'PUT', headers: {
        'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(),
      }, body: JSON.stringify({ chave: ${JSON.stringify(chave)}, revisaoEsperada: publicacao.revisao }) })).status;
    })()`);
    for (const tema of ['natal', 'black-friday', 'pascoa', 'ano-novo', 'dia-das-maes', 'original']) {
      assert.equal(await publicar(tema), 200);
      for (const width of [375, 1280]) {
        await call('Emulation.setDeviceMetricsOverride', {
          width, height: 900, mobile: false, deviceScaleFactor: 1,
        }, conta.sessionId);
        await conta.ir('/minha-conta');
        const temaAtual = JSON.stringify(tema);
        await waitFor(() => conta.ev(`document.querySelector('app-store-layout')?.dataset.theme === ${temaAtual}
          && !!document.querySelector('.conta-dados')`), 'Tema e perfil em Minha conta');
        assert.ok(await conta.ev(`(() => {
          const painel = document.querySelector('.conta-dados section');
          const estilo = getComputedStyle(painel);
          const luminancia = cor => (cor.match(/[\\d.]+/g) ?? []).slice(0, 3)
            .map(x => Number(x) / 255).map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4)
            .reduce((s, x, i) => s + x * [.2126, .7152, .0722][i], 0);
          const cores = [luminancia(estilo.color), luminancia(estilo.backgroundColor)].sort((a, b) => a - b);
          return document.documentElement.scrollWidth <= innerWidth
            && (cores[1] + .05) / (cores[0] + .05) >= 4.5;
        })()`), 'Contraste ou largura em ' + tema + ' a ' + width + ' px');
        for (const rotulo of ['Editar dados pessoais', 'Editar endereço padrão']) {
          await conta.ev(`document.querySelector('button[aria-label="' + ${JSON.stringify(rotulo)} + '"]').click()`);
          await waitFor(() => conta.ev("!!document.querySelector('.conta form input')"), rotulo);
          assert.ok(await conta.ev('document.documentElement.scrollWidth <= innerWidth'), rotulo + ' em ' + tema + ' a ' + width + ' px');
          await conta.ev("document.querySelector('.conta form .conta-acoes button[type=button]').click()");
          await waitFor(() => conta.ev("!document.querySelector('.conta form')"), 'Cancelar edição');
        }
        await conta.ev("document.querySelector('.conta nav a').focus()");
        await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, conta.sessionId);
        await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, conta.sessionId);
        assert.ok(await conta.ev("document.activeElement.matches('.conta nav a[href=\"/cotacoes-correios\"]:focus-visible')"));
      }
    }
    await conta.ev("document.querySelector('button[aria-label=\"Editar dados pessoais\"]').click()");
    await waitFor(() => conta.ev("!!document.querySelector('#conta-nome')"), 'Formulário dos dados pessoais');
    await conta.ev(`(() => {
      for (const [id, valor] of Object.entries({ 'conta-nome': 'Cliente Editado', 'conta-telefone': '(54) 99900-0000' })) {
        const campo = document.getElementById(id);
        campo.value = valor;
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      }
      document.querySelector('.conta form').requestSubmit();
    })()`);
    await waitFor(() => conta.ev("document.querySelector('.conta [role=status]')?.textContent.includes('Alterações salvas')"), 'Edição salva');
    await conta.ir('/minha-conta');
    await waitFor(() => conta.ev("document.querySelector('.conta-dados')?.textContent.includes('Cliente Editado')"), 'Dados atualizados após recarregar');
    await conta.ev("document.querySelector('[aria-label=\"Editar dados pessoais\"]').click()");
    await waitFor(() => conta.ev("!!document.querySelector('#conta-nome')"), 'Edição iniciada');
    assert.equal(await conta.ev(`(async () => {
      const url = '/api/clientes/me/perfil';
      const perfil = await fetch(url);
      const { telefone } = await perfil.json();
      const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
      return (await fetch(url, { method: 'PUT', headers: {
        'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken,
        'If-Match': perfil.headers.get('X-Perfil-Versao'),
      }, body: JSON.stringify({ nomeCompleto: 'Cliente em outra sessão', telefone }) })).status;
    })()`), 200);
    await conta.ev("document.querySelector('.conta form').requestSubmit()");
    await waitFor(() => conta.ev("document.querySelector('.conta [role=alert]')?.textContent.includes('Os dados mudaram')"), 'Versão desatualizada');
    assert.equal(await conta.ev("document.querySelector('#conta-nome').value"), 'Cliente Editado');
    await conta.ev("[...document.querySelectorAll('.conta button')].find(b => b.textContent.trim() === 'Recarregar dados').click()");
    await waitFor(() => conta.ev("document.querySelector('.conta-dados')?.textContent.includes('Cliente em outra sessão')"), 'Revisão carregada');
    if (dados.modoAdmin) {
      const pedidos = await conta.ev("fetch('/api/pedidos').then(r => r.json())");
      const pedidoId = pedidos.itens[0].id;
      const destino = () => conta.ev(`fetch('/api/pedidos/${pedidoId}').then(r => r.json()).then(p => p.entrega.destino)`);
      const historico = await destino();
      await conta.ev("document.querySelector('[aria-label=\"Editar endereço padrão\"]').click()");
      await waitFor(() => conta.ev("!!document.querySelector('#conta-cep')"), 'Editar endereço');
      await conta.ev("document.querySelector('app-consulta-cep button').click()");
      await waitFor(() => conta.ev("document.querySelector('app-consulta-cep [role=status]')?.textContent.includes('Endereço atualizado')"), 'Consulta de CEP simulada');
      await conta.ev(`(() => {
        const numero = document.querySelector('#conta-numero');
        if (numero.value !== '12') throw Error('Número alterado pela consulta');
        numero.value = '99';
        numero.dispatchEvent(new Event('input', { bubbles: true }));
        document.querySelector('.conta form').requestSubmit();
      })()`);
      await waitFor(() => conta.ev("document.querySelector('.conta [role=status]')?.textContent.includes('Alterações salvas')"), 'Endereço salvo');
      assert.deepEqual(await destino(), historico);
    }
    await conta.ev("document.querySelector('.store-footer__account button').click()");
    await waitFor(() => conta.ev("location.pathname === '/' && !document.querySelector('.conta-dados')"), 'Saída de Minha conta');
    await conta.ir('/minha-conta');
    await waitFor(() => conta.ev("location.pathname === '/login' && new URLSearchParams(location.search).get('destino') === '/minha-conta'"),
      'Proteção de Minha conta após sair');
    if (dados.outroCliente) {
      await login(conta, dados.outroCliente, '/api/auth');
      await conta.ir('/minha-conta');
      await waitFor(() => conta.ev("!!document.querySelector('.conta-dados')"), 'Outra conta');
      assert.ok(await conta.ev("!document.querySelector('.conta-dados').textContent.includes('Cliente em outra sessão')"));
    }
    console.log('Minha conta conferida nos seis temas em 375 e 1280 px.');
  }

  if (process.env.XAQUIAMUNI_CHROME_LOGIN === '1') {
    const origem = '/produtos?busca=lavanda#catalogo';
    for (const width of [375, 1280]) {
      const navegador = await abrir(origem, "app-root .store-account__link");
      await call('Emulation.setDeviceMetricsOverride', {
        width, height: 900, mobile: false, deviceScaleFactor: 1,
      }, navegador.sessionId);
      const seletor = width === 375 ? '.store-mobile-nav a' : '.store-account__link';
      if (width === 375) await navegador.ev("document.querySelector('details.store-mobile-nav').open = true");
      await waitFor(() => navegador.ev(`(() => {
        const link = [...document.querySelectorAll(${JSON.stringify(seletor)})]
          .find(a => a.textContent.trim() === 'Entrar' && a.getClientRects().length);
        if (!link) return false;
        link.click();
        return true;
      })()`), 'Entrar pelo cabeçalho em ' + width);
      await waitFor(() => navegador.ev("location.pathname === '/login' && !!document.querySelector('#senha')"), 'Login público');
      assert.equal(await navegador.ev("new URLSearchParams(location.search).get('destino')"), origem);
      assert.ok(await navegador.ev('document.documentElement.scrollWidth <= innerWidth'));
      await navegador.ev(`(() => {
        for (const [id, valor] of Object.entries(${JSON.stringify({ email: cliente.Email, senha: cliente.Senha })})) {
          const input = document.getElementById(id);
          input.value = valor;
          input.dispatchEvent(new Event('input', { bubbles: true }));
        }
        document.querySelector('main form').requestSubmit();
      })()`);
      const voltou = () => navegador.ev(`location.pathname + location.search + location.hash === ${JSON.stringify(origem)}`);
      await waitFor(voltou, 'Retorno após autenticação');
      await navegador.ir('/login?destino=' + encodeURIComponent(origem));
      await waitFor(voltou, 'Retorno com sessão ativa');
      await navegador.ir('/login?destino=' + encodeURIComponent('//externo.example/'));
      await waitFor(() => navegador.ev("location.pathname === '/'"), 'Destino inválido encaminhado à loja');
      await navegador.ir('/login');
      await waitFor(() => navegador.ev("!!document.querySelector('main nav[aria-label=\"Atalhos da sessão\"]')"), 'Atalhos da sessão ativa');
      console.log('Retorno de login verificado no Chrome em ' + width + ' px.');
    }
  }

  if (dados.promocoes)
    await validarPromocoes({ dados, cliente: c, admin: a, call, waitFor, checkpoint, abrir, login });

  if (dados.agenda) {
    assert.equal(await c.ev('fetch("/api/admin/agenda").then(r => r.status)'), 403);
    const botaoAgenda = rotulo => `[...document.querySelectorAll('main button')]
      .find(b => b.textContent.trim() === ${JSON.stringify(rotulo)} && !b.matches(':disabled') && b.getClientRects().length)`;
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
      await waitFor(() => a.ev("!!document.querySelector('#agenda-dia ~ .itens')"), 'Consulta inicial da Agenda');
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
    const preencherConta = campos => a.ev(`(() => {
      for (const [nome, valor] of Object.entries(${JSON.stringify(campos)})) {
        const campo = document.querySelector('main [name="' + nome + '"]');
        campo.value = valor;
        campo.dispatchEvent(new Event(campo.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
      }
    })()`);
    const clicarFinanceiro = async rotulo => {
      await waitFor(() => a.ev(`!!(${botaoFinanceiro(rotulo)})`), 'Botão indisponível: ' + rotulo);
      await a.ev(`(${botaoFinanceiro(rotulo)}).click()`);
    };
    const base = '/api/admin/financeiro';
    const ler = rota => a.ev(`fetch(${JSON.stringify(base + rota)}).then(async r => {
      if (!r.ok) throw Error('Consulta financeira: ' + r.status);
      return r.json();
    })`);
    const escrever = (rota, corpo, chave) => a.ev(`(async () => {
      const {requestToken} = await fetch('/api/auth/csrf').then(r => r.json());
      const r = await fetch(${JSON.stringify(base + rota)}, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken,
          'Idempotency-Key': ${chave ? JSON.stringify(chave) : 'crypto.randomUUID()'} },
        body: ${JSON.stringify(JSON.stringify(corpo))},
      });
      return { status: r.status, corpo: await r.json() };
    })()`);
    const localizar = async descricao => {
      const registro = (await ler('/lancamentos')).find(item => item.descricao === descricao);
      assert.ok(registro, descricao);
      return registro;
    };
    const movimentos = async id => (await ler('/movimentacoes')).itens.filter(item => item.recursoId === id);
    const criarPendencia = async (tipo, descricao, vencimento) => {
      await a.ir('/admin/financeiro');
      await clicarFinanceiro('Lançamentos');
      await clicarFinanceiro('Novo lançamento');
      await preencherConta({ tipo, inicial: 'pendente', categoria: 'Ensaio', descricao,
        valor: '19,90', contraparte: 'Pessoa E2E' });
      await waitFor(() => a.ev(`!!document.querySelector('main input[name="vencimento"]')`), 'Vencimento ausente');
      await preencherConta({ vencimento });
      assert.ok(await a.ev(`!document.querySelector('main input[name="data"]') && document.documentElement.scrollWidth <= innerWidth`));
      await clicarFinanceiro('Confirmar');
      await waitFor(() => a.ev(`document.querySelector('#lancamento-titulo')?.textContent.trim() === ${JSON.stringify(descricao)}`),
        'Criação pendente não abriu o detalhe');
      const registro = await localizar(descricao);
      assert.equal(registro.categoria, 'Ensaio');
      assert.equal(registro.contraparte, 'Pessoa E2E');
      assert.equal(registro.situacao, 'Pendente');
      return registro;
    };
    for (const width of [375, 1280]) {
      const tipo = width === 375 ? 'Saida' : 'Entrada';
      const direcao = tipo === 'Saida' ? 'APagar' : 'AReceber';
      const descricao = 'Conta E2E ' + width;
      const acaoQuitar = tipo === 'Saida' ? 'Marcar como pago' : 'Marcar como recebido';
      const detalhe = () => a.ev(`document.querySelector('#lancamento-titulo')?.textContent.trim() === ${JSON.stringify(descricao)}`);
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      const anterior = await ler('/resumo');
      const registro = await criarPendencia(tipo, descricao, '2020-01-02');
      assert.deepEqual(await movimentos(registro.id), []);
      const aberto = await ler('/resumo');
      assert.deepEqual(aberto.fluxoPeriodo, anterior.fluxoPeriodo);
      const grupo = direcao === 'APagar' ? 'aPagar' : 'aReceber';
      assert.equal(BigInt(aberto.posicaoAtual.contasEmAberto[grupo].valorCentavos)
        - BigInt(anterior.posicaoAtual.contasEmAberto[grupo].valorCentavos), 1990n);
      assert.equal(aberto.posicaoAtual.contasEmAberto[grupo].quantidade
        - anterior.posicaoAtual.contasEmAberto[grupo].quantidade, 1);
      await a.ir('/admin');
      const atalho = `[...document.querySelectorAll('main a')].find(a => a.href.includes('direcao=${direcao}') && a.href.includes('classificacao=Vencidas'))`;
      await waitFor(() => a.ev(`!!(${atalho})`), 'Alerta de conta vencida ausente no Dashboard');
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      await a.ev(`(${atalho}).focus()`);
      assert.ok(await a.ev(`(() => {
        const link = (${atalho});
        const estilo = getComputedStyle(link);
        return document.activeElement === link && link.matches(':focus-visible')
          && estilo.outlineStyle !== 'none' && parseFloat(estilo.outlineWidth) > 0;
      })()`));
      await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 }, a.sessionId);
      await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 }, a.sessionId);
      const conta = `[...document.querySelectorAll('app-admin-contas-em-aberto article')].find(a => a.textContent.includes(${JSON.stringify(descricao)}))`;
      await waitFor(() => a.ev(`(${conta})?.textContent.includes('R$ 19,90')`), 'Lista filtrada não encontrou a conta');
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
      await a.ev(`(${conta}).querySelector('button').click()`);
      await waitFor(detalhe, 'Origem manual não abriu');
      await clicarFinanceiro(acaoQuitar);
      await preencherConta({ data: '2020-01-03', meio: 'Dinheiro' });
      assert.ok(await a.ev(`document.querySelector('#formulario-financeiro')?.textContent.includes('quitação integral')
        && document.documentElement.scrollWidth <= innerWidth`));
      await a.ev(`document.querySelector('main input[name="confirmacao"]').click()`);
      const rota = '/lancamentos/' + registro.id + '/quitar';
      const perda = { rota: base + rota };
      const antesDosPosts = postsFinanceiros;
      if (tipo === 'Entrada') quitacoesSemResposta.set(a.sessionId, perda);
      await clicarFinanceiro('Confirmar');
      if (tipo === 'Entrada') {
        await waitFor(() => a.ev(`document.querySelector('main [role="alert"]')?.textContent.includes('conexão interrompida')`),
          'A quitação sem resposta não foi reconhecida como tentativa incerta');
        assert.equal(perda.erro, undefined);
        assert.equal(perda.status, 200);
        const guardada = await a.ev(`JSON.parse(sessionStorage.getItem('xaquiamuni:financeiro:v1:' + ${adminId}))`);
        assert.equal(guardada.chave, perda.chave);
        assert.deepEqual(guardada.corpo, perda.corpo);
        const persistida = await ler('/lancamentos/' + registro.id);
        assert.equal(persistida.situacao, 'Realizado');
        const rotaTentativa = '/tentativas/' + perda.chave;
        await a.ir('/admin/financeiro');
        await waitFor(() => a.ev(`document.querySelector('main')?.textContent.includes('Operação recuperada')`),
          'O resultado persistido da quitação não foi recuperado na recarga');
        assert.equal(postsFinanceiros, antesDosPosts + 1);
        assert.equal(respostas.get(base + rotaTentativa), 200);
        const tentativa = await ler(rotaTentativa);
        assert.equal(tentativa.operacao, 'quitar');
        assert.deepEqual(tentativa.resultado, persistida);
        const replay = await escrever(rota, perda.corpo, perda.chave);
        assert.equal(replay.status, 200);
        assert.deepEqual(replay.corpo, tentativa.resultado);
        assert.equal((await escrever(rota, { ...perda.corpo, meio: 'Pix' }, perda.chave)).status, 409);
        assert.equal((await escrever(rota, perda.corpo)).status, 409);
        console.log('D: resposta HTTP 200 interrompida após commit, recuperação sem escrita, replay idêntico e conflitos 409.');
      }
      await waitFor(() => a.ev(`document.querySelector('main')?.textContent.includes('Quitação registrada em')`), 'Quitação não refletida no detalhe');
      await a.ir('/admin/financeiro?secao=contas&direcao=' + direcao + '&classificacao=Vencidas');
      await waitFor(() => a.ev(`document.querySelector('app-admin-contas-em-aberto')?.textContent.includes('Nenhuma conta em aberto')`),
        'Conta quitada continuou na lista');
      const eventos = await movimentos(registro.id);
      assert.deepEqual(eventos.map(({ tipo, data, valorCentavos, origem }) => ({ tipo, data, valorCentavos, origem })),
        [{ tipo, data: '2020-01-03', valorCentavos: '1990', origem: 'LancamentoManual' }]);
      const quitado = await ler('/resumo');
      assert.deepEqual(quitado.posicaoAtual.contasEmAberto, anterior.posicaoAtual.contasEmAberto);
      const caixa = tipo === 'Saida' ? 'saidasPagasCentavos' : 'entradasRecebidasCentavos';
      assert.equal(BigInt(quitado.fluxoPeriodo[caixa]) - BigInt(anterior.fluxoPeriodo[caixa]), 1990n);
      console.log('A/B em ' + width + ' px: pendência de 1990 centavos, caixa intacto até quitação, um evento na data efetiva.');
    }

    for (const tipo of ['Entrada', 'Saida']) {
      const antes = await ler('/resumo');
      const registro = await criarPendencia(tipo, 'Cancelamento E2E ' + tipo, '2020-01-02');
      const vencidas = '/contas-em-aberto?classificacao=Vencidas';
      assert.ok((await ler(vencidas)).itens.some(item => item.id === registro.id));
      await clicarFinanceiro('Cancelar lançamento');
      await preencherConta({ motivo: 'Cancelamento de homologação' });
      await a.ev(`document.querySelector('main input[name="confirmacao"]').click()`);
      await clicarFinanceiro('Confirmar');
      await waitFor(() => a.ev(`document.querySelector('main article[aria-labelledby="lancamento-titulo"]')?.textContent.includes('Cancelamento de homologação')`),
        'Auditoria do cancelamento ausente');
      const cancelado = await ler('/lancamentos/' + registro.id);
      assert.equal(cancelado.situacao, 'Cancelado');
      assert.equal(cancelado.canceladoPorId, admin.Id);
      assert.equal(cancelado.motivoCancelamento, 'Cancelamento de homologação');
      assert.ok(await a.ev(`!(${botaoFinanceiro('Marcar como pago')}) && !(${botaoFinanceiro('Marcar como recebido')})`));
      assert.ok(!(await ler(vencidas)).itens.some(item => item.id === registro.id));
      assert.deepEqual(await movimentos(registro.id), []);
      assert.deepEqual(await ler('/resumo'), antes);
      assert.equal((await escrever('/lancamentos/' + registro.id + '/quitar', { data: '2020-01-03', meio: 'Dinheiro' })).status, 409);
      console.log('C: ' + tipo + ' cancelada com auditoria, fora dos alertas e sem caixa.');
    }

    const contasCompras = (await ler('/contas-em-aberto?origem=Compra')).itens;
    for (const id of comprasTeste) {
      const compraEmAberto = contasCompras.find(item => item.id === id);
      assert.equal(compraEmAberto?.vencimento, null);
      assert.equal(compraEmAberto.situacao, 'EmAberto');
      assert.deepEqual(await movimentos(id), []);
    }
    assert.ok(!(await ler('/contas-em-aberto?origem=VendaExterna')).itens.some(item => item.id === vendaId));
    assert.deepEqual((await movimentos(vendaId)).map(evento => [evento.origem, evento.tipo, evento.valorCentavos]),
      [['VendaExterna', 'Entrada', '500']]);
    console.log('E/F: compra sem vencimento e sem caixa, venda externa quitada com um único recebimento de 500 centavos.');
    const posicaoAntes = (await ler('/resumo')).posicaoAtual.contasEmAberto;
    const deslocar = dias => {
      const data = new Date(posicaoAntes.dataOperacional + 'T00:00:00Z');
      data.setUTCDate(data.getUTCDate() + dias);
      return data.toISOString().slice(0, 10);
    };
    const classificacoes = [
      [-1, 'Vencida', 'Vencidas'], [0, 'VenceHoje', 'VenceHoje'],
      [3, 'VencendoEmBreve', 'ProximosTresDias'], [4, 'EmAberto', null],
    ];
    for (const [dias, situacao, filtro] of classificacoes) {
      const { status, corpo } = await escrever('/lancamentos/pendentes', {
        tipo: 'Saida', categoria: 'Ensaio', descricao: 'Limite E2E ' + dias,
        valorCentavos: '101', vencimento: deslocar(dias),
      });
      assert.equal(status, 201);
      const rota = filtro ? '/contas-em-aberto?classificacao=' + filtro : '/contas-em-aberto';
      assert.equal((await ler(rota)).itens.find(item => item.id === corpo.id)?.situacao, situacao);
      if (!filtro) {
        assert.ok(!(await ler('/contas-em-aberto?classificacao=VencendoEmBreve')).itens.some(item => item.id === corpo.id));
      }
    }
    const posicao = (await ler('/resumo')).posicaoAtual.contasEmAberto;
    assert.equal(posicao.aPagar.quantidade - posicaoAntes.aPagar.quantidade, 4);
    assert.equal(BigInt(posicao.aPagar.valorCentavos) - BigInt(posicaoAntes.aPagar.valorCentavos), 404n);
    for (const grupo of ['vencidas', 'venceHoje', 'proximosTresDias']) {
      assert.equal(posicao.aPagar[grupo].quantidade - posicaoAntes.aPagar[grupo].quantidade, 1);
      assert.equal(BigInt(posicao.aPagar[grupo].valorCentavos) - BigInt(posicaoAntes.aPagar[grupo].valorCentavos), 101n);
    }
    const lerCartoes = () => a.ev(`[...document.querySelectorAll('.grade-contas section')].map(cartao => ({
      total: cartao.querySelector('p').textContent.trim(),
      alertas: [...cartao.querySelectorAll('.alertas-contas a')].map(a => [a.search, a.textContent.trim()]),
    }))`);
    for (const width of [375, 1280]) {
      await call('Emulation.setDeviceMetricsOverride', { width, height: 900, mobile: false, deviceScaleFactor: 1 }, a.sessionId);
      await a.ir('/admin/financeiro');
      await waitFor(async () => (await lerCartoes()).length === 2, 'Indicadores do Financeiro ausentes');
      const financeiro = await lerCartoes();
      await a.ir('/admin');
      await waitFor(async () => (await lerCartoes()).length === 2, 'Indicadores do Dashboard ausentes');
      const dashboard = await lerCartoes();
      assert.equal(dashboard[0].alertas.length, 3);
      for (const [i, cartao] of dashboard.entries()) {
        assert.equal(cartao.total, financeiro[i].total);
        for (const alerta of cartao.alertas) assert.deepEqual(alerta, financeiro[i].alertas.find(a => a[0] === alerta[0]));
      }
      assert.ok(await a.ev('document.documentElement.scrollWidth <= innerWidth'));
    }
    console.log('E: ontem, hoje, +3 e +4 pela data operacional da API; cartões conciliados entre Financeiro e Dashboard.');

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
      r.ok && (await r.json()).pix.instrucoesAdicionais.includes('Não faça transferências.'))`), true);
    const novo = await abrir('/cadastro');
    const registrado = await novo.ev(`(() => {
      const senha = 'E2e!' + crypto.randomUUID() + 'a1';
      const campos = {
        nomeCompleto: 'Cliente sintético', cpf: '52998224725', telefone: '11912345678',
        email: 'demo-' + crypto.randomUUID() + '@example.test', senha, confirmacaoSenha: senha,
        cep: ${JSON.stringify(dados.pixCnpj ? '95000000' : '95200000')}, uf: 'RS', cidade: 'Caxias do Sul', bairro: 'Bairro de teste',
        logradouro: 'Rua de teste', numero: '100',
      };
      for (const [nome, valor] of Object.entries(campos)) {
        const campo = document.querySelector('[formControlName="' + nome + '"]');
        if (!campo) return false;
        campo.value = valor;
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      }
      return true;
    })()`);
    assert.equal(registrado, true);
    if (!dados.pixCnpj) {
      await novo.ev("document.querySelector('app-consulta-cep button').click()");
      await waitFor(() => novo.ev("document.querySelector('#cidade').value === 'Vacaria' && document.querySelector('#numero').value === '100'"),
        'Preenchimento assistido do cadastro');
    }
    await novo.ev("document.querySelector('form button[type=submit]').click()");
    await waitFor(() => novo.ev("document.querySelector('main')?.textContent.includes('Cadastro realizado com sucesso')"),
      'Cadastro Angular da demonstração');
    const link = new URL(await checkpoint('email'));
    assert.equal(link.origin, origin);
    await novo.ir(link.pathname + link.search + link.hash);
    await waitFor(() => novo.ev("document.querySelector('main')?.textContent.includes('E-mail confirmado')"),
      'Confirmação pelo e-mail capturado localmente');
    if (!dados.pixCnpj) {
      await c.ir('/minha-conta');
      await waitFor(() => c.ev("!!document.querySelector('button[aria-label=\"Editar endereço padrão\"]')"), 'Minha conta');
      await c.ev("document.querySelector('button[aria-label=\"Editar endereço padrão\"]').click()");
      await waitFor(() => c.ev("!!document.querySelector('#conta-cep')"), 'Edição do endereço');
      const preencherCep = cep => c.ev(`(() => {
        const campo = document.querySelector('#conta-cep');
        campo.value = ${JSON.stringify(cep)};
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      })()`);
      for (const [cep, mensagem] of [['99999999', 'CEP não encontrado'], ['99999998', 'temporariamente indisponível']]) {
        await preencherCep(cep);
        await c.ev("document.querySelector('app-consulta-cep button').click()");
        await waitFor(() => c.ev(`document.querySelector('app-consulta-cep [role=alert]')?.textContent.includes(${JSON.stringify(mensagem)})`), mensagem);
      }
      await preencherCep('88500000');
      await c.ev("document.querySelector('app-consulta-cep button').click()");
      await waitFor(() => c.ev("document.querySelector('#conta-cidade').value === 'Lages' && document.querySelector('#conta-uf').value === 'SC'"), 'CEP geral de Lages');
      assert.ok(await c.ev("document.querySelector('#conta-numero').value === '100'"));
      await c.ev("document.querySelector('.conta form').requestSubmit()");
      await waitFor(() => c.ev("!document.querySelector('.conta form') && document.querySelector('.conta-dados').textContent.includes('Lages')"), 'Endereço salvo');
      await c.ir('/minha-conta');
      await waitFor(() => c.ev("document.querySelector('.conta-dados')?.textContent.includes('Lages')"), 'Endereço persistido após recarga');
      const resultado = await c.ev(`(async () => {
        const modalidades = await fetch('/api/loja/modalidades-entrega').then(r => r.json());
        const { endereco } = await fetch('/api/clientes/me/endereco-padrao').then(r => r.json());
        const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
        const post = (rota, corpo) => fetch(rota, { method: 'POST', headers: {
          'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(),
        }, body: JSON.stringify(corpo) });
        const { produto, variante } = ${JSON.stringify(dados.produtoDemo)};
        const itens = [{ produtoId: produto, produtoVarianteId: variante, quantidade: 1 }];
        const entrega = await post('/api/checkout/cotacoes-entrega', { itens, endereco: {
          cep: '95200-000', uf: 'RS', cidade: 'Vacaria', bairro: 'Teste', logradouro: 'Rua fictícia', numero: '100',
        } });
        const ofertas = (await entrega.json()).ofertas?.map(o => [o.modalidade, o.freteCentavos]);
        const outra = await post('/api/checkout/cotacoes-entrega', { itens, usarEnderecoPadrao: true });
        const ofertasOutra = (await outra.json()).ofertas?.map(o => o.modalidade);
        const { cep, uf, cidade, bairro, logradouro, numero, complemento } = endereco;
        const postal = await post('/api/cotacoes-correios', { itens, endereco: { cep, uf, cidade, bairro, logradouro, numero, complemento } });
        const invalida = await post('/api/cotacoes-correios', { itens, endereco: { cep, uf, cidade: 'Outro município', bairro, logradouro, numero } });
        const recuperacao = await post('/api/auth/forgot-password', { email: ${JSON.stringify(cliente.Email)} });
        const indisponivel = await post('/api/checkout/cotacoes-entrega', { itens, endereco: { cep: '99999998', uf, cidade, bairro, logradouro, numero } });
        const falha = await indisponivel.json();
        return { modalidades, cep, uf, cidade, ofertas, ofertasOutra, falha,
          postal: postal.status, invalida: invalida.status, recuperacao: recuperacao.status };
      })()`);
      assert.deepEqual([resultado.cep, resultado.uf, resultado.cidade], ['88500000', 'SC', 'Lages']);
      assert.ok(resultado.modalidades.retiradaLocal && resultado.modalidades.entregaPropria && resultado.modalidades.correiosCotacaoManual);
      assert.deepEqual(resultado.ofertas, [['RetiradaLocal', '0'], ['EntregaPropria', '1250']]);
      assert.deepEqual(resultado.ofertasOutra, ['RetiradaLocal']);
      assert.equal(resultado.falha.motivoEntregaPropriaIndisponivel, 'cep_indisponivel');
      assert.deepEqual(resultado.falha.ofertas.map(o => o.modalidade), ['RetiradaLocal']);
      assert.deepEqual([resultado.postal, resultado.invalida, resultado.recuperacao], [201, 400, 202]);
      const recuperacao = new URL(await checkpoint('recuperacao'));
      assert.equal(recuperacao.origin, origin);
      assert.equal(recuperacao.pathname, '/reset-password');
      assert.ok(new URLSearchParams(recuperacao.hash.slice(1)).get('token'));
      console.log('Demonstração: três modalidades, CEP determinístico e recuperação de senha local conferidos.');
    }
    console.log('Demonstração: catálogo com imagens, painel, PIX sintético e confirmação de e-mail local conferidos.');
  }

  if (dados.postal) await validarCorreios({ a, c, call, waitFor, dados, abrir, login, cliente });

  if (dados.modoAdmin)
    await validarModoAdmin({ a, c, call, waitFor, login, dados, admin, cliente });

  if (process.env.XAQUIAMUNI_CHROME_CONTA === '1') await validarConta();

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
