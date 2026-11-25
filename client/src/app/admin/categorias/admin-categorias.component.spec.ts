import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import {
  AdminCategoria,
  CategoriasService,
  CategoriaWriteRequest,
} from '../../core/admin-categorias/categorias.service';
import { AdminCategoriasComponent } from './admin-categorias.component';

describe('AdminCategoriasComponent', () => {
  let fixture: ComponentFixture<AdminCategoriasComponent>;
  let listar: ReturnType<typeof vi.fn<CategoriasService['listar']>>;
  let obter: ReturnType<typeof vi.fn<CategoriasService['obter']>>;
  let criar: ReturnType<typeof vi.fn<CategoriasService['criar']>>;
  let editar: ReturnType<typeof vi.fn<CategoriasService['editar']>>;
  let excluir: ReturnType<typeof vi.fn<CategoriasService['excluir']>>;
  let alterarStatus: ReturnType<typeof vi.fn<CategoriasService['alterarStatus']>>;
  let navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>>;

  const categories: AdminCategoria[] = [
    {
      id: 'category-soap',
      versao: '661ce433-8b05-4a12-b577-a6473f6ddf3d',
      nome: 'Sabonetes Artesanais',
      descricao: 'Feitos à mão',
      ativa: true,
      ordemExibicao: 0,
    },
    {
      id: 'category-candle',
      nome: 'Velas Aromáticas',
      descricao: null,
      ativa: false,
      ordemExibicao: 1,
    },
    {
      id: 'category-natural',
      nome: 'Cuidados Naturais',
      descricao: 'Linha botânica',
      ativa: true,
      ordemExibicao: 2,
    },
  ];

  beforeEach(async () => {
    listar = vi.fn<CategoriasService['listar']>().mockReturnValue(of(categories));
    obter = vi.fn<CategoriasService['obter']>();
    criar = vi.fn<CategoriasService['criar']>();
    editar = vi.fn<CategoriasService['editar']>();
    excluir = vi.fn<CategoriasService['excluir']>();
    alterarStatus = vi.fn<CategoriasService['alterarStatus']>();

    await TestBed.configureTestingModule({
      imports: [AdminCategoriasComponent],
      providers: [
        provideRouter([]),
        {
          provide: CategoriasService,
          useValue: { listar, obter, criar, editar, alterarStatus, excluir },
        },
      ],
    }).compileComponents();
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('loads categories and calculates summary counts from the full list', () => {
    render();

    expect(listar).toHaveBeenCalledOnce();
    expect(text()).toContain('Categorias');
    expect(text()).toContain('Sabonetes Artesanais');
    expect(text()).toContain('Velas Aromáticas');
    expect(fixture.nativeElement.querySelector('[aria-label="Total de categorias"]').textContent).toContain('3');
    expect(fixture.nativeElement.querySelector('[aria-label="Categorias ativas"]').textContent).toContain('2');
    expect(fixture.nativeElement.querySelector('[aria-label="Categorias inativas"]').textContent).toContain('1');
  });

  it('combines case-insensitive name search with the selected status filter without reordering', () => {
    render();
    setSearch('VELA');
    clickButton('Ativas');
    expect(categoryNames()).toEqual([]);

    clickButton('Inativas');
    expect(categoryNames()).toEqual(['Velas Aromáticas']);
    setSearch('');
    clickButton('Todas');
    expect(categoryNames()).toEqual([
      'Sabonetes Artesanais',
      'Velas Aromáticas',
      'Cuidados Naturais',
    ]);
  });

  it('shows distinct empty-list and no-search-results states', () => {
    listar.mockReturnValueOnce(of([]));
    render();
    expect(text()).toContain('Nenhuma categoria cadastrada');

    listar.mockReturnValueOnce(of(categories));
    fixture.destroy();
    render();
    setSearch('não existe');
    expect(text()).toContain('Nenhuma categoria encontrada');
    expect(text()).not.toContain('Nenhuma categoria cadastrada');
  });

  it('shows a safe load error and allows retrying the list request', () => {
    listar
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 503,
        error: { message: 'internal server details' },
      })))
      .mockReturnValueOnce(of(categories));
    render();

    expect(text()).toContain('Não foi possível carregar as categorias.');
    expect(text()).not.toContain('internal server details');
    clickButton('Tentar novamente');
    expect(listar).toHaveBeenCalledTimes(2);
    expect(categoryNames()).toHaveLength(3);
  });

  it('validates the reusable form and creates a category with feedback', () => {
    listar.mockReturnValueOnce(of([])).mockReturnValueOnce(of([categories[0]]));
    criar.mockReturnValue(of(categories[0]));
    render();
    clickButton('Nova categoria');
    setField('category-order', '');
    submitForm();

    expect(text()).toContain('Informe o nome da categoria.');
    expect(text()).toContain('Informe a ordem de exibição.');
    expect(criar).not.toHaveBeenCalled();

    setField('category-name', 'Sabonetes Artesanais');
    setField('category-description', 'Feitos à mão');
    setField('category-order', '0');
    submitForm();

    expect(criar).toHaveBeenCalledOnce();
    expect(criar).toHaveBeenCalledWith({
      nome: 'Sabonetes Artesanais',
      descricao: 'Feitos à mão',
      ordemExibicao: 0,
    } satisfies CategoriaWriteRequest);
    expect(text()).toContain('Categoria criada com sucesso.');
    expect(listar).toHaveBeenCalledTimes(2);
  });

  it('loads fresh data for editing and updates without changing category status', () => {
    const updated = { ...categories[0], nome: 'Sabonetes Naturais' };
    listar.mockReturnValueOnce(of(categories)).mockReturnValueOnce(of([updated]));
    obter.mockReturnValue(of(categories[0]));
    editar.mockReturnValue(of(updated));
    render();
    clickButton('Editar Sabonetes Artesanais');

    expect(obter).toHaveBeenCalledWith(categories[0].id);
    expect((fixture.nativeElement.querySelector('#category-name') as HTMLInputElement).value)
      .toBe(categories[0].nome);
    setField('category-name', updated.nome);
    submitForm();

    expect(editar).toHaveBeenCalledWith(categories[0].id, {
      nome: updated.nome,
      descricao: categories[0].descricao,
      ordemExibicao: categories[0].ordemExibicao,
    }, categories[0].versao);
    expect(text()).toContain('Categoria atualizada com sucesso.');
    expect(categoryNames()).toContain(updated.nome);
  });

  it('refreshes form fields when switching the open category', () => {
    obter.mockReturnValueOnce(of(categories[0])).mockReturnValueOnce(of(categories[1]));
    render();
    clickButton('Editar ' + categories[0].nome);
    setField('category-name', 'Unsaved');
    clickButton('Editar ' + categories[1].nome);
    expect((root().querySelector('#category-name') as HTMLInputElement).value).toBe(categories[1].nome);
  });

  it('preserves form values and shows safe conflict and validation feedback', () => {
    criar.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409,
      error: { detail: 'database index and query details' },
    })));
    render();
    clickButton('Nova categoria');
    setField('category-name', 'Nome já usado');
    setField('category-order', '1');
    submitForm();

    expect(text()).toContain('Já existe uma categoria com esse nome.');
    expect(text()).not.toContain('database index and query details');
    expect((fixture.nativeElement.querySelector('#category-name') as HTMLInputElement).value)
      .toBe('Nome já usado');

    criar.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: { errors: { ordemExibicao: ['mensagem interna não exibida'] } },
    })));
    submitForm();
    expect(text()).toContain('Revise ordem de exibição');
    expect(text()).not.toContain('mensagem interna não exibida');
  });

  it('prevents duplicate submissions while a create request is pending', () => {
    const pending = new Subject<AdminCategoria>();
    criar.mockReturnValue(pending);
    listar.mockReturnValueOnce(of([])).mockReturnValueOnce(of([categories[0]]));
    render();
    clickButton('Nova categoria');
    setField('category-name', 'Sabonetes Artesanais');
    setField('category-order', '0');
    submitForm();
    submitForm();

    expect(criar).toHaveBeenCalledOnce();
    expect((fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled)
      .toBe(true);

    pending.next(categories[0]);
    pending.complete();
    fixture.detectChanges();
    expect(text()).toContain('Categoria criada com sucesso.');
  });

  it('reports a category missing during edit and permits refreshing the list', () => {
    obter.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    render();
    clickButton('Editar Sabonetes Artesanais');

    expect(text()).toContain('Esta categoria não está mais disponível.');
    clickButton('Atualizar lista');
    expect(listar).toHaveBeenCalledTimes(2);
  });

  it('confirms deactivation, supports cancellation, and updates status only after success', () => {
    const confirm = vi.fn().mockReturnValue(false);
    vi.stubGlobal('confirm', confirm);
    const pending = new Subject<AdminCategoria>();
    alterarStatus.mockReturnValue(pending);
    render();

    clickButton('Desativar Sabonetes Artesanais');
    expect(confirm).toHaveBeenCalledOnce();
    expect(alterarStatus).not.toHaveBeenCalled();
    expect(text()).toContain('Ativa');

    confirm.mockReturnValue(true);
    clickButton('Desativar Sabonetes Artesanais');
    fixture.detectChanges();
    clickButton('Desativar Sabonetes Artesanais');
    expect(alterarStatus).toHaveBeenCalledOnce();
    expect(alterarStatus).toHaveBeenCalledWith(categories[0].id, { ativa: false });
    expect(text()).toContain('Ativa');

    pending.next({ ...categories[0], ativa: false });
    pending.complete();
    fixture.detectChanges();
    expect(text()).toContain('Inativa');
    expect(text()).toContain('Categoria desativada com sucesso.');
  });

  it('preserves status on failure and allows reactivation', () => {
    const confirm = vi.fn().mockReturnValue(true);
    vi.stubGlobal('confirm', confirm);
    alterarStatus
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 500,
        error: { message: 'internal detail' },
      })))
      .mockReturnValueOnce(of({ ...categories[1], ativa: true }));
    render();

    clickButton('Desativar Sabonetes Artesanais');
    expect(text()).toContain('Ativa');
    expect(text()).toContain('Não foi possível alterar o status');
    expect(text()).not.toContain('internal detail');

    clickButton('Ativar Velas Aromáticas');
    expect(alterarStatus).toHaveBeenCalledWith(categories[1].id, { ativa: true });
    expect(categoryStatus(categories[1].nome)).toBe('Ativa');
  });

  it('redirects expired sessions and reports forbidden access safely', () => {
    listar.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 401 })));
    render();
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/login');
    expect(text()).not.toContain('Não foi possível carregar as categorias.');

    listar.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 403,
      error: { detail: 'authorization internals' },
    })));
    fixture.destroy();
    render();
    expect(text()).toContain('Você não tem permissão para acessar as categorias.');
    expect(text()).not.toContain('authorization internals');
  });

  it('confirms permanent deletion and retries a lost response with the original key', () => {
    const confirm = vi.fn().mockReturnValue(false);
    vi.stubGlobal('confirm', confirm);
    obter.mockReturnValue(of(categories[0]));
    const pending = new Subject<void>();
    excluir.mockReturnValueOnce(pending).mockReturnValueOnce(of(void 0));
    listar.mockReturnValueOnce(of(categories)).mockReturnValueOnce(of(categories.slice(1)));
    render();
    clickButton('Editar Sabonetes Artesanais');
    clickButton('Excluir categoria');
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining(categories[0].nome));
    expect(excluir).not.toHaveBeenCalled();
    confirm.mockReturnValue(true);
    clickButton('Excluir categoria');
    clickButton('Excluindo…');
    expect(excluir).toHaveBeenCalledOnce();
    pending.error(new HttpErrorResponse({ status: 0 }));
    fixture.detectChanges();
    clickButton('Recuperar exclusão');
    expect(excluir.mock.calls[1]).toEqual(excluir.mock.calls[0]);
    expect(excluir.mock.calls[0]).toEqual([categories[0].id, categories[0].versao, expect.any(String)]);
    expect(categoryNames()).toEqual(categories.slice(1).map(category => category.nome));
  });

  it('loads the latest category from the list and recovers without fetching or re-confirming', () => {
    const confirmar = vi.fn().mockReturnValue(false);
    vi.stubGlobal('confirm', confirmar);
    const atual = { ...categories[0], nome: 'Categoria atualizada', versao: 'versao-atual' };
    obter.mockReturnValue(of(atual));
    excluir
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(of(void 0));
    listar.mockReturnValueOnce(of(categories)).mockReturnValueOnce(of(categories.slice(1)));
    render();
    clickButton('Excluir Sabonetes Artesanais');
    expect(confirmar).toHaveBeenCalledWith(expect.stringContaining(atual.nome));
    expect(excluir).not.toHaveBeenCalled();
    confirmar.mockReturnValue(true);
    clickButton('Excluir Sabonetes Artesanais');
    expect(excluir).toHaveBeenCalledWith(atual.id, atual.versao, expect.any(String));
    setSearch('ocultar categoria');
    clickButton('Recuperar exclusão de Categoria atualizada');
    expect(excluir.mock.calls[1]).toEqual(excluir.mock.calls[0]);
    expect(obter).toHaveBeenCalledTimes(2);
    expect(confirmar).toHaveBeenCalledTimes(2);
    expect((root().querySelector('#category-search') as HTMLInputElement).value).toBe('ocultar categoria');
    setSearch('');
    expect(categoryNames()).toEqual(categories.slice(1).map(categoria => categoria.nome));
  });

  it('requires a fresh confirmation after an outdated version and reports linked products', () => {
    const confirm = vi.fn().mockReturnValue(true);
    vi.stubGlobal('confirm', confirm);
    obter.mockReturnValue(of(categories[0]));
    excluir
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409, error: { codigo: 'versao_desatualizada' } })))
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409, error: { codigo: 'categoria_com_produtos' } })));
    render();
    clickButton('Editar Sabonetes Artesanais');
    clickButton('Excluir categoria');
    expect(text()).toContain('Recarregar categoria');
    clickButton('Recarregar categoria');
    expect(excluir).toHaveBeenCalledOnce();
    clickButton('Excluir categoria');
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(text()).toContain('produtos vinculados');
    expect(categoryNames()).toContain(categories[0].nome);
  });

  function render(): void {
    fixture = TestBed.createComponent(AdminCategoriasComponent);
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function categoryNames(): string[] {
    return Array.from(root().querySelectorAll('article h2'))
      .map(heading => heading.textContent?.trim() ?? '');
  }

  function categoryStatus(name: string): string | null {
    const article = Array.from(root().querySelectorAll('article'))
      .find(element => element.querySelector('h2')?.textContent?.trim() === name);
    return article?.querySelector('span')?.textContent?.trim() ?? null;
  }

  function clickButton(name: string): void {
    const button = Array.from(root().querySelectorAll('button'))
      .find(element =>
        element.getAttribute('aria-label') === name || element.textContent?.trim() === name);
    if (!button) {
      throw new Error(`Button not found: ${name}`);
    }
    button.click();
    fixture.detectChanges();
  }

  function setField(id: string, value: string): void {
    const field = root().querySelector(`#${id}`) as HTMLInputElement | HTMLTextAreaElement;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function setSearch(value: string): void {
    setField('category-search', value);
  }

  function submitForm(): void {
    const form = root().querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
});
