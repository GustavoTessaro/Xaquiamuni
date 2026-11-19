import { DatePipe } from '@angular/common';
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
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { EMPTY, Observable, expand, finalize, forkJoin, reduce } from 'rxjs';
import {
  InsumosService,
  AdminResumoInsumoPreciso,
  UnidadeBaseEstoque,
} from '../../core/admin-estoque/insumos.service';
import { AdminProduto, ProdutosService } from '../../core/admin-produtos/produtos.service';
import {
  DetalheReceita,
  ItemReceitaInput,
  ReceitasProducaoService,
  ResumoReceita,
  VersaoReceita,
} from '../../core/receitas-producao.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { decimalEscalado, decimalTexto } from '../compras/compra-decimais';

interface ItemEditor {
  insumoId: string;
  quantidade: string;
  salvo?: VersaoReceita['itens'][number];
}
interface EditorReceita {
  detalhe: DetalheReceita | null;
  nome: string;
  produtoId: string;
  varianteId: string;
  rendimento: number | null;
  itens: ItemEditor[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, FormsModule, DatePipe],
  templateUrl: './admin-receitas.component.html',
})
export class AdminReceitasComponent {
  private readonly api = inject(ReceitasProducaoService);
  private readonly insumosApi = inject(InsumosService);
  private readonly produtosApi = inject(ProdutosService);
  private readonly destroy = inject(DestroyRef);
  private readonly router = inject(Router);

  private readonly receitas = signal<ResumoReceita[]>([]);
  protected readonly insumos = signal<AdminResumoInsumoPreciso[]>([]);
  protected readonly produtos = signal<AdminProduto[]>([]);
  protected readonly editor = signal<EditorReceita | null>(null);
  protected readonly historico = signal<VersaoReceita | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly falhaLista = signal(false);
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');
  protected readonly busca = signal('');
  protected readonly filtro = signal('');

  protected readonly visiveis = computed(() => {
    const termo = this.normalizar(this.busca());
    return this.receitas().filter(
      (r) =>
        this.normalizar(r.nome + ' ' + r.produto + ' ' + r.variante).includes(termo) &&
        (!this.filtro() || r.ativa === (this.filtro() === 'ativa')),
    );
  });

  constructor() {
    this.carregar();
  }

  protected carregar(): void {
    if (this.ocupado()) return;
    this.falhaLista.set(false);
    this.executar(
      this.api.listar(),
      (lista) => this.receitas.set(lista),
      'Não foi possível carregar as receitas.',
      () => this.falhaLista.set(true),
    );
  }

  protected abrir(id?: string): void {
    if (this.ocupado()) return;
    this.sucesso.set('');
    this.historico.set(null);
    const pagina = (numero: number) =>
      this.insumosApi.listar({ ativo: true, pagina: numero, itensPorPagina: 50 });
    const insumos = pagina(1).pipe(
      expand((p) => (p.pagina * p.itensPorPagina < p.totalItens ? pagina(p.pagina + 1) : EMPTY)),
      reduce((itens, p) => [...itens, ...p.itens], [] as AdminResumoInsumoPreciso[]),
    );
    this.executar(
      forkJoin({
        insumos,
        contexto: id ? this.api.obter(id) : this.produtosApi.listar(),
      }),
      ({ insumos, contexto }) => {
        this.insumos.set(insumos);
        if (Array.isArray(contexto)) {
          this.produtos.set(contexto.filter((p) => p.variantes.some((v) => v.ativa)));
          this.aplicarDetalhe(null);
        } else {
          this.aplicarDetalhe(contexto);
        }
      },
      'Não foi possível abrir a receita. Tente novamente.',
    );
  }

  protected fechar(): void {
    this.editor.set(null);
    this.historico.set(null);
    this.erro.set('');
  }

  protected variantes(produtoId: string) {
    return (
      this.produtos()
        .find((p) => p.id === produtoId)
        ?.variantes.filter((v) => v.ativa) ?? []
    );
  }

  protected trocarProduto(editor: EditorReceita, produtoId: string): void {
    editor.produtoId = produtoId;
    const variantes = this.variantes(produtoId);
    editor.varianteId = variantes.length === 1 ? variantes[0].id : '';
  }

  protected novoItem(): ItemEditor {
    return { insumoId: '', quantidade: '' };
  }

  protected unidade(item: ItemEditor): UnidadeBaseEstoque | undefined {
    return (
      this.insumos().find((i) => i.insumoId === item.insumoId)?.unidadeBase ??
      (item.salvo?.insumoId === item.insumoId ? item.salvo?.unidadeBase : undefined)
    );
  }

