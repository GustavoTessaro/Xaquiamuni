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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, finalize } from 'rxjs';
import {
  AdminProdutoImagem,
  ImagensProdutoService,
} from '../../core/admin-produtos/imagens-produto.service';
import {
  AdminProduto,
  AdminProdutoVariante,
  ProdutosService,
} from '../../core/admin-produtos/produtos.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

type UploadStatus = 'pending' | 'uploading' | 'failed' | 'uncertain';

interface SelectedImage {
  id: number;
  file: File;
  previewUrl: string;
  status: UploadStatus;
  message: string;
}

const MAX_IMAGES = 5;
const MAX_FILE_SIZE = 5 * 1024 * 1024;
const ALLOWED_MIME_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);
const ALLOWED_EXTENSIONS = new Set(['jpg', 'jpeg', 'png', 'webp']);

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, RouterLink],
  selector: 'app-admin-produto-imagens',
  templateUrl: './admin-produto-imagens.component.html',
})
export class AdminProdutoImagensComponent {
  protected readonly produto = signal<AdminProduto | null>(null);
  protected readonly imagens = signal<AdminProdutoImagem[]>([]);
  protected readonly carregando = signal(true);
  protected readonly erroCarregamento = signal('');
  protected readonly erroOperacao = signal('');
  protected readonly mensagem = signal('');
  protected readonly selecao = signal<SelectedImage[]>([]);
  protected readonly arrastandoArquivos = signal(false);
  protected readonly imagemEmDestaqueId = signal('');
  protected readonly enviando = signal(false);
  protected readonly mutacaoEmAndamento = signal(false);
  protected readonly imagemPrincipal = computed(
    () => this.imagens().find((imagem) => imagem.principal) ?? null,
  );
  protected readonly imagemEmDestaque = computed(
    () =>
      this.imagens().find((imagem) => imagem.id === this.imagemEmDestaqueId()) ??
      this.imagemPrincipal(),
  );
  protected readonly produtoSimples = computed(() => {
    const produto = this.produto();
    return !!produto && produto.caracteristicas.length === 0 && produto.variantes.length === 1;
  });
  protected readonly espacosDisponiveis = computed(() =>
    Array.from({
      length: Math.max(0, MAX_IMAGES - this.imagens().length - this.selecao().length),
    }),
  );
  protected readonly podeSelecionar = computed(
    () => this.imagens().length + this.selecao().length < MAX_IMAGES && !this.enviando(),
  );
  protected readonly temEnvioPendente = computed(() =>
    this.selecao().some((item) => item.status === 'pending'),
  );
  protected readonly ocupado = computed(
    () => this.carregando() || this.enviando() || this.mutacaoEmAndamento(),
  );

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly produtosService = inject(ProdutosService);
  private readonly imagensService = inject(ImagensProdutoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly produtoId: string;
  private readonly idsImagemIndisponivel = signal<ReadonlySet<string>>(new Set());
  protected readonly imagemArrastadaId = signal('');
  private nextSelectionId = 0;

  constructor() {
    this.produtoId = this.route.snapshot.paramMap.get('produtoId') ?? '';
    if (!this.produtoId) {
      this.carregando.set(false);
      this.erroCarregamento.set('Produto não encontrado.');
      return;
    }
    this.carregarDados();
    this.destroyRef.onDestroy(() => {
      for (const selecionada of this.selecao()) {
        URL.revokeObjectURL(selecionada.previewUrl);
      }
    });
  }

  protected carregarDados(): void {
    if (!this.produtoId || (this.carregando() && this.produto())) {
      return;
    }
    this.carregando.set(true);
    this.erroCarregamento.set('');
    forkJoin({
      produto: this.produtosService.obter(this.produtoId),
      imagens: this.imagensService.listar(this.produtoId),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: ({ produto, imagens }) => {
          this.produto.set(produto);
          this.definirImagens(imagens);
        },
        error: (error: unknown) => {
          this.erroCarregamento.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'Produto não encontrado.'
              : 'Não foi possível carregar o produto e suas fotos. Tente novamente.',
          );
          this.redirecionarSessaoExpirada(error);
        },
      });
  }

