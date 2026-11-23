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
});
