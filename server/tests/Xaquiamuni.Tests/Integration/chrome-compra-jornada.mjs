import assert from 'node:assert/strict';

export async function validarJornada({ abrir, waitFor, checkpoint, call, cliente, admin, outroCliente, seed }) {
  const c = await abrir('/cadastro');
  const a = await abrir('/admin/login');
  let etapa = 'cadastro';
  const pronto = (b, expr, nome) => waitFor(() => b.ev(expr), nome);
  const tem = (b, texto) => pronto(b,
    'document.querySelector("main")?.textContent.includes(' + JSON.stringify(texto) + ')', texto);
  async function clicar(b, texto) {
    await pronto(b, `(() => {
      const alvo = [...document.querySelectorAll('main button, main a')].find(
        e => e.textContent.trim() === ${JSON.stringify(texto)} && !e.disabled);
      if (!alvo) return false;
      alvo.click();
      return true;
    })()`, texto);
  }
  async function preencher(b, campos) {
    await pronto(b, '!!document.querySelector("[formControlName]")', 'Formulário');
    assert.equal(await b.ev(`(() => {
      for (const [nome, valor] of Object.entries(${JSON.stringify(campos)})) {
        const campo = document.querySelector('[formControlName="' + nome + '"]');
        if (!campo) return false;
        campo.value = valor;
        campo.dispatchEvent(new Event('input', { bubbles: true }));
      }
      return true;
    })()`), true);
  }
  async function enviarFormulario(b) {
    await pronto(b, `(() => {
      const botao = document.querySelector('form button[type=submit]');
      if (!botao || botao.disabled) return false;
      botao.click();
      return true;
    })()`, 'Enviar formulário');
  }
  async function api(b, url, method = 'GET', body, chave) {
    return b.ev(`(async () => {
      const headers = {};
      const method = ${JSON.stringify(method)};
      const key = ${JSON.stringify(chave ?? null)};
      if (method === 'POST') {
        const csrf = await (await fetch('/api/auth/csrf')).json();
        headers['Content-Type'] = 'application/json';
        headers['X-XSRF-TOKEN'] = csrf.requestToken;
      }
      if (key) headers['Idempotency-Key'] = key;
      const r = await fetch(${JSON.stringify(url)}, { method, headers, body: ${JSON.stringify(body ?? null)} });
      return { status: r.status, body: await r.json().catch(() => null) };
    })()`);
  }
  async function entrar(b, conta, admin = false) {
    await preencher(b, { email: conta.Email, senha: conta.Senha });
    await enviarFormulario(b);
    const endpoint = admin ? '/api/admin/auth/me' : '/api/auth/me';
    await waitFor(async () => (await api(b, endpoint)).status === 200, 'Sessão autenticada');
  }
  async function botaoCaixa(b, iniciar, enviar) {
    await clicar(b, iniciar);
    assert.equal(await b.ev(`(() => {
      const check = document.querySelector('form input[type=checkbox]');
      const submit = document.querySelector('form button[type=submit]');
      return check && !check.checked && submit?.disabled;
    })()`), true);
    await b.ev('document.querySelector("form input[type=checkbox]").click()');
    await clicar(b, enviar);
  }
  const rotaAdmin = id => '/api/admin/pedidos/' + id;
  const posicao = '/api/admin/estoque/produtos-acabados/variantes/' + seed.variante + '/posicao';
  async function estoque(fisico, reservado, disponivel) {
    const r = await api(a, posicao);
    assert.equal(r.status, 200);
    assert.deepEqual([r.body.saldoFisico, r.body.saldoReservado, r.body.saldoDisponivel], [fisico, reservado, disponivel]);
    console.log('Estoque físico/reservado/disponível: ' + [fisico, reservado, disponivel].join('/'));
  }
  async function novoPedido(quantidade = 1) {
    const payload = JSON.stringify({ itens: [{ produtoId: seed.produto, produtoVarianteId: seed.variante, quantidade }] });
    const chave = await c.ev('crypto.randomUUID()');
    const r = await api(c, '/api/pedidos', 'POST', payload, chave);
    assert.equal(r.status, 201);
    return r.body.id;
  }
  async function postAdmin(id, acao, dados) {
    return api(a, rotaAdmin(id) + '/' + acao, 'POST', JSON.stringify(dados));
  }

  try {
    await preencher(c, {
      nomeCompleto: 'Cliente sintético', cpf: '52998224725', telefone: '11912345678',
      email: cliente.Email, senha: cliente.Senha, confirmacaoSenha: cliente.Senha,
      cep: '95000000', uf: 'RS', cidade: 'Caxias do Sul', bairro: 'Bairro de teste',
      logradouro: 'Rua de teste', numero: '100',
    });
    await enviarFormulario(c);
    await tem(c, 'Cadastro realizado com sucesso');
    etapa = 'confirmação de e-mail';
    await c.ir(await checkpoint('email'));
    await tem(c, 'E-mail confirmado');
    await c.ir('/login');
    etapa = 'logins e isolamento';
    await entrar(c, cliente);
    await entrar(a, admin, true);
    assert.equal((await api(c, '/api/admin/pedidos')).status, 403);
    for (const b of [c, a]) {
      const { cookies } = await call('Network.getCookies', {}, b.sessionId);
      assert.equal(cookies.some(cookie => cookie.name === '__Host-Xaquiamuni.Auth' && cookie.secure && cookie.httpOnly), true);
    }

    etapa = 'catálogo e carrinho';
    await estoque(7, 0, 7);
    await c.ir('/produtos');
    await pronto(c, `(() => {
      const link = document.querySelector('a[href="/produtos/${seed.produto}"]');
      if (!link) return false;
      link.click();
      return true;
    })()`, 'Produto publicado');
    await pronto(c, '[...document.images].some(i => i.src.includes("/conteudo") && i.naturalWidth > 0)', 'Imagem do catálogo');
    await clicar(c, 'Adicionar ao carrinho');
    await clicar(c, 'Adicionar ao carrinho');
    await c.ir('/carrinho');
    await tem(c, '27,80');
    assert.equal(await c.ev('document.querySelector("#cart-quantity-" + ' + JSON.stringify(seed.variante) + ')?.value'), '2');
    etapa = 'checkout e repetição de compra';
    await clicar(c, 'Revisar compra');
    await clicar(c, 'Finalizar compra');
    const caminho = await pronto(c, 'location.pathname.startsWith("/pedidos/") && location.pathname', 'Detalhe após checkout');
    const id = caminho.split('/').at(-1);
    await tem(c, 'Aguardando pagamento');
    await tem(c, '27,80');
    const compra = c.compra();
    assert.equal(typeof compra?.chave, 'string');
    assert.equal(typeof compra?.corpo, 'string');
    const replay = await api(c, '/api/pedidos', 'POST', compra.corpo, compra.chave);
    assert.equal(replay.status, 200);
    assert.equal(replay.body.id === id, true);
    await checkpoint('pedido', id);
    await estoque(7, 2, 5);

    etapa = 'painel e pagamento';
    await a.ir('/admin/pedidos');
    await pronto(a, `(() => {
      const link = document.querySelector('main a[href="/admin/pedidos/${id}"]');
      if (!link) return false;
      link.click();
      return true;
    })()`, 'Link do pedido na lista administrativa');
    await tem(a, 'Aguardando pagamento');
    await botaoCaixa(a, 'Confirmar pagamento', 'Registrar confirmação');
    await tem(a, 'Pagamento confirmado.');
    await checkpoint('pagamento', id);
    await estoque(7, 2, 5);
    const pago = await postAdmin(id, 'confirmar-pagamento', { recebimentoConferido: true });
    assert.deepEqual([pago.status, pago.body?.repetida], [200, true]);
    await checkpoint('pagamento-repetido', id);
    etapa = 'preparação e liberação';
    await clicar(a, 'Iniciar preparação');
    await clicar(a, 'Confirmar início');
    await tem(a, 'Em preparação');
    await checkpoint('preparacao', id);
    await estoque(7, 2, 5);
    await clicar(a, 'Liberar para retirada');
    await clicar(a, 'Confirmar liberação');
    await tem(a, 'Pronto para retirada');
    await checkpoint('retirada', id);
    await estoque(7, 2, 5);
    etapa = 'entrega e repetição';
    assert.equal((await postAdmin(id, 'confirmar-entrega', {})).status, 400);
    await botaoCaixa(a, 'Confirmar entrega', 'Registrar entrega');
    await tem(a, 'Entregue');
    await c.ir(caminho);
    await tem(c, 'Entregue');
    await checkpoint('entrega', id);
    await estoque(5, 0, 5);
    const entregue = await postAdmin(id, 'confirmar-entrega', { entregaPresencialConfirmada: true });
    assert.deepEqual([entregue.status, entregue.body?.repetida], [200, true]);
    await checkpoint('entrega-repetida', id);
    await estoque(5, 0, 5);

    etapa = 'isolamento entre clientes';
    const outro = await abrir('/login');
    await entrar(outro, outroCliente);
    assert.equal((await api(outro, '/api/pedidos/' + id)).status, 404);

    etapa = 'lote inelegível em outro pedido';
    const inelegivel = await novoPedido(2);
    for (const [acao, dados] of [
      ['confirmar-pagamento', { recebimentoConferido: true }], ['iniciar-preparacao', {}], ['liberar-retirada', {}],
    ]) assert.equal((await postAdmin(inelegivel, acao, dados)).status, 200);
    await checkpoint('avancar-relogio-lote', inelegivel);
    const recusa = await postAdmin(inelegivel, 'confirmar-entrega', { entregaPresencialConfirmada: true });
    assert.deepEqual([recusa.status, recusa.body?.codigo], [409, 'lote_inelegivel']);
    await checkpoint('lote-inelegivel', inelegivel);
    etapa = 'expiração no instante exato';
    const expirado = await novoPedido();
    await checkpoint('expirar', expirado);
    const consulta = await api(c, '/api/pedidos/' + expirado);
    assert.deepEqual([consulta.status, consulta.body?.situacao], [200, 'Expirado']);
    await estoque(5, 2, 3);
    console.log('Jornada Chrome/Angular/API/SQL e cenários negativos concluídos.');
  } catch (erro) {
    throw new Error('Checkpoint ' + etapa + ': ' + erro.message);
  }
}
