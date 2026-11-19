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
import { EMPTY, Observable, expand, finalize, forkJoin, map, of, reduce } from 'rxjs';
import {
  AdminFormaCompraPrecisa,
  AdminResumoInsumoPreciso,
  InsumosService,
} from '../../core/admin-estoque/insumos.service';
import {
  CompraInput,
  ComprasMateriasPrimasService,
  DetalheCompra,
  ResumoCompra,
} from '../../core/compras-materias-primas.service';
import { Fornecedor, FornecedoresService } from '../../core/fornecedores.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { decimalEscalado, decimalTexto, exibirReais } from './compra-decimais';

type Snapshot = DetalheCompra['linhas'][number]['componentes'][number];
interface ItemEditor {
  insumoId: string;
  apresentacaoId: string;
  quantidade: string;
  snapshot?: Snapshot;
}
interface LinhaEditor {
  tipo: 'material' | 'kit';
  kit: string;
  quantidade: string;
  valor: string;
  itens: ItemEditor[];
}
interface EditorCompra {
  id: string | null;
  versao: string | null;
  fornecedorId: string;
  fornecedorOriginal?: ResumoCompra;
  data: string;
  cancelada: boolean;
  linhas: LinhaEditor[];
}

const maximo = 999_999_999_999_999_999n;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, FormsModule],
  templateUrl: './admin-compras-materias-primas.component.html',
})
export class AdminComprasMateriasPrimasComponent {
  private readonly api = inject(ComprasMateriasPrimasService);
  private readonly fornecedoresApi = inject(FornecedoresService);
  private readonly insumosApi = inject(InsumosService);
  private readonly destroy = inject(DestroyRef);
  private readonly router = inject(Router);

  protected readonly compras = signal<ResumoCompra[]>([]);
  protected readonly fornecedores = signal<Fornecedor[]>([]);
  protected readonly insumos = signal<AdminResumoInsumoPreciso[]>([]);
  protected readonly formas = signal<Record<string, AdminFormaCompraPrecisa[]>>({});
  protected readonly carregandoFormas = signal<ReadonlySet<string>>(new Set());
  protected readonly falhasFormas = signal<ReadonlySet<string>>(new Set());
  protected readonly editor = signal<EditorCompra | null>(null);
  protected readonly carregando = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly erro = signal('');
  protected readonly falhaLista = signal(false);
  protected readonly sucesso = signal('');
  protected readonly confirmarCancelamento = signal(false);
  protected readonly busca = signal('');
  protected readonly filtro = signal('');
  private tentativa: { assinatura: string; chave: string; incerta: boolean } | null = null;

  protected readonly visiveis = computed(() => {
    const termo = this.busca().trim().toLocaleLowerCase('pt-BR');
    return this.compras().filter(
      (c) =>
        c.nomeFornecedor.toLocaleLowerCase('pt-BR').includes(termo) &&
        (!this.filtro() || this.filtro() === c.situacao),
    );
  });

  constructor() {
    this.carregar();
  }

