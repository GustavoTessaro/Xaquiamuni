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
import { Router } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { AdminCategoria, CategoriasService } from '../../core/admin-categorias/categorias.service';
import {
  AdminProduto,
  AdminProdutoVariante,
  AdminProdutoVarianteRequest,
  ProdutosService,
} from '../../core/admin-produtos/produtos.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { AdminProdutoFormComponent, ProdutoFormSubmission } from './admin-produto-form.component';
import { AdminProdutoVarianteFormComponent } from './admin-produto-variante-form.component';

type PublicacaoFilter = 'all' | 'published' | 'unpublished';
type VarianteFormMode = 'create' | 'edit' | null;

const UNIDADE_VENDA_LABEL: Readonly<Record<string, string>> = {
  UN: 'Unidade',
  KIT: 'Kit',
  PAR: 'Par',
  PCT: 'Pacote',
  CX: 'Caixa',
  CJ: 'Conjunto',
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, AdminProdutoFormComponent, AdminProdutoVarianteFormComponent],
  selector: 'app-admin-produtos',
  styleUrl: './admin-produtos.component.css',
  templateUrl: './admin-produtos.component.html',
})
export class AdminProdutosComponent {
  protected readonly produtos = signal<AdminProduto[]>([]);
  protected readonly categorias = signal<AdminCategoria[]>([]);
  protected readonly carregando = signal(false);
  protected readonly erroCarregamento = signal('');
  protected readonly erroOperacao = signal('');
  protected readonly mensagem = signal('');
  protected readonly busca = signal('');
  protected readonly categoriaFiltro = signal('');
  protected readonly publicacaoFiltro = signal<PublicacaoFilter>('all');
  protected readonly total = computed(() => this.produtos().length);
  protected readonly publicados = computed(
    () => this.produtos().filter((produto) => produto.publicado).length,
  );
  protected readonly naoPublicados = computed(() => this.total() - this.publicados());
  protected readonly produtosVisiveis = computed(() => {
    const busca = this.busca().trim().toLocaleLowerCase('pt-BR');
    const categoriaId = this.categoriaFiltro();
    const publicacao = this.publicacaoFiltro();
    return this.produtos().filter(
      (produto) =>
        (!busca || produto.nome.toLocaleLowerCase('pt-BR').includes(busca)) &&
        (!categoriaId || produto.categoriaId === categoriaId) &&
        (publicacao === 'all' ||
          (publicacao === 'published' ? produto.publicado : !produto.publicado)),
    );
  });

  protected readonly formularioProdutoVisivel = signal(false);
  protected readonly produtoEditando = signal<AdminProduto | null>(null);
  protected readonly salvandoProduto = signal(false);
  protected readonly carregandoDetalheId = signal('');
  protected readonly produtosEmPublicacao = signal<ReadonlySet<string>>(new Set());

  protected readonly produtoVariantes = signal<AdminProduto | null>(null);
  protected readonly carregandoVariantes = signal(false);
  protected readonly varianteFormMode = signal<VarianteFormMode>(null);
  protected readonly varianteEditando = signal<AdminProdutoVariante | null>(null);
  protected readonly salvandoVariante = signal(false);
  protected readonly erroVariantes = signal('');
  protected readonly mensagemVariantes = signal('');
  protected readonly variantesEmAtualizacao = signal<ReadonlySet<string>>(new Set());
  protected readonly varianteParaDesativar = signal<string | null>(null);
  protected readonly novoPadraoId = signal('');

