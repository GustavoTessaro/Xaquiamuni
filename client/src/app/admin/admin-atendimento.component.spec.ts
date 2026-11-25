import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import type { ConfiguracaoAtendimento } from '../core/atendimento.service';
import { AdminAtendimentoComponent, tarifaEmCentavos } from './admin-atendimento.component';
import { TemaAdminService } from './tema-admin.service';

describe('Entregas e fretes ADMIN', () => {
  const base = '/api/admin/loja/atendimento';
  const config: ConfiguracaoAtendimento = {
    retiradaHabilitada: true, entregaPropriaHabilitada: false, enderecoRetirada: null, instrucoesRetirada: null,
    whatsApp: null, whatsAppConfirmado: false, whatsAppHabilitado: false, revisao: 0,
  };
  const cidade = { id: 'cidade-teste', nome: 'Vacaria', codigoIbge: '4322509', uf: 'RS', tarifaCentavos: '1250', ativa: false, revisao: 1 };

  it('converte tarifa e mantém os centavos exatos', () => {
    expect(tarifaEmCentavos('12,5')).toBe('1250');
    expect(tarifaEmCentavos('0')).toBe('0');
    expect(tarifaEmCentavos('92233720368547758,07')).toBe('9223372036854775807');
    expect(tarifaEmCentavos('92233720368547758,08')).toBeNull();
    expect(tarifaEmCentavos('-1')).toBeNull();
  });

  it('recupera exclusão incerta e exige confirmação separada para desabilitar cidade com pedidos', async () => {
    TestBed.configureTestingModule({
      imports: [AdminAtendimentoComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AdminAtendimentoComponent);
    const http = TestBed.inject(HttpTestingController);
    const ativa = { ...cidade, ativa: true };
    const carregar = async (cidades: typeof cidade[]) => {
      http.expectOne(base).flush(config);
      http.expectOne(base + '/cidades').flush(cidades);
      await fixture.whenStable();
      http.match(base + '/municipios').forEach(requisicao => requisicao.flush([{ uf: 'RS', nome: 'Rio Grande do Sul' }]));
      fixture.detectChanges();
    };
    const clicar = (texto: string) => {
      const raiz = fixture.nativeElement as HTMLElement;
      const botao = [...raiz.querySelectorAll('button')].find(b => b.textContent?.trim() === texto);
      expect(botao).toBeDefined();
      botao!.click();
    };
    await carregar([ativa]);
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(false);
    try {
      const rota = base + '/cidades/' + cidade.id;
      clicar('Excluir');
      expect(confirmacao).toHaveBeenCalledWith(expect.stringContaining('A exclusão é permanente'));
      http.expectNone('/api/auth/csrf');
      confirmacao.mockReturnValue(true);
      clicar('Excluir');
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      const primeira = http.expectOne(rota + '?revisao=1');
      expect(primeira.request.method).toBe('DELETE');
      primeira.error(new ProgressEvent('error'));
      await fixture.whenStable();
      fixture.detectChanges();
      clicar('Recuperar exclusão de Vacaria');
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      const replay = http.expectOne(rota + '?revisao=1');
      expect(replay.request.headers.get('Idempotency-Key')).toBe(primeira.request.headers.get('Idempotency-Key'));
      replay.flush({ codigo: 'cidade_com_pedidos', detail: 'Há pedidos vinculados.' }, { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('Há pedidos vinculados');
      confirmacao.mockReturnValue(false);
      clicar('Desabilitar atendimento em Vacaria');
      http.expectNone('/api/auth/csrf');
      confirmacao.mockReturnValue(true);
      clicar('Desabilitar atendimento em Vacaria');
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      const desabilitar = http.expectOne(rota);
      expect(desabilitar.request.method).toBe('PUT');
      expect(desabilitar.request.body).toMatchObject({ ativa: false, revisao: 1, tarifaCentavos: '1250' });
      desabilitar.flush(cidade);
      await Promise.resolve();
      await carregar([cidade]);
      http.verify();
    } finally {
      confirmacao.mockRestore();
    }
  });

  it('grava retirada e uma cidade inativa, trata conflito e não publica um número novo automaticamente', async () => {
    TestBed.configureTestingModule({
      imports: [AdminAtendimentoComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AdminAtendimentoComponent);
    const http = TestBed.inject(HttpTestingController);
    const root: HTMLElement = fixture.nativeElement;
    const carregar = async (configuracao = config, cidades: (typeof cidade)[] = []) => {
      http.expectOne(base).flush(configuracao);
      http.expectOne(base + '/cidades').flush(cidades);
      await fixture.whenStable();
      http.match(base + '/municipios').forEach(requisicao => requisicao.flush([
        { uf: 'RS', nome: 'Rio Grande do Sul' }, { uf: 'SC', nome: 'Santa Catarina' },
      ]));
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const digitar = (nome: string, valor: string) => {
      const input = root.querySelector<HTMLInputElement>(`[name="${nome}"]`)!;
      input.value = valor;
      input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
      fixture.detectChanges();
    };
    const submeter = (indice: number, rota: string) => {
      root.querySelectorAll('form')[indice].dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      const req = http.expectOne(base + rota);
      expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/i);
      expect(req.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
      return req;
    };
    await carregar();
    expect(root.querySelector('h1')?.textContent).toBe('Entregas e fretes');
    expect(root.querySelector('a[href="/admin/cotacoes-correios"]')).toBeTruthy();
    expect(root.querySelector('input[name="ibge"]')).toBeNull();
    expect(root.querySelector('.admin-shell')?.getAttribute('data-admin-theme')).toBe('light');
    TestBed.inject(TemaAdminService).alternar();
    fixture.detectChanges();
    expect(root.querySelector('.admin-shell')?.getAttribute('data-admin-theme')).toBe('dark');
    digitar('endereco', 'Rua da loja');
    const retirada = submeter(0, '/modalidades');
    expect(retirada.request.body).toMatchObject({ retiradaHabilitada: true, enderecoRetirada: 'Rua da loja', revisao: 0 });
    retirada.flush({});
    await Promise.resolve();
    await carregar({ ...config, revisao: 1 });
    digitar('uf', 'SC');
    const consultaAntiga = http.expectOne(base + '/municipios/SC');
    digitar('uf', 'RS');
    expect(consultaAntiga.cancelled).toBe(true);
    http.expectOne(base + '/municipios/RS').flush([{ codigoIbge: '4322509', nome: 'Vacaria' }]);
    await fixture.whenStable();
    fixture.detectChanges();
    digitar('cidade', '4322509');
    digitar('tarifa', '12,50');
    const incluir = submeter(1, '/cidades');
    expect(incluir.request.body).toMatchObject({ ativa: false, tarifaCentavos: '1250', codigoIbge: '4322509', nome: 'Vacaria', uf: 'RS' });
    incluir.flush(cidade);
    await Promise.resolve();
    await carregar({ ...config, revisao: 1 }, [cidade]);
    const editar = [...root.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent?.trim() === 'Editar')!;
    editar.click();
    http.expectOne(base + '/municipios/RS').flush([{ codigoIbge: '4322509', nome: 'Vacaria' }]);
    fixture.detectChanges();
    digitar('tarifa', '15,00');
    root.querySelector<HTMLInputElement>('[name="ativa"]')!.click();
    const alteracao = submeter(1, '/cidades/cidade-teste');
    expect(alteracao.request.body).toMatchObject({ ativa: true, tarifaCentavos: '1500', revisao: 1 });
    alteracao.flush({ detail: 'Revisão desatualizada.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Revisão desatualizada');
    [...root.querySelectorAll<HTMLButtonElement>('button')]
      .find(b => b.textContent?.trim() === 'Recarregar dados')!.click();
    await carregar({ ...config, revisao: 2 }, [cidade]);
    digitar('numero', '+55 54 9256-0258');
    const contato = submeter(3, '/whatsapp');
    expect(contato.request.body).toMatchObject({ confirmado: false, habilitado: false, revisao: 2 });
    contato.flush({});
    await Promise.resolve();
    await carregar({ ...config, revisao: 3 });
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLInputElement>('[name="correiosManual"]')!.click();
    digitar('validadeCorreios', '169');
    root.querySelectorAll('form')[2].dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectNone('/api/auth/csrf');
    fixture.detectChanges();
    expect(root.querySelector('#correios-titulo')?.parentElement?.parentElement?.textContent).toContain('entre 1 e 168 horas');
    digitar('validadeCorreios', '72');
    const postal = submeter(2, '/correios-manual');
    expect(postal.request.body).toEqual({ habilitada: true, validadeHoras: 72, revisao: 3 });
    root.querySelectorAll('form')[2].dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectNone('/api/auth/csrf');
    postal.flush({});
    await Promise.resolve();
    await carregar({ ...config, revisao: 4, cotacaoManualCorreiosHabilitada: true, validadePropostaCorreiosHoras: 72 });
    expect(root.querySelector('#correios-titulo')?.parentElement?.textContent).toContain('Habilitado');
    confirmacao.mockRestore();
    http.verify();
    localStorage.removeItem('xaquiamuni:admin:tema');
  });
});
