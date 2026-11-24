import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, finalize } from 'rxjs';
import { Fornecedor, FornecedoresService } from '../../core/fornecedores.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, ReactiveFormsModule],
  templateUrl: './admin-fornecedores.component.html',
})
export class AdminFornecedoresComponent {
  private readonly api = inject(FornecedoresService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly lista = signal<Fornecedor[]>([]);
  protected readonly carregando = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erroLista = signal('');
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');
  protected readonly busca = signal('');
  protected readonly editando = signal<Fornecedor | null>(null);
  protected readonly formularioAberto = signal(false);
  protected readonly confirmar = signal<string | null>(null);
  protected readonly exclusaoPendente = signal<{ id: string; versao: string; chave: string } | null>(null);
  protected readonly versaoDesatualizada = signal(false);
  protected readonly exclusaoBloqueada = signal(false);
  protected readonly visiveis = computed(() => {
    const termo = this.normalizar(this.busca());
    return this.lista().filter((item) => this.normalizar(item.nome).includes(termo));
  });
  protected readonly form = inject(FormBuilder).nonNullable.group({
    nome: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(120)]],
    telefone: ['', [Validators.maxLength(30), Validators.pattern(/^[0-9 +().-]*$/)]],
    email: ['', [Validators.email, Validators.maxLength(254)]],
    observacoes: ['', Validators.maxLength(1000)],
  });

  constructor() {
    this.carregar();
  }

  protected carregar(): void {
    if (this.carregando()) return;
    this.carregando.set(true);
    this.erroLista.set('');
    this.api
      .listar()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (lista) => this.lista.set(lista),
        error: (erro: unknown) =>
          this.erroLista.set(this.mensagem(erro, 'Não foi possível carregar os fornecedores.')),
      });
  }

  protected abrir(item: Fornecedor | null): void {
    this.editando.set(item);
    this.erro.set('');
    this.sucesso.set('');
    this.confirmar.set(null);
    this.versaoDesatualizada.set(false);
    this.exclusaoBloqueada.set(false);
    this.form.reset({
      nome: item?.nome ?? '',
      telefone: item?.telefone ?? '',
      email: item?.email ?? '',
      observacoes: item?.observacoes ?? '',
    });
    this.formularioAberto.set(true);
  }

  protected salvar(): void {
    if (this.salvando() || this.exclusaoPendente() || this.versaoDesatualizada()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { nome, telefone, email, observacoes } = this.form.getRawValue();
    this.executar(
      this.api.salvar(this.editando()?.id ?? null, {
        nome: nome.trim(),
        telefone: telefone.trim() || null,
        email: email.trim() || null,
        observacoes: observacoes.trim() || null,
      }, this.editando()?.versao),
      this.editando() ? 'Alterações salvas.' : 'Fornecedor cadastrado.',
    );
  }

  protected inativar(id: string): void {
    if (this.salvando() || this.confirmar() !== id) return;
    this.executar(this.api.inativar(id), 'Fornecedor inativado.');
  }

  protected oferecerInativacao(item: Fornecedor): void {
    this.formularioAberto.set(false);
    this.erro.set('');
    this.confirmar.set(item.id);
  }

  protected recarregarFornecedor(item: Fornecedor): void {
    if (this.salvando()) return;
    this.salvando.set(true);
    this.api.obter(item.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.salvando.set(false)),
    ).subscribe({
      next: atual => {
        this.lista.update(itens => itens.map(f => f.id === atual.id ? atual : f));
        this.abrir(atual);
      },
      error: (erro: unknown) => this.erro.set(this.mensagem(erro, 'Não foi possível recarregar o fornecedor.')),
    });
  }

  protected excluir(item: Fornecedor): void {
    if (this.salvando() || this.versaoDesatualizada() || !item.versao) return;
    if (!this.exclusaoPendente()) {
      if (!window.confirm(`Excluir “${item.nome}”? A exclusão é permanente.`)) return;
      this.exclusaoPendente.set({ id: item.id, versao: item.versao, chave: crypto.randomUUID() });
    }
    const tentativa = this.exclusaoPendente()!;
    this.salvando.set(true);
    this.erro.set('');
    this.exclusaoBloqueada.set(false);
    this.api.excluir(tentativa.id, tentativa.versao, tentativa.chave).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.salvando.set(false)),
    ).subscribe({
      next: () => {
        this.exclusaoPendente.set(null);
        this.lista.update(itens => itens.filter(f => f.id !== tentativa.id));
        this.formularioAberto.set(false);
        this.sucesso.set('Fornecedor excluído.');
        this.carregar();
      },
      error: (erro: unknown) => {
        if (erro instanceof HttpErrorResponse && erro.status >= 400 && erro.status < 500) {
          this.exclusaoPendente.set(null);
          const codigo = erro.error?.codigo;
          this.exclusaoBloqueada.set(['fornecedor_com_compras', 'fornecedor_com_historico', 'fornecedor_historico_inconclusivo'].includes(codigo));
          this.erro.set(this.mensagem(erro, 'Não foi possível excluir o fornecedor.'));
        } else {
          this.erro.set('Resultado da exclusão desconhecido. Recupere a tentativa antes de continuar.');
        }
      },
    });
  }

  private executar(requisicao: Observable<Fornecedor>, mensagem: string): void {
    this.salvando.set(true);
    this.erro.set('');
    this.sucesso.set('');
    requisicao
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvando.set(false)),
      )
      .subscribe({
        next: (item) => {
          this.lista.update((itens) =>
            [...itens.filter((atual) => atual.id !== item.id), item].sort((a, b) =>
              a.nome.localeCompare(b.nome, 'pt-BR'),
            ),
          );
          this.formularioAberto.set(false);
          this.confirmar.set(null);
          this.sucesso.set(mensagem);
        },
        error: (erro: unknown) =>
          this.erro.set(
            this.mensagem(erro, 'Não foi possível salvar. Confira a conexão e tente novamente.'),
          ),
      });
  }

  private mensagem(erro: unknown, padrao: string): string {
    if (!(erro instanceof HttpErrorResponse)) return padrao;
    switch (erro.status) {
      case 401:
        void this.router.navigateByUrl('/admin/login');
        return 'Sua sessão expirou.';
      case 403:
        return 'Acesso negado ou sessão inválida. Entre novamente e tente de novo.';
      case 404:
        return 'Fornecedor não encontrado. Atualize a listagem.';
      case 409:
        switch (erro.error?.codigo) {
          case 'versao_desatualizada':
          case 'conflito_concorrencia':
            this.versaoDesatualizada.set(true);
            return 'O cadastro foi alterado. Recarregue os dados antes de tentar novamente.';
          case 'fornecedor_com_compras': return 'Há compras vinculadas a este fornecedor. O cadastro foi preservado.';
          case 'fornecedor_com_historico': return 'Este fornecedor já foi utilizado em uma compra. O cadastro foi preservado.';
          case 'fornecedor_historico_inconclusivo': return 'Não foi possível confirmar que este fornecedor nunca foi utilizado. O cadastro foi preservado.';
          case 'chave_em_uso': return 'A tentativa já foi utilizada em outra operação. Atualize a listagem.';
          default: return 'Já existe um fornecedor com esse nome.';
        }
      case 400: {
        const detalhe = erro.error?.errors?.fornecedor?.[0];
        return typeof detalhe === 'string'
          ? detalhe
          : 'Não foi possível validar a solicitação. Confira os dados e tente novamente.';
      }
      default:
        return padrao;
    }
  }

  private normalizar(valor: string): string {
    return valor
      .trim()
      .replace(/\s+/g, ' ')
      .normalize('NFD')
      .replace(/\p{M}/gu, '')
      .toLocaleLowerCase('pt-BR');
  }
}
