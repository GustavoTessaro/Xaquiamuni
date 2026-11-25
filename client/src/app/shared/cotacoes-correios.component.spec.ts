import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { DetalheCotacao } from '../core/cotacoes-correios.service';
import { CheckoutService } from '../public-store/checkout.service';
import { CotacoesCorreiosComponent } from './cotacoes-correios.component';

@Component({ template: '' })
class DestinoTeste {}

describe('atalho para o pedido convertido em Minhas cotações', () => {
  const id = '742bb152-908a-4b13-88db-92829280cb3d';
  const pedidoId = 'e2d2d73d-e96b-4592-b85f-204b448eef61';
  const caminho = '/cotacoes-correios/' + id;
  const rota = '/api' + caminho;
  const detalhe: DetalheCotacao = {
    resumo: { id, situacao: 'ConvertidaEmPedido', criadaEmUtc: '2026-11-23T10:00:00Z', revisao: 1, validaAteUtc: null },
    itens: [],
    endereco: { cep: '95200000', uf: 'RS', cidade: 'Vacaria', bairro: 'Centro', logradouro: 'Rua A', numero: '1', complemento: null },
    propostas: [],
    revisaoAceita: 1,
    pedidoConvertidoId: pedidoId,
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideRouter([
          { path: 'cotacoes-correios/:id', component: CotacoesCorreiosComponent },
          { path: 'admin/cotacoes-correios/:id', component: CotacoesCorreiosComponent, data: { admin: true } },
          { path: 'pedidos/:id', component: DestinoTeste },
        ]),
        { provide: CheckoutService, useValue: {
          atualizarTentativa: vi.fn(), pendente: signal(null), enviando: signal(false),
        } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function responder(harness: RouterTestingHarness, cotacao: DetalheCotacao) {
    http.expectOne(rota).flush(cotacao);
    await Promise.resolve();
    harness.detectChanges();
  }

  function link(harness: RouterTestingHarness) {
    return [...harness.routeNativeElement!.querySelectorAll('a')].find(a => a.textContent.trim() === 'Ver meu pedido');
  }

  it('abre o pedido informado pela API após recriar a tela com armazenamento vazio', async () => {
    const armazenamentos = [localStorage, sessionStorage];
    armazenamentos.forEach(armazenamento => armazenamento.clear());
    const harness = await RouterTestingHarness.create(caminho);
    await responder(harness, detalhe);
    expect(link(harness)?.getAttribute('href')).toBe('/pedidos/' + pedidoId);
    await harness.navigateByUrl('/pedidos/' + pedidoId);
    await harness.navigateByUrl(caminho);
    await responder(harness, detalhe);
    const atalho = link(harness)!;
    expect(atalho.getAttribute('href')).toBe('/pedidos/' + pedidoId);
    atalho.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/pedidos/' + pedidoId));
    armazenamentos.forEach(armazenamento => expect(armazenamento.length).toBe(0));
  });

  it('mantém a ação da cotação aceita até a conversão', async () => {
    const harness = await RouterTestingHarness.create(caminho);
    await responder(harness, { ...detalhe, resumo: { ...detalhe.resumo, situacao: 'Aceita' } });
    expect(link(harness)).toBeUndefined();
    expect(harness.routeNativeElement!.textContent).toContain('Continuar para pagamento');
  });

  it('exibe os valores congelados da revisão e conserva a ação de aceite', async () => {
    const harness = await RouterTestingHarness.create(caminho);
    await responder(harness, {
      ...detalhe, resumo: { ...detalhe.resumo, situacao: 'PropostaDisponivel' }, revisaoAceita: null,
      propostas: [{ revisao: 1, servico: 'PAC', prazoDias: 3, pesoTotalGramas: 300, volumes: [],
        publicadaEmUtc: '2026-11-23T10:00:00Z', validaAteUtc: '2026-11-24T10:00:00Z', freteCentavos: '0',
        valores: { freteCentavos: '0', subtotalProdutosCentavos: '1199', totalCentavos: '1199',
          descontoFrete: { campanhaId: 'campanha', nomeCampanha: 'Oferta postal', originalCentavos: '1290', descontoCentavos: '1290' } },
      }],
    });
    const tela = harness.routeNativeElement!;
    for (const texto of ['Frete grátis', '12,90', 'Oferta postal', 'Total publicado', '11,99', 'Aceitar proposta']) {
      expect(tela.textContent).toContain(texto);
    }
  });

  it('encaminha a Meus pedidos se a cotação convertida estiver sem vínculo', async () => {
    const harness = await RouterTestingHarness.create(caminho);
    await responder(harness, { ...detalhe, pedidoConvertidoId: null });
    expect(link(harness)).toBeUndefined();
    expect(harness.routeNativeElement!.querySelector('a[href="/pedidos"]')?.textContent).toContain('Meus pedidos');
  });

  it('mostra o erro da consulta sem disponibilizar um atalho', async () => {
    const harness = await RouterTestingHarness.create(caminho);
    http.expectOne(rota).flush({}, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    harness.detectChanges();
    expect(link(harness)).toBeUndefined();
    expect(harness.routeNativeElement!.querySelector('[role="alert"]')?.textContent).toContain('Cotação indisponível');
  });

  it('separa a prévia do envio e exige nova prévia após editar o prazo', async () => {
    const harness = await RouterTestingHarness.create('/admin' + caminho);
    const endereco = '/api/admin' + caminho;
    http.expectOne(endereco).flush({
      ...detalhe, resumo: { ...detalhe.resumo, situacao: 'AguardandoAnalise' }, revisaoAceita: null,
    }, { headers: { ETag: '"1"' } });
    await harness.fixture.whenStable();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    const tela = harness.routeNativeElement!;
    const preencher = async (nome: string, valor: string) => {
      const input = tela.querySelector<HTMLInputElement>(`input[name="${nome}"]`)!;
      input.value = valor;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      harness.detectChanges();
      await harness.fixture.whenStable();
      harness.detectChanges();
    };
    await preencher('servico', 'PAC');
    await preencher('frete', '12,90');
    await preencher('prazo', '3');
    await preencher('pesoTotal', '200');
    const botao = (titulo: string) => [...tela.querySelectorAll('button')].find(b => b.textContent.trim() === titulo);
    await harness.fixture.whenStable();
    harness.detectChanges();
    tela.querySelector('main form.ficha-grade')!.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
    harness.detectChanges();
    expect(tela.querySelector('main [role="alert"]')?.textContent).toBeUndefined();
    await vi.waitFor(() => http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' }));
    const previa = http.expectOne(endereco + '/propostas/previa');
    expect(previa.request.headers.get('If-Match')).toBe('"1"');
    expect(previa.request.body).toMatchObject({ freteCentavos: '1290', pesoTotalGramas: 200 });
    previa.flush({ valores: { freteCentavos: '990', subtotalProdutosCentavos: '1000', totalCentavos: '1990',
      descontoFrete: { nomeCampanha: 'Frete parcial', originalCentavos: '1290', descontoCentavos: '300' } } });
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(tela.textContent).toContain('19,90');
    expect(botao('Enviar proposta ao cliente')).toBeDefined();
    http.expectNone(endereco + '/propostas');
    await preencher('prazo', '4');
    expect(botao('Enviar proposta ao cliente')).toBeUndefined();
    expect(tela.querySelector('a[target="_blank"]')?.getAttribute('href')).toBe('https://www2.correios.com.br/sistemas/precosprazos/');
  });
});
