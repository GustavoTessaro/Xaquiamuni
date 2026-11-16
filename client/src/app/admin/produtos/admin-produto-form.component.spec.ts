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
  const simpleProduct: AdminProduto = {
    id: 'product-1',
    nome: 'Sabonete',
    descricao: 'Lavanda',
    categoriaId: 'inactive',
    categoriaNome: 'Velas',
    categoriaAtiva: false,
    publicado: false,
    quantidadeImagens: 0,
    fotoPrincipalUrl: null,
    variantes: [
      {
        id: 'variant-1',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 19.9,
        ativa: true,
        padrao: true,
        pesoLiquidoGramas: null,
        volumeLiquidoMililitros: null,
        caracteristicas: [],
      },
    ],
    caracteristicas: [],
  };
  const optionsProduct: AdminProduto = {
    ...simpleProduct,
    caracteristicas: [
      {
        id: 'weight-feature',
        nome: 'Peso',
        tipoMedidaFisica: 'PesoLiquido',
        valores: [{ id: 'weight-100', nome: '', valorMedidaCanonico: 100 }],
      },
    ],
    variantes: [
      {
        ...simpleProduct.variantes[0],
        identificacao: '100 g',
        pesoLiquidoGramas: 100,
        caracteristicas: [
          {
            caracteristicaId: 'weight-feature',
            caracteristicaNome: 'Peso',
            valorId: 'weight-100',
            valorNome: '',
            valorMedidaCanonico: 100,
          },
        ],
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

  it('validates required product fields and price before submission', () => {
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
  });

  it('submits the simple contract without asking for an internal variant name', () => {
    setField('product-name', ' Sabonete de Lavanda ');
    setField('product-description', ' Feito à mão ');
    setField('product-category', 'active');
    setField('default-variant-unit', 'KIT');
    setField('default-variant-price', '19.90');
    submit();

    expect(root().querySelector('#default-variant-name')).toBeNull();
    expect(text()).not.toContain('Nome da variante');
    expect(submissions).toEqual([
      {
        mode: 'create',
        request: {
          nome: 'Sabonete de Lavanda',
          descricao: 'Feito à mão',
          categoriaId: 'active',
          simples: true,
          variantePadrao: {
            unidadeVenda: 'KIT',
            precoVenda: 19.9,
          },
          variantes: [],
        },
      },
    ]);
  });

  it('offers all six sale units for new simple products', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    setField('default-variant-price', '10');
    for (const unit of ['UN', 'KIT', 'PAR', 'PCT', 'CX', 'CJ']) {
      setField('default-variant-unit', unit);
      submit();
    }

    expect(
      submissions.map((item) => item.mode === 'create' && item.request.variantePadrao.unidadeVenda),
    ).toEqual(['UN', 'KIT', 'PAR', 'PCT', 'CX', 'CJ']);
  });

  it('converts kg and litres exactly and rejects measures requiring rounding', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    setField('default-variant-price', '19.90');
    setField('simple-weight', '1.234');
    setField('simple-weight-unit', 'KG');
    setField('simple-volume', '0.125');
    setField('simple-volume-unit', 'L');
    submit();

    const request = submissions[0];
    expect(request?.mode === 'create' && request.request.variantePadrao).toMatchObject({
      pesoLiquidoGramas: 1234,
      volumeLiquidoMililitros: 125,
    });

    setField('simple-weight', '0.0001');
    setField('simple-weight-unit', 'G');
    submit();
    expect(submissions).toHaveLength(1);
    expect(text()).toContain(
      'Peso e volume devem ser positivos e ter até três casas decimais canônicas.',
    );

    setField('simple-weight', '0.000001');
    setField('simple-weight-unit', 'KG');
    submit();
    expect(submissions).toHaveLength(2);
    expect(
      submissions[1]?.mode === 'create' && submissions[1].request.variantePadrao.pesoLiquidoGramas,
    ).toBe(0.001);
  });

  it('changes modes with an empty default combination and preserves common simple fields', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    setField('default-variant-price', '1500');
    setField('default-variant-unit', 'PCT');
    setField('simple-weight', '100');
    setField('simple-volume', '250');
    selectMode('product-mode-options');
    expect(text()).toContain('Quais opções o cliente poderá escolher?');
    clickButton('Adicionar combinação');
    selectMode('product-mode-simple');
    expect(text()).not.toContain('Trocar para produto simples?');
    expect((root().querySelector('#default-variant-price') as HTMLInputElement).value).toBe(
      'R$ 15,00',
    );
    expect((root().querySelector('#default-variant-unit') as HTMLSelectElement).value).toBe('PCT');
    expect((root().querySelector('#simple-weight') as HTMLInputElement).value).toBe('100');
    expect((root().querySelector('#simple-volume') as HTMLInputElement).value).toBe('250');
  });

  it('confirms discarding a filled additional combination without characteristics', () => {
    setField('default-variant-price', '2000');
    selectMode('product-mode-options');
    clickButton('Adicionar combinação');
    setField('combination-price-1', '3000');
    setField('combination-unit-1', 'KIT');
    setField('combination-weight-1', '200');
    setField('combination-volume-1', '500');
    choosePrimaryCombination(1);
    selectMode('product-mode-simple');

    expect(text()).toContain('Trocar para produto simples?');
    expect((root().querySelector('#product-mode-options') as HTMLInputElement).checked).toBe(true);
    clickButton('Manter produto com opções');
    expect((root().querySelector('#combination-price-1') as HTMLInputElement).value).toBe(
      'R$ 30,00',
    );
    selectMode('product-mode-simple');
    clickButton('Descartar opções e continuar');

    expect(root().querySelector('#combination-price-1')).toBeNull();
    expect((root().querySelector('#default-variant-price') as HTMLInputElement).value).toBe(
      'R$ 30,00',
    );
    expect((root().querySelector('#default-variant-unit') as HTMLSelectElement).value).toBe('KIT');
    expect((root().querySelector('#simple-weight') as HTMLInputElement).value).toBe('200');
    expect((root().querySelector('#simple-volume') as HTMLInputElement).value).toBe('500');
  });

  it('keeps all data on cancel and preserves a physical measure derived from the main option', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    setField('default-variant-price', '1500');
    setField('default-variant-unit', 'PCT');
    setField('simple-weight', '100');
    setField('simple-volume', '250');
    selectMode('product-mode-options');
    clickButton('Peso');
    clickButton('Adicionar valor');
    setField('measure-value-valor-2', '100');
    chooseSelection(0, 'caracteristica-1', 'valor-2');
    clickButton('Adicionar combinação');
    setField('combination-price-1', '3000');
    selectMode('product-mode-simple');

    expect(text()).toContain('Trocar para produto simples?');
    expect((root().querySelector('#product-mode-options') as HTMLInputElement).checked).toBe(true);
    expect((root().querySelector('#product-mode-simple') as HTMLInputElement).checked).toBe(false);
    clickButton('Manter produto com opções');
    expect((root().querySelector('#product-mode-options') as HTMLInputElement).checked).toBe(true);
    expect(root().querySelector('#measure-value-valor-2')).not.toBeNull();
    expect(root().querySelector('#combination-weight-0')).toBeNull();
    expect((root().querySelector('#combination-price-1') as HTMLInputElement).value).toBe(
      'R$ 30,00',
    );
    expect(root().querySelector('#default-variant-price')).toBeNull();

    selectMode('product-mode-simple');
    clickButton('Descartar opções e continuar');
    expect((root().querySelector('#product-mode-simple') as HTMLInputElement).checked).toBe(true);
    expect((root().querySelector('#product-mode-options') as HTMLInputElement).checked).toBe(false);
    expect(root().querySelector('#measure-value-valor-2')).toBeNull();
    expect((root().querySelector('#default-variant-price') as HTMLInputElement).value).toBe(
      'R$ 15,00',
    );
    expect((root().querySelector('#default-variant-unit') as HTMLSelectElement).value).toBe('PCT');
    expect((root().querySelector('#simple-weight') as HTMLInputElement).value).toBe('100');
    expect((root().querySelector('#simple-volume') as HTMLInputElement).value).toBe('250');
    submit();

    expect(submissions[0]?.mode === 'create' && submissions[0].request.variantePadrao).toMatchObject({
      unidadeVenda: 'PCT',
      precoVenda: 15,
      pesoLiquidoGramas: 100,
      volumeLiquidoMililitros: 250,
    });
  });

  it('uses characteristic names as headings and numbers visible values by position', () => {
    selectMode('product-mode-options');
    clickButton('Peso');
    clickButton('Cor');
    clickButton('Outra característica');
    expect(text()).toContain('Peso');
    expect(text()).toContain('Cor');
    expect(text()).toContain('Nova característica');
    setField('feature-name-caracteristica-3', 'Material');
    expect(text()).toContain('Material');
    expect(text()).not.toContain('Característica 3');

    clickButton('Adicionar valor', 0);
    clickButton('Adicionar valor', 0);
    clickButton('Adicionar valor', 0);
    clickButton('Remover valor', 0);

    expect(
      root().querySelector('label[for="measure-value-valor-5"]')?.textContent?.trim(),
    ).toBe('Peso 1');
    expect(
      root().querySelector('label[for="measure-value-valor-6"]')?.textContent?.trim(),
    ).toBe('Peso 2');
  });

  it('creates only selected combinations with physical, color and fragrance values', () => {
    setField('product-name', 'Sabonete artesanal');
    setField('product-category', 'active');
    selectMode('product-mode-options');
    clickButton('Peso');
    clickButton('Cor');
    clickButton('Fragrância');

    clickButton('Adicionar valor', 0);
    clickButton('Adicionar valor', 0);
    clickButton('Adicionar valor', 1);
    clickButton('Adicionar valor', 1);
    clickButton('Adicionar valor', 2);
    clickButton('Adicionar valor', 2);

    setField('measure-value-valor-4', '100');
    setField('measure-value-valor-5', '200');
    setField('feature-value-valor-6', 'Branca');
    setField('feature-value-valor-7', 'Rosa');
    setField('feature-value-valor-8', 'Lavanda');
    setField('feature-value-valor-9', 'Baunilha');
    chooseCombination(0, ['valor-4', 'valor-6', 'valor-8']);
    setField('combination-price-0', '1999');
    clickButton('Adicionar combinação');
    chooseCombination(1, ['valor-5', 'valor-7', 'valor-9']);
    setField('combination-price-1', '2500');
    submit();

    const submission = submissions[0];
    expect(submission?.mode).toBe('create');
    if (submission?.mode !== 'create') {
      throw new Error('Expected a product creation request.');
    }
    expect(submission.request.simples).toBe(false);
    expect(submission.request.caracteristicas).toEqual([
      {
        referencia: 'caracteristica-1',
        nome: 'Peso',
        tipoMedidaFisica: 'PesoLiquido',
        valores: [
          { referencia: 'valor-4', valorMedidaCanonico: 100 },
          { referencia: 'valor-5', valorMedidaCanonico: 200 },
        ],
      },
      {
        referencia: 'caracteristica-2',
        nome: 'Cor',
        tipoMedidaFisica: null,
        valores: [
          { referencia: 'valor-6', nome: 'Branca' },
          { referencia: 'valor-7', nome: 'Rosa' },
        ],
      },
      {
        referencia: 'caracteristica-3',
        nome: 'Fragrância',
        tipoMedidaFisica: null,
        valores: [
          { referencia: 'valor-8', nome: 'Lavanda' },
          { referencia: 'valor-9', nome: 'Baunilha' },
        ],
      },
    ]);
    expect(submission.request.variantePadrao).toMatchObject({
      identificacao: '100 g · Branca · Lavanda',
      unidadeVenda: 'UN',
      precoVenda: 19.99,
      caracteristicas: [
        { referenciaCaracteristica: 'caracteristica-1', referenciaValor: 'valor-4' },
        { referenciaCaracteristica: 'caracteristica-2', referenciaValor: 'valor-6' },
        { referenciaCaracteristica: 'caracteristica-3', referenciaValor: 'valor-8' },
      ],
    });
    expect(submission.request.variantes).toEqual([
      expect.objectContaining({
        identificacao: '200 g · Rosa · Baunilha',
        precoVenda: 25,
        caracteristicas: [
          { referenciaCaracteristica: 'caracteristica-1', referenciaValor: 'valor-5' },
          { referenciaCaracteristica: 'caracteristica-2', referenciaValor: 'valor-7' },
          { referenciaCaracteristica: 'caracteristica-3', referenciaValor: 'valor-9' },
        ],
      }),
    ]);
    expect(submission.request.variantes).toHaveLength(1);
  });

  it('supports a custom characteristic and detects duplicate combinations independent of order', () => {
    setField('product-name', 'Vela');
    setField('product-category', 'active');
    selectMode('product-mode-options');
    clickButton('Outra característica');
    setField('feature-name-caracteristica-1', 'Material');
    clickButton('Adicionar valor');
    setField('feature-value-valor-2', 'Cera de soja');
    chooseCombination(0, ['valor-2']);
    setField('combination-price-0', '20');
    clickButton('Adicionar combinação');
    chooseCombination(1, ['valor-2']);
    setField('combination-price-1', '30');
    submit();

    expect(submissions).toEqual([]);
    expect(text()).toContain('Há combinações repetidas.');
  });

  it('rejects duplicate normalized value names before sending the product', () => {
    setField('product-name', 'Sabonete');
    setField('product-category', 'active');
    selectMode('product-mode-options');
    clickButton('Cor');
    clickButton('Adicionar valor');
    clickButton('Adicionar valor');
    setField('feature-value-valor-2', 'Verde');
    setField('feature-value-valor-3', ' verde  ');
    chooseCombination(0, ['valor-2']);
    setField('combination-price-0', '20');
    submit();

    expect(submissions).toEqual([]);
    expect(text()).toContain('remova textos repetidos');
  });

  it('omits unsaved characteristics and values removed before creation', () => {
    setField('product-name', 'Vela');
    setField('product-category', 'active');
    selectMode('product-mode-options');
    clickButton('Cor');
    clickButton('Fragrância');
    clickButton('Adicionar valor');
    clickButton('Adicionar valor', 1);
    setField('feature-value-valor-4', 'Lavanda');
    clickByLabel('Remover característica Cor');
    chooseSelection(0, 'caracteristica-2', 'valor-4');
    setField('combination-price-0', '20');
    submit();

    const submission = submissions[0];
    expect(submission?.mode).toBe('create');
    if (submission?.mode !== 'create') {
      throw new Error('Expected a product creation request.');
    }
    expect(submission.request.caracteristicas).toEqual([
      {
        referencia: 'caracteristica-2',
        nome: 'Fragrância',
        tipoMedidaFisica: null,
        valores: [{ referencia: 'valor-4', nome: 'Lavanda' }],
      },
    ]);
  });

  it('edits option products using persisted IDs and keeps the existing combination identity', () => {
    fixture.componentRef.setInput('produto', optionsProduct);
    fixture.detectChanges();
    setField('product-name', 'Sabonete renovado');
    submit();

    const submission = submissions[0];
    expect(submission?.mode).toBe('edit');
    if (submission?.mode !== 'edit') {
      throw new Error('Expected a product edit request.');
    }
    expect(submission.request.caracteristicas).toEqual([
      {
        id: 'weight-feature',
        nome: 'Peso',
        tipoMedidaFisica: 'PesoLiquido',
        valores: [{ id: 'weight-100', valorMedidaCanonico: 100 }],
      },
    ]);
    expect(submission.request.variantes).toEqual([
      expect.objectContaining({
        id: 'variant-1',
        identificacao: '100 g',
        padrao: true,
        ativa: true,
        atualizarMedidas: true,
        caracteristicas: [{ caracteristicaId: 'weight-feature', valorId: 'weight-100' }],
      }),
    ]);
  });

  it('adds a new explicit combination during edit without replacing the existing variant', () => {
    fixture.componentRef.setInput('produto', optionsProduct);
    fixture.detectChanges();
    clickButton('Adicionar valor');
    setField('measure-value-valor-1', '200');
    clickButton('Adicionar combinação');
    setField('selection-1-weight-feature', 'valor-1');
    setField('combination-price-1', '25');
    submit();

    const submission = submissions[0];
    expect(submission?.mode).toBe('edit');
    if (submission?.mode !== 'edit') {
      throw new Error('Expected a product edit request.');
    }
    expect(submission.request.caracteristicas?.[0]?.valores).toEqual([
      { id: 'weight-100', valorMedidaCanonico: 100 },
      { referencia: 'valor-1', valorMedidaCanonico: 200 },
    ]);
    expect(submission.request.variantes).toEqual([
      expect.objectContaining({ id: 'variant-1', padrao: true }),
      expect.objectContaining({
        identificacao: '200 g',
        padrao: false,
        caracteristicas: [{ caracteristicaId: 'weight-feature', referenciaValor: 'valor-1' }],
      }),
    ]);
  });

  it('preserves a legacy sale unit on unrelated edits', () => {
    const legacy = {
      ...simpleProduct,
      variantes: [{ ...simpleProduct.variantes[0], unidadeVenda: 'G' }],
    };
    fixture.componentRef.setInput('produto', legacy);
    fixture.detectChanges();
    expect(text()).toContain('G (legada — manter)');

    setField('product-name', 'Sabonete sem outras mudanças');
    submit();
    const submission = submissions[0];
    expect(submission?.mode === 'edit' && submission.request.variantes?.[0]?.unidadeVenda).toBe(
      'G',
    );
  });

  it('updates and clears direct measures while retaining the existing variant ID', () => {
    const measuredProduct: AdminProduto = {
      ...simpleProduct,
      variantes: [
        {
          ...simpleProduct.variantes[0],
          pesoLiquidoGramas: 100,
          volumeLiquidoMililitros: 250,
        },
      ],
    };
    fixture.componentRef.setInput('produto', measuredProduct);
    fixture.detectChanges();
    setField('simple-weight', '');
    setField('simple-volume', '0.5');
    setField('simple-volume-unit', 'L');
    submit();

    const submission = submissions[0];
    expect(submission?.mode === 'edit' && submission.request.variantes?.[0]).toMatchObject({
      id: 'variant-1',
      pesoLiquidoGramas: null,
      volumeLiquidoMililitros: 500,
      atualizarMedidas: true,
    });
  });

  it('keeps form data visible when an API error is returned', () => {
    setField('product-name', 'Sabonete preenchido');
    setField('product-category', 'active');
    setField('default-variant-price', '1500');
    fixture.componentRef.setInput('mensagemErro', 'Conflito de combinação. Revise as opções.');
    fixture.detectChanges();

    expect((root().querySelector('#product-name') as HTMLInputElement).value).toBe(
      'Sabonete preenchido',
    );
    expect((root().querySelector('#default-variant-price') as HTMLInputElement).value).toBe(
      'R$ 15,00',
    );
    expect(text()).toContain('Conflito de combinação. Revise as opções.');
  });

  it('edits products with active categories while retaining the current inactive category', () => {
    fixture.componentRef.setInput('produto', simpleProduct);
    fixture.detectChanges();
    const options = Array.from(root().querySelectorAll('#product-category option')).map((option) =>
      option.textContent?.trim(),
    );
    expect(options).toContain('Velas (inativa — categoria atual)');
    expect(options).toContain('Sabonetes');
    expect(options).toHaveLength(3);

    setField('product-name', 'Sabonete editado');
    setField('product-category', 'active');
    submit();
    const submission = submissions[0];
    expect(submission?.mode === 'edit' && submission.request.variantes).toEqual([
      expect.objectContaining({
        id: 'variant-1',
        identificacao: 'Padrão',
        unidadeVenda: 'UN',
        precoVenda: 19.9,
        padrao: true,
        ativa: true,
        atualizarMedidas: true,
      }),
    ]);
  });

  function chooseCombination(index: number, values: string[]): void {
    const refs = ['caracteristica-1', 'caracteristica-2', 'caracteristica-3'];
    values.forEach((value, featureIndex) => {
      const field = root().querySelector(
        `#selection-${index}-${refs[featureIndex]}`,
      ) as HTMLSelectElement | null;
      if (!field) {
        throw new Error(`Combination selector not found for ${index}/${refs[featureIndex]}`);
      }
      setField(field.id, value);
    });
  }

  function choosePrimaryCombination(index: number): void {
    const radio = root().querySelectorAll<HTMLInputElement>(
      'input[name="principal-combination"]',
    )[index];
    if (!radio) {
      throw new Error(`Primary option control not found: ${index}`);
    }
    radio.checked = true;
    radio.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function chooseSelection(index: number, featureReference: string, valueReference: string): void {
    setField(`selection-${index}-${featureReference}`, valueReference);
  }

  function clickByLabel(label: string): void {
    const button = root().querySelector(`[aria-label="${label}"]`) as HTMLButtonElement | null;
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    button.click();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return root().textContent ?? '';
  }

  function setField(id: string, value: string): void {
    const field = root().querySelector(`#${id}`) as
      HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null;
    if (!field) {
      throw new Error(`Field not found: ${id}`);
    }
    field.value = value;
    field.dispatchEvent(
      new Event(field instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
    fixture.detectChanges();
  }

  function clickButton(label: string, occurrence = 0): void {
    const buttons = Array.from(root().querySelectorAll('button')).filter(
      (item) => item.textContent?.trim() === label,
    );
    const button = buttons[occurrence];
    if (!button) {
      throw new Error(`Button not found: ${label} (${occurrence})`);
    }
    button.click();
    fixture.detectChanges();
  }

  function selectMode(id: string): void {
    const radio = root().querySelector(`#${id}`) as HTMLInputElement;
    radio.checked = true;
    radio.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function submit(): void {
    const form = root().querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
});
