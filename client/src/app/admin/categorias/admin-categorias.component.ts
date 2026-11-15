import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { AdminCategoria, CategoriasService, CategoriaWriteRequest } from '../../core/admin-categorias/categorias.service';
import { CategoriaFormComponent } from './categoria-form.component';

type CategoriaFilter = 'all' | 'active' | 'inactive';
type CategoriaFormMode = 'create' | 'edit' | null;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, CategoriaFormComponent],
  selector: 'app-admin-categorias',
  styleUrl: './admin-categorias.component.css',
  templateUrl: './admin-categorias.component.html',
})
export class AdminCategoriasComponent {
  protected readonly categorias = signal<AdminCategoria[]>([]);
  protected readonly carregando = signal(false);
  protected readonly erroCarregamento = signal('');
  protected readonly mensagem = signal('');
  protected readonly busca = signal('');
  protected readonly filtro = signal<CategoriaFilter>('all');
  protected readonly modoFormulario = signal<CategoriaFormMode>(null);
  protected readonly categoriaSelecionada = signal<AdminCategoria | null>(null);
  protected readonly carregandoEdicao = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erroFormulario = signal('');
  protected readonly statusEmAtualizacao = signal<ReadonlySet<string>>(new Set());

  protected readonly total = computed(() => this.categorias().length);
  protected readonly ativas = computed(() => this.categorias().filter((categoria) => categoria.ativa).length);
  protected readonly inativas = computed(() => this.total() - this.ativas());
  protected readonly categoriasVisiveis = computed(() => {
    const busca = this.busca().trim().toLocaleLowerCase('pt-BR');
    const filtro = this.filtro();

    return this.categorias().filter((categoria) => {
      const nomeCorresponde = !busca || categoria.nome.toLocaleLowerCase('pt-BR').includes(busca);
      const statusCorresponde =
        filtro === 'all' || (filtro === 'active' ? categoria.ativa : !categoria.ativa);
      return nomeCorresponde && statusCorresponde;
    });
  });

