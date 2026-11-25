import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { linkEmailComercial } from '../public-store/contato-loja.component';
import { AdminContatoComponent } from './admin-contato.component';

describe('Contato da loja', () => {
  const url = '/api/admin/loja/atendimento';
  const inicial = {
    whatsApp: '+555492560258', whatsAppConfirmado: true, whatsAppHabilitado: true,
    emailComercial: null, emailComercialHabilitado: false, revisao: 2,
  };

  it('valida a prévia e recupera o salvamento sem perder o rascunho do outro canal', async () => {
    TestBed.configureTestingModule({
      imports: [AdminContatoComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AdminContatoComponent);
    const http = TestBed.inject(HttpTestingController);
    const root = fixture.nativeElement as HTMLElement;
    http.expectOne(url + '/contato').flush(inicial);
    await fixture.whenStable();
    fixture.detectChanges();
    const campo = (nome: string) => root.querySelector<HTMLInputElement>(`[name="${nome}"]`)!;
    const digitar = async (nome: string, valor: string) => {
      campo(nome).value = valor;
      campo(nome).dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const submeter = () => root.querySelectorAll('form')[1].dispatchEvent(new Event('submit', { cancelable: true }));
    const email = () => {
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      return http.expectOne(url + '/contato/email');
    };
    await digitar('numero', '+5554999990000');
    expect(campo('publicado').checked).toBe(false);
    expect(root.textContent).toContain('WhatsApp aguarda confirmação');
    await digitar('emailComercial', 'a@example.com?bcc=b@example.com');
    campo('publicarEmail').click();
    submeter();
    expect(root.textContent).not.toContain('E-mail comercial · a@');
    http.expectNone('/api/auth/csrf');
    await digitar('emailComercial', 'loja+teste@EXAMPLE.COM');
    expect(root.textContent).toContain('Há alterações ainda não salvas');
    expect(root.textContent).toContain('E-mail comercial · loja+teste@example.com');
    submeter();
    submeter();
    const primeira = email();
    expect(primeira.request.body).toEqual({ endereco: 'loja+teste@example.com', habilitado: true, revisao: 2 });
    primeira.error(new ProgressEvent('error'));
    await fixture.whenStable();
    fixture.detectChanges();
    [...root.querySelectorAll('button')].find(item => item.textContent?.includes('Recuperar salvamento'))!.click();
    const replay = email();
    expect(replay.request.headers.get('Idempotency-Key')).toBe(primeira.request.headers.get('Idempotency-Key'));
    replay.flush({ ...inicial, emailComercial: 'loja+teste@example.com', emailComercialHabilitado: true, revisao: 3 });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(campo('numero').value).toBe('+5554999990000');
    expect(root.textContent).toContain('E-mail comercial salvo.');
    http.verify();
  });

  it('cria somente destinos mailto seguros', () => {
    expect(linkEmailComercial('loja+teste@EXAMPLE.COM')).toBe('mailto:loja%2Bteste@example.com');
    for (const invalido of ['javascript:alert(1)', 'a%0A@example.com', 'a@example.com\r\n', 'a@example.com?bcc=x@example.com']) {
      expect(linkEmailComercial(invalido)).toBeNull();
    }
  });
});
