import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConsultaCepComponent } from './consulta-cep.component';

describe('ConsultaCepComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  function montar(cep: string) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(ConsultaCepComponent);
    fixture.componentRef.setInput('cep', cep);
    const atualizou = vi.fn();
    fixture.componentInstance.endereco.subscribe(atualizou);
    fixture.detectChanges();
    const clicar = () => {
      (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
      fixture.detectChanges();
    };
    return { fixture, clicar, atualizou, http: TestBed.inject(HttpTestingController) };
  }

  it('preenche somente campos retornados, sem alterar número ou complemento', () => {
    const { fixture, clicar, atualizou, http } = montar('01001-000');
    clicar();
    expect(fixture.nativeElement.textContent).toContain('Consultando CEP');
    http.expectOne('/api/cep/01001-000').flush({ uf: 'SP', cidade: 'São Paulo', bairro: null, logradouro: '' });
    fixture.detectChanges();
    expect(atualizou).toHaveBeenCalledWith({ uf: 'SP', cidade: 'São Paulo' });
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('Endereço atualizado');
  });

  it('rejeita formato localmente, cancela resposta antiga e permite nova tentativa', () => {
    const { fixture, clicar, atualizou, http } = montar('010');
    clicar();
    http.expectNone(() => true);
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('oito dígitos');
    fixture.componentRef.setInput('cep', '01001000');
    fixture.detectChanges();
    clicar();
    const antiga = http.expectOne('/api/cep/01001000');
    fixture.componentRef.setInput('cep', '95200000');
    fixture.detectChanges();
    expect(antiga.cancelled).toBe(true);
    clicar();
    http.expectOne('/api/cep/95200000').flush({ codigo: 'cep_indisponivel' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('Tente novamente');
    clicar();
    http.expectOne('/api/cep/95200000').flush({ codigo: 'cep_nao_encontrado' }, { status: 404, statusText: 'Not found' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('CEP não encontrado');
    expect(atualizou).not.toHaveBeenCalled();
  });
});
