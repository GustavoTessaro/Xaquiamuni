import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { CatalogoTemasAdmin, TemaLojaAdmin, TemasLojaService } from '../../core/temas-loja.service';
import { TEMA_ORIGINAL } from '../../public-store/manifestos-tema-loja';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

interface TentativaPublicacao {
  tema: TemaLojaAdmin;
  revisao: number;
  chave: string;
}

@Component({
  selector: 'app-admin-temas',
  imports: [AdminWorkspaceComponent],
  templateUrl: './admin-temas.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminTemasComponent {
  private readonly api = inject(TemasLojaService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly original = TEMA_ORIGINAL;
  protected readonly catalogo = signal<CatalogoTemasAdmin | null>(null);
  protected readonly nomePublicado = computed(() => {
    const dados = this.catalogo();
    return dados?.temas.find((tema) => tema.chave === dados.publicacao.chave)?.nome ?? dados?.publicacao.chave;
  });
  protected readonly carregando = signal(false);
  protected readonly publicando = signal(false);
  protected readonly tentativa = signal<TentativaPublicacao | null>(null);
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');

  constructor() {
    this.carregar();
  }

  protected carregar(): void {
    if (this.carregando() || this.publicando()) return;
    this.erro.set('');
    this.carregando.set(true);
    this.catalogo.set(null);
    this.api.consultarAdmin().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.carregando.set(false)),
    ).subscribe({
      next: (catalogo) => this.catalogo.set(catalogo),
      error: () => this.erro.set('Não foi possível carregar os temas. Tente novamente.'),
    });
  }

  protected preparar(tema: TemaLojaAdmin): void {
    const dados = this.catalogo();
    if (!dados || !tema.disponivel || this.publicando() || this.tentativa()) return;
    this.erro.set('');
    this.sucesso.set('');
    this.tentativa.set({ tema, revisao: dados.publicacao.revisao, chave: crypto.randomUUID() });
  }

  protected cancelar(): void {
    this.tentativa.set(null);
    this.carregar();
  }

  protected confirmar(): void {
    const dados = this.catalogo();
    const tentativa = this.tentativa();
    if (!dados || !tentativa || this.publicando()) return;
    this.erro.set('');
    this.publicando.set(true);
    this.api.publicar(tentativa.tema.chave, tentativa.revisao, tentativa.chave).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.publicando.set(false)),
    ).subscribe({
      next: (publicacao) => {
        this.catalogo.set({ ...dados, publicacao });
        this.tentativa.set(null);
        this.sucesso.set('Tema publicado na loja.');
      },
      error: (erro: HttpErrorResponse) => {
        if (erro.status === 409) {
          this.tentativa.set(null);
          this.catalogo.set(null);
          this.erro.set('A publicação mudou. Consulte a revisão atual antes de confirmar novamente.');
        } else if (erro.status === 401 || erro.status === 403) {
          this.tentativa.set(null);
          this.catalogo.set(null);
          this.erro.set('Sua sessão não permite publicar temas. Entre novamente como administradora.');
        } else if (erro.status >= 400 && erro.status < 500) {
          this.tentativa.set(null);
          this.catalogo.set(null);
          this.erro.set('A publicação foi recusada. Consulte os temas disponíveis e tente novamente.');
        } else {
          this.erro.set('Não foi possível confirmar o resultado. Você pode reenviar a mesma tentativa ou consultar a revisão.');
        }
      },
    });
  }
}
