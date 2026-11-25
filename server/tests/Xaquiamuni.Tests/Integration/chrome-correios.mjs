import assert from 'node:assert/strict';

export async function validarCorreios({ a, c, call, waitFor, dados, abrir, login, cliente }) {
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
  const modalidades = async () => c.ev("fetch('/api/loja/modalidades-entrega').then(r=>r.json())");
  const atendimento = async () => a.ev("fetch('/api/admin/loja/atendimento').then(r=>r.json())");
  const inicial = await modalidades();
  assert.equal(inicial.correiosCotacaoManual ?? false, false);
  assert.equal(inicial.retiradaLocal, true);
  assert.equal(await a.ev("document.querySelector('[name=validadeCorreios]').value"), '48');
  const revisaoInicial = (await atendimento()).revisao;
  await a.ev("window.confirm = () => true; document.querySelector('[name=correiosManual]').click()");
  await clicar(a, 'Salvar cotação manual');
  await waitFor(() => c.ev("fetch('/api/loja/modalidades-entrega').then(r=>r.json()).then(x=>x.correiosCotacaoManual === true)"),
    'Modalidade publicada');
  const configurar = (habilitada, revisao) => escrever('/api/admin/loja/atendimento/correios-manual',
    'PUT', { habilitada, validadeHoras: 48, revisao });
  assert.equal(await configurar(false, revisaoInicial), 409);
  assert.equal(await configurar(false, (await atendimento()).revisao), 200);
  assert.equal((await modalidades()).correiosCotacaoManual ?? false, false);
  assert.equal(await configurar(true, (await atendimento()).revisao), 200);
  const publicada = await modalidades();
  assert.equal(publicada.correiosCotacaoManual, true);
  assert.equal(publicada.retiradaLocal, inicial.retiradaLocal);
  assert.equal(publicada.entregaPropria, inicial.entregaPropria);
  console.log('Correios manual: padrão desabilitado, validade de 48 h, conflito 409 e reabilitação conferidos.');
  assert.equal(await a.ev(`(async () => {
    const rota = '/api/admin/promocoes-frete';
    const csrf = async () => (await fetch('/api/auth/csrf').then(r => r.json())).requestToken;
    const resposta = await fetch(rota, { method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': await csrf() },
      body: JSON.stringify({ nome: 'Postal grátis E2E', tipo: 'FreteGratis', valor: '0',
        modalidades: ['CorreiosManual'], subtotalMinimoCentavos: '0' }) });
    if (resposta.status !== 201) return resposta.status;
    const etag = resposta.headers.get('ETag');
    const { id } = await resposta.json();
    return (await fetch(rota + '/' + id + '/status', { method: 'PATCH',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': await csrf(), 'If-Match': etag },
      body: JSON.stringify({ ativa: true }) })).status;
  })()`), 200);

  for (const width of [375, 1280]) {
    for (const contexto of [a, c]) {
      await call('Emulation.setDeviceMetricsOverride',
        { width, height: 900, mobile: false, deviceScaleFactor: 1 }, contexto.sessionId);
    }
    await c.ir('/produtos/' + dados.produtoFinanceiro);
    if (width === 375) await clicar(c, 'Adicionar ao carrinho');
    await c.ir('/checkout');
    await waitFor(() => c.ev("!!document.querySelector('#modalidade-entrega option[value=Correios]')"), 'Opção Correios');
    const rotaEndereco = '/api/clientes/me/endereco-padrao';
    const seletor = i => '.resumo fieldset label:nth-of-type(' + (i + 1) + ') input';
    const preencher = async (i, valor) => assert.ok(await campo(c, seletor(i), valor));
    const ler = i => c.ev(`document.querySelector(${JSON.stringify(seletor(i))})?.value`);
    const selecionar = modalidade => campo(c, '#modalidade-entrega', modalidade);
    if (width === 375) {
      c.falharProximaConsultaPix(rotaEndereco);
      assert.ok(await selecionar('Correios'));
      await texto(c, 'Não foi possível consultar o endereço cadastrado');
      await preencher(0, '95000000');
      assert.equal(await ler(0), '95000-000');
      for (const [resposta, aviso] of [
        [`new Response('{"endereco":null}', {status: 200})`, 'Endereço cadastrado ausente ou incompleto'],
        [`new Response(null, {status: 401})`, 'Entre na sua conta para consultar o endereço cadastrado'],
      ]) {
        await selecionar('RetiradaLocal');
        await c.ev(`(() => {
          const original = window.fetch;
          window.fetch = (url, opcoes) => {
            if (new URL(url, location.origin).pathname !== ${JSON.stringify(rotaEndereco)})
              return original(url, opcoes);
            window.fetch = original;
            return Promise.resolve(${resposta});
          };
        })()`);
        assert.ok(await selecionar('Correios'));
        await texto(c, aviso);
        assert.equal(await ler(0), '95000-000');
      }
      await selecionar('RetiradaLocal');
      await c.ev(`(() => {
        const original = window.fetch;
        window.fetch = async (url, opcoes) => {
          if (new URL(url, location.origin).pathname !== ${JSON.stringify(rotaEndereco)})
            return original(url, opcoes);
          window.fetch = original;
          const espera = new Promise(resolve => window.liberarEndereco = resolve);
          const resposta = await original(url, opcoes);
          await espera;
          return resposta;
        };
      })()`);
    }
    assert.ok(await selecionar('Correios'));
    if (width === 375) {
      await texto(c, 'Consultando endereço cadastrado');
      await preencher(4, 'Digitado durante a consulta');
      await waitFor(() => c.ev("typeof window.liberarEndereco === 'function'"), 'Consulta retida');
      await c.ev('window.liberarEndereco()');
      await texto(c, 'Usar endereço cadastrado');
      assert.equal(await ler(4), 'Digitado durante a consulta');
    }
    await clicar(c, 'Usar endereço cadastrado');
    for (const [i, esperado] of ['95200000', 'RS', 'Vacaria', 'Centro', 'Rua cadastrada do ensaio', '12', ''].entries())
      assert.equal(await ler(i), esperado);
    await clicar(c, 'Informar outro endereço');
    assert.equal(await ler(0), '');
    await clicar(c, 'Usar endereço cadastrado');
    assert.equal(await ler(4), 'Rua cadastrada do ensaio');
    await preencher(4, 'Rua de teste');
    await preencher(5, String(width));
    if (width === 375) {
      await c.ev('window.confirm = () => true');
      for (const [i, valor, original] of [[2, 'Caxias do Sul', 'Vacaria'], [1, 'SP', 'RS'], [0, '99999999', '95200000']]) {
        await preencher(i, valor);
        await clicar(c, 'Solicitar cotação');
        await texto(c, i === 0 ? 'CEP não encontrado' : 'O CEP precisa corresponder ao município informado');
        await preencher(i, original);
      }
      console.log('Endereço postal: falha, ausência, 401, resposta tardia, alternativa e destinos divergentes verificados.');
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

    await a.ir('/admin/cotacoes-correios');
    await texto(a, 'Aguardando análise');
    assert.ok(await campo(a, 'main select', 'AguardandoAnalise'));
    await waitFor(() => a.ev(`!!document.querySelector('a[href="${'/admin' + rota}"]')`), 'Cotação na lista filtrada');
    await a.ir('/admin' + rota);
    await texto(a, 'Rua de teste');
    const snapshot = await c.ev(`fetch(${JSON.stringify('/api' + rota)}).then(r=>r.json()).then(x=>x.endereco)`);
    assert.deepEqual([snapshot.cep, snapshot.uf, snapshot.cidade, snapshot.logradouro, snapshot.numero],
      ['95200000', 'RS', 'Vacaria', 'Rua de teste', String(width)]);
    if (width === 1280) {
      await a.ev("document.querySelector('.admin-theme-toggle').click()");
      await waitFor(() => a.ev("document.querySelector('.admin-shell').dataset.adminTheme === 'dark'"), 'ADMIN escuro');
    }
    for (const [nome, valor] of Object.entries({ servico: 'PAC', frete: '12,90', prazo: '5', pesoTotal: '200' })) {
      assert.ok(await campo(a, 'input[name=' + nome + ']', valor));
    }
    for (let volume = 0; volume < 2; volume++) {
      await clicar(a, 'Adicionar volume');
      const grupo = 'main form fieldset fieldset:nth-of-type(' + (volume + 1) + ')';
      await waitFor(() => a.ev('!!document.querySelector(' + JSON.stringify(grupo) + ')'), 'Campos do volume');
      for (const [indice, valor] of ['100', '18', '12', '8'].entries()) {
        assert.ok(await campo(a, grupo + ' label:nth-of-type(' + (indice + 1) + ') input', valor));
      }
    }
    await a.ev('window.confirm = mensagem => { window.previaPublicacao = mensagem; return true; }');
    await clicar(a, 'Revisar e publicar proposta');
    await texto(a, 'Proposta disponível');
    await texto(a, 'Volume 2');
    const primeira = await a.ev(`fetch('${'/api/admin' + rota}').then(r=>r.json()).then(x=>x.propostas[0])`);
    assert.equal(Date.parse(primeira.validaAteUtc) - Date.parse(primeira.publicadaEmUtc), 48 * 3600000);
    assert.equal(primeira.volumes.length, 2);
    assert.equal(primeira.freteCentavos, '0');
    assert.equal(primeira.valores.totalCentavos, '1199');
    assert.equal(primeira.valores.descontoFrete.originalCentavos, '1290');
    assert.match(await a.ev('window.previaPublicacao'), /Frete grátis/);
    for (const [nome, valor] of Object.entries({ servico: 'PAC', frete: '12,90', prazo: '6', pesoTotal: '200' })) {
      assert.ok(await campo(a, 'input[name=' + nome + ']', valor));
    }
    await clicar(a, 'Revisar e publicar proposta');
    await texto(a, 'Revisão 2');
    const historico = await a.ev(`fetch('${'/api/admin' + rota}').then(r=>r.json()).then(x=>x.propostas)`);
    assert.deepEqual(historico.find(p => p.revisao === primeira.revisao), primeira);
    assert.equal(historico.find(p => p.revisao === 2).prazoDias, 6);
    assert.ok(await largura(a));

    await c.ir(rota);
    await texto(c, 'Proposta disponível');
    assert.ok(await c.ev("![...document.querySelectorAll('main a')].some(a => a.textContent.trim() === 'Ver meu pedido')"));
    await c.ev('window.confirm = () => true');
    await clicar(c, 'Aceitar proposta');
    await clicar(c, 'Continuar para pagamento');
    assert.equal(await c.ev(`fetch('${'/api' + rota}').then(r=>r.json()).then(x=>x.revisaoAceita)`), 2);
    await texto(c, 'Frete grátis');
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
    await texto(c, 'Frete grátis');
    const pedido = await c.ev('location.pathname');
    const numeroPublico = await waitFor(() => c.ev("document.querySelector('#pedido-title')?.textContent.match(/XQ-[23456789ACDEFHJKMNPQRTUVWXY]{10}/)?.[0]"),
      'Número público do pedido postal');
    const pix = await c.ev(`fetch(${JSON.stringify('/api' + pedido + '/pagamento')}).then(r=>r.json())`);
    assert.equal(pix.valorCentavos, '1199');

    if (width === 375) {
      const outroContexto = await abrir('/login');
      await login(outroContexto, cliente, '/api/auth');
      assert.equal(await outroContexto.ev('localStorage.length + sessionStorage.length'), 0);
      const linkPedido = "Array.from(document.querySelectorAll('main a')).find(a => a.textContent.trim() === 'Ver meu pedido')";
      await outroContexto.ir(rota);
      await waitFor(() => outroContexto.ev(`(${linkPedido})?.getAttribute('href') === ${JSON.stringify(pedido)}`),
        'Atalho em contexto limpo');
      await outroContexto.ev('window.atalhoAntesDeRecarregar = true');
      await call('Page.reload', {}, outroContexto.sessionId);
      await waitFor(() => outroContexto.ev(`!window.atalhoAntesDeRecarregar && (${linkPedido})?.getAttribute('href') === ${JSON.stringify(pedido)}`),
        'Atalho após recarregar');
      await outroContexto.ev(`(${linkPedido}).click()`);
      await waitFor(() => outroContexto.ev(`location.pathname === ${JSON.stringify(pedido)}`), 'Pedido vinculado');
      await texto(outroContexto, 'Aguardando pagamento');
      console.log('Pedido convertido aberto após recarregar em outro contexto Chrome autenticado, com armazenamento limpo.');
    }

    if (width === 375) a.falharProximaConsultaPix('/api/admin' + pedido + '/historico-rastreamento');
    await a.ir('/admin' + pedido);
    await texto(a, numeroPublico);
    if (width === 375) {
      await texto(a, 'Não foi possível consultar o histórico de rastreamento.');
      const botao = "document.querySelector('app-historico-rastreamento button')";
      const tecla = async (key, windowsVirtualKeyCode, code = key) => {
        for (const type of ['keyDown', 'keyUp']) {
          await call('Input.dispatchKeyEvent', { type, key, code, windowsVirtualKeyCode }, a.sessionId);
        }
      };
      await tecla('Tab', 9);
      assert.ok(await a.ev(`(${botao}).focus(), document.activeElement === ${botao} &&
        (${botao}).matches(':focus-visible') && getComputedStyle(${botao}).outlineStyle !== 'none'`));
      await tecla(' ', 32, 'Space');
    }
    await texto(a, 'Nenhuma correção de rastreamento registrada.');
    const confirmar = async (acao, confirmar, preencher) => {
      await clicar(a, acao);
      if (preencher) await preencher();
      await a.ev("document.querySelector('form input[type=checkbox]').click()");
      await clicar(a, confirmar);
    };
    await confirmar('Confirmar pagamento', 'Registrar confirmação');
    await texto(a, 'Pagamento confirmado');
    const entradas = await a.ev(`fetch('/api/admin/financeiro/movimentacoes').then(r=>r.json()).then(x =>
      x.itens.filter(m => m.recursoId === ${JSON.stringify(pedido.split('/').at(-1))}))`);
    assert.deepEqual(entradas.map(m => m.valorCentavos), ['1199']);
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
    const corrigir = codigo => confirmar('Corrigir rastreamento', 'Salvar código', async () => {
      assert.ok(await campo(a, 'form input[maxlength="32"]', codigo));
    });
    const lerHistoricoRastreamento = () => a.ev("Array.from(document.querySelectorAll('app-historico-rastreamento li'), linha => [...linha.querySelectorAll('dd')].map(campo => campo.textContent.trim()))");
    await corrigir('AA123456789BR');
    await waitFor(async () => (await lerHistoricoRastreamento())[0]?.[1] === 'AA123456789BR', 'Primeira correção no histórico');
    assert.equal((await lerHistoricoRastreamento())[0][0], 'Não informado');
    await corrigir('AB123456789BR');
    await waitFor(async () => (await lerHistoricoRastreamento())[0]?.[1] === 'AB123456789BR', 'Segunda correção no histórico');
    const correcoes = await lerHistoricoRastreamento();
    assert.deepEqual(correcoes.map(linha => linha.slice(0, 2)), [
      ['AA123456789BR', 'AB123456789BR'], ['Não informado', 'AA123456789BR'],
    ]);
    for (const [, , data, autor] of correcoes) {
      assert.match(data, /^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}:\d{2}$/);
      assert.match(autor, /^[a-f0-9-]{36}$/i);
    }
    for (const tema of ['light', 'dark']) {
      const temaAtual = () => a.ev("document.querySelector('.admin-shell')?.dataset.adminTheme");
      if (await temaAtual() !== tema) await a.ev("document.querySelector('.admin-theme-toggle').click()");
      await waitFor(async () => await temaAtual() === tema, 'Tema administrativo ' + tema);
      assert.ok(await largura(a));
      assert.ok(await a.ev(`(() => {
        const texto = document.querySelector('app-historico-rastreamento dd');
        let fundo = texto;
        while (getComputedStyle(fundo).backgroundColor === 'rgba(0, 0, 0, 0)' && fundo.parentElement)
          fundo = fundo.parentElement;
        const luz = css => css.match(/[0-9]+/g).slice(0,3).map(n => Number(n) / 255)
          .map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4)
          .reduce((s, n, i) => s + n * [.2126, .7152, .0722][i], 0);
        const cores = [luz(getComputedStyle(texto).color), luz(getComputedStyle(fundo).backgroundColor)].sort((a, b) => a - b);
        return (cores[1] + .05) / (cores[0] + .05) >= 4.5;
      })()`), 'Contraste do histórico em ' + tema + ', ' + width + ' px');
    }
    if (width === 375) await a.ev("document.querySelector('.admin-theme-toggle').click()");
    await confirmar('Confirmar entrega postal', 'Registrar entrega postal');
    await texto(a, 'Entrega confirmada em');
    await c.ir(pedido);
    await texto(c, 'AB123456789BR');
    assert.equal(await c.ev("document.querySelector('main a[href^=\"https://rastreamento.correios.com.br\"]').href"),
      'https://rastreamento.correios.com.br/app/index.php');
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
      await texto(c, 'Frete grátis');
      assert.ok(await largura(c));
      await c.ir(pedido);
      await waitFor(() => c.ev("document.querySelector('app-store-layout')?.dataset.theme === " + JSON.stringify(chave)),
        'Pedido com tema ' + chave);
      await texto(c, 'AB123456789BR');
      await texto(c, 'Frete grátis');
      assert.ok(await largura(c));
    }
    console.log('Correios em ' + width + ' px: 11,99 + frete grátis = 11,99; pedido, postagem, rastreamento e entrega.');
  }
}