  private readonly categoriasService = inject(CategoriasService);
  private readonly produtosService = inject(ProdutosService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  constructor() {
    this.carregarProdutos();
  }

  protected carregarProdutos(): void {
    if (this.carregando()) {
      return;
    }

    this.carregando.set(true);
    this.erroCarregamento.set('');
    forkJoin({
      produtos: this.produtosService.listar(),
      categorias: this.categoriasService.listar(),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: ({ produtos, categorias }) => {
          this.produtos.set(produtos);
          this.categorias.set(categorias);
          this.erroOperacao.set('');
        },
        error: (error: unknown) => {
          if (this.redirecionarSessaoExpirada(error)) {
            return;
          }
          this.erroCarregamento.set(
            error instanceof HttpErrorResponse && error.status === 403
              ? 'Você não tem permissão para acessar os produtos.'
              : 'Não foi possível carregar os produtos e categorias. Tente novamente.',
          );
        },
      });
  }

  protected abrirNovoProduto(): void {
    if (this.salvandoProduto() || this.carregandoDetalheId()) {
      return;
    }
    this.mensagem.set('');
    this.erroOperacao.set('');
    this.produtoEditando.set(null);
    this.formularioProdutoVisivel.set(true);
  }

  protected editarProduto(produto: AdminProduto): void {
    if (this.salvandoProduto() || this.carregandoDetalheId()) {
      return;
    }
    this.mensagem.set('');
    this.erroOperacao.set('');
    this.carregandoDetalheId.set(produto.id);
    this.produtosService
      .obter(produto.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoDetalheId.set('')),
      )
      .subscribe({
        next: (detalhe) => {
          this.produtoEditando.set(detalhe);
          this.formularioProdutoVisivel.set(true);
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroOperacao.set(
              this.mensagemErroHttp(error, 'Não foi possível carregar os dados atuais do produto.'),
            );
          }
        },
      });
  }

  protected cancelarFormularioProduto(): void {
    if (this.salvandoProduto()) {
      return;
    }
    this.formularioProdutoVisivel.set(false);
    this.produtoEditando.set(null);
  }

  protected salvarProduto(submission: ProdutoFormSubmission): void {
    if (this.salvandoProduto()) {
      return;
    }
    const produtoAtual = this.produtoEditando();
    const operation =
      submission.mode === 'create'
        ? this.produtosService.criar(submission.request)
        : produtoAtual
          ? this.produtosService.editar(produtoAtual.id, submission.request)
          : null;
    if (!operation) {
      return;
    }

    this.salvandoProduto.set(true);
    this.erroOperacao.set('');
    this.mensagem.set('');
    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvandoProduto.set(false)),
      )
      .subscribe({
        next: (produto) => {
          this.atualizarProduto(produto);
          this.formularioProdutoVisivel.set(false);
          this.produtoEditando.set(null);
          this.mensagem.set(
            submission.mode === 'create'
              ? 'Produto cadastrado com sucesso. Ele começa não publicado.'
              : 'Produto atualizado com sucesso.',
          );
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroOperacao.set(
              this.mensagemErroHttp(
                error,
                'Não foi possível salvar o produto. Revise os dados e tente novamente.',
              ),
            );
          }
        },
      });
  }

  protected abrirGerenciamentoVariantes(produto: AdminProduto): void {
    if (this.carregandoVariantes() || this.salvandoVariante()) {
      return;
    }
    this.carregandoVariantes.set(true);
    this.erroVariantes.set('');
    this.mensagemVariantes.set('');
    this.produtosService
      .obter(produto.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregandoVariantes.set(false)),
      )
      .subscribe({
        next: (detalhe) => {
          this.produtoVariantes.set(detalhe);
          this.varianteFormMode.set(null);
          this.varianteEditando.set(null);
          this.varianteParaDesativar.set(null);
          this.novoPadraoId.set('');
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroOperacao.set(
              this.mensagemErroHttp(error, 'Não foi possível carregar as opções do produto.'),
            );
          }
        },
      });
  }

  protected fecharGerenciamentoVariantes(): void {
    if (
      this.carregandoVariantes() ||
      this.salvandoVariante() ||
      this.variantesEmAtualizacao().size > 0
    ) {
      return;
    }
    this.produtoVariantes.set(null);
    this.varianteFormMode.set(null);
    this.erroVariantes.set('');
    this.varianteParaDesativar.set(null);
  }

  protected iniciarAdicaoVariante(): void {
    if (this.salvandoVariante() || this.variantesEmAtualizacao().size > 0) {
      return;
    }
    this.varianteEditando.set(null);
    this.varianteFormMode.set('create');
    this.erroVariantes.set('');
    this.mensagemVariantes.set('');
  }

  protected iniciarEdicaoVariante(variante: AdminProdutoVariante): void {
    if (this.salvandoVariante() || this.variantesEmAtualizacao().size > 0) {
      return;
    }
    this.varianteEditando.set(variante);
    this.varianteFormMode.set('edit');
    this.erroVariantes.set('');
    this.mensagemVariantes.set('');
  }

  protected cancelarFormularioVariante(): void {
    if (this.salvandoVariante()) {
      return;
    }
    this.varianteFormMode.set(null);
    this.varianteEditando.set(null);
    this.erroVariantes.set('');
  }

  protected salvarVariante(request: AdminProdutoVarianteRequest): void {
    if (this.salvandoVariante()) {
      return;
    }
    const produto = this.produtoVariantes();
    const mode = this.varianteFormMode();
    const variante = this.varianteEditando();
    const operation =
      produto && mode === 'create'
        ? this.produtosService.adicionarVariante(produto.id, request)
        : produto && mode === 'edit' && variante
          ? this.produtosService.editarVariante(produto.id, variante.id, request)
          : null;
    if (!operation) {
      return;
    }

    this.salvandoVariante.set(true);
    this.erroVariantes.set('');
    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.salvandoVariante.set(false)),
      )
      .subscribe({
        next: (updated) => {
          this.atualizarProduto(updated);
          this.produtoVariantes.set(updated);
          this.varianteFormMode.set(null);
          this.varianteEditando.set(null);
          this.mensagemVariantes.set(
            mode === 'create'
              ? 'Opção adicionada com sucesso.'
              : 'Opção atualizada com sucesso.',
          );
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroVariantes.set(
              this.mensagemErroHttp(
                error,
                'Não foi possível salvar a opção. Revise os dados e tente novamente.',
              ),
            );
          }
        },
      });
  }

  protected definirVariantePadrao(variante: AdminProdutoVariante): void {
    const produto = this.produtoVariantes();
    if (!produto || !variante.ativa || variante.padrao || this.variantesEmAtualizacao().size > 0) {
      return;
    }
    this.definirVariantePendente(produto.id, variante.id, true);
    this.erroVariantes.set('');
    this.produtosService
      .definirVariantePadrao(produto.id, variante.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.definirVariantePendente(produto.id, variante.id, false)),
      )
      .subscribe({
        next: (updated) => {
          this.atualizarProduto(updated);
          this.produtoVariantes.set(updated);
          this.mensagemVariantes.set('Opção principal atualizada.');
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroVariantes.set(
              this.mensagemErroHttp(error, 'Não foi possível alterar a opção principal.'),
            );
          }
        },
      });
  }

  protected alterarStatusVariante(variante: AdminProdutoVariante, ativa: boolean): void {
    const produto = this.produtoVariantes();
    if (!produto || this.variantesEmAtualizacao().size > 0) {
      return;
    }
    if (ativa) {
      this.enviarStatusVariante(produto, variante, true);
      return;
    }
    if (this.quantidadeVariantesAtivas(produto) <= 1) {
      return;
    }
    if (variante.padrao) {
      this.varianteParaDesativar.set(variante.id);
      this.novoPadraoId.set('');
      this.erroVariantes.set('');
      return;
    }
    this.enviarStatusVariante(produto, variante, false);
  }

  protected confirmarDesativacaoVariante(): void {
    const produto = this.produtoVariantes();
    const varianteId = this.varianteParaDesativar();
    const novaVarianteId = this.novoPadraoId();
    const variante = produto?.variantes.find((item) => item.id === varianteId);
    if (
      !produto ||
      !variante ||
      !variante.padrao ||
      !novaVarianteId ||
      !produto.variantes.some((item) => item.id === novaVarianteId && item.ativa)
    ) {
      return;
    }
    this.enviarStatusVariante(produto, variante, false, novaVarianteId);
  }

  protected cancelarDesativacaoVariante(): void {
    this.varianteParaDesativar.set(null);
    this.novoPadraoId.set('');
  }

  protected selecionarNovoPadrao(event: Event): void {
    const target = event.target;
    if (target instanceof HTMLSelectElement) {
      this.novoPadraoId.set(target.value);
    }
  }

  protected publicar(produto: AdminProduto): void {
    const atualizado = !produto.publicado;
    if (this.produtoPendente(produto.id)) {
      return;
    }
    this.definirProdutoPendente(produto.id, true);
    this.erroOperacao.set('');
    this.mensagem.set('');
    this.produtosService
      .alterarPublicacao(produto.id, { publicado: atualizado })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.definirProdutoPendente(produto.id, false)),
      )
      .subscribe({
        next: (updated) => {
          this.atualizarProduto(updated);
          this.mensagem.set(
            atualizado
              ? 'Produto publicado. A disponibilidade para compra depende do estoque.'
              : 'Produto despublicado com sucesso.',
          );
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroOperacao.set(
              this.mensagemErroHttp(
                error,
                atualizado
                  ? 'Não foi possível publicar o produto.'
                  : 'Não foi possível despublicar o produto.',
              ),
            );
          }
        },
      });
  }

  protected definirBusca(event: Event): void {
    const target = event.target;
    if (target instanceof HTMLInputElement) {
      this.busca.set(target.value);
    }
  }

  protected definirCategoria(event: Event): void {
    const target = event.target;
    if (target instanceof HTMLSelectElement) {
      this.categoriaFiltro.set(target.value);
    }
  }

  protected definirFiltroPublicacao(filtro: PublicacaoFilter): void {
    this.publicacaoFiltro.set(filtro);
  }

  protected filtroPublicacaoSelecionado(filtro: PublicacaoFilter): boolean {
    return this.publicacaoFiltro() === filtro;
  }

  protected limparFiltros(): void {
    this.busca.set('');
    this.categoriaFiltro.set('');
    this.publicacaoFiltro.set('all');
  }

  protected precoResumo(produto: AdminProduto): string {
    const ativas = produto.variantes.filter((variante) => variante.ativa);
    const padrao = produto.variantes.find((variante) => variante.padrao);
    if (ativas.length <= 1) {
      return this.formatarMoeda(padrao?.precoVenda ?? ativas[0]?.precoVenda);
    }
    const precos = ativas.map((variante) => variante.precoVenda);
    const menor = Math.min(...precos);
    const maior = Math.max(...precos);
    return menor === maior
      ? this.formatarMoeda(menor)
      : `${this.formatarMoeda(menor)} – ${this.formatarMoeda(maior)}`;
  }

  protected produtoSimples(produto: AdminProduto): boolean {
    return produto.caracteristicas.length === 0 && produto.variantes.length === 1;
  }

  protected resumoVendaSimples(produto: AdminProduto): string {
    const variante = produto.variantes[0];
    if (!variante) {
      return '';
    }
    const detalhes: string[] = [];
    if (variante.pesoLiquidoGramas != null) {
      detalhes.push(`${this.formatarMedida(variante.pesoLiquidoGramas)} g`);
    }
    if (variante.volumeLiquidoMililitros != null) {
      detalhes.push(`${this.formatarMedida(variante.volumeLiquidoMililitros)} ml`);
    }
    detalhes.push(UNIDADE_VENDA_LABEL[variante.unidadeVenda] ?? variante.unidadeVenda);
    return detalhes.join(' · ');
  }

  protected formatarPreco(preco: number): string {
    return this.formatarMoeda(preco);
  }

  protected nomeLegivelVariante(produto: AdminProduto, variante: AdminProdutoVariante): string {
    const caracteristicas = [...variante.caracteristicas].sort((left, right) => {
      const featureOrder = (id: string): number => {
        const feature = produto.caracteristicas.find((item) => item.id === id);
        if (feature?.tipoMedidaFisica === 'PesoLiquido') {
          return 0;
        }
        if (feature?.tipoMedidaFisica === 'VolumeLiquido') {
          return 1;
        }
        return 2 + produto.caracteristicas.findIndex((item) => item.id === id);
      };
      return featureOrder(left.caracteristicaId) - featureOrder(right.caracteristicaId);
    });
    if (caracteristicas.length === 0) {
      return variante.identificacao;
    }
    return caracteristicas
      .map((associacao) => {
        const caracteristica = produto.caracteristicas.find(
          (item) => item.id === associacao.caracteristicaId,
        );
        if (associacao.valorMedidaCanonico !== null && caracteristica?.tipoMedidaFisica) {
          const unidade = caracteristica.tipoMedidaFisica === 'PesoLiquido' ? 'g' : 'ml';
          const medida = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(
            associacao.valorMedidaCanonico,
          );
          return `${medida} ${unidade}`;
        }
        return associacao.valorNome;
      })
      .join(' · ');
  }

  protected resumoCombinacoes(produto: AdminProduto): string {
    const ativas = produto.variantes.filter((variante) => variante.ativa);
    const resumo = ativas
      .slice(0, 3)
      .map((variante) => this.nomeLegivelVariante(produto, variante));
    const restantes = ativas.length - resumo.length;
    return `${resumo.join(' · ')}${restantes > 0 ? ` · +${restantes}` : ''}`;
  }

  protected produtoPendente(id: string): boolean {
    return this.produtosEmPublicacao().has(id);
  }

  protected variantePendente(produtoId: string, varianteId: string): boolean {
    return this.variantesEmAtualizacao().has(this.chaveVariante(produtoId, varianteId));
  }

  protected variantesAtivas(produto: AdminProduto): AdminProdutoVariante[] {
    return produto.variantes.filter((variante) => variante.ativa);
  }

  protected quantidadeVariantesAtivas(produto: AdminProduto): number {
    return produto.variantes.filter((variante) => variante.ativa).length;
  }

  private enviarStatusVariante(
    produto: AdminProduto,
    variante: AdminProdutoVariante,
    ativa: boolean,
    novoPadraoId?: string,
  ): void {
    if (this.variantePendente(produto.id, variante.id)) {
      return;
    }
    const request = novoPadraoId ? { ativa, novoPadraoId } : { ativa };
    this.definirVariantePendente(produto.id, variante.id, true);
    this.erroVariantes.set('');
    this.produtosService
      .alterarStatusVariante(produto.id, variante.id, request)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.definirVariantePendente(produto.id, variante.id, false)),
      )
      .subscribe({
        next: (updated) => {
          this.atualizarProduto(updated);
          this.produtoVariantes.set(updated);
          this.varianteParaDesativar.set(null);
          this.novoPadraoId.set('');
          this.mensagemVariantes.set(
            ativa ? 'Opção ativada com sucesso.' : 'Opção desativada com sucesso.',
          );
        },
        error: (error: unknown) => {
          if (!this.redirecionarSessaoExpirada(error)) {
            this.erroVariantes.set(
              this.mensagemErroHttp(
                error,
                ativa
                  ? 'Não foi possível ativar a opção.'
                  : 'Não foi possível desativar a opção.',
              ),
            );
          }
        },
      });
  }

  private atualizarProduto(updated: AdminProduto): void {
    this.produtos.update((produtos) => {
      const exists = produtos.some((produto) => produto.id === updated.id);
      const next = exists
        ? produtos.map((produto) => (produto.id === updated.id ? updated : produto))
        : [...produtos, updated];
      return next.sort((left, right) => left.nome.localeCompare(right.nome, 'pt-BR'));
    });
  }

  private formatarMoeda(value: number | undefined): string {
    return value === undefined
      ? 'Sem preço'
      : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  private formatarMedida(value: number): string {
    return new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(value);
  }

  private mensagemErroHttp(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) {
      return fallback;
    }
    if (error.status === 403) {
      return 'Você não tem permissão para executar esta ação.';
    }
    if (error.status === 404) {
      return 'O produto, a categoria ou a opção não está mais disponível. Atualize os dados.';
    }
    if (error.status === 400) {
      return 'Os dados informados são inválidos. Revise os campos e tente novamente.';
    }
    if (error.status === 409) {
      const body = error.error as { title?: unknown; detail?: unknown } | null;
      if (body && typeof body === 'object') {
        if (body.title === 'Categoria inativa') {
          return typeof body.detail === 'string'
            ? body.detail
            : 'Não é possível publicar ou atribuir o produto a uma categoria inativa.';
        }
        if (body.title === 'Conflito no produto' && typeof body.detail === 'string') {
          return body.detail;
        }
      }
      return 'O produto foi alterado por outra operação ou há um conflito. Atualize os dados e tente novamente.';
    }
    return fallback;
  }

  private redirecionarSessaoExpirada(error: unknown): boolean {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return true;
    }
    return false;
  }

  private definirProdutoPendente(id: string, pending: boolean): void {
    this.produtosEmPublicacao.update((ids) => {
      const next = new Set(ids);
      if (pending) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  private definirVariantePendente(produtoId: string, varianteId: string, pending: boolean): void {
    const key = this.chaveVariante(produtoId, varianteId);
    this.variantesEmAtualizacao.update((ids) => {
      const next = new Set(ids);
      if (pending) {
        next.add(key);
      } else {
        next.delete(key);
      }
      return next;
    });
  }

  private chaveVariante(produtoId: string, varianteId: string): string {
    return `${produtoId}:${varianteId}`;
  }
}
