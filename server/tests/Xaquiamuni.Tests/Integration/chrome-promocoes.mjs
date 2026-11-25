import assert from 'node:assert/strict';

export async function validarPromocoes({ dados, cliente, admin, call, waitFor, checkpoint, abrir, login }) {
  const { produto, variante, alternativa } = dados.promocoes;
  const promocoes = '/api/admin/promocoes';
  const detalhe = '/produtos/' + produto;
  const tentativa = 'xaquiamuni:tentativa-compra:v1';
  const literal = JSON.stringify;
  const botao = texto => `[...document.querySelectorAll('main button')].find(b =>
    b.textContent.trim() === ${literal(texto)} && !b.disabled && b.getClientRects().length)`;
  const clicar = (p, texto) => waitFor(() => p.ev(`(() => {
    const alvo = ${botao(texto)}; if (!alvo) return false; alvo.click(); return true;
  })()`), 'Botão: ' + texto);
  const tem = (p, texto) => waitFor(() => p.ev(`document.querySelector('main')?.innerText.includes(${literal(texto)})`), texto);
  const largura = (p, width) => call('Emulation.setDeviceMetricsOverride',
    { width, height: 900, mobile: false, deviceScaleFactor: 1 }, p.sessionId);
  const cabe = async p => assert.ok(await p.ev('document.documentElement.scrollWidth <= innerWidth'));
  async function campo(p, seletor, valor) {
    await waitFor(() => p.ev(`(() => {
      const e = document.querySelector(${literal(seletor)});
      if (!e || (e.tagName === 'SELECT' && ![...e.options].some(o => o.value === ${literal(valor)}))) return false;
      e.value = ${literal(valor)};
      e.dispatchEvent(new Event(e.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
      return true;
    })()`), 'Campo: ' + seletor);
  }
  async function api(p, rota, metodo = 'GET', corpo, chave) {
    return p.ev(`(async () => {
      const headers = {};
      if (${literal(metodo)} !== 'GET') {
        headers['Content-Type'] = 'application/json';
        headers['X-XSRF-TOKEN'] = (await (await fetch('/api/auth/csrf')).json()).requestToken;
        headers['Idempotency-Key'] = ${literal(chave ?? null)} ?? crypto.randomUUID();
      }
      const r = await fetch(${literal(rota)}, { method: ${literal(metodo)}, headers,
        body: ${corpo === undefined ? 'undefined' : literal(literal(corpo))} });
      return { status: r.status, body: await r.json().catch(() => null) };
    })()`);
  }
  const dataLocal = utc => admin.ev(`(() => {
    const d = new Date(${literal(utc)});
    return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0,16);
  })()`);
  const consulta = async p => {
    const r = await api(p, '/api/pedidos');
    assert.equal(r.status, 200);
    return r.body.itens;
  };
  const capturar = p => p.ev(`JSON.parse(localStorage.getItem(${literal(tentativa)}))`);
  const perderResposta = (p, metodo, rota) => p.ev(`(() => {
    const original = window.fetch;
    window.fetch = async (url, opcoes) => {
      const alvo = opcoes?.method === ${literal(metodo)} && new URL(url, location.origin).pathname === ${literal(rota)};
      const resposta = await original.call(window, url, opcoes).catch(erro => {
        if (alvo) window.statusAntesDaPerda = String(erro);
        throw erro;
      });
      if (alvo) {
        window.fetch = original;
        window.statusAntesDaPerda = resposta.status;
        if (resposta.ok) throw new TypeError('Resposta descartada pelo ensaio depois do commit.');
      }
      return resposta;
    };
    return true;
  })()`);
  const abrirCheckout = async p => {
    await p.ir(detalhe);
    await campo(p, '#produto-opcao', variante);
    await clicar(p, 'Adicionar ao carrinho');
    await p.ir('/carrinho');
    await tem(p, '11,90');
    await cabe(p);
    await p.ir('/checkout');
    await tem(p, '11,90');
    await cabe(p);
    await clicar(p, 'Consultar valores de entrega');
    await tem(p, 'Total final');
  };

  await cliente.ir('/admin/promocoes');
  await waitFor(() => cliente.ev('location.pathname === "/admin/login"'), 'Guarda ADMIN');
  assert.equal((await api(cliente, promocoes)).status, 403);
  const alternarAdmin = async modo => {
    const seletor = ".admin-shell";
    await waitFor(() => admin.ev(`!!document.querySelector('${seletor}')`), 'Shell de promoções');
    if (await admin.ev(`document.querySelector('${seletor}').dataset.adminTheme`) !== modo)
      await admin.ev("document.querySelector('.admin-theme-toggle').click()");
    await waitFor(() => admin.ev(`getComputedStyle(document.querySelector('${seletor}')).colorScheme === '${modo}'`), modo);
    await cabe(admin);
  };
  await largura(admin, 375);
  await admin.ir('/admin/promocoes');
  await clicar(admin, 'Nova promoção');
  await campo(admin, '[name="produto"]', produto);
  await campo(admin, '[name="valor"]', '20');
  await tem(admin, '11,12');
  await tem(admin, '16,00');
  await tem(admin, '20% OFF');
  await alternarAdmin('dark');
  await tem(admin, '20% OFF');
  await alternarAdmin('light');
  await cabe(admin);
  await clicar(admin, 'Salvar como inativa');
  await clicar(admin, 'Ativar após revisão');
  await tem(admin, 'Situação: Ativa');
  const geral = (await api(admin, promocoes)).body.find(p => p.escopo === 'TodasVariantes');
  assert.equal(geral.ativa, true);
  await alternarAdmin('dark');
  await tem(admin, 'Situação: Ativa');
  await alternarAdmin('light');

  await largura(admin, 1280);
  await clicar(admin, 'Nova promoção');
  await campo(admin, '[name="produto"]', produto);
  await campo(admin, '[name="escopo"]', 'VariantesSelecionadas');
  await campo(admin, '[name="tipo"]', 'PrecoFixo');
  await campo(admin, '[name="valor"]', '11,90');
  await campo(admin, '[name="inicio"]', await dataLocal(dados.inicio));
  await campo(admin, '[name="fim"]', await dataLocal(dados.fim));
  await waitFor(() => admin.ev(`(() => {
    const e = [...document.querySelectorAll('form input[type=checkbox]')].find(x => x.parentElement.textContent.includes('Lavanda'));
    if (!e) return false; e.click(); return true;
  })()`), 'Variante Lavanda');
  await tem(admin, '14,38% OFF');
  await tem(admin, 'desconto menor');
  await alternarAdmin('dark');
  await tem(admin, '14,38% OFF');
  await alternarAdmin('light');
  await cabe(admin);
  await clicar(admin, 'Salvar como inativa');
  await alternarAdmin('dark');
  await tem(admin, 'Situação: Inativa');
  await alternarAdmin('light');
  await clicar(admin, 'Ativar após revisão');
  await tem(admin, 'Situação: Agendada');
  const especifica = (await api(admin, promocoes)).body.find(p => p.escopo === 'VariantesSelecionadas');
  assert.deepEqual([especifica.valor, especifica.variantesIds, especifica.ativa], ['1190', [variante], true]);
  assert.equal(Date.parse(especifica.inicioUtc), Date.parse(dados.inicio));
  assert.equal(Date.parse(especifica.fimUtc), Date.parse(dados.fim));
  let opcoes = (await api(cliente, '/api/catalogo' + detalhe)).body.opcoes;
  assert.equal(opcoes.find(v => v.id === variante).preco.finalCentavos, '1112');
  await checkpoint('inicio-promocao');

  let revisaoTema = 0;
  for (const [width, tema] of [[375, 'original'], [1280, 'black-friday']]) {
    const publicado = await api(admin, '/api/admin/loja/tema', 'PUT', { chave: tema, revisaoEsperada: revisaoTema++ });
    assert.equal(publicado.status, 200);
    await largura(cliente, width);
    for (const pagina of ['/', '/produtos']) {
      await cliente.ir(pagina);
      await waitFor(() => cliente.ev(`document.querySelector('app-store-layout')?.dataset.theme === ${literal(tema)}`), tema);
      await tem(cliente, '14,38% OFF');
      assert.ok(await cliente.ev(`[...document.querySelectorAll('app-public-product-card')].some(c =>
        c.querySelector('del')?.textContent.includes('13,90') && c.innerText.includes('11,90'))`));
      await cabe(cliente);
    }
    await cliente.ir(detalhe);
    await campo(cliente, '#produto-opcao', alternativa);
    await tem(cliente, '16,00');
    await tem(cliente, '20% OFF');
    await campo(cliente, '#produto-opcao', variante);
    await tem(cliente, '11,90');
    await tem(cliente, '14,38% OFF');
    await cabe(cliente);
    console.log(`Chrome ${width} px e ${tema}: home, faixa do catálogo e troca de variante OK.`);
  }
  opcoes = (await api(cliente, '/api/catalogo' + detalhe)).body.opcoes;
  const oferta = opcoes.find(v => v.id === variante).preco;
  assert.deepEqual([oferta.regularCentavos, oferta.finalCentavos, oferta.economiaCentavos, oferta.offPontosBase],
    ['1390', '1190', '200', 1438]);

  const outro = await abrir('/login');
  await login(outro, dados.cliente, '/api/auth');
  await largura(outro, 375);
  await abrirCheckout(outro);
  await abrirCheckout(cliente);
  assert.equal((await api(admin, '/api/admin/configuracoes/pix-manual', 'PUT',
    { tipoChave: 'Email', revisao: 0, chavePix: 'promocoes@example.test', nomeRecebedor: 'Loja E2E' })).status, 200);
  await perderResposta(cliente, 'POST', '/api/pedidos');
  await clicar(cliente, 'Confirmar pedido');
  await waitFor(() => cliente.ev('window.statusAntesDaPerda'), 'Resposta real do pedido antes da perda');
  await tem(cliente, 'Repita a tentativa original');
  assert.ok(cliente.compra(), 'POST não foi observado no Chrome.');
  assert.equal(await cliente.ev('window.statusAntesDaPerda'), 201);
  const original = await capturar(cliente);
  assert.equal(original.itens[0].precoAceitoCentavos, '1190');
  const persistidos = await consulta(cliente);
  assert.equal(persistidos.length, 1, 'O pedido deve estar gravado antes do replay.');
  const [pedido] = persistidos;
  assert.equal(pedido.totalCentavos, '1190');
  await checkpoint('fim-promocao');
  const envios = cliente.compra().total;
  await cliente.ir('/checkout');
  await tem(cliente, 'Recuperar pedido');
  assert.deepEqual(await capturar(cliente), original);
  assert.equal(cliente.compra().total, envios);
  await clicar(cliente, 'Recuperar tentativa original');
  await waitFor(() => cliente.ev(`location.pathname === ${literal('/pedidos/' + pedido.id)}`),
    'Detalhe do pedido recuperado após o replay');
  await tem(cliente, '14,38% OFF');
  assert.equal(cliente.compra().total, envios + 1, 'Envios observados após a recuperação do pedido');
  assert.equal(cliente.compra().chave, original.chave);
  assert.deepEqual(JSON.parse(cliente.compra().corpo).itens, original.itens);
  assert.equal((await consulta(cliente)).length, 1);
  const pix = await api(cliente, '/api/pedidos/' + pedido.id + '/pagamento');
  assert.deepEqual([pix.body.situacao, pix.body.valorCentavos], ['disponivel', '1190']);

  await clicar(outro, 'Confirmar pedido');
  await tem(outro, 'Confira a nova cotação');
  await tem(outro, '11,12');
  assert.equal((await consulta(outro)).length, 1);
  const expirada = outro.compra();
  assert.equal(expirada.total, 1);
  assert.equal(await capturar(outro), null);
  await admin.ir('/admin/promocoes');

  await waitFor(() => admin.ev(`(() => {
    const b = [...document.querySelectorAll('li')].find(li => li.textContent.includes('Todas as variantes'))
      ?.querySelector('button'); if (!b || b.disabled) return false; b.click(); return true;
  })()`), 'Detalhe da promoção geral');
  await tem(admin, 'Situação: Ativa');
  const editada = await api(admin, promocoes + '/' + geral.id, 'PUT', {
    configuracao: { produtoId: produto, tipo: 'Percentual', escopo: 'TodasVariantes', valor: '2000', variantesIds: [] },
    versao: geral.versao,
  });
  assert.equal(editada.status, 200);
  await clicar(admin, 'Desativar');
  await clicar(admin, 'Confirmar desativação');
  await tem(admin, 'Outra alteração foi salva');
  await clicar(admin, 'Recarregar promoção');
  await clicar(admin, 'Desativar');
  await perderResposta(admin, 'PATCH', promocoes + '/' + geral.id + '/status');
  await clicar(admin, 'Confirmar desativação');
  await waitFor(() => admin.ev('window.statusAntesDaPerda'), 'Resposta real da desativação antes da perda');
  await tem(admin, 'A resposta ainda não foi confirmada');
  assert.equal(await admin.ev('window.statusAntesDaPerda'), 200);
  await admin.ir('/admin/promocoes');
  await tem(admin, 'Inativa');
  assert.equal((await api(admin, promocoes + '/' + geral.id)).body.ativa, false);

  await clicar(outro, 'Confirmar pedido');
  await waitFor(() => outro.compra()?.total === expirada.total + 1, 'POST com a oferta anterior após desativar a promoção');
  await tem(outro, '13,90');
  await waitFor(() => outro.ev(`!!${botao('Confirmar pedido')} && !document.querySelector('main app-preco-exibido del')`),
    'Preço regular exibido antes da nova confirmação');
  const alterada = outro.compra();
  assert.equal(alterada.total, 2, 'Envios observados após desativar a promoção');
  assert.notEqual(alterada.chave, expirada.chave);
  assert.equal((await consulta(outro)).length, 1);
  await clicar(outro, 'Confirmar pedido');
  await tem(outro, 'Aguardando pagamento');
  assert.notEqual(outro.compra().chave, alterada.chave);
  const cancelavel = (await consulta(outro)).find(p => p.id !== pedido.id);
  assert.equal(cancelavel.totalCentavos, '1390');
  assert.equal((await api(admin, '/api/admin/pedidos/' + cancelavel.id + '/cancelar', 'POST', { motivo: 'Ensaio' })).status, 200);

  const rota = '/api/admin/pedidos/' + pedido.id;
  assert.equal((await api(admin, rota + '/confirmar-pagamento', 'POST', { recebimentoConferido: true })).status, 200);
  const financeiro = (await api(admin, '/api/admin/financeiro/movimentacoes')).body.itens;
  assert.deepEqual(financeiro.filter(m => m.recursoId === pedido.id).map(m => m.valorCentavos), ['1190']);
  for (const [acao, corpo] of [
    ['iniciar-preparacao', {}], ['liberar-retirada', {}], ['confirmar-entrega', { entregaPresencialConfirmada: true }],
  ]) {
    const r = await api(admin, rota + '/' + acao, 'POST', corpo);
    assert.equal(r.status, 200, acao + ': ' + JSON.stringify(r.body));
  }
  await cliente.ir('/pedidos/' + pedido.id);
  await tem(cliente, 'Entregue');
  await tem(cliente, '11,90');
  const financeiroFinal = (await api(admin, '/api/admin/financeiro/movimentacoes')).body.itens;
  assert.deepEqual(financeiroFinal.map(m => [m.recursoId, m.valorCentavos]), [[pedido.id, '1190']]);
  console.log('Promoções: SQL/API/Chrome reais; R$ 11,90 em pedido, PIX e Financeiro; replay pós-expiração, nova chave, conflito, desativação e entrega verificados.');
}
