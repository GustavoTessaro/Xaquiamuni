import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  AdminProdutoVariante,
  AdminProdutoVarianteRequest,
} from '../../core/admin-produtos/produtos.service';
import { AdminProdutoVarianteFormComponent } from './admin-produto-variante-form.component';

describe('AdminProdutoVarianteFormComponent', () => {
  let fixture: ComponentFixture<AdminProdutoVarianteFormComponent>;
  let submissions: AdminProdutoVarianteRequest[];

  beforeEach(async () => {
    submissions = [];
    await TestBed.configureTestingModule({
      imports: [AdminProdutoVarianteFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminProdutoVarianteFormComponent);
    fixture.componentInstance.salvar.subscribe((request) => submissions.push(request));
    fixture.detectChanges();
  });

  it('creates an active-catalog variant with a selected sale unit and numeric price', () => {
    setField('managed-variant-name', 'Kit Lavanda');
    setField('managed-variant-unit', 'KIT');
    setField('managed-variant-price', '1500');
    submit();

    expect(submissions).toEqual([
      {
        identificacao: 'Kit Lavanda',
        unidadeVenda: 'KIT',
        precoVenda: 15,
      },
    ]);
    expect(text()).toContain('A nova opção começa ativa e não principal.');
  });

  it('shows the default-variant rule and edits existing values without changing its state', () => {
    const defaultVariant: AdminProdutoVariante = {
      id: 'variant-1',
      identificacao: '100 g',
      unidadeVenda: 'UN',
      precoVenda: 25.5,
      ativa: true,
      padrao: true,
      pesoLiquidoGramas: null,
      volumeLiquidoMililitros: null,
      caracteristicas: [],
    };
    fixture.componentRef.setInput('variante', defaultVariant);
    fixture.detectChanges();

    expect(text()).toContain('Esta é a opção principal');
    expect((root().querySelector('#managed-variant-price') as HTMLInputElement).value).toBe(
      'R$ 25,50',
    );

    setField('managed-variant-name', '200 g');
    setField('managed-variant-unit', 'KIT');
    setField('managed-variant-price', '3499');
    submit();

    expect(submissions).toEqual([
      {
        identificacao: '200 g',
        unidadeVenda: 'KIT',
        precoVenda: 34.99,
      },
    ]);
  });

  it('does not submit missing variant names or invalid prices', () => {
    setField('managed-variant-unit', 'UN');
    setField('managed-variant-price', '0');
    submit();

    expect(text()).toContain('Informe uma identificação para a opção.');
    expect(text()).toContain('O preço deve ser maior que zero.');
    expect(submissions).toEqual([]);
  });

  it('keeps an existing legacy sale unit available without converting it', () => {
    const legacyVariant: AdminProdutoVariante = {
      id: 'legacy-variant',
      identificacao: 'Apresentação antiga',
      unidadeVenda: 'G',
      precoVenda: 25.5,
      ativa: true,
      padrao: true,
      pesoLiquidoGramas: null,
      volumeLiquidoMililitros: null,
      caracteristicas: [],
    };
    fixture.componentRef.setInput('variante', legacyVariant);
    fixture.detectChanges();

    expect(text()).toContain('G (legada — manter)');
    setField('managed-variant-price', '2600');
    submit();

    expect(submissions).toEqual([
      {
        identificacao: 'Apresentação antiga',
        unidadeVenda: 'G',
        precoVenda: 26,
      },
    ]);
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return root().textContent ?? '';
  }

  function setField(id: string, value: string): void {
    const field = root().querySelector(`#${id}`) as HTMLInputElement | HTMLSelectElement;
    field.value = value;
    field.dispatchEvent(
      new Event(field instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
    fixture.detectChanges();
  }

  function submit(): void {
    root()
      .querySelector('form')
      ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
});
