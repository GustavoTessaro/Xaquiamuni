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
    this.form.reset({
      nome: item?.nome ?? '',
      telefone: item?.telefone ?? '',
      email: item?.email ?? '',
      observacoes: item?.observacoes ?? '',
    });
    this.formularioAberto.set(true);
  }

  protected salvar(): void {
    if (this.salvando()) return;
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
      }),
      this.editando() ? 'Alterações salvas.' : 'Fornecedor cadastrado.',
    );
  }

  protected inativar(id: string): void {
    if (this.salvando() || this.confirmar() !== id) return;
    this.executar(this.api.inativar(id), 'Fornecedor inativado.');
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
        return 'Já existe um fornecedor com esse nome.';
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
