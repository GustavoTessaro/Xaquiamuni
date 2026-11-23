import assert from 'node:assert/strict';

export async function validarCorreios({ a, c, call, waitFor, dados }) {
  const escrever = (rota, metodo, corpo) => a.ev(`(async () => {
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    return (await fetch(${JSON.stringify(rota)}, {
      method: ${JSON.stringify(metodo)}, body: ${JSON.stringify(JSON.stringify(corpo))},
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID() },
    })).status;
  })()`);
  const clicar = async (contexto, rotulo) => {
    const seletor = `[...document.querySelectorAll('main button')].find(b =>
      b.textContent.trim() === ${JSON.stringify(rotulo)} && !b.disabled && b.getClientRects().length)`;
    await waitFor(() => contexto.ev(`!!(${seletor})`), rotulo);
    await contexto.ev(`(${seletor}).click()`);
  };
  const campo = (contexto, seletor, valor) => contexto.ev(`(() => {
    const e = document.querySelector(${JSON.stringify(seletor)});
    if (!e) return false;
    e.value = ${JSON.stringify(valor)};
    e.dispatchEvent(new Event(e.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    return true;
  })()`);
  const texto = (contexto, conteudo) => waitFor(
    () => contexto.ev(`document.querySelector('main')?.textContent.includes(${JSON.stringify(conteudo)})`), conteudo);
  const largura = contexto => contexto.ev('document.documentElement.scrollWidth <= innerWidth');

  assert.equal(await escrever('/api/admin/configuracoes/pix-manual', 'PUT', {
    tipoChave: 'Email', chavePix: 'postal@example.test', nomeRecebedor: 'Loja E2E',
  }), 200);
  assert.equal(await escrever('/api/admin/estoque/produtos-acabados/variantes/' + dados.varianteFinanceiro + '/entradas', 'POST', {
    tipo: 'EntradaManual', quantidade: 3, condicaoDestino: 'Vendavel', semControleValidade: true, motivo: 'Homologação postal',
  }), 201);

  await a.ir('/admin/configuracoes/atendimento');
  await texto(a, 'Cotação manual dos Correios');
  await a.ev("window.confirm = () => true; document.querySelector('[name=correiosManual]').click()");
  await clicar(a, 'Salvar cotação manual');
  await waitFor(() => c.ev("fetch('/api/loja/modalidades-entrega').then(r=>r.json()).then(x=>x.correiosCotacaoManual === true)"),
    'Modalidade publicada');

  for (const width of [375, 1280]) {
    for (const contexto of [a, c]) {
      await call('Emulation.setDeviceMetricsOverride',
        { width, height: 900, mobile: false, deviceScaleFactor: 1 }, contexto.sessionId);
    }
    await c.ir('/produtos/' + dados.produtoFinanceiro);
    if (width === 375) await clicar(c, 'Adicionar ao carrinho');
    await c.ir('/checkout');
    await waitFor(() => c.ev("!!document.querySelector('#modalidade-entrega option[value=Correios]')"), 'Opção Correios');
    assert.ok(await campo(c, '#modalidade-entrega', 'Correios'));
    const valores = ['95200-000', 'RS', 'Vacaria', 'Centro', 'Rua de teste', String(width)];
    for (let i = 0; i < valores.length; i++) {
      assert.ok(await campo(c, '.resumo fieldset label:nth-of-type(' + (i + 1) + ') input', valores[i]));
    }
    await c.ev('window.confirm = () => true');
    const perdaSolicitacao = width === 375 ? c.perderResposta('/api/cotacoes-correios') : null;
    await clicar(c, 'Solicitar cotação');
    if (perdaSolicitacao) {
      await texto(c, 'Resultado incerto');
      assert.equal(perdaSolicitacao.status[0], 201);
      await clicar(c, 'Recuperar solicitação');
    }
    await texto(c, 'Solicitação registrada');
    if (perdaSolicitacao) {
      assert.deepEqual(perdaSolicitacao.envios[0], perdaSolicitacao.envios[1]);
      assert.match(perdaSolicitacao.envios[0].chave, /^[a-f0-9-]{36}$/i);
    }
    assert.ok(await largura(c));
    const rota = await c.ev("document.querySelector('a[href^=\"/cotacoes-correios/\"]').getAttribute('href')");
    assert.equal(await c.ev("fetch('/api/pedidos').then(r=>r.json()).then(x=>x.totalItens)"), width === 375 ? 0 : 1);

    await a.ir('/admin' + rota);
    await texto(a, 'Rua de teste');
    if (width === 1280) {
      await a.ev("document.querySelector('.admin-theme-toggle').click()");
      await waitFor(() => a.ev("document.querySelector('.admin-shell').dataset.adminTheme === 'dark'"), 'ADMIN escuro');
    }
    for (const [nome, valor] of Object.entries({ servico: 'PAC', frete: '12,90', prazo: '5', pesoTotal: '200' })) {
      assert.ok(await campo(a, 'input[name=' + nome + ']', valor));
    }
    await a.ev('window.confirm = () => true');
    await clicar(a, 'Revisar e publicar proposta');
    await texto(a, 'Proposta disponível');
    assert.ok(await largura(a));

    await c.ir(rota);
    await texto(c, 'Proposta disponível');
    await c.ev('window.confirm = () => true');
    await clicar(c, 'Aceitar proposta');
    await clicar(c, 'Continuar para pagamento');
    await texto(c, '24,89');
    const perdaPedido = width === 375 ? c.perderResposta('/api/pedidos') : null;
    await clicar(c, 'Confirmar total e criar pedido');
    if (perdaPedido) {
      await texto(c, 'Repita a tentativa original');
      assert.equal(perdaPedido.status[0], 201);
      await c.ir(rota);
      await clicar(c, 'Recuperar compra');
    }
    await waitFor(() => c.ev("location.pathname.startsWith('/pedidos/')"), 'Conversão postal');
    await texto(c, 'Aguardando pagamento');
    if (perdaPedido) {
      assert.deepEqual(perdaPedido.envios[0], perdaPedido.envios[1]);
      assert.match(perdaPedido.envios[0].chave, /^[a-f0-9-]{36}$/i);
    }
    await texto(c, '24,89');
    const pedido = await c.ev('location.pathname');

    await a.ir('/admin' + pedido);
    const confirmar = async (acao, confirmar, preencher) => {
      await clicar(a, acao);
      if (preencher) await preencher();
      await a.ev("document.querySelector('form input[type=checkbox]').click()");
      await clicar(a, confirmar);
    };
    await confirmar('Confirmar pagamento', 'Registrar confirmação');
    await texto(a, 'Pagamento confirmado');
    await clicar(a, 'Iniciar preparação');
    await clicar(a, 'Confirmar início');
    await texto(a, 'Em preparação');
    const perdaPostagem = width === 375 ? a.perderResposta('/api/admin' + pedido + '/registrar-postagem') : null;
    await confirmar('Registrar postagem', 'Registrar postagem e baixar estoque', async () => {
      assert.ok(await campo(a, 'form input[maxlength="60"]', 'SEDEX'));
    });
    if (perdaPostagem) {
      await texto(a, 'Resultado incerto');
      assert.equal(perdaPostagem.status[0], 200);
      await clicar(a, 'Recuperar operação');
    }
    await texto(a, 'Rastreamento não informado');
    if (perdaPostagem) assert.deepEqual(perdaPostagem.envios[0], perdaPostagem.envios[1]);
    await confirmar('Corrigir rastreamento', 'Salvar código', async () => {
      assert.ok(await campo(a, 'form input[maxlength="32"]', 'AB123456789BR'));
    });
    await texto(a, 'AB123456789BR');
    await confirmar('Confirmar entrega postal', 'Registrar entrega postal');
    await texto(a, 'Entrega confirmada em');
    await c.ir(pedido);
    await texto(c, 'AB123456789BR');
    await texto(c, 'Entrega confirmada em');
    assert.ok(await largura(a));
    assert.ok(await largura(c));
    for (const chave of ['original', 'natal', 'black-friday', 'pascoa', 'ano-novo', 'dia-das-maes']) {
      const revisao = await a.ev("fetch('/api/admin/loja/tema').then(r=>r.json()).then(x=>x.publicacao.revisao)");
      assert.equal(await escrever('/api/admin/loja/tema', 'PUT', { chave, revisaoEsperada: revisao }), 200);
      await c.ir(rota);
      await waitFor(() => c.ev("document.querySelector('app-store-layout')?.dataset.theme === " + JSON.stringify(chave)),
        'Cotação com tema ' + chave);
      await texto(c, 'Convertida em pedido');
      assert.ok(await largura(c));
      await c.ir(pedido);
      await waitFor(() => c.ev("document.querySelector('app-store-layout')?.dataset.theme === " + JSON.stringify(chave)),
        'Pedido com tema ' + chave);
      await texto(c, 'AB123456789BR');
      assert.ok(await largura(c));
    }
    console.log('Correios em ' + width + ' px: 11,99 + 12,90 = 24,89; pedido, postagem, rastreamento e entrega.');
  }
}