  protected unidadeTexto(unidade?: UnidadeBaseEstoque): string {
    return unidade === 'Grama'
      ? 'g'
      : unidade === 'Mililitro'
        ? 'ml'
        : unidade === 'Unidade'
          ? 'un'
          : '';
  }

  protected existeAtivo(id: string): boolean {
    return this.insumos().some((i) => i.insumoId === id);
  }

  protected salvar(editor: EditorReceita): void {
    if (this.ocupado()) return;
    if (
      !editor.nome.trim() ||
      !editor.varianteId ||
      !editor.rendimento ||
      !Number.isInteger(editor.rendimento) ||
      editor.rendimento < 1 ||
      editor.rendimento > 2147483647
    ) {
      this.erro.set('Informe variante, nome e rendimento inteiro positivo.');
      return;
    }
    const itens: ItemReceitaInput[] = [];
    const ids = new Set<string>();
    for (const item of editor.itens) {
      const unidade = this.unidade(item);
      const casas = unidade === 'Unidade' ? 0 : 6;
      const quantidade = decimalEscalado(item.quantidade, casas, 12);
      if (!unidade || !quantidade) {
        this.erro.set(
          'Selecione os materiais e informe quantidades positivas, inteiras para un e com até 6 casas para g e ml.',
        );
        return;
      }
      if (ids.has(item.insumoId)) {
        this.erro.set('Cada material pode aparecer apenas uma vez.');
        return;
      }
      ids.add(item.insumoId);
      itens.push({
        insumoId: item.insumoId,
        unidadeBase: unidade,
        quantidade: decimalTexto(quantidade, casas),
      });
    }
    const detalhe = editor.detalhe;
    this.executar(
      this.api.salvar(detalhe?.resumo.id ?? null, {
        produtoVarianteId: editor.varianteId,
        nome: editor.nome.trim(),
        rendimento: editor.rendimento,
        itens,
        token: detalhe?.token ?? null,
      }),
      (resposta) => this.atualizar(resposta, 'Receita salva.'),
      'Não foi possível salvar a receita.',
    );
  }

  protected alterarSituacao(detalhe: DetalheReceita): void {
    this.executar(
      this.api.alterarStatus(detalhe.resumo.id, !detalhe.resumo.ativa, detalhe.token),
      (resposta) =>
        this.atualizar(
          resposta,
          resposta.resumo.ativa ? 'Receita reativada.' : 'Receita inativada.',
        ),
      'Não foi possível alterar a situação.',
    );
  }

  protected verVersao(detalhe: DetalheReceita, numero: number): void {
    this.historico.set(null);
    this.executar(
      this.api.versao(detalhe.resumo.id, numero),
      (v) => this.historico.set(v),
      'Não foi possível carregar a versão.',
    );
  }

  private atualizar(detalhe: DetalheReceita, mensagem: string): void {
    this.receitas.update((lista) =>
      [...lista.filter((r) => r.id !== detalhe.resumo.id), detalhe.resumo].sort((a, b) =>
        a.nome.localeCompare(b.nome, 'pt-BR'),
      ),
    );
    this.historico.set(null);
    this.aplicarDetalhe(detalhe);
    this.sucesso.set(mensagem);
  }

  private aplicarDetalhe(detalhe: DetalheReceita | null): void {
    this.editor.set({
      detalhe,
      nome: detalhe?.resumo.nome ?? '',
      produtoId: detalhe?.resumo.produtoId ?? '',
      varianteId: detalhe?.resumo.varianteId ?? '',
      rendimento: detalhe?.atual.rendimento ?? null,
      itens: detalhe?.atual.itens.map((item) => ({
        insumoId: item.insumoId,
        quantidade: item.quantidade,
        salvo: item,
      })) ?? [this.novoItem()],
    });
  }

  private executar<T>(
    requisicao: Observable<T>,
    sucesso: (valor: T) => void,
    mensagem: string,
    falha?: () => void,
  ): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.erro.set('');
    requisicao
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: sucesso,
        error: (erro: unknown) => {
          falha?.();
          if (erro instanceof HttpErrorResponse) {
            if (erro.status === 401) void this.router.navigateByUrl('/admin/login');
            if (erro.status === 403)
              mensagem = 'Acesso negado ou sessão inválida. Entre novamente.';
            if (erro.status === 404) mensagem = 'Receita ou versão não encontrada.';
            const detalhe =
              erro.status === 409 ? erro.error?.detail : erro.error?.errors?.receita?.[0];
            if (typeof detalhe === 'string') mensagem = detalhe;
          }
          this.erro.set(mensagem);
        },
      });
  }

  private normalizar(texto: string): string {
    return texto.trim().normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase('pt-BR');
  }
}
