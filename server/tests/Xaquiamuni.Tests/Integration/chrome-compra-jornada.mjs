import assert from 'node:assert/strict';

export async function validarJornada({ abrir, waitFor, checkpoint, call, cliente, admin, outroCliente, seed, pixManual, pixCnpj, origin, demonstracao }) {
  const c = await abrir(pixCnpj ? '/login' : '/cadastro');
  const a = await abrir('/admin/login');
  let etapa = 'cadastro';
  const pronto = (b, expr, nome) => waitFor(() => b.ev(expr), nome);
  const tem = (b, texto) => pronto(b,
    'document.querySelector("main")?.textContent.includes(' + JSON.stringify(texto) + ')', texto);
  async function clicar(b, texto, seletor = 'main button, main a') {
    await pronto(b, `(() => {
      const alvo = [...document.querySelectorAll(${JSON.stringify(seletor)})].find(
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
  async function api(b, url, method = 'GET', body, chave, usarCsrf = true) {
    return b.ev(`(async () => {
      const headers = {};
      const method = ${JSON.stringify(method)};
      const key = ${JSON.stringify(chave ?? null)};
      if (method === 'POST' || method === 'PUT') {
        const csrf = await (await fetch('/api/auth/csrf')).json();
        headers['Content-Type'] = 'application/json';
        if (${usarCsrf}) headers['X-XSRF-TOKEN'] = csrf.requestToken;
      }
      if (key) headers['Idempotency-Key'] = key;
      const r = await fetch(${JSON.stringify(url)}, { method, headers, body: ${JSON.stringify(body ?? null)} });
      return { status: r.status, body: await r.json().catch(() => null), cache: r.headers.get('Cache-Control') };
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
    const payload = JSON.stringify({ itens: [{ produtoId: seed.produto, produtoVarianteId: seed.variante, quantidade,
      ...(demonstracao ? { precoAceitoCentavos: '1390' } : {}) }] });
    const chave = await c.ev('crypto.randomUUID()');
    const r = await api(c, '/api/pedidos', 'POST', payload, chave);
    assert.equal(r.status, 201);
    return r.body.id;
  }
  async function postAdmin(id, acao, dados) {
    return api(a, rotaAdmin(id) + '/' + acao, 'POST', JSON.stringify(dados));
  }

  async function verificarPixInativo(id) {
    await tem(c, 'O pagamento não está disponível para a situação atual do pedido.');
    assert.equal(await c.ev('!document.querySelector("app-pedido-pix > section button")'), true);
    const r = await api(c, '/api/pedidos/' + id + '/pagamento');
    assert.equal(r.status, 200);
    assert.match(r.cache ?? '', /\bno-store\b/i);
    assert.deepEqual([r.body?.situacao, r.body?.pix], ['pagamento_indisponivel', null]);
  }

  const cnpjNormalizado = '00ABC34501DE56';
  const nomeRecebedorCnpj = 'Recebedor sintético da demonstração';
  async function configurarCnpj() {
    const config = '/api/admin/configuracoes/pix-manual';
    const tela = '/admin/configuracoes/pix-manual';
    const dados = { tipoChave: 'Cnpj', chavePix: '00.abc.345/01de-56',
      nomeRecebedor: nomeRecebedorCnpj, instrucoesAdicionais: 'Simulação. Não faça transferências.' };
    await a.ir(tela);
    await pronto(a, 'document.querySelector("#pix-tipo")?.value === "Email"', 'PIX inicial da demonstração');
    assert.equal((await api(c, config, 'PUT', JSON.stringify(dados))).status, 403);
    assert.equal((await api(a, config, 'PUT', JSON.stringify(dados), null, false)).status, 400);
    await a.ev(`(() => {
      const tipo = document.querySelector('#pix-tipo');
      tipo.value = 'Cnpj';
      tipo.dispatchEvent(new Event('change', { bubbles: true }));
    })()`);
    const { tipoChave, ...campos } = dados;
    await preencher(a, campos);
    await clicar(a, 'Salvar configuração');
    await tem(a, 'Configuração PIX salva.');
    const persistido = await api(a, config);
    assert.equal(persistido.status, 200);
    assert.match(persistido.cache ?? '', /\bno-store\b/i);
    assert.ok(persistido.body.tipoChave === tipoChave && persistido.body.chavePix === cnpjNormalizado,
      'API deve retornar CNPJ sem pontuação e com zeros à esquerda');
    await a.ir(tela);
    await pronto(a, `document.querySelector('#pix-tipo')?.value === 'Cnpj'
      && document.querySelector('#pix-chave')?.value === ${JSON.stringify(cnpjNormalizado)}`, 'CNPJ após recarregar configuração');
    console.log('PIX CNPJ: cadastro formatado, normalização, zeros à esquerda, recarga, ADMIN e CSRF conferidos.');
  }

  async function verHistorico(id, situacao) {
    await c.ir('/produtos');
    await clicar(c, 'Meus pedidos', 'header .store-account__link');
    const link = `app-meus-pedidos a[href^="/pedidos/${id}"]`;
    await pronto(c, `(() => {
      const item = document.querySelector(${JSON.stringify(link)})?.closest('li');
      return location.pathname === '/pedidos' && item?.textContent.includes(${JSON.stringify(situacao)})
        && item.textContent.includes('27,80');
    })()`, 'Pedido e situação no histórico');
    const pagina = await api(c, '/api/pedidos?pagina=1');
    assert.deepEqual([pagina.status, pagina.body?.totalItens, pagina.body?.itens?.[0]?.id], [200, 1, id]);
    await c.ev('document.querySelector(' + JSON.stringify(link) + ').click()');
    await pronto(c, 'location.pathname === ' + JSON.stringify('/pedidos/' + id), 'Detalhe a partir do histórico');
    await tem(c, situacao);
  }

  try {
    if (!pixCnpj) {
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
    }
    etapa = 'logins e isolamento';
    await entrar(c, cliente);
    await entrar(a, admin, true);
    assert.equal((await api(c, '/api/admin/pedidos')).status, 403);
    for (const b of [c, a]) {
      const { cookies } = await call('Network.getCookies', {}, b.sessionId);
      assert.equal(cookies.some(cookie => cookie.name === '__Host-Xaquiamuni.Auth' && cookie.secure && cookie.httpOnly && cookie.sameSite === 'Lax'), true);
    }

    if (pixCnpj) {
      etapa = 'configuração CNPJ';
      await configurarCnpj();
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

    if (pixManual) {
      etapa = 'PIX ausente e configuração administrativa';
      const config = '/api/admin/configuracoes/pix-manual';
      const pagamento = '/api/pedidos/' + id + '/pagamento';
      const tela = '/admin/configuracoes/pix-manual';
      const exemplo = {
        tipoChave: 'Aleatoria', chavePix: '5fba3231-744a-4cc4-bc54-c958bb1cc890',
        nomeRecebedor: 'Recebedor sintético', instrucoesAdicionais: 'Instrução sintética inicial',
      };
      const atual = { ...exemplo, chavePix: 'e3862bc7-5bdf-4a07-8268-0ba542a1366f',
        instrucoesAdicionais: 'Instrução sintética atualizada' };
      function semCache(r, status) {
        assert.equal(r.status, status);
        assert.ok(r.cache?.split(',').some(v => v.trim().toLowerCase() === 'no-store'));
        return r.body;
      }
      await tem(c, 'A loja ainda não disponibilizou as instruções de pagamento.');
      const ausente = semCache(await api(c, pagamento), 200);
      const pedido = (await api(c, '/api/pedidos/' + id)).body;
      assert.deepEqual([ausente.situacao, ausente.valorCentavos, ausente.prazoPagamentoEmUtc, ausente.pix],
        ['configuracao_ausente', '2780', pedido.prazoPagamentoEmUtc, null]);
      assert.equal(await c.ev('!document.querySelector("app-pedido-pix > section button")'), true);
      semCache(await api(a, config), 404);
      await a.ir(tela);
      await tem(a, 'A loja ainda não tem uma configuração PIX.');
      assert.equal(await a.ev('[...document.querySelectorAll("main [formControlName]")].length === 4 && [...document.querySelectorAll("main [formControlName]")].every(e => e.value === "")'), true);
      const corpo = JSON.stringify(exemplo);
      semCache(await api(a, config, 'PUT', corpo, null, false), 400);
      semCache(await api(c, config, 'PUT', corpo), 403);
      semCache(await api(a, config), 404);

      async function salvar(dados) {
        await a.ev(`(() => {
          const tipo = document.querySelector('#pix-tipo');
          tipo.value = 'Aleatoria';
          tipo.dispatchEvent(new Event('change', { bubbles: true }));
        })()`);
        const { tipoChave, ...campos } = dados;
        await preencher(a, campos);
        await clicar(a, 'Salvar configuração');
        await tem(a, 'Configuração PIX salva.');
        const persisted = semCache(await api(a, config), 200);
        assert.ok(Object.entries(dados).every(([campo, valor]) => persisted[campo] === valor));
      }
      await salvar(exemplo);
      await a.ir(tela);
      await pronto(a, 'document.querySelector("#pix-chave")?.value === ' + JSON.stringify(exemplo.chavePix), 'Persistência do PIX após recarregar');
      await salvar(atual);

      etapa = 'PIX disponível, falha temporária e cópia';
      c.falharProximaConsultaPix(pagamento);
      await c.ir(caminho);
      await tem(c, 'Não foi possível consultar as instruções de pagamento agora.');
      await clicar(c, 'Tentar novamente');
      await tem(c, 'Copiar chave PIX');
      const disponivel = semCache(await api(c, pagamento), 200);
      assert.deepEqual([disponivel.situacao, disponivel.valorCentavos, disponivel.prazoPagamentoEmUtc],
        ['disponivel', '2780', pedido.prazoPagamentoEmUtc]);
      assert.ok(Object.entries(atual).every(([campo, valor]) => disponivel.pix?.[campo] === valor));
      assert.equal(await c.ev(`(() => {
        const texto = document.querySelector('app-pedido-pix').textContent;
        const prazo = document.querySelector('main time');
        const d = new Date(${JSON.stringify(pedido.prazoPagamentoEmUtc)});
        const pad = v => String(v).padStart(2, '0');
        const local = pad(d.getDate()) + '/' + pad(d.getMonth()+1) + '/' + d.getFullYear()
          + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
        return ['27,80', 'Aleatória', ...${JSON.stringify(Object.values(atual).slice(1))}].every(v => texto.includes(v))
          && prazo.dateTime === ${JSON.stringify(pedido.prazoPagamentoEmUtc)} && prazo.textContent.trim() === local;
      })()`), true);
      let clipboardPermitido = true;
      try {
        await call('Browser.grantPermissions', { origin, browserContextId: c.browserContextId,
          permissions: ['clipboardReadWrite', 'clipboardSanitizedWrite'] });
      } catch { clipboardPermitido = false; }
      if (clipboardPermitido) {
        await call('Page.bringToFront', {}, c.sessionId);
        await clicar(c, 'Copiar chave PIX');
        await tem(c, 'Chave PIX copiada.');
        assert.equal(await c.ev('navigator.clipboard.readText().then(v => v === ' + JSON.stringify(atual.chavePix) + ')'), true);
      }
      console.log(clipboardPermitido ? 'PIX: cópia conferida no Chrome.' : 'PIX: Chrome recusou permissão de área de transferência.');
      await checkpoint('pedido', id);
      await estoque(7, 2, 5);
      console.log('PIX: ausência, cadastro, atualização, recarga, CSRF, valor, prazo local, no-store e retry conferidos.');
      etapa = 'comprovante PIX';
      const anexo = '/api/pedidos/' + id + '/comprovante-pix';
      const painel = 'app-comprovante-pix';
      const largura = (b, width) => call('Emulation.setDeviceMetricsOverride',
        { width, height: 900, mobile: false, deviceScaleFactor: 1 }, b.sessionId);
      const legivel = browser => browser.ev(`(() => {
        const painel = document.querySelector('${painel} section');
        if (!painel) return false;
        const estilo = getComputedStyle(painel);
        const luminancia = cor => (cor.match(/[0-9]+/g) ?? []).slice(0, 3)
          .map(v => Number(v) / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4)
          .reduce((total, v, i) => total + v * [.2126, .7152, .0722][i], 0);
        const [escuro, claro] = [luminancia(estilo.color), luminancia(estilo.backgroundColor)].sort((a,b)=>a-b);
        return (claro + .05) / (escuro + .05) >= 4.5 && document.documentElement.scrollWidth <= innerWidth;
      })()`);
      async function escolher(nome, cor) {
        await pronto(c, `!!document.querySelector('${painel} input[type=file]')`, 'Campo de comprovante');
        await c.ev(`(async () => {
          const canvas = document.createElement('canvas');
          canvas.width = 2; canvas.height = 2;
          const ctx = canvas.getContext('2d');
          ctx.fillStyle = ${JSON.stringify(cor)}; ctx.fillRect(0, 0, 2, 2);
          const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
          const campo = document.querySelector('${painel} input');
          const transferencia = new DataTransfer();
          transferencia.items.add(new File([blob], ${JSON.stringify(nome)}, { type: 'image/png' }));
          campo.files = transferencia.files;
          campo.dispatchEvent(new Event('change', { bubbles: true }));
        })()`);
      }
      async function baixar(browser) {
        await browser.ev(`(() => {
          const criar = URL.createObjectURL;
          URL.createObjectURL = blob => {
            document.documentElement.dataset.downloadComprovante = blob.type + ':' + blob.size;
            URL.createObjectURL = criar;
            return criar.call(URL, blob);
          };
        })()`);
        await clicar(browser, 'Baixar comprovante', painel + ' button');
        await pronto(browser, "document.documentElement.dataset.downloadComprovante?.startsWith('image/png:')", 'Download privado');
      }
      await largura(c, 375);
      await escolher('comprovante.png', 'green');
      await clicar(c, 'Enviar comprovante', painel + ' button');
      await tem(c, 'Comprovante enviado — aguardando conferência');
      const primeiro = (await api(c, anexo)).body;
      await largura(c, 1280);
      await escolher('substituto.png', 'blue');
      await clicar(c, 'Substituir comprovante', painel + ' button');
      await clicar(c, 'Confirmar substituição', painel + ' button');
      await pronto(c, `fetch(${JSON.stringify(anexo)}).then(r => r.json()).then(d => d.versao !== ${JSON.stringify(primeiro.versao)})`, 'Versão atualizada');
      await baixar(c);
      assert.equal((await api(c, '/api/pedidos/' + id)).body.situacao, 'AguardandoPagamento');
      await a.ir('/admin/pedidos/' + id);
      await tem(a, 'Comprovante PIX recebido');
      for (const width of [375, 1280]) {
        await largura(a, width);
        for (const modo of ['light', 'dark']) {
          await a.ev(`(() => {
            const shell = document.querySelector('.admin-shell');
            if (shell.dataset.adminTheme !== '${modo}') shell.querySelector('.admin-theme-toggle').click();
          })()`);
          await pronto(a, `document.querySelector('.admin-shell')?.dataset.adminTheme === '${modo}'`, 'Modo ADMIN');
          assert.ok(await legivel(a), 'Contraste ou largura do comprovante ADMIN');
          assert.ok(await a.ev(`!document.querySelector('${painel} input')`));
        }
      }
      await baixar(a);
      const temaUrl = '/api/admin/loja/tema';
      const temaInicial = (await api(a, temaUrl)).body.publicacao;
      let publicada = temaInicial;
      for (const tema of ['original', 'natal', 'black-friday', 'pascoa', 'ano-novo', 'dia-das-maes']) {
        if (publicada.chave !== tema) {
          const troca = await api(a, temaUrl, 'PUT', JSON.stringify({ chave: tema, revisaoEsperada: publicada.revisao }),
            await a.ev('crypto.randomUUID()'));
          assert.equal(troca.status, 200);
          publicada = troca.body;
        }
        for (const width of [375, 1280]) {
          await largura(c, width);
          await c.ir(caminho);
          await pronto(c, `document.querySelector('app-store-layout')?.dataset.theme === '${tema}'`, tema);
          await tem(c, 'Comprovante enviado — aguardando conferência');
          assert.ok(await legivel(c), 'Contraste ou largura em ' + tema);
        }
      }
      assert.equal((await api(a, temaUrl, 'PUT', JSON.stringify({ chave: temaInicial.chave, revisaoEsperada: publicada.revisao }),
        await a.ev('crypto.randomUUID()'))).status, 200);
      console.log('Comprovantes PIX: envio, substituição e downloads reais, seis temas em 375 e 1280 px e ADMIN nos dois modos.');

    }

    if (pixCnpj) {
      etapa = 'histórico e PIX CNPJ';
      const antes = (await api(c, '/api/pedidos/' + id)).body;
      const enviosAntes = c.compra().total;
      await verHistorico(id, 'Aguardando pagamento');
      await tem(c, nomeRecebedorCnpj);
      const pix = await api(c, '/api/pedidos/' + id + '/pagamento');
      assert.deepEqual([pix.status, pix.body?.valorCentavos, pix.body?.prazoPagamentoEmUtc],
        [200, '2780', antes.prazoPagamentoEmUtc]);
      assert.ok(pix.body.pix.tipoChave === 'Cnpj' && pix.body.pix.chavePix === cnpjNormalizado
        && pix.body.pix.nomeRecebedor === nomeRecebedorCnpj, 'Instruções CNPJ incompatíveis');
      assert.equal(await c.ev(`(() => {
        const painel = document.querySelector('app-pedido-pix');
        const prazo = document.querySelector('main time');
        const data = new Date(${JSON.stringify(antes.prazoPagamentoEmUtc)});
        const pad = v => String(v).padStart(2, '0');
        const local = pad(data.getDate()) + '/' + pad(data.getMonth() + 1) + '/' + data.getFullYear()
          + ' ' + pad(data.getHours()) + ':' + pad(data.getMinutes());
        return ['27,80', 'CNPJ', ${JSON.stringify(cnpjNormalizado)}].every(v => painel.textContent.includes(v))
          && prazo.dateTime === ${JSON.stringify(antes.prazoPagamentoEmUtc)} && prazo.textContent.trim() === local;
      })()`), true);
      await call('Browser.grantPermissions', { origin, browserContextId: c.browserContextId,
        permissions: ['clipboardReadWrite', 'clipboardSanitizedWrite'] });
      await call('Page.bringToFront', {}, c.sessionId);
      await clicar(c, 'Copiar chave PIX');
      await tem(c, 'Chave PIX copiada.');
      assert.ok(await c.ev('navigator.clipboard.readText().then(v => v === ' + JSON.stringify(cnpjNormalizado) + ')'),
        'A área de transferência deve conter o CNPJ completo');
      assert.equal(c.compra().total, enviosAntes);
      await checkpoint('pedido', id);
      await estoque(7, 2, 5);
      console.log('Meus pedidos: detalhe reencontrado, valor e prazo conferidos, CNPJ copiado e nenhum novo POST.');
    }

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
    if (pixManual || pixCnpj) {
      etapa = 'PIX após pagamento manual';
      await c.ir(caminho);
      await verificarPixInativo(id);
      if (pixManual) {
        await tem(c, 'Baixar comprovante');
        assert.ok(await c.ev("!document.querySelector('app-comprovante-pix input')"));
      }
    }
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

    if (pixCnpj) {
      etapa = 'histórico entregue e isolamento';
      const enviosAntes = c.compra().total;
      await verHistorico(id, 'Entregue');
      await verificarPixInativo(id);
      assert.equal(c.compra().total, enviosAntes);
      const alheio = await api(outro, '/api/pedidos/' + id + '/pagamento');
      assert.equal(alheio.status, 404);
      await outro.ir('/produtos');
      await clicar(outro, 'Meus pedidos', 'header .store-account__link');
      await tem(outro, 'Você ainda não tem pedidos.');
      assert.equal(await outro.ev('!document.querySelector("app-meus-pedidos ul")'), true);
      console.log('Meus pedidos: Entregue após baixa única, PIX oculto e nenhum pedido visível para outro cliente.');
      return;
    }

    if (pixManual) {
      etapa = 'PIX de pedido alheio e pedido cancelado';
      const r = await api(outro, '/api/pedidos/' + id + '/pagamento');
      assert.equal(r.status, 404);
      assert.match(r.cache ?? '', /\bno-store\b/i);
      const cancelado = await novoPedido();
      assert.equal((await postAdmin(cancelado, 'cancelar', { motivo: 'Cancelamento sintético E2E' })).status, 200);
      await c.ir('/pedidos/' + cancelado);
      await verificarPixInativo(cancelado);
    }

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
    if (pixManual) {
      await c.ir('/pedidos/' + expirado);
      await verificarPixInativo(expirado);
      console.log('PIX: pagamento confirmado, cancelamento, expiração e isolamento conferidos.');
    }
    await estoque(5, 2, 3);
    console.log('Jornada Chrome/Angular/API/SQL e cenários negativos concluídos.');
  } catch (erro) {
    throw new Error('Checkpoint ' + etapa + ': ' + erro.message);
  }
}
