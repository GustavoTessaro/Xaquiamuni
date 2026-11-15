import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminCategoria } from '../../core/admin-categorias/categorias.service';
import { AdminProduto } from '../../core/admin-produtos/produtos.service';
import { AdminProdutoFormComponent, ProdutoFormSubmission } from './admin-produto-form.component';

describe('AdminProdutoFormComponent', () => {
  let fixture: ComponentFixture<AdminProdutoFormComponent>;
  let submissions: ProdutoFormSubmission[];
  const categories: AdminCategoria[] = [
    { id: 'active', nome: 'Sabonetes', descricao: null, ativa: true, ordemExibicao: 0 },
    { id: 'inactive', nome: 'Velas', descricao: null, ativa: false, ordemExibicao: 1 },
  ];
  const product: AdminProduto = {
    id: 'product-1',
    nome: 'Sabonete',
    descricao: 'Lavanda',
    categoriaId: 'inactive',
    categoriaNome: 'Velas',
    categoriaAtiva: false,
    publicado: false,
    variantes: [
      {
        id: 'variant-1',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 19.9,
        ativa: true,
        padrao: true,
      },
    ],
  };

  beforeEach(async () => {
    submissions = [];
    await TestBed.configureTestingModule({
      imports: [AdminProdutoFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminProdutoFormComponent);
    fixture.componentRef.setInput('categorias', categories);
    fixture.componentInstance.salvar.subscribe((submission) => submissions.push(submission));
    fixture.detectChanges();
  });

  it('validates required and bounded fields before submission', () => {
    submit();

    expect(text()).toContain('Informe o nome do produto.');
    expect(text()).toContain('Selecione uma categoria ativa.');
    expect(text()).toContain('Informe o preço de venda.');
    expect(submissions).toEqual([]);

    setField('product-name', '   ');
    setField('product-category', 'active');
    setField('default-variant-price', '0');
    submit();

    expect(text()).toContain('Informe o nome do produto.');
    expect(text()).toContain('O preço deve ser maior que zero.');
    expect(submissions).toEqual([]);

    setField('default-variant-price', '12.345');
    submit();
    expect(text()).toContain('Informe o preço com no máximo duas casas decimais.');
    expect(submissions).toEqual([]);
  });

  it('submits a simple product with the required internal default variant', () => {
    setField('product-name', ' Sabonete de Lavanda ');
    setField('product-description', ' Feito à mão ');
    setField('product-category', 'active');
    setField('default-variant-price', '19.90');
    submit();

    expect(submissions).toEqual([
      {
        mode: 'create',
        request: {
          nome: 'Sabonete de Lavanda',
          descricao: 'Feito à mão',
          categoriaId: 'active',
          variantePadrao: {
            identificacao: 'Padrão',
            unidadeVenda: 'UN',
            precoVenda: 19.9,
          },
          variantes: [],
        },
      },
    ]);
  });

  it('submits additional variants with numeric prices and their individual units', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    setField('default-variant-name', '100 g');
    setField('default-variant-unit', 'g');
    setField('default-variant-price', '19.90');
    clickButton('Adicionar variante');
    setField('variant-name-0', '200 g');
    setField('variant-unit-0', 'g');
    setField('variant-price-0', '35.50');
    submit();

    const submission = submissions[0];
    expect(submission?.mode).toBe('create');
    if (submission?.mode !== 'create') {
      throw new Error('Expected a product creation request.');
    }
    expect(submission.request.variantePadrao).toEqual({
      identificacao: '100 g',
      unidadeVenda: 'G',
      precoVenda: 19.9,
    });
    expect(submission.request.variantes).toEqual([
      {
        identificacao: '200 g',
        unidadeVenda: 'G',
        precoVenda: 35.5,
      },
    ]);
  });

  it('validates every additional variant before creating the product', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    setField('default-variant-price', '19.9');
    clickButton('Adicionar variante');
    setField('variant-name-0', '200 g');
    setField('variant-unit-0', '');
    setField('variant-price-0', '12.345');
    submit();

    expect(text()).toContain('Informe a unidade de venda.');
    expect(text()).toContain('Informe o preço com no máximo duas casas decimais.');
    expect(submissions).toEqual([]);
  });

  it('edits product fields using active categories and preserves the current inactive category option', () => {
    fixture.componentRef.setInput('produto', product);
    fixture.detectChanges();

    const options = Array.from(root().querySelectorAll('#product-category option')).map((option) =>
      option.textContent?.trim(),
    );
    expect(options).toContain('Velas (inativa — categoria atual)');
    expect(options).toContain('Sabonetes');
    expect(options).toHaveLength(3);
    expect(root().querySelector('#default-variant-price')).toBeNull();

    setField('product-name', 'Sabonete editado');
    setField('product-category', 'active');
    submit();

    expect(submissions).toEqual([
      {
        mode: 'edit',
        request: {
          nome: 'Sabonete editado',
          descricao: 'Lavanda',
          categoriaId: 'active',
        },
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
    const field = root().querySelector(`#${id}`) as
      HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
    field.value = value;
    field.dispatchEvent(
      new Event(field instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
    fixture.detectChanges();
  }

  function clickButton(label: string): void {
    const button = Array.from(root().querySelectorAll('button')).find(
      (item) => item.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    button.click();
    fixture.detectChanges();
  }

  function submit(): void {
    const form = root().querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
});
