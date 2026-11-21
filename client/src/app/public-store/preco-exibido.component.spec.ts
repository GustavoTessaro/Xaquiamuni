import { TestBed } from '@angular/core/testing';
import { PrecoExibidoComponent } from './preco-exibido.component';

describe('Preço exibido na loja', () => {
  it('exibe somente o valor normal quando o tema está ativo e a variante não tem promoção', () => {
    const f = TestBed.createComponent(PrecoExibidoComponent);
    f.componentRef.setInput('textoNormal', 'R$ 19,90');
    f.componentRef.setInput('preco', {
      regularCentavos: '1990', finalCentavos: '1990', economiaCentavos: '0', offPontosBase: 0, promocaoId: null,
    });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('R$ 19,90');
    expect(f.nativeElement.querySelector('del')).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('OFF');
  });

  it('usa os valores da variante e atualiza a apresentação ao trocar a opção', () => {
    const f = TestBed.createComponent(PrecoExibidoComponent);
    f.componentRef.setInput('textoNormal', 'R$ 17,50');
    f.componentRef.setInput('preco', {
      regularCentavos: '2000', finalCentavos: '1750', economiaCentavos: '250', offPontosBase: 1250, promocaoId: 'p',
    });
    f.detectChanges();
    expect(f.nativeElement.querySelector('del').textContent).toContain('20,00');
    expect(f.nativeElement.querySelector('strong').textContent).toContain('17,50');
    expect(f.nativeElement.textContent).toContain('12,5% OFF');
    expect(f.nativeElement.textContent).toContain('Preço promocional');
    f.componentRef.setInput('textoNormal', 'R$ 25,00');
    f.componentRef.setInput('preco', undefined);
    f.detectChanges();
    expect(f.nativeElement.querySelector('del')).toBeNull();
    expect(f.nativeElement.textContent).toContain('25,00');
  });

  it('mostra o desconto histórico a partir dos centavos congelados', () => {
    const f = TestBed.createComponent(PrecoExibidoComponent);
    f.componentRef.setInput('textoNormal', 'R$ 9,00');
    f.componentRef.setInput('regularHistorico', '1199');
    f.componentRef.setInput('finalHistorico', '900');
    f.detectChanges();
    expect(f.nativeElement.querySelector('del').textContent).toContain('11,99');
    expect(f.nativeElement.textContent).toContain('24,93% OFF');
  });
});
