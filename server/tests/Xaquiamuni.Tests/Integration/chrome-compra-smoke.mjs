import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

let input = '';
for await (const chunk of process.stdin) input += chunk;
const { origin, pinSpki, chromePath, profile, cliente, admin } = JSON.parse(input);
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
  const respostas = new Map();
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
      void call(local ? 'Fetch.continueRequest' : 'Fetch.failRequest',
        local ? { requestId } : { requestId, errorReason: 'BlockedByClient' }, data.sessionId)
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
      if (r.exceptionDetails) throw new Error('Falha ao avaliar verificação no Chrome');
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
    return { ev, sessionId };
  }

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
} catch (erro) {
  console.error(erro.message);
  process.exitCode = 1;
} finally {
  if (call && socket?.readyState === WebSocket.OPEN) await call('Browser.close').catch(() => {});
  socket?.close();
  if (await Promise.race([closed.then(() => false), delay(1500, true, { ref: false })])) {
    chrome.kill();
    await Promise.race([closed, delay(3000, undefined, { ref: false })]);
  }
}
