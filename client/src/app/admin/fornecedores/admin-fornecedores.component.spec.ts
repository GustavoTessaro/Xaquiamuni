import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';
import { Fornecedor, FornecedoresService } from '../../core/fornecedores.service';
import { AdminFornecedoresComponent } from './admin-fornecedores.component';

const item: Fornecedor = {
  id: 'f1',
  versao: '00112233-4455-4677-8899-aabbccddeeff',
  nome: 'Ervas São João',
  ativo: true,
  email: null,
  telefone: null,
  observacoes: null,
};

function montar(resposta: Observable<Fornecedor[]> = of([item])) {
  const api = {
    listar: vi.fn(() => resposta),
    salvar: vi.fn(() => of(item)),
    obter: vi.fn(() => of(item)),
    excluir: vi.fn<FornecedoresService['excluir']>(() => of(undefined)),
    inativar: vi.fn(() => of({ ...item, ativo: false })),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: FornecedoresService, useValue: api }],
  });
  const fixture = TestBed.createComponent(AdminFornecedoresComponent);
  return { api, fixture, root: fixture.nativeElement as HTMLElement };
}

describe('AdminFornecedoresComponent', () => {
  it('lista, busca pelo nome e mantém fornecedores inativos consultáveis', () => {
    const { fixture, root } = montar(
      of([item, { ...item, id: 'f2', nome: 'Vidros', ativo: false }]),
    );
    fixture.detectChanges();
    expect(root.textContent).toContain('Ervas São João');
    expect(root.textContent).toContain('Inativo');
    expect(root.querySelector('a[href="/admin/fornecedores"]')?.getAttribute('aria-current')).toBe(
      'page',
    );
    const busca = root.querySelector<HTMLInputElement>('#supplier-search')!;
    busca.value = 'sao joao';
    busca.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(root.textContent).toContain('Ervas São João');
    expect(root.textContent).not.toContain('Vidros');
    busca.value = 'inexistente';
    busca.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(root.textContent).toContain('Nenhum fornecedor encontrado.');
  });

  it('exibe carregamento, erro recuperável e lista vazia', () => {
    const resposta = new Subject<Fornecedor[]>();
    const { fixture, root, api } = montar(resposta);
    api.listar.mockReturnValueOnce(of([]));
    fixture.detectChanges();
    expect(root.textContent).toContain('Carregando fornecedores');
    resposta.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('carregar os fornecedores');
    root.querySelector<HTMLButtonElement>('section button.underline')!.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Nenhum fornecedor cadastrado.');
  });

  it('valida o nome, cadastra, edita e confirma inativação', () => {
    const { fixture, root, api } = montar();
    const clicar = (texto: string) => {
      [...root.querySelectorAll('button')].find((b) => b.textContent.trim() === texto)!.click();
      fixture.detectChanges();
    };
    fixture.detectChanges();
    clicar('Novo fornecedor');
    clicar('Salvar fornecedor');
    expect(api.salvar).not.toHaveBeenCalled();
    expect(root.querySelector('#supplier-name')?.getAttribute('aria-describedby')).toBe(
      'supplier-name-error',
    );
    const campo = root.querySelector<HTMLInputElement>('#supplier-name')!;
    campo.value = item.nome;
    campo.dispatchEvent(new Event('input'));
    clicar('Salvar fornecedor');
    expect(api.salvar).toHaveBeenCalledWith(null, {
      nome: item.nome,
      email: null,
      telefone: null,
      observacoes: null,
    }, undefined);
    expect(root.textContent).toContain('Fornecedor cadastrado.');
    clicar('Editar');
    expect(root.querySelector<HTMLInputElement>('#supplier-name')?.value).toBe(item.nome);
    api.salvar.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409 })));
    clicar('Salvar fornecedor');
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Já existe');
    clicar('Salvar fornecedor');
    expect(api.salvar).toHaveBeenLastCalledWith(
      item.id,
      expect.objectContaining({ nome: item.nome }),
      item.versao,
    );
    clicar('Inativar');
    expect(api.inativar).not.toHaveBeenCalled();
    clicar('Confirmar inativação');
    expect(api.inativar).toHaveBeenCalledWith(item.id);
    expect(root.textContent).toContain('Inativo');
    expect(root.textContent).toContain('Fornecedor inativado.');
  });

  it('cancela a confirmação, recupera a resposta com a mesma chave e atualiza a lista', () => {
    const { fixture, root, api } = montar();
    const resposta = new Subject<void>();
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const clicar = (texto: string) => {
      [...root.querySelectorAll('button')].find(b => b.textContent.trim() === texto)!.click();
      fixture.detectChanges();
    };
    try {
      fixture.detectChanges();
      clicar('Editar');
      clicar('Excluir fornecedor');
      expect(confirmacao).toHaveBeenCalledWith(expect.stringContaining(item.nome));
      expect(api.excluir).not.toHaveBeenCalled();
      confirmacao.mockReturnValue(true);
      api.excluir.mockReturnValueOnce(resposta);
      clicar('Excluir fornecedor');
      clicar('Excluindo…');
      expect(api.excluir).toHaveBeenCalledTimes(1);
      resposta.error(new HttpErrorResponse({ status: 0 }));
      fixture.detectChanges();
      api.listar.mockReturnValue(of([]));
      clicar('Recuperar exclusão');
      expect(api.excluir.mock.calls[0]).toEqual(api.excluir.mock.calls[1]);
      expect(root.textContent).toContain('Fornecedor excluído.');
      expect(root.textContent).toContain('Nenhum fornecedor cadastrado.');
    } finally {
      confirmacao.mockRestore();
    }
  });

  it.each([
    ['fornecedor_com_compras', 'Há compras vinculadas'],
    ['fornecedor_com_historico', 'já foi utilizado'],
    ['fornecedor_historico_inconclusivo', 'Não foi possível confirmar'],
  ])('preserva o cadastro e oferece inativação quando o servidor retorna %s', (codigo, mensagem) => {
    const { fixture, root, api } = montar();
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const clicar = (texto: string) => {
      [...root.querySelectorAll('button')].find(b => b.textContent.trim() === texto)!.click();
      fixture.detectChanges();
    };
    try {
      api.excluir.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409, error: { codigo } })));
      fixture.detectChanges();
      clicar('Editar');
      clicar('Excluir fornecedor');
      expect(root.querySelector('[role=alert]')?.textContent).toContain(mensagem);
      clicar('Inativar fornecedor');
      expect(api.inativar).not.toHaveBeenCalled();
      clicar('Confirmar inativação');
      expect(api.inativar).toHaveBeenCalledWith(item.id);
    } finally {
      confirmacao.mockRestore();
    }
  });

  it('recarrega a versão e exige nova confirmação após conflito', () => {
    const { fixture, root, api } = montar();
    const confirmacao = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const clicar = (texto: string) => {
      [...root.querySelectorAll('button')].find(b => b.textContent.trim() === texto)!.click();
      fixture.detectChanges();
    };
    try {
      api.excluir.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
        status: 409, error: { codigo: 'versao_desatualizada' },
      })));
      api.obter.mockReturnValueOnce(of({ ...item, versao: 'versao-nova' }));
      fixture.detectChanges();
      clicar('Editar');
      clicar('Excluir fornecedor');
      clicar('Recarregar fornecedor');
      expect(api.excluir).toHaveBeenCalledTimes(1);
      clicar('Excluir fornecedor');
      expect(confirmacao).toHaveBeenCalledTimes(2);
      expect(api.excluir).toHaveBeenLastCalledWith(item.id, 'versao-nova', expect.any(String));
    } finally {
      confirmacao.mockRestore();
    }
  });
});