  private readonly categoriasService = inject(CategoriasService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  constructor() {
    this.carregarCategorias();
  }

  protected carregarCategorias(): void {
    if (this.carregando()) {
      return;
    }

    this.carregando.set(true);
    this.erroCarregamento.set('');
    this.categoriasService.listar()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (categorias) => this.categorias.set(categorias),
        error: (error: unknown) => {
          if (this.redirecionarSessaoExpirada(error)) {
            return;
          }
          this.erroCarregamento.set(
            error instanceof HttpErrorResponse && error.status === 403
              ? 'Você não tem permissão para acessar as categorias.'
              : 'Não foi possível carregar as categorias. Tente novamente.',
          );
        },
      });
  }

  protected abrirNovaCategoria(): void {
    if (this.salvando()) {
      return;
    }

    this.mensagem.set('');
    this.erroFormulario.set('');
    this.categoriaSelecionada.set(null);
    this.modoFormulario.set('create');
  }

  protected editarCategoria(categoria: AdminCategoria): void {
    if (this.carregandoEdicao() || this.salvando()) {
      return;
    }

    this.mensagem.set('');
    this.erroFormulario.set('');
    this.carregandoEdicao.set(true);
    this.categoriasService.obter(categoria.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoEdicao.set(false)),
      )
      .subscribe({
        next: (categoriaAtualizada) => {
          this.categoriaSelecionada.set(categoriaAtualizada);
          this.modoFormulario.set('edit');
        },
        error: (error: unknown) => {
          if (this.redirecionarSessaoExpirada(error)) {
            return;
          }
          this.erroCarregamento.set(
            error instanceof HttpErrorResponse && error.status === 403
              ? 'Você não tem permissão para acessar esta categoria.'
              : error instanceof HttpErrorResponse && error.status === 404
                ? 'Esta categoria não está mais disponível. Atualize a listagem.'
                : 'Não foi possível carregar esta categoria. Tente novamente.',
          );
        },
      });
  }

  protected cancelarFormulario(): void {
    if (this.salvando()) {
      return;
    }
    this.modoFormulario.set(null);
    this.categoriaSelecionada.set(null);
    this.erroFormulario.set('');
  }

  protected salvarCategoria(request: CategoriaWriteRequest): void {
    if (this.salvando()) {
      return;
    }

    const modo = this.modoFormulario();
    const categoria = this.categoriaSelecionada();
    if (!modo || (modo === 'edit' && !categoria)) {
      return;
    }

    let operation;
    if (modo === 'create') {
      operation = this.categoriasService.criar(request);
    } else if (categoria) {
      operation = this.categoriasService.editar(categoria.id, request);
    } else {
      return;
    }

    this.salvando.set(true);
    this.erroFormulario.set('');
    this.mensagem.set('');

    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvando.set(false)),
      )
      .subscribe({
        next: () => {
          this.modoFormulario.set(null);
          this.categoriaSelecionada.set(null);
          this.mensagem.set(modo === 'create'
            ? 'Categoria criada com sucesso.'
            : 'Categoria atualizada com sucesso.');
          this.carregarCategorias();
        },
        error: (error: unknown) => this.tratarErroFormulario(error),
      });
  }

  protected alterarStatus(categoria: AdminCategoria): void {
    if (this.statusEmAtualizacao().has(categoria.id)) {
      return;
    }

    const ativa = !categoria.ativa;
    if (categoria.ativa && !window.confirm(
      'Ao desativar esta categoria, ela deixará de aparecer na futura vitrine pública. Deseja continuar?',
    )) {
      return;
    }

    this.atualizarStatusPendente(categoria.id, true);
    this.mensagem.set('');
    this.categoriasService.alterarStatus(categoria.id, { ativa })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.atualizarStatusPendente(categoria.id, false)),
      )
      .subscribe({
        next: (categoriaAtualizada) => {
          this.categorias.update((categorias) =>
            categorias.map((item) => item.id === categoria.id ? categoriaAtualizada : item),
          );
          this.mensagem.set(ativa
            ? 'Categoria ativada com sucesso.'
            : 'Categoria desativada com sucesso.');
          this.erroCarregamento.set('');
        },
        error: (error: unknown) => {
          if (this.redirecionarSessaoExpirada(error)) {
            return;
          }
          this.erroCarregamento.set(this.mensagemErroStatus(error));
        },
      });
  }

  protected definirBusca(value: string): void {
    this.busca.set(value);
  }

  protected lerBusca(event: Event): void {
    const target = event.target;
    if (target instanceof HTMLInputElement) {
      this.definirBusca(target.value);
    }
  }

  protected definirFiltro(filtro: CategoriaFilter): void {
    this.filtro.set(filtro);
  }

  protected filtroSelecionado(filtro: CategoriaFilter): boolean {
    return this.filtro() === filtro;
  }

  protected estaAtualizandoStatus(id: string): boolean {
    return this.statusEmAtualizacao().has(id);
  }

  private tratarErroFormulario(error: unknown): void {
    if (this.redirecionarSessaoExpirada(error)) {
      return;
    }

    if (error instanceof HttpErrorResponse) {
      if (error.status === 400) {
        this.erroFormulario.set(this.mensagemValidacao(error));
        return;
      }
      if (error.status === 403) {
        this.erroFormulario.set('Você não tem permissão para alterar categorias.');
        return;
      }
      if (error.status === 404) {
        this.erroFormulario.set('Esta categoria não está mais disponível. Atualize a listagem.');
        return;
      }
      if (error.status === 409) {
        this.erroFormulario.set('Já existe uma categoria com esse nome. Escolha outro nome.');
        return;
      }
    }

    this.erroFormulario.set('Não foi possível salvar a categoria. Tente novamente.');
  }

  private mensagemErroStatus(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 403) {
        return 'Você não tem permissão para alterar o status desta categoria.';
      }
      if (error.status === 404) {
        return 'Esta categoria não está mais disponível. Atualize a listagem.';
      }
    }
    return 'Não foi possível alterar o status da categoria. Tente novamente.';
  }

  private mensagemValidacao(error: HttpErrorResponse): string {
    const response = error.error as { errors?: Record<string, unknown> } | null;
    const errors = response && typeof response === 'object' ? response.errors : null;
    if (!errors || typeof errors !== 'object') {
      return 'Os dados informados são inválidos. Revise os campos e tente novamente.';
    }

    const labels: Record<string, string> = {
      nome: 'nome',
      descricao: 'descrição',
      ordemExibicao: 'ordem de exibição',
      ativa: 'status',
      categoria: 'dados da categoria',
    };
    const fields = Object.keys(errors)
      .map((field) => labels[field])
      .filter((field): field is string => Boolean(field));

    return fields.length
      ? `Revise ${fields.join(', ')} e tente novamente.`
      : 'Os dados informados são inválidos. Revise os campos e tente novamente.';
  }

  private redirecionarSessaoExpirada(error: unknown): boolean {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return true;
    }
    return false;
  }

  private atualizarStatusPendente(id: string, pendente: boolean): void {
    this.statusEmAtualizacao.update((ids) => {
      const updated = new Set(ids);
      if (pendente) {
        updated.add(id);
      } else {
        updated.delete(id);
      }
      return updated;
    });
  }
}
