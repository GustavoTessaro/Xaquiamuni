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
    numero: '+55 (54) 99999-0000', confirmado: true, habilitado: true, revisao: 0,
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
  const modalidades = () => a.ev("fetch('/api/loja/modalidades-entrega').then(r=>r.json())");
  assert.deepEqual(await modalidades(), { retiradaLocal: true, entregaPropria: false, correios: false, freteRetiradaCentavos: '0' });
  await campo(a, 'input[name=endereco]', 'Endereço fictício de retirada');
  await campo(a, 'textarea[name=instrucoes]', 'Compareça com o número do pedido.');
  await botaoTexto('Salvar modalidades', a);
  await waitFor(() => a.ev("document.querySelector('main').textContent.includes('Modalidades salvas.')"), 'retirada salva');
  await abrirAdmin('/admin/configuracoes/atendimento');
  await waitFor(() => a.ev("document.querySelector('[name=endereco]')?.value === 'Endereço fictício de retirada'"), 'endereço após recarga');
  assert.equal(await a.ev("document.querySelector('[name=instrucoes]').value"), 'Compareça com o número do pedido.');
  await waitFor(() => a.ev("!!document.querySelector('[name=uf] option[value=RS]')"), 'Estados carregados');
  await campo(a, 'select[name=uf]', 'RS');
  await waitFor(() => a.ev("!!document.querySelector('[name=cidade] option[value=\"4322509\"]')"), 'Municípios do RS');
  await campo(a, 'select[name=cidade]', '4322509');
  await campo(a, 'input[name=tarifa]', '12,50');
  assert.equal(await a.ev("!!document.querySelector('[name=ibge]')"), false);
  await a.ev("document.querySelector('input[name=ativa]').click()");
  await botaoTexto('Salvar cidade', a);
  await waitFor(() => a.ev("document.querySelector('main').textContent.includes('Vacaria/RS')"), 'Cidade gravada');
  await a.ev("document.querySelector('input[name=entrega]').click()");
  await botaoTexto('Salvar modalidades', a);
  await waitFor(() => a.ev("fetch('/api/loja/modalidades-entrega').then(r=>r.json()).then(x=>x.entregaPropria)"), 'Entrega própria habilitada');
  console.log('Entrega própria: modalidade habilitada e cidade ativa.');
  const lerAtendimento = sufixo => a.ev(`fetch(${JSON.stringify(atendimento + sufixo)}).then(r=>r.json())`);
  const [cidade] = await lerAtendimento('/cidades');
  const rotaCidade = '/cidades/' + cidade.id;
  const alterar = async (ativa, tarifaCentavos) => {
    const atual = await lerAtendimento(rotaCidade);
    return escrever(atendimento + rotaCidade, 'PUT', {
      codigoIbge: atual.codigoIbge, nome: atual.nome, uf: atual.uf, ativa, tarifaCentavos, revisao: atual.revisao,
    });
  };
  assert.equal(await alterar(false, '1300'), 200);
  assert.equal((await modalidades()).entregaPropria, false);
  assert.equal(await escrever(atendimento + rotaCidade, 'PUT', {
    codigoIbge: cidade.codigoIbge, nome: cidade.nome, uf: cidade.uf, ativa: true, tarifaCentavos: '1250', revisao: cidade.revisao,
  }), 409);
  const config = await lerAtendimento('');
  const salvarModalidades = (retiradaHabilitada, correiosHabilitados) => escrever(atendimento + '/modalidades', 'PUT', {
    retiradaHabilitada, correiosHabilitados, entregaPropriaHabilitada: true, revisao: config.revisao,
  });
  assert.equal(await salvarModalidades(false, false), 409);
  assert.equal(await salvarModalidades(true, true), 400);
  assert.equal((await lerAtendimento(rotaCidade)).tarifaCentavos, '1300');
  assert.equal(await alterar(true, '1250'), 200);
  assert.equal((await modalidades()).entregaPropria, true);
  console.log('Atendimento: tarifa editada, cidade desativada e reativada, revisão antiga e última modalidade rejeitadas.');

  const conferirWhatsApp = contexto => contexto.ev(`(() => {
    const links = [...document.querySelectorAll('a[href^="https://wa.me/"]')];
    return [links.length, links[0]?.pathname, links[0] && new URL(links[0].href).searchParams.get('text')];
  })()`);
  const whatsapp = [1, '/5554999990000', 'Olá! Gostaria de falar com a Xaquiamuni.'];
  const promocaoProduto = await escrever('/api/admin/promocoes', 'POST', {
    configuracao: { produtoId: dados.produtoFinanceiro, tipo: 'PrecoFixo', escopo: 'VariantesSelecionadas',
      variantesIds: [dados.varianteFinanceiro], valor: '1099' },
    ativa: true,
  }, true);
  assert.equal(promocaoProduto.status, 201, JSON.stringify(promocaoProduto));
  const campanha = {
    nome: 'Economia na entrega', tipo: 'Percentual', valor: '1000',
    modalidades: ['EntregaPropria'], subtotalMinimoCentavos: '1099',
  };
  const campanhaRota = await a.ev(`(async () => {
    const rota = '/api/admin/promocoes-frete';
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    const resposta = await fetch(rota, { method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken },
      body: JSON.stringify(${JSON.stringify(campanha)}),
    });
    if (resposta.status !== 201) return null;
    return rota + '/' + (await resposta.json()).id;
  })()`);
  assert.ok(campanhaRota);
  const escreverCampanha = (metodo, sufixo, dados, versao) => a.ev(`(async () => {
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    return (await fetch(${JSON.stringify(campanhaRota + sufixo)}, { method: ${JSON.stringify(metodo)},
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken,
        'If-Match': ${JSON.stringify('"' + versao + '"')} },
      body: JSON.stringify(${JSON.stringify(dados)}),
    })).status;
  })()`);
  assert.equal(await escreverCampanha('PATCH', '/status', { ativa: true }, 1), 200);

  assert.equal(await c.ev(`(async () => {
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    return (await fetch('/api/auth/logout', { method: 'POST', headers: { 'X-XSRF-TOKEN': requestToken } })).status;
  })()`), 204);
  let primeiroPedido;
  for (const width of [375, 1280]) {
    await viewport(c, width);
    await viewport(a, width);
    if (width === 1280) {
      assert.equal(await escreverCampanha('PUT', '', { ...campanha, tipo: 'FreteGratis', valor: '0' }, 2), 200);
      await abrir(c, '/produtos/' + dados.produtoFinanceiro, '!!document.querySelector("#product-name")');
      await botaoTexto('Adicionar ao carrinho', c);
    }
    await abrir(c, '/carrinho', "!!document.querySelector('a[href^=\"https://wa.me/\"]')");
    assert.deepEqual(await conferirWhatsApp(c), whatsapp);
    if (width === 375) {
      await c.ir('/checkout');
      await botaoTexto('Consultar valores de entrega', c);
      await waitFor(() => c.ev("location.pathname === '/login' && new URLSearchParams(location.search).get('retorno') === 'checkout' && !!document.querySelector('#senha')"), 'Login após iniciar cotação no checkout');
      await campo(c, '#email', dados.cliente.Email);
      await campo(c, '#senha', dados.cliente.Senha);
      await c.ev("document.querySelector('main form').requestSubmit()");
      await waitFor(() => c.ev("location.pathname === '/checkout' && !!document.querySelector('#modalidade-entrega')"), 'Retorno ao checkout com carrinho');
    }
    await abrir(c, '/checkout', "!!document.querySelector('#modalidade-entrega option[value=EntregaPropria]')");
    await waitFor(() => c.ev("!!document.querySelector('a[href^=\"https://wa.me/\"]')"), 'Contato no checkout');
    assert.deepEqual(await conferirWhatsApp(c), whatsapp);
    await campo(c, '#modalidade-entrega', 'EntregaPropria');
    await botaoTexto('Consultar valores de entrega', c);
    const totalCotado = width === 375 ? '22,24' : '10,99';
    await waitFor(() => c.ev(`document.querySelector('app-frete-pedido')?.textContent.includes('Economia na entrega')
      && [...document.querySelectorAll('main p')].some(p => p.textContent.includes('Total final:') && p.textContent.includes('${totalCotado}'))`),
      'Total com frete promocional');
    if (width === 1280) assert.ok(await c.ev("document.querySelector('app-frete-pedido')?.textContent.includes('Frete grátis')"));
    console.log(`Entrega própria em ${width} px: cotação real com desconto conferida.`);
    assert.ok(await semEstouro(c));
    if (width === 375) {
      assert.equal(await alterar(true, '1300'), 200);
      await botaoTexto('Confirmar valor e finalizar compra', c);
      await waitFor(() => c.ev("location.pathname === '/checkout' && document.querySelector('main').textContent.includes('Confira a nova cotação') && document.querySelector('main').textContent.includes('22,69')"), 'Reconfirmação após mudar a tarifa');
    }
    const perdaDesconto = width === 1280 ? c.perderResposta('/api/pedidos') : null;
    await botaoTexto('Confirmar valor e finalizar compra', c);
    if (perdaDesconto) {
      await botaoTexto('Recuperar tentativa original', c);
    }
    const id = await waitFor(() => c.ev("location.pathname.startsWith('/pedidos/') && location.pathname.split('/').at(-1)"), 'Pedido entregue pela loja');
    if (perdaDesconto) {
      assert.equal(perdaDesconto.status[0], 201);
      assert.deepEqual(perdaDesconto.envios[0], perdaDesconto.envios[1]);
    }
    const rota = '/admin/pedidos/' + id;
    const titulo = await waitFor(() => c.ev("document.querySelector('#pedido-title')?.textContent.match(/XQ-[23456789ACDEFHJKMNPQRTUVWXY]{10}/)?.[0]"),
      'Número público em entrega própria');
    const endereco = width === 375 ? 'Rua cadastrada do ensaio' : 'Rua do ensaio';
    await waitFor(() => c.ev(`document.querySelector('main')?.textContent.includes(${JSON.stringify(endereco)})`), 'Destino congelado');
    assert.deepEqual(await conferirWhatsApp(c), whatsapp);
    const pix = await c.ev(`fetch('/api/pedidos/${id}/pagamento').then(r=>r.json())`);
    const totalPedido = width === 375 ? '2269' : '1099';
    assert.equal(pix.valorCentavos, totalPedido);
    if (width === 375) {
      await c.ev(`Object.defineProperty(navigator, 'clipboard', {
        configurable: true, value: { writeText: async numero => { window.numeroCopiado = numero; } },
      })`);
      await botaoTexto('Copiar número do pedido', c);
      await waitFor(() => c.ev(`window.numeroCopiado === ${JSON.stringify(titulo)}
        && [...document.querySelectorAll('main [role=status]')].some(p => p.textContent.includes('Número do pedido copiado.'))`),
        'Número público encaminhado para cópia');
      await waitFor(() => c.ev("!!document.querySelector('app-comprovante-pix input[type=file]')"), 'Campo de comprovante');
      await c.ev(`(async () => {
        const desenho = document.createElement('canvas');
        desenho.width = desenho.height = 2;
        const blob = await new Promise(resolve => desenho.toBlob(resolve, 'image/png'));
        const transferencia = new DataTransfer();
        transferencia.items.add(new File([blob], 'entrega-teste.png', { type: 'image/png' }));
        const campo = document.querySelector('app-comprovante-pix input[type=file]');
        campo.files = transferencia.files;
        campo.dispatchEvent(new Event('change', { bubbles: true }));
      })()`);
      await botaoTexto('Enviar comprovante', c);
      await waitFor(() => c.ev("document.querySelector('app-comprovante-pix')?.textContent.includes('Baixar comprovante')"), 'Comprovante registrado');
      assert.equal(await c.ev(`fetch('/api/pedidos/${id}').then(r=>r.json()).then(p=>p.situacao)`), 'AguardandoPagamento');
      assert.ok(await a.ev(`fetch('/api/admin/financeiro/movimentacoes').then(r=>r.json()).then(p=>p.itens.every(m=>m.recursoId!=='${id}'))`));
    }
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
    for (const modo of ['light', 'dark']) {
      if (await a.ev(tema) !== modo) await alternar(modo);
      assert.ok(await a.ev(`document.querySelector('main dl')?.textContent.includes(${JSON.stringify(titulo)})`));
      assert.ok(await semEstouro(a));
      assert.ok(await a.ev("document.querySelector('app-frete-pedido')?.textContent.includes('Economia na entrega')"));
    }
    const eventos = await a.ev(`fetch('/api/admin/financeiro/movimentacoes').then(r=>r.json())
      .then(p=>p.itens.filter(m=>m.recursoId === '${id}').map(m=>m.valorCentavos))`);
    assert.deepEqual(eventos, [totalPedido]);
    assert.ok(await semEstouro(a));
    if (width === 375) {
      primeiroPedido = id;
      await abrir(c, '/minha-conta', "!!document.querySelector('.conta-dados')");
      await c.ev("document.querySelector('button[aria-label=\"Editar endereço padrão\"]').click()");
      await waitFor(() => c.ev("!!document.querySelector('#conta-logradouro')"), 'Edição de endereço padrão');
      await campo(c, '#conta-logradouro', 'Rua do ensaio');
      await c.ev("document.querySelector('.conta form').requestSubmit()");
      await waitFor(() => c.ev("!document.querySelector('.conta form') && document.querySelector('.conta-dados').textContent.includes('Rua do ensaio')"), 'Endereço atualizado');
    } else {
      await abrir(c, '/pedidos/' + primeiroPedido, "document.querySelector('main')?.textContent.includes('Rua cadastrada do ensaio')");
      const bloqueio = await a.ev(`(async () => {
        const rota = '/api/admin/produtos/${dados.produtoFinanceiro}';
        const cadastro = await fetch(rota);
        const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
        const resposta = await fetch(rota, { method: 'DELETE', headers: {
          'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID(), 'If-Match': cadastro.headers.get('ETag'),
        } });
        return [resposta.status, (await resposta.json()).codigo];
      })()`);
      assert.deepEqual(bloqueio, [409, 'variante_com_historico_comercial']);
      const revisaoCidade = (await lerAtendimento(rotaCidade)).revisao;
      const bloqueioCidade = await a.ev(`(async () => {
        const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
        return (await fetch(${JSON.stringify(atendimento + rotaCidade + '?revisao=' + revisaoCidade)}, {
          method: 'DELETE', headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID() },
        })).status;
      })()`);
      assert.equal(bloqueioCidade, 409);
      assert.equal((await lerAtendimento(rotaCidade)).revisao, revisaoCidade);
    }
    console.log(`Entrega própria em ${width} px: endereço, frete, total e ações administrativas confirmados.`);
  }
  const pedidoTema = await c.ev("(async () => (await (await fetch('/api/pedidos')).json()).itens[0])()");
  for (const chavePublica of temas) {
    const { publicacao } = await a.ev("fetch('/api/admin/loja/tema').then(r => r.json())");
    if (publicacao.chave !== chavePublica) {
      assert.equal(await escrever('/api/admin/loja/tema', 'PUT', {
        chave: chavePublica, revisaoEsperada: publicacao.revisao,
      }), 200);
    }
    for (const width of [375, 1280]) {
      await viewport(c, width);
      await abrir(c, '/pedidos/' + pedidoTema.id, `document.querySelector('app-store-layout')?.dataset.theme === '${chavePublica}'
        && document.querySelector('#pedido-title')?.textContent.includes('${pedidoTema.numeroPublico}')
        && document.querySelector('app-frete-pedido')?.textContent.includes('Frete grátis')`);
      assert.ok(await semEstouro(c), 'Número público e desconto em ' + chavePublica + ' a ' + width + ' px');
    }
  }
  console.log('Desconto congelado conferido nos seis temas em 375 e 1280 px.');
  const rotaAtendimento = '/admin/configuracoes/atendimento';
  const acaoCidade = nome => a.ev(`(() => {
    const linha = [...document.querySelectorAll('main section span')].find(s => s.textContent.startsWith(${JSON.stringify(nome + '/RS')}));
    const botao = [...linha.parentElement.querySelectorAll('button')].find(b => b.textContent.trim() === 'Excluir');
    botao.focus();
    botao.click();
  })()`);
  const cotacaoAntesDaInativacao = await c.ev(`(async () => {
    const pedido = await fetch('/api/pedidos/${pedidoTema.id}').then(r => r.json());
    const itens = pedido.itens.map(({ produtoId, produtoVarianteId }) => ({ produtoId, produtoVarianteId, quantidade: 1 }));
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    const { codigoIbge, ...endereco } = pedido.entrega.destino;
    const resposta = await fetch('/api/checkout/cotacoes-entrega', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken },
      body: JSON.stringify({ itens, endereco }),
    });
    const { ofertas } = await resposta.json();
    return { itens, ofertaEntregaId: ofertas.find(o => o.modalidade === 'EntregaPropria')?.id };
  })()`);
  assert.ok(cotacaoAntesDaInativacao.ofertaEntregaId);
  for (const [width, modo] of [[375, 'dark'], [1280, 'light']]) {
    await viewport(a, width);
    const nome = 'Porto Alegre';
    await abrirAdmin(rotaAtendimento);
    await waitFor(() => a.ev("!!document.querySelector('[name=uf] option[value=RS]')"), 'catálogo de estados');
    await a.ev(`(() => {
      const uf = document.querySelector('[name=uf]');
      uf.value = 'RS';
      uf.dispatchEvent(new Event('change', { bubbles: true }));
    })()`);
    await waitFor(() => a.ev("!!document.querySelector('[name=cidade] option[value=\"4314902\"]')"), 'municípios do RS');
    await a.ev(`(() => {
      const municipio = document.querySelector('[name=cidade]');
      municipio.value = '4314902';
      municipio.dispatchEvent(new Event('change', { bubbles: true }));
      const tarifa = document.querySelector('[name=tarifa]');
      tarifa.value = '12,50';
      tarifa.dispatchEvent(new Event('input', { bubbles: true }));
      document.querySelector('#form-cidade button[type=submit], #form-cidade button:not([type])').click();
    })()`);
    await waitFor(() => a.ev(`document.querySelector('main').textContent.includes(${JSON.stringify(nome)})`), 'cidade descartável');
    await waitFor(() => a.ev("document.querySelector('main').textContent.includes('Cidade salva.')"), 'cidade cadastrada');
    assert.equal((await lerAtendimento('/cidades')).find(cidade => cidade.nome === nome)?.tarifaCentavos, '1250');
    if (await a.ev(tema) !== modo) await alternar(modo);
    assert.ok(await semEstouro(a));
    await a.ev(`[...document.querySelectorAll('main section span')].find(s => s.textContent.startsWith(${JSON.stringify(nome + '/RS')}))
      .parentElement.querySelector('button[aria-label^="Ativar"]').focus()`);
    await call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
    await call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, a.sessionId);
    assert.ok(await a.ev("document.activeElement.textContent.trim() === 'Excluir' && document.activeElement.matches(':focus-visible')"));
    await a.ev("window.confirm = texto => { window.confirmacaoCidade = texto; return false; }");
    await acaoCidade(nome);
    assert.ok(await a.ev(`window.confirmacaoCidade.includes(${JSON.stringify(nome)}) && window.confirmacaoCidade.includes('permanente')`));
    assert.ok((await lerAtendimento('/cidades')).some(x => x.nome === nome));
    await a.ev("window.confirm = () => true");
    await acaoCidade(nome);
    await waitFor(() => a.ev("document.querySelector('main').textContent.includes('Cidade excluída.')"), 'exclusão concluída');
    await abrirAdmin(rotaAtendimento);
    assert.ok(!(await lerAtendimento('/cidades')).some(x => x.nome === nome));
  }
  await waitFor(() => a.ev("document.querySelector('main').textContent.includes('Vacaria/RS')"), 'histórico disponível');
  await a.ev("window.confirm = () => true");
  await acaoCidade('Vacaria');
  await waitFor(() => a.ev("document.querySelector('[role=alert]')?.textContent.includes('Há pedidos vinculados')"), 'histórico bloqueia exclusão');
  await a.ev("window.confirm = () => false");
  await botaoTexto('Desabilitar atendimento em Vacaria', a);
  assert.equal((await lerAtendimento(rotaCidade)).ativa, true);
  await a.ev("window.confirm = () => true");
  await botaoTexto('Desabilitar atendimento em Vacaria', a);
  await waitFor(async () => !(await modalidades()).entregaPropria, 'cidade desabilitada');
  assert.equal((await lerAtendimento(rotaCidade)).ativa, false);
  assert.equal(await c.ev(`fetch('/api/pedidos/${pedidoTema.id}').then(r=>r.json()).then(p=>p.situacao)`), 'Entregue');
  const usoOfertaAntiga = await c.ev(`(async () => {
    const totalPedidos = async () => (await fetch('/api/pedidos').then(r => r.json())).itens.length;
    const antes = await totalPedidos();
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    const resposta = await fetch('/api/pedidos', { method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify(${JSON.stringify(cotacaoAntesDaInativacao)}),
    });
    return [resposta.status, (await resposta.json()).codigo, antes === await totalPedidos()];
  })()`);
  assert.deepEqual(usoOfertaAntiga, [409, 'oferta_entrega_invalida', true]);
  assert.equal(await escrever(atendimento + '/cidades', 'POST', {
    codigoIbge: '4314902', nome: 'Porto Alegre', uf: 'RS', ativa: true, tarifaCentavos: '2000',
  }), 201);
  const outrasModalidades = await c.ev(`(async () => {
    const { requestToken } = await fetch('/api/auth/csrf').then(r => r.json());
    const resposta = await fetch('/api/checkout/cotacoes-entrega', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': requestToken },
      body: JSON.stringify({ itens: ${JSON.stringify(cotacaoAntesDaInativacao.itens)},
        endereco: { cep: '90000000', uf: 'RS', cidade: 'Porto Alegre', bairro: 'Centro', logradouro: 'Rua Teste', numero: '1' } }),
    });
    return (await resposta.json()).ofertas.map(o => [o.modalidade, o.freteCentavos]);
  })()`);
  assert.deepEqual(outrasModalidades, [['RetiradaLocal', '0'], ['EntregaPropria', '0']]);
  console.log('Exclusão, bloqueio por histórico e desabilitação conferidos no ADMIN.');
  const atual = await lerAtendimento('');
  assert.equal(await escrever(atendimento + '/whatsapp', 'PUT', {
    numero: atual.whatsApp, confirmado: true, habilitado: false, revisao: atual.revisao,
  }), 200);
  await abrir(c, '/', '!!document.querySelector("app-contato-whatsapp")');
  assert.deepEqual(await c.ev("fetch('/api/loja/contato').then(r=>r.json())"), { whatsApp: null });
  assert.equal((await conferirWhatsApp(c))[0], 0);
  console.log('WhatsApp: publicação, destino normalizado, mensagem genérica e remoção após desativar.');
}