  protected carregar(): void {
    if (this.carregando()) return;
    this.carregando.set(true);
    this.falhaLista.set(false);
    this.erro.set('');
    this.api
      .listar()
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.carregando.set(false)),
      )
      .subscribe({
        next: (compras) => this.compras.set(compras),
        error: (erro: unknown) => {
          this.falhaLista.set(true);
          this.erro.set(this.mensagem(erro, 'Não foi possível carregar as compras.'));
        },
      });
  }

  protected novo(): void {
    this.abrir(
      forkJoin({
        detalhe: of(null),
        fornecedores: this.fornecedoresApi.listar(),
        insumos: this.listarInsumos(),
      }),
    );
  }

  protected consultar(id: string): void {
    this.abrir(
      forkJoin({
        detalhe: this.api.obter(id),
        fornecedores: this.fornecedoresApi.listar(),
        insumos: this.listarInsumos(),
      }),
    );
  }

  private listarInsumos() {
    const pagina = (numero: number) =>
      this.insumosApi.listar({ ativo: true, pagina: numero, itensPorPagina: 50 });
    return pagina(1).pipe(
      expand((resultado) =>
        resultado.pagina * resultado.itensPorPagina < resultado.totalItens
          ? pagina(resultado.pagina + 1)
          : EMPTY,
      ),
      reduce(
        (itens, resultado) => [...itens, ...resultado.itens],
        [] as AdminResumoInsumoPreciso[],
      ),
    );
  }

  private abrir(
    dados: Observable<{
      detalhe: DetalheCompra | null;
      fornecedores: Fornecedor[];
      insumos: AdminResumoInsumoPreciso[];
    }>,
  ): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    this.confirmarCancelamento.set(false);
    dados
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: ({ detalhe, fornecedores, insumos }) => {
          this.fornecedores.set(fornecedores.filter((f) => f.ativo));
          this.insumos.set(insumos);
          this.tentativa = null;
          const hoje = new Date();
          const dataLocal = [hoje.getFullYear(), hoje.getMonth() + 1, hoje.getDate()]
            .map((valor) => String(valor).padStart(2, '0'))
            .join('-');
          const linhas: LinhaEditor[] = detalhe?.linhas.map((l) => ({
            tipo: l.kit ? 'kit' : 'material',
            kit: l.kit ?? '',
            quantidade: l.quantidade,
            valor: l.valorTotal ?? '',
            itens: l.componentes.map((s) => ({
              insumoId: s.insumoId,
              apresentacaoId: s.apresentacaoId ?? '',
              quantidade: s.quantidade,
              snapshot: s,
            })),
          })) ?? [this.novaLinha('material')];
          this.editor.set({
            id: detalhe?.resumo.id ?? null,
            versao: detalhe?.versao ?? null,
            fornecedorId: detalhe?.resumo.fornecedorId ?? '',
            fornecedorOriginal: detalhe?.resumo,
            data: detalhe?.resumo.dataCompra ?? dataLocal,
            cancelada: detalhe?.resumo.situacao === 'Cancelada',
            linhas,
          });
          if (detalhe?.resumo.situacao !== 'Cancelada') {
            for (const id of new Set(linhas.flatMap((l) => l.itens.map((i) => i.insumoId))))
              this.carregarFormas(id);
          }
        },
        error: (erro: unknown) =>
          this.erro.set(
            this.mensagem(erro, 'Não foi possível abrir o formulário. Tente novamente.'),
          ),
      });
  }

  protected fechar(): void {
    if (this.tentativa?.incerta) {
      this.erro.set(
        'Repita o envio com os mesmos dados para confirmar o resultado antes de fechar.',
      );
      return;
    }
    this.editor.set(null);
    this.erro.set('');
  }

  protected novaLinha(tipo: LinhaEditor['tipo']): LinhaEditor {
    return {
      tipo,
      kit: '',
      quantidade: '1',
      valor: '',
      itens: tipo === 'kit' ? [this.novoItem(), this.novoItem()] : [this.novoItem()],
    };
  }

  protected novoItem(): ItemEditor {
    return { insumoId: '', apresentacaoId: '', quantidade: '' };
  }

  protected trocarMaterial(item: ItemEditor, id: string): void {
    item.insumoId = id;
    item.apresentacaoId = '';
    this.carregarFormas(id);
  }

  protected carregarFormas(id: string): void {
    if (!id || this.formas()[id] || this.carregandoFormas().has(id)) return;
    this.carregandoFormas.update((ids) => new Set([...ids, id]));
    this.falhasFormas.update((ids) => new Set([...ids].filter((outro) => outro !== id)));
    this.insumosApi
      .obterParaEntrada(id)
      .pipe(
        map((detalhe) => detalhe.formasCompra),
        takeUntilDestroyed(this.destroy),
        finalize(() =>
          this.carregandoFormas.update((ids) => new Set([...ids].filter((outro) => outro !== id))),
        ),
      )
      .subscribe({
        next: (formas) =>
          this.formas.update((catalogo) => ({ ...catalogo, [id]: formas.filter((f) => f.ativa) })),
        error: () => this.falhasFormas.update((ids) => new Set([...ids, id])),
      });
  }

  protected temFornecedor(id: string): boolean {
    return this.fornecedores().some((f) => f.id === id);
  }

  protected temInsumo(id: string): boolean {
    return this.insumos().some((i) => i.insumoId === id);
  }

  protected temForma(insumoId: string, id: string): boolean {
    return this.formas()[insumoId]?.some((f) => f.id === id) ?? false;
  }

  protected unidade(item: ItemEditor): string {
    const unidade =
      this.insumos().find((i) => i.insumoId === item.insumoId)?.unidadeBase ??
      item.snapshot?.unidadeBase;
    return unidade === 'Grama'
      ? 'g'
      : unidade === 'Mililitro'
        ? 'ml'
        : unidade === 'Unidade'
          ? 'un'
          : '';
  }

  protected conversao(item: ItemEditor): string {
    const qtd = decimalEscalado(item.quantidade, item.apresentacaoId ? 0 : 6, 12);
    const snapshot = item.snapshot;
    const preservar =
      snapshot &&
      snapshot.insumoId === item.insumoId &&
      (snapshot.apresentacaoId ?? '') === item.apresentacaoId &&
      qtd === decimalEscalado(snapshot.quantidade, item.apresentacaoId ? 0 : 6, 12);
    const forma = this.formas()[item.insumoId]?.find((f) => f.id === item.apresentacaoId);
    const fator = preservar ? snapshot.fator : forma?.quantidadeEquivalentePorEmbalagemDecimal;
    const fatorEscalado = fator ? decimalEscalado(fator, 6, 12) : null;
    if (qtd === null || qtd <= 0n || fatorEscalado === null || !item.apresentacaoId) return '';
    if (qtd * fatorEscalado > maximo) return 'A quantidade convertida excede o limite.';
    return `${qtd} × ${decimalTexto(fatorEscalado, 6)} ${this.unidade(item)} = ${decimalTexto(qtd * fatorEscalado, 6)} ${this.unidade(item)}`;
  }

  protected formatarValor(valor: string | null): string {
    const centavos = valor === null ? null : decimalEscalado(valor, 2, 16);
    return centavos === null ? 'Não informado' : exibirReais(centavos);
  }

  protected total(linhas: LinhaEditor[]): string {
    let total = 0n;
    for (const linha of linhas) {
      const valor = decimalEscalado(linha.valor, 2, 16);
      if (valor === null) return 'Pendente';
      total += valor;
    }
    return total > maximo ? 'Acima do limite' : exibirReais(total);
  }

  protected salvar(compra: EditorCompra): void {
    if (this.ocupado() || compra.cancelada) return;
    this.erro.set('');
    const dados = this.montar(compra);
    if (!dados) return;
    const assinatura = JSON.stringify(dados);
    if (this.tentativa?.incerta && this.tentativa.assinatura !== assinatura) {
      this.erro.set(
        'O resultado é incerto. Reenvie os mesmos dados para verificar se a compra já foi criada.',
      );
      return;
    }
    if (!this.tentativa || this.tentativa.assinatura !== assinatura)
      this.tentativa = { assinatura, chave: crypto.randomUUID(), incerta: false };
    this.gravar(
      this.api.salvar(compra.id, dados, this.tentativa.chave),
      'Rascunho salvo.',
      !compra.id,
    );
  }

  protected cancelar(compra: EditorCompra): void {
    if (
      this.ocupado() ||
      !this.confirmarCancelamento() ||
      !compra.id ||
      !compra.versao ||
      compra.cancelada
    )
      return;
    this.gravar(this.api.cancelar(compra.id, compra.versao), 'Compra cancelada.');
  }

  private montar(compra: EditorCompra): CompraInput | null {
    if (!compra.fornecedorId || !compra.data || !compra.linhas.length) {
      this.erro.set('Informe o fornecedor, a data e pelo menos uma linha.');
      return null;
    }
    const linhas: CompraInput['linhas'] = [];
    for (const [indice, linha] of compra.linhas.entries()) {
      const kit = linha.tipo === 'kit' ? linha.kit.trim() : null;
      const valor = decimalEscalado(linha.valor, 2, 16);
      const quantidadeKit = decimalEscalado(linha.quantidade, 0, 12);
      if (
        (kit !== null && (!kit || !quantidadeKit || linha.itens.length < 2)) ||
        (linha.valor.trim() && valor === null)
      ) {
        this.erro.set(`Confira o nome e a quantidade do kit ou o valor da linha ${indice + 1}.`);
        return null;
      }
      const componentes: CompraInput['linhas'][number]['componentes'] = [];
      for (const item of linha.itens) {
        const casas = item.apresentacaoId || this.unidade(item) === 'un' ? 0 : 6;
        const quantidade = decimalEscalado(item.quantidade, casas, 12);
        if (!item.insumoId || !quantidade) {
          this.erro.set(
            `Selecione o material e informe uma quantidade positiva na linha ${indice + 1}. Use inteiros para embalagens e unidades.`,
          );
          return null;
        }
        componentes.push({
          insumoId: item.insumoId,
          apresentacaoId: item.apresentacaoId || null,
          quantidade: decimalTexto(quantidade, casas),
          snapshotId: item.snapshot?.id ?? null,
        });
      }
      linhas.push({
        kit,
        quantidade: kit ? quantidadeKit!.toString() : componentes[0].quantidade,
        valorTotal: valor === null ? null : decimalTexto(valor, 2),
        componentes,
      });
    }
    return {
      fornecedorId: compra.fornecedorId,
      dataCompra: compra.data,
      versao: compra.versao,
      linhas,
    };
  }

  private gravar(requisicao: Observable<DetalheCompra>, mensagem: string, criacao = false): void {
    this.ocupado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    requisicao
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: () => {
          this.tentativa = null;
          this.editor.set(null);
          this.sucesso.set(mensagem);
          this.carregar();
        },
        error: (erro: unknown) => {
          if (
            criacao &&
            this.tentativa &&
            (!(erro instanceof HttpErrorResponse) || erro.status === 0 || erro.status >= 500)
          ) {
            this.tentativa.incerta = true;
            this.erro.set(
              'O resultado é incerto. Repita o envio com os mesmos dados para evitar duplicação.',
            );
          } else {
            this.erro.set(
              this.mensagem(erro, 'Não foi possível salvar a compra. Tente novamente.'),
            );
          }
        },
      });
  }

  private mensagem(erro: unknown, padrao: string): string {
    if (erro instanceof HttpErrorResponse) {
      if (erro.status === 401) void this.router.navigateByUrl('/admin/login');
      if (erro.status === 403) return 'Acesso negado ou sessão inválida. Entre novamente.';
      if (erro.status === 404) return 'Compra não encontrada. Atualize a listagem.';
      const detalhe =
        erro.status === 409
          ? erro.error?.detail
          : erro.status === 400
            ? erro.error?.errors?.compra?.[0]
            : null;
      if (typeof detalhe === 'string') return detalhe;
    }
    return padrao;
  }
}
