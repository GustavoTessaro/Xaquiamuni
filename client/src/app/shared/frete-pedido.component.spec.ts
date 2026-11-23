import { TestBed } from '@angular/core/testing';
import { FretePedidoComponent } from './frete-pedido.component';

describe('Valores congelados de frete', () => {
  it('mostra frete grátis, valor original e campanha', () => {
    const fixture = TestBed.createComponent(FretePedidoComponent);
    fixture.componentRef.setInput('frete', '0');
    fixture.componentRef.setInput('desconto', {
      campanhaId: 'campanha', nomeCampanha: 'Entrega da loja', originalCentavos: '1300', descontoCentavos: '1300',
    });
    fixture.detectChanges();
    const texto = fixture.nativeElement.textContent;
    expect(texto).toContain('Frete original');
    expect(texto).toContain('13,00');
    expect(texto).toContain('Desconto de frete');
    expect(texto).toContain('Frete grátis');
    expect(texto).toContain('Entrega da loja');
  });

  it('mantém o frete comum para pedidos sem campanha ou históricos', () => {
    const fixture = TestBed.createComponent(FretePedidoComponent);
    fixture.componentRef.setInput('frete', '1400');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('14,00');
    expect(fixture.nativeElement.textContent).not.toContain('Campanha');
  });
});