  protected selecionarArquivos(event: Event): void {
    const input = event.target;
    if (!(input instanceof HTMLInputElement)) {
      return;
    }
    this.adicionarArquivos(Array.from(input.files ?? []));
    input.value = '';
  }

  protected iniciarArrasteArquivos(event: DragEvent): void {
    event.preventDefault();
    this.arrastandoArquivos.set(true);
  }

  protected sairArrasteArquivos(event: DragEvent): void {
    event.preventDefault();
    this.arrastandoArquivos.set(false);
  }

  protected soltarArquivos(event: DragEvent): void {
    event.preventDefault();
    this.arrastandoArquivos.set(false);
    this.adicionarArquivos(Array.from(event.dataTransfer?.files ?? []));
  }

  protected removerSelecao(id: number): void {
    const selecionada = this.selecao().find((item) => item.id === id);
    if (!selecionada) {
      return;
    }
    URL.revokeObjectURL(selecionada.previewUrl);
    this.selecao.update((itens) => itens.filter((item) => item.id !== id));
  }

  protected async enviarFotos(): Promise<void> {
    if (this.enviando() || this.mutacaoEmAndamento()) {
      return;
    }
    const pendentes = this.selecao()
      .filter((item) => item.status === 'pending')
      .map((item) => item.id);
    if (pendentes.length === 0) {
      return;
    }
    this.enviando.set(true);
    this.erroOperacao.set('');
    for (const id of pendentes) {
      if (this.destroyRef.destroyed) {
        break;
      }
      await this.enviarUmaFoto(id);
    }
    if (!this.destroyRef.destroyed) {
      this.enviando.set(false);
    }
  }

  protected async tentarNovamente(id: number): Promise<void> {
    if (this.enviando() || this.mutacaoEmAndamento()) {
      return;
    }
    const selecionada = this.selecao().find((item) => item.id === id);
    if (!selecionada || selecionada.status !== 'failed') {
      return;
    }
    this.enviando.set(true);
    this.erroOperacao.set('');
    await this.enviarUmaFoto(id);
    if (!this.destroyRef.destroyed) {
      this.enviando.set(false);
    }
  }

  protected conferirGaleria(): void {
    this.erroOperacao.set('');
    this.mensagem.set('');
    void this.atualizarImagens().then((atualizada) => {
      if (atualizada && !this.destroyRef.destroyed) {
        this.mensagem.set(
          this.selecao().some((item) => item.status === 'uncertain')
            ? 'Galeria atualizada. Confira as fotos antes de repetir qualquer envio.'
            : 'Galeria atualizada.',
        );
      }
    });
  }

  protected definirPrincipal(imagem: AdminProdutoImagem): void {
    if (this.ocupado() || imagem.principal) {
      return;
    }
    this.executarMutacao(
      this.imagensService.definirPrincipal(this.produtoId, imagem.id),
      'Foto principal atualizada.',
    );
  }

  protected visualizarImagem(imagem: AdminProdutoImagem): void {
    this.imagemEmDestaqueId.set(imagem.id);
  }

  protected mover(imagem: AdminProdutoImagem, direcao: -1 | 1): void {
    if (this.ocupado()) {
      return;
    }
    const atuais = this.imagens();
    const indice = atuais.findIndex((item) => item.id === imagem.id);
    const destino = indice + direcao;
    if (indice < 0 || destino < 0 || destino >= atuais.length) {
      return;
    }
    const novaOrdem = [...atuais];
    [novaOrdem[indice], novaOrdem[destino]] = [novaOrdem[destino], novaOrdem[indice]];
    this.reordenar(novaOrdem);
  }

