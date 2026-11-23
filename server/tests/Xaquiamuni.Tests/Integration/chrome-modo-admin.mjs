import assert from 'node:assert/strict';

export async function validarModoAdmin({ a, c, call, waitFor, login, dados, admin }) {
  const tema = "document.querySelector('.admin-shell')?.dataset.adminTheme";
  const botao = "document.querySelector('.admin-theme-toggle')";
  const chave = 'xaquiamuni:admin:tema';
  const viewport = (contexto, width) => call('Emulation.setDeviceMetricsOverride',
    { width, height: 900, mobile: false, deviceScaleFactor: 1 }, contexto.sessionId);
  const abrir = async (contexto, rota, expressao) => {
    await contexto.ir(rota);
    await waitFor(() => contexto.ev(`location.pathname + location.search === ${JSON.stringify(rota)} && (${expressao})`), rota);
  };
  const abrirAdmin = rota => abrir(a, rota, "!!document.querySelector('.admin-shell main h1')");
  const escrever = (rota, metodo, corpo, lerCorpo = false) => a.ev(`(async () => {
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    const r = await fetch(${JSON.stringify(rota)}, {
      method: ${JSON.stringify(metodo)},
      headers: { 'Content-Type':'application/json', 'X-XSRF-TOKEN':requestToken, 'Idempotency-Key':crypto.randomUUID() },
      body: JSON.stringify(${JSON.stringify(corpo)}),
    });
    return ${lerCorpo} ? { status: r.status, corpo: await r.json() } : r.status;
  })()`);
  const teclado = async (key, code, windowsVirtualKeyCode) => {
    for (const type of ['keyDown', 'keyUp'])
      await call('Input.dispatchKeyEvent', { type, key, code, windowsVirtualKeyCode }, a.sessionId);
  };
  const alternar = async destino => {
    await a.ev(`(${botao}).click()`);
    await waitFor(() => a.ev(`${tema} === '${destino}'`), 'Alternância para ' + destino);
  };
  const semEstouro = contexto => contexto.ev('document.documentElement.scrollWidth <= innerWidth');
  const adminReal = [
    '/admin', '/admin/financeiro', '/admin/financeiro?secao=contas', '/admin/agenda',
    '/admin/produtos', '/admin/categorias', '/admin/promocoes', '/admin/pedidos',
    '/admin/configuracoes/pix-manual', '/admin/vendas-externas', '/admin/estoque/produtos-acabados',
    '/admin/estoque/insumos', '/admin/estoque/insumos/entradas', '/admin/estoque/insumos/novo',
    '/admin/fornecedores', '/admin/compras', '/admin/receitas', '/admin/producao', '/admin/temas',
    '/admin/configuracoes/atendimento',
  ];

  await abrirAdmin('/admin/financeiro');
  const hoje = await a.ev("fetch('/api/admin/financeiro/resumo').then(r=>r.json()).then(r=>r.posicaoAtual.contasEmAberto.dataOperacional)");
  assert.equal(await escrever('/api/admin/financeiro/lancamentos/pendentes', 'POST', {
    tipo: 'Entrada', valorCentavos: '1100', categoria: 'Homologação', descricao: 'Conta que vence hoje',
    vencimento: hoje,
  }), 201);
  assert.equal(await escrever('/api/admin/agenda', 'POST', {
    titulo: 'Compromisso de homologação', data: hoje, horarioInicial: '09:00', horarioFinal: '09:30',
  }), 201);
  assert.equal(await escrever('/api/admin/configuracoes/pix-manual', 'PUT', {
    tipoChave: 'Email', chavePix: 'modo-admin@example.test', nomeRecebedor: 'Loja E2E',
  }), 200);
  assert.equal(await escrever('/api/admin/estoque/produtos-acabados/variantes/' + dados.varianteFinanceiro + '/entradas', 'POST', {
    tipo: 'EntradaManual', quantidade: 3, condicaoDestino: 'Vendavel', semControleValidade: true, motivo: 'Homologação visual',
  }), 201);

  assert.equal(await escrever('/api/admin/insumos/' + dados.insumoFinanceiro + '/entradas', 'POST', {
    tipo: 'EstoqueInicial', quantidadeOriginal: 25, unidadeOriginal: 'Grama', semControleValidade: true,
  }), 201);
  const receita = await escrever('/api/admin/receitas', 'POST', {
    produtoVarianteId: dados.varianteFinanceiro, nome: 'Receita de homologação', rendimento: 2,
    itens: [{ insumoId: dados.insumoFinanceiro, quantidade: '2', unidadeBase: 'Grama' }],
  }, true);
  assert.equal(receita.status, 201, JSON.stringify(receita.corpo));
  const receitaId = receita.corpo.resumo.id;
  const previa = await a.ev(`fetch('/api/admin/producao/previa?receitaId=${receitaId}&quantidade=2').then(r=>r.json())`);
  assert.ok(previa.receitaVersaoId);
  assert.equal(await escrever('/api/admin/producao', 'POST', {
    receitaId, receitaVersaoId: previa.receitaVersaoId, quantidade: 2,
  }), 201);

  for (const width of [375, 1280]) {
    await viewport(a, width);
    await viewport(c, width);
    await a.ev(`localStorage.removeItem('${chave}')`);
    await abrirAdmin('/admin');
    assert.equal(await a.ev(tema), 'light');
    await a.ev("document.querySelector('.admin-mark').focus()");
    await teclado('Tab', 'Tab', 9);
    assert.ok(await a.ev(`(() => {
      const b = ${botao}, estilo = getComputedStyle(b), tamanho = b.getBoundingClientRect();
      return document.activeElement === b && b.matches(':focus-visible') && estilo.outlineStyle !== 'none'
        && parseFloat(estilo.outlineWidth) > 0 && tamanho.width >= 44 && tamanho.height >= 44
        && b.nextElementSibling.textContent.trim() === 'Sair' && b.getAttribute('aria-label') === 'Ativar modo escuro';
    })()`));
    await teclado(' ', 'Space', 32);
    await waitFor(() => a.ev(`${tema} === 'dark'`), 'Alternância via teclado');
    assert.equal(await a.ev(`(${botao}).getAttribute('aria-label')`), 'Ativar modo claro');
    await abrirAdmin('/admin/financeiro');
    assert.equal(await a.ev(tema), 'dark');
    await abrirAdmin('/admin/financeiro');
    assert.equal(await a.ev(tema), 'dark');
    if (width === 375) {
      await a.ev("document.querySelector('.admin-menu-toggle').click()");
      assert.ok(await a.ev("document.querySelector('.admin-drawer').open && getComputedStyle(document.querySelector('.admin-drawer')).colorScheme === 'dark'"));
      await teclado('Escape', 'Escape', 27);
      await waitFor(() => a.ev("!document.querySelector('.admin-drawer').open && document.activeElement === document.querySelector('.admin-menu-toggle')"), 'Fechar menu e restaurar foco');
    }
    await a.ev("document.querySelector('.admin-signout').click()");
    await waitFor(() => a.ev("location.pathname === '/admin/login' && document.querySelector('main')?.dataset.adminLoginTheme === 'dark'"), 'Login ADMIN escuro após Sair');
    await login(a, admin, '/api/admin/auth');

    for (const rota of adminReal) {
      await abrirAdmin(rota);
      if (rota.includes('secao=contas'))
        await waitFor(() => a.ev("document.querySelector('main')?.textContent.includes('Conta que vence hoje')"), 'Conta real');
      if (rota === '/admin/agenda')
        await waitFor(() => a.ev("document.querySelector('main')?.textContent.includes('Compromisso de homologação')"), 'Compromisso real');
      if (rota === '/admin/receitas' || rota === '/admin/producao')
        await waitFor(() => a.ev("document.querySelector('main')?.textContent.includes('Receita de homologação')"), 'Receita ou ordem real');
      if (rota === '/admin' || rota === '/admin/financeiro')
        await waitFor(() => a.ev("!!document.querySelector('.alertas-contas .conta-hoje')"), 'Alerta financeiro real');
      for (const modo of ['dark', 'light']) {
        if (await a.ev(tema) !== modo) await alternar(modo);
        assert.ok(await semEstouro(a), modo + ' ' + width + ' ' + rota);
        assert.equal(await a.ev("getComputedStyle(document.querySelector('.admin-shell')).colorScheme"), modo);
        assert.ok(await a.ev(`(() => {
          const shell = document.querySelector('.admin-shell'), alvo = shell.querySelector('main h1');
          const lux = css => (css.match(/[0-9]+/g) ?? []).slice(0,3).map(n=>Number(n)/255)
            .map(n=>n<=.04045?n/12.92:((n+.055)/1.055)**2.4)
            .reduce((s,n,i)=>s+n*[.2126,.7152,.0722][i],0);
          const contraste = (fg, bg) => {
            const [escuro, claro] = [lux(fg), lux(bg)].sort((x,y)=>x-y);
            return (claro+.05)/(escuro+.05);
          };
          const verificar = el => {
            let fundo = el;
            while (getComputedStyle(fundo).backgroundColor === 'rgba(0, 0, 0, 0)' && fundo.parentElement)
              fundo = fundo.parentElement;
            return contraste(getComputedStyle(el).color,getComputedStyle(fundo).backgroundColor) >= 4.5;
          };
          if (!verificar(alvo)) return false;
          const alvos = shell.querySelectorAll('.alertas-contas a, input[type="date"]:enabled, .cartao strong');
          return [...alvos].filter(el => el.getClientRects().length).every(verificar);
        })()`), 'Contraste de título, campos ou alertas em ' + [modo, width, rota].join(' '));
      }
      await alternar('dark');
    }
    console.log('Modo ADMIN em ' + width + ' px: 20 URLs nos dois modos, contraste de títulos, campos e alertas, foco, recarga, sessão, conta, agenda e produção reais.');
  }

  let itemAdicionado = false;
  const atendimento = '/api/admin/loja/atendimento';
  assert.equal(await escrever(atendimento + '/whatsapp', 'PUT', {
    numero: '+5554999990000', confirmado: true, habilitado: true, revisao: 0,
  }), 200);
  const temas = ['original','natal','black-friday','pascoa','ano-novo','dia-das-maes'];
  for (const width of [375, 1280]) {
    await viewport(a, width);
    await viewport(c, width);
    for (const chavePublica of temas) {
      await abrirAdmin('/admin/temas');
      const antes = await a.ev("fetch('/api/admin/loja/tema').then(r=>r.json()).then(r=>r.publicacao)");
      if (antes.chave !== chavePublica)
        assert.equal(await escrever('/api/admin/loja/tema', 'PUT', { chave: chavePublica, revisaoEsperada: antes.revisao }), 200);
      const atual = await a.ev("fetch('/api/loja/tema').then(r=>r.json())");
      const layout = contexto => contexto.ev(`(() => {
        const e = document.querySelector('app-store-layout');
        return [e?.dataset.theme, e?.style.getPropertyValue('--tema-painel'), !!e?.closest('[data-admin-theme]')];
      })()`);
      const temaPronto = `document.querySelector('app-store-layout')?.dataset.theme === '${chavePublica}'`;
      if (await a.ev(tema) !== 'light') await alternar('light');
      await abrir(c, '/', temaPronto);
      await waitFor(() => c.ev("!!document.querySelector('a[href^=\"https://wa.me/5554999990000\"]')"), 'Contato autorizado');
      const referencia = await layout(c);
      assert.equal(referencia[2], false);
      assert.equal(!!referencia[1], chavePublica !== 'original');
      await alternar('dark');
      assert.deepEqual(await layout(c), referencia);
      await abrir(a, '/admin/temas/previa/' + chavePublica, temaPronto);
      assert.deepEqual(await layout(a), referencia);
      for (const rota of ['/produtos', '/produtos/' + dados.produtoFinanceiro, '/carrinho', '/checkout']) {
        const titulo = rota === '/produtos' ? '#catalog-title' : rota.startsWith('/produtos/') ? '#product-name' : 'main h1';
        await abrir(c, rota, `(${temaPronto}) && !!document.querySelector(${JSON.stringify(titulo)})`);
        assert.deepEqual(await layout(c), referencia);
        assert.ok(await semEstouro(c), chavePublica + ' ' + rota + ' ' + width);
        if (!itemAdicionado && rota.startsWith('/produtos/')) {
          await waitFor(() => c.ev("!![...document.querySelectorAll('main button')].find(b=>b.textContent.includes('Adicionar ao carrinho')&&!b.disabled)"), 'Produto disponível');
          await c.ev("[...document.querySelectorAll('main button')].find(b=>b.textContent.includes('Adicionar ao carrinho')).click()");
          itemAdicionado = true;
        }
      }
      await abrir(a, '/login', '!!document.querySelector("main h1")');
      assert.ok(await a.ev("!document.querySelector('[data-admin-theme],[data-admin-login-theme]')"));
      assert.deepEqual(await a.ev("fetch('/api/loja/tema').then(r=>r.json())"), atual);
      console.log('Tema ' + chavePublica + ' em ' + width + ' px: loja, catálogo, detalhe, carrinho, checkout, login e prévia isolados.');
    }
  }

  const botaoTexto = (texto, contexto) => waitFor(() => contexto.ev(`(() => {
    const botao = [...document.querySelectorAll('main button')].find(e => e.textContent.trim() === ${JSON.stringify(texto)});
    if (!botao || botao.disabled) return false;
    botao.click();
    return true;
  })()`), texto);
  const campo = (contexto, seletor, valor) => contexto.ev(`(() => {
    const campo = document.querySelector(${JSON.stringify(seletor)});
    campo.value = ${JSON.stringify(valor)};
    campo.dispatchEvent(new Event(campo.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
  })()`);
  await viewport(a, 375);
  console.log('Entrega própria: cadastro de cidade pelo formulário ADMIN.');
  await abrirAdmin('/admin/configuracoes/atendimento');
  await waitFor(() => a.ev("!!document.querySelector('input[name=tarifa]')"), 'Formulário de cidades');
  for (const [seletor, valor] of Object.entries({ cidade: 'Vacaria', uf: 'RS', ibge: '4322509', tarifa: '12,50' }))
    await campo(a, `input[name=${seletor}]`, valor);
  await a.ev("document.querySelector('input[name=ativa]').click()");
  await botaoTexto('Salvar cidade', a);
  await waitFor(() => a.ev("document.querySelector('main').textContent.includes('Vacaria/RS')"), 'Cidade gravada');
  await a.ev("document.querySelector('input[name=entrega]').click()");
  await botaoTexto('Salvar modalidades', a);
  await waitFor(() => a.ev("fetch('/api/loja/modalidades-entrega').then(r=>r.json()).then(x=>x.entregaPropria)"), 'Entrega própria habilitada');
  console.log('Entrega própria: modalidade habilitada e cidade ativa.');

  for (const width of [375, 1280]) {
    await viewport(c, width);
    await viewport(a, width);
    if (width === 1280) {
      await abrir(c, '/produtos/' + dados.produtoFinanceiro, '!!document.querySelector("#product-name")');
      await botaoTexto('Adicionar ao carrinho', c);
    }
    await abrir(c, '/checkout', "!!document.querySelector('#modalidade-entrega option[value=EntregaPropria]')");
    await campo(c, '#modalidade-entrega', 'EntregaPropria');
    await c.ev("document.querySelector('main input[type=checkbox]').click()");
    const campos = ['95200-000', 'RS', 'Vacaria', 'Centro', 'Rua do ensaio', '12'];
    for (let i = 0; i < campos.length; i++)
      await campo(c, `main fieldset label:nth-of-type(${i + 1}) input`, campos[i]);
    await botaoTexto('Consultar valores de entrega', c);
    await waitFor(() => c.ev("document.querySelector('main').textContent.includes('24,49')"), 'Total com frete');
    console.log(`Entrega própria em ${width} px: cotação real confirmada.`);
    assert.ok(await semEstouro(c));
    await botaoTexto('Confirmar valor e finalizar compra', c);
    const id = await waitFor(() => c.ev("location.pathname.startsWith('/pedidos/') && location.pathname.split('/').at(-1)"), 'Pedido entregue pela loja');
    const rota = '/admin/pedidos/' + id;
    await waitFor(() => c.ev("document.querySelector('main')?.textContent.includes('Rua do ensaio')"), 'Destino congelado');
    await abrirAdmin(rota);
    await botaoTexto('Confirmar pagamento', a);
    await a.ev("document.querySelector('main [aria-labelledby=pedido-acoes] input[type=checkbox]').click()");
    await botaoTexto('Registrar confirmação', a);
    await botaoTexto('Iniciar preparação', a);
    await botaoTexto('Confirmar início', a);
    await botaoTexto('Iniciar entrega própria', a);
    await a.ev("document.querySelector('main [aria-labelledby=pedido-acoes] input[type=checkbox]').click()");
    await botaoTexto('Registrar saída e baixar estoque', a);
    await botaoTexto('Confirmar recebimento', a);
    await a.ev("document.querySelector('main [aria-labelledby=pedido-acoes] input[type=checkbox]').click()");
    await botaoTexto('Registrar recebimento', a);
    await waitFor(() => a.ev("document.querySelector('main dl')?.textContent.includes('Entregue')"), 'Recebimento registrado');
    assert.ok(await semEstouro(a));
    console.log(`Entrega própria em ${width} px: endereço, frete, total e ações administrativas confirmados.`);
  }
}
