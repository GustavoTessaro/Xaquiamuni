import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminAtendimentoComponent, tarifaEmCentavos } from './admin-atendimento.component';
import { TemaAdminService } from './tema-admin.service';

describe('Entregas e contato ADMIN', () => {
  const base = '/api/admin/loja/atendimento';
  const config = {
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
      fixture.detectChanges();
    };
    const digitar = (nome: string, valor: string) => {
      const input = root.querySelector<HTMLInputElement>(`[name="${nome}"]`)!;
      input.value = valor;
      input.dispatchEvent(new Event('input', { bubbles: true }));
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
    digitar('cidade', 'Vacaria');
    digitar('uf', 'RS');
    digitar('ibge', '4322509');
    digitar('tarifa', '12,50');
    const incluir = submeter(2, '/cidades');
    expect(incluir.request.body).toMatchObject({ ativa: false, tarifaCentavos: '1250' });
    incluir.flush(cidade);
    await Promise.resolve();
    await carregar({ ...config, revisao: 1 }, [cidade]);
    const editar = [...root.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent?.trim() === 'Editar')!;
    editar.click();
    fixture.detectChanges();
    digitar('tarifa', '15,00');
    root.querySelector<HTMLInputElement>('[name="ativa"]')!.click();
    const alteracao = submeter(2, '/cidades/cidade-teste');
    expect(alteracao.request.body).toMatchObject({ ativa: true, tarifaCentavos: '1500', revisao: 1 });
    alteracao.flush({ detail: 'Revisão desatualizada.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Revisão desatualizada');
    [...root.querySelectorAll<HTMLButtonElement>('button')]
      .find(b => b.textContent?.trim() === 'Recarregar dados')!.click();
    await carregar({ ...config, revisao: 2 }, [cidade]);
    digitar('numero', '+55 54 9256-0258');
    const contato = submeter(1, '/whatsapp');
    expect(contato.request.body).toMatchObject({ confirmado: false, habilitado: false, revisao: 2 });
    contato.flush({});
    await Promise.resolve();
    await carregar({ ...config, revisao: 3 });
    http.verify();
    localStorage.removeItem('xaquiamuni:admin:tema');
  });
});