  protected iniciarArrasteImagem(event: DragEvent, imagemId: string): void {
    if (this.ocupado()) {
      event.preventDefault();
      return;
    }
    this.imagemArrastadaId.set(imagemId);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', imagemId);
    }
  }

  protected permitirSoltarImagem(event: DragEvent): void {
    event.preventDefault();
  }

  protected soltarImagem(event: DragEvent, destino: AdminProdutoImagem): void {
    event.preventDefault();
    const origemId = event.dataTransfer?.getData('text/plain') || this.imagemArrastadaId();
    this.imagemArrastadaId.set('');
    if (!origemId || origemId === destino.id || this.ocupado()) {
      return;
    }
    const ordem = [...this.imagens()];
    const origem = ordem.findIndex((imagem) => imagem.id === origemId);
    const alvo = ordem.findIndex((imagem) => imagem.id === destino.id);
    if (origem < 0 || alvo < 0) {
      return;
    }
    const [movida] = ordem.splice(origem, 1);
    ordem.splice(alvo, 0, movida);
    this.reordenar(ordem);
  }

  protected alterarVinculo(
    imagem: AdminProdutoImagem,
    variante: AdminProdutoVariante,
    event: Event,
  ): void {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || this.ocupado()) {
      return;
    }
    const operation = input.checked
      ? this.imagensService.associarVariante(this.produtoId, imagem.id, variante.id)
      : this.imagensService.desassociarVariante(this.produtoId, imagem.id, variante.id);
    this.executarMutacao(operation, 'Vínculo da foto atualizado.');
  }

  protected removerImagem(imagem: AdminProdutoImagem): void {
    if (this.ocupado() || this.naoPodeRemoverUltima()) {
      return;
    }
    if (!globalThis.confirm(`Remover a foto "${imagem.nomeOriginal}" da galeria?`)) {
      return;
    }
    this.mutacaoEmAndamento.set(true);
    this.erroOperacao.set('');
    this.imagensService
      .remover(this.produtoId, imagem.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.mutacaoEmAndamento.set(false)),
      )
      .subscribe({
        next: (response) => {
          if (response.status === 204 || response.body?.removidaDoCatalogo === true) {
            this.imagens.update((atuais) => atuais.filter((item) => item.id !== imagem.id));
            const limpezaPendente =
              response.status !== 204 && response.body?.limpezaFisicaConcluida === false;
            this.mensagem.set(
              limpezaPendente
                ? 'A foto foi removida da galeria. A limpeza do arquivo físico ficou pendente.'
                : 'Foto removida da galeria.',
            );
            void this.atualizarImagens();
            return;
          }
          this.erroOperacao.set(
            'A remoção não foi confirmada. Atualize a galeria antes de tentar novamente.',
          );
        },
        error: (error: unknown) => {
          this.erroOperacao.set(
            this.conflitoUltimaFotoPublicada(error)
              ? 'A última foto de um produto publicado não pode ser removida. Despublique o produto ou adicione outra foto.'
              : this.mensagemErroHttp(error, 'Não foi possível remover a foto.'),
          );
          this.redirecionarSessaoExpirada(error);
        },
      });
  }

  protected imagemIndisponivel(imagemId: string): boolean {
    return this.idsImagemIndisponivel().has(imagemId);
  }

  protected marcarImagemIndisponivel(imagemId: string): void {
    this.idsImagemIndisponivel.update((atuais) => new Set(atuais).add(imagemId));
  }

  protected formatarTamanho(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected naoPodeRemoverUltima(): boolean {
    return this.produto()?.publicado === true && this.imagens().length <= 1;
  }

  protected temVinculo(imagem: AdminProdutoImagem, varianteId: string): boolean {
    return imagem.varianteIds.includes(varianteId);
  }

  private adicionarArquivos(arquivos: File[]): void {
    const avisos: string[] = [];
    for (const arquivo of arquivos) {
      if (this.imagens().length + this.selecao().length >= MAX_IMAGES) {
        avisos.push('O limite é de cinco fotos por produto.');
        break;
      }
      if (!this.formatoAceito(arquivo)) {
        avisos.push(`${arquivo.name}: selecione um arquivo JPEG, PNG ou WebP.`);
        continue;
      }
      if (arquivo.size <= 0) {
        avisos.push(`${arquivo.name}: o arquivo está vazio.`);
        continue;
      }
      if (arquivo.size > MAX_FILE_SIZE) {
        avisos.push(`${arquivo.name}: o tamanho máximo é 5 MiB.`);
        continue;
      }
      this.selecao.update((atuais) => [
        ...atuais,
        {
          id: ++this.nextSelectionId,
          file: arquivo,
          previewUrl: URL.createObjectURL(arquivo),
          status: 'pending',
          message: '',
        },
      ]);
    }
    if (avisos.length > 0) {
      this.erroOperacao.set(avisos.join(' '));
    } else {
      this.erroOperacao.set('');
    }
    this.mensagem.set('');
  }

  private formatoAceito(arquivo: File): boolean {
    if (ALLOWED_MIME_TYPES.has(arquivo.type.toLowerCase())) {
      return true;
    }
    const extensao = arquivo.name.split('.').pop()?.toLowerCase() ?? '';
    return arquivo.type === '' && ALLOWED_EXTENSIONS.has(extensao);
  }

  private async enviarUmaFoto(id: number): Promise<void> {
    const selecionada = this.selecao().find((item) => item.id === id);
    if (!selecionada) {
      return;
    }
    this.atualizarSelecao(id, { status: 'uploading', message: 'Enviando foto…' });
    try {
      const criada = await firstValueFrom(
        this.imagensService
          .adicionar(this.produtoId, selecionada.file)
          .pipe(takeUntilDestroyed(this.destroyRef)),
      );
      if (this.destroyRef.destroyed) {
        return;
      }
      this.imagens.update((atuais) => this.ordenarImagens([...atuais, criada]));
      this.removerSelecao(id);
      this.mensagem.set(`Foto "${criada.nomeOriginal}" enviada.`);
      await this.atualizarImagens();
    } catch (error: unknown) {
      if (this.destroyRef.destroyed) {
        return;
      }
      const resultado = this.classificarFalhaUpload(error);
      this.atualizarSelecao(id, {
        status: resultado.incerto ? 'uncertain' : 'failed',
        message: resultado.mensagem,
      });
      this.erroOperacao.set(resultado.mensagem);
      if (resultado.incerto) {
        await this.atualizarImagens();
      }
      this.redirecionarSessaoExpirada(error);
    }
  }

  private classificarFalhaUpload(error: unknown): { mensagem: string; incerto: boolean } {
    if (!(error instanceof HttpErrorResponse)) {
      return {
        mensagem:
          'Não foi possível confirmar o envio. Confira a galeria antes de tentar novamente.',
        incerto: true,
      };
    }
    if (error.status === 0 || error.status >= 500) {
      const titulo = this.tituloProblemDetails(error);
      const detalhe = this.detalheProblemDetails(error);
      if (titulo === 'Armazenamento indisponível') {
        return {
          mensagem:
            'O armazenamento está temporariamente indisponível. Tente novamente mais tarde.',
          incerto: false,
        };
      }
      if (titulo === 'Limpeza pendente') {
        const resultadoIncerto = detalhe.includes('não confirmou a remoção lógica');
        return {
          mensagem: resultadoIncerto
            ? 'A remoção não foi confirmada. Confira a galeria antes de tentar novamente.'
            : 'O envio não foi concluído e a limpeza do arquivo temporário está pendente. Você pode tentar novamente.',
          incerto: resultadoIncerto,
        };
      }
      return {
        mensagem:
          'Não foi possível confirmar o envio. Confira a galeria antes de tentar novamente.',
        incerto: true,
      };
    }
    return {
      mensagem: this.mensagemErroHttp(error, 'Não foi possível enviar esta foto.'),
      incerto: false,
    };
  }

  private async atualizarImagens(): Promise<boolean> {
    try {
      const imagens = await firstValueFrom(
        this.imagensService.listar(this.produtoId).pipe(takeUntilDestroyed(this.destroyRef)),
      );
      if (this.destroyRef.destroyed) {
        return false;
      }
      this.definirImagens(imagens);
      return true;
    } catch (error: unknown) {
      if (!this.destroyRef.destroyed) {
        this.erroOperacao.set(
          'A operação foi concluída, mas não foi possível atualizar a galeria. Use "Atualizar galeria" para conferir o estado atual.',
        );
        this.redirecionarSessaoExpirada(error);
      }
      return false;
    }
  }

  private executarMutacao(
    operation: ReturnType<ImagensProdutoService['definirPrincipal']>,
    sucesso: string,
  ): void {
    if (this.ocupado()) {
      return;
    }
    this.mutacaoEmAndamento.set(true);
    this.erroOperacao.set('');
    this.mensagem.set('');
    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.mutacaoEmAndamento.set(false)),
      )
      .subscribe({
        next: (imagens) => {
          this.definirImagens(imagens);
          this.mensagem.set(sucesso);
        },
        error: (error: unknown) => {
          this.erroOperacao.set(
            this.mensagemErroHttp(error, 'Não foi possível atualizar a galeria.'),
          );
          this.redirecionarSessaoExpirada(error);
        },
      });
  }

  private reordenar(imagens: AdminProdutoImagem[]): void {
    this.executarMutacao(
      this.imagensService.reordenar(
        this.produtoId,
        imagens.map((imagem) => imagem.id),
      ),
      'Ordem das fotos atualizada.',
    );
  }

  private definirImagens(imagens: AdminProdutoImagem[]): void {
    this.imagens.set(this.ordenarImagens(imagens));
    const idsAtivos = new Set(imagens.map((imagem) => imagem.id));
    this.idsImagemIndisponivel.update(
      (atuais) => new Set([...atuais].filter((id) => idsAtivos.has(id))),
    );
  }

  private ordenarImagens(imagens: AdminProdutoImagem[]): AdminProdutoImagem[] {
    return [...imagens].sort((a, b) => a.ordemExibicao - b.ordemExibicao);
  }

  private atualizarSelecao(id: number, patch: Partial<SelectedImage>): void {
    this.selecao.update((itens) =>
      itens.map((item) => (item.id === id ? { ...item, ...patch } : item)),
    );
  }

  private mensagemErroHttp(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) {
      return fallback;
    }
    switch (error.status) {
      case 0:
        return 'Não foi possível conectar ao serviço. Tente novamente.';
      case 400:
        return 'Os dados enviados ou a imagem foram rejeitados. Confira e tente novamente.';
      case 401:
        return 'Sua sessão expirou. Entre novamente para continuar.';
      case 403:
        return 'Sua conta não tem permissão para gerenciar as fotos deste produto.';
      case 404:
        return 'O produto ou a foto não foi encontrado. Atualize a galeria.';
      case 409:
        return 'A operação conflita com o estado atual do produto. Atualize a galeria e tente novamente.';
      case 413:
        return 'O arquivo ou a requisição excede o limite permitido. O arquivo deve ter até 5 MiB.';
      case 415:
        return 'O formato enviado não é suportado. Use JPEG, PNG ou WebP.';
      case 503:
        if (this.tituloProblemDetails(error) === 'Resultado da operação desconhecido') {
          return 'O resultado não foi confirmado. Atualize a galeria antes de tentar novamente.';
        }
        if (this.tituloProblemDetails(error) === 'Armazenamento indisponível') {
          return 'O armazenamento de imagens está temporariamente indisponível.';
        }
        if (
          this.tituloProblemDetails(error) === 'Limpeza pendente' &&
          this.detalheProblemDetails(error).includes('não confirmou a remoção lógica')
        ) {
          return 'A remoção não foi confirmada. Atualize a galeria antes de tentar novamente.';
        }
        return 'O serviço de imagens está temporariamente indisponível.';
      default:
        return fallback;
    }
  }

  private tituloProblemDetails(error: HttpErrorResponse): string {
    const body = error.error as { title?: unknown } | null;
    return typeof body?.title === 'string' ? body.title : '';
  }

  private detalheProblemDetails(error: HttpErrorResponse): string {
    const body = error.error as { detail?: unknown } | null;
    return typeof body?.detail === 'string' ? body.detail : '';
  }

  private conflitoUltimaFotoPublicada(error: unknown): boolean {
    if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
      return false;
    }
    const body = error.error as { title?: unknown; detail?: unknown } | null;
    return (
      body?.title === 'Conflito na galeria' &&
      typeof body.detail === 'string' &&
      body.detail.includes('sua última imagem')
    );
  }

  private redirecionarSessaoExpirada(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/admin/login');
    }
  }
}
