import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { EMPTY, Observable, expand, finalize, forkJoin, map, of, reduce, switchMap } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { OperacaoPendenteStore } from '../../core/admin-estoque/operacao-pendente.store';
import {
  AdminFormaCompraPrecisa,
  AdminResumoInsumoPreciso,
  InsumosService,
} from '../../core/admin-estoque/insumos.service';
import {
  CompraInput,
  ComprasMateriasPrimasService,
  DetalheCompra,
  ReceberCompraInput,
  ResumoCompra,
} from '../../core/compras-materias-primas.service';
import { Fornecedor, FornecedoresService } from '../../core/fornecedores.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { decimalEscalado, decimalTexto, exibirReais } from './compra-decimais';

import { dataCivilValida, lerRecebimento, RecebimentoPendente } from './recebimento-pendente';

type PoliticaValidade = { controlaLote: boolean; validadeObrigatoria: boolean };

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
  imports: [AdminWorkspaceComponent, FormsModule, DatePipe, RouterLink],
  templateUrl: './admin-compras-materias-primas.component.html',
})
export class AdminComprasMateriasPrimasComponent {
  private readonly api = inject(ComprasMateriasPrimasService);
  private readonly fornecedoresApi = inject(FornecedoresService);
  private readonly insumosApi = inject(InsumosService);
  private readonly destroy = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly auth = inject(AdminAuthService);
  private readonly storage = inject(OperacaoPendenteStore);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly administrador = signal<string | null>(null);
  protected readonly revisao = signal<DetalheCompra | null>(null);
  protected readonly politicas = signal<Record<string, PoliticaValidade> | null>(null);
  protected readonly validades = signal<Record<string, string>>({});
  protected readonly pendente = signal<RecebimentoPendente | null>(null);
  protected readonly podeReenviar = signal(false);
  protected readonly validadesValidas = computed(() => {
    const compra = this.revisao();
    const politicas = this.politicas();
    if (!compra || !politicas) return false;
    const validades = this.validades();
    return (
      compra.linhas.length > 0 &&
      compra.linhas.every(
        (linha) =>
          linha.componentes.length > 0 &&
          linha.componentes.every((item) => {
            const politica = politicas[item.insumoId];
            const valor = validades[item.id];
            return (
              !!politica &&
              (!politica.controlaLote ||
                (valor ? dataCivilValida(valor) : !politica.validadeObrigatoria))
            );
          }),
      )
    );
  });

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
    this.auth
      .getCurrentSession()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (sessao) => {
          this.administrador.set(sessao?.userId ?? null);
          if (!sessao) return;
          try {
            const pendente = lerRecebimento(this.storage.ler(this.chaveStorage(sessao.userId)));
            if (pendente) {
              this.pendente.set(pendente);
              this.conferirPendente();
            }
          } catch {
            // A listagem permanece utilizável quando o armazenamento está bloqueado.
          }
        },
        error: () => {
          /* A consulta de sessão pode ser repetida ao entrar novamente. */
        },
      });
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
        next: (compras) => {
          const confirmado = this.revisao()?.resumo;
          this.compras.set(
            compras.map((compra) => (compra.id === confirmado?.id ? confirmado : compra)),
          );
        },
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
    if (this.pendente()?.compraId === id) {
      this.conferirPendente();
      return;
    }
    this.abrir(
      this.api
        .obter(id)
        .pipe(
          switchMap((detalhe) =>
            detalhe.resumo.situacao !== 'Rascunho'
              ? of({ detalhe, fornecedores: [], insumos: [] })
              : forkJoin({
                  detalhe: of(detalhe),
                  fornecedores: this.fornecedoresApi.listar(),
                  insumos: this.listarInsumos(),
                }),
          ),
        ),
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
          if (detalhe && detalhe.resumo.situacao !== 'Rascunho') {
            this.exibirRevisao(detalhe);
            return;
          }
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

  protected editarAposConferencia(id: string): void {
    if (this.ocupado() || (this.pendente() && !this.podeReenviar())) return;
    this.limparPendente();
    this.revisao.set(null);
    this.consultar(id);
  }

  protected iniciarRecebimento(id: string): void {
    if (this.ocupado()) return;
    if (this.pendente()) {
      this.conferirPendente();
      return;
    }
    this.ocupado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    this.api
      .obter(id)
      .pipe(
        switchMap((compra) => {
          if (compra.resumo.situacao !== 'Rascunho') return of({ compra, politicas: null });
          const ids = [
            ...new Set(compra.linhas.flatMap((l) => l.componentes.map((item) => item.insumoId))),
          ];
          if (!ids.length) return of({ compra, politicas: null });
          return forkJoin(
            ids.map((insumoId) =>
              this.insumosApi.obterParaEntrada(insumoId).pipe(
                map(
                  ({ resumo }) =>
                    [
                      insumoId,
                      {
                        controlaLote: resumo.controlaLote,
                        validadeObrigatoria: resumo.validadeObrigatoria,
                      },
                    ] as const,
                ),
              ),
            ),
          ).pipe(map((dados) => ({ compra, politicas: Object.fromEntries(dados) })));
        }),
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: ({ compra, politicas }) => {
          this.exibirRevisao(compra);
          this.politicas.set(politicas);
          if (!politicas && compra.resumo.situacao === 'Rascunho')
            this.erro.set('A compra não tem materiais para receber.');
        },
        error: (erro) =>
          this.erro.set(
            this.mensagem(
              erro,
              'Não foi possível consultar a compra e as regras de validade. Tente novamente.',
            ),
          ),
      });
  }

  protected selecionarValidade(id: string, valor: string): void {
    this.validades.update((validades) => ({ ...validades, [id]: valor }));
  }

  protected confirmarRecebimento(): void {
    if (this.ocupado()) return;
    const compra = this.revisao();
    if (!compra || compra.resumo.situacao !== 'Rascunho') return;
    const pendente = this.pendente();
    if (pendente) {
      if (this.podeReenviar() && pendente.compraId === compra.resumo.id)
        this.enviarRecebimento(pendente, compra, true);
      return;
    }
    if (!this.validadesValidas()) {
      this.erro.set('Confira as validades obrigatórias e informe datas válidas.');
      return;
    }
    const ator = this.administrador();
    const chave = this.storage.criarChaveIdempotencia();
    if (!ator || !chave) {
      this.erro.set(
        'Não foi possível preparar uma tentativa segura. Entre novamente ou use outro navegador.',
      );
      return;
    }
    const politicas = this.politicas()!;
    const validades = this.validades();
    const corpo: ReceberCompraInput = {
      versao: compra.versao,
      validades: compra.linhas.flatMap((linha) =>
        linha.componentes
          .filter((item) => politicas[item.insumoId].controlaLote && validades[item.id])
          .map((item) => ({ componenteId: item.id, dataValidade: validades[item.id] })),
      ),
    };
    const tentativa = { compraId: compra.resumo.id, chave, corpo };
    if (!this.storage.salvar(this.chaveStorage(ator), tentativa)) {
      this.erro.set(
        'O navegador não permitiu guardar a tentativa. Habilite o armazenamento da sessão e tente novamente.',
      );
      return;
    }
    this.pendente.set(tentativa);
    this.enviarRecebimento(tentativa, compra, false);
  }

  protected conferirPendente(): void {
    const pendente = this.pendente();
    if (!pendente || this.ocupado()) return;
    this.ocupado.set(true);
    this.podeReenviar.set(false);
    this.erro.set('');
    this.api
      .obter(pendente.compraId)
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: (compra) => {
          this.exibirRevisao(compra);
          if (compra.resumo.situacao === 'Recebida') {
            this.limparPendente();
            this.sucesso.set('Compra recebida. O estoque foi atualizado.');
          } else if (
            compra.resumo.situacao !== 'Rascunho' ||
            compra.versao !== pendente.corpo.versao
          ) {
            this.limparPendente();
            this.erro.set('A compra foi alterada. A tentativa anterior não pode ser reenviada.');
          } else {
            this.validades.set(
              Object.fromEntries(
                pendente.corpo.validades.map((v) => [v.componenteId, v.dataValidade]),
              ),
            );
            this.podeReenviar.set(true);
          }
        },
        error: (erro) => {
          if (erro instanceof HttpErrorResponse && erro.status === 404) this.limparPendente();
          this.erro.set(
            this.mensagem(
              erro,
              'Não foi possível conferir a situação. A tentativa foi preservada.',
            ),
          );
        },
      });
  }

  private enviarRecebimento(
    pendente: RecebimentoPendente,
    compra: DetalheCompra,
    reenvio: boolean,
  ): void {
    this.ocupado.set(true);
    this.podeReenviar.set(false);
    this.erro.set('');
    this.sucesso.set('');
    this.api
      .receber(pendente.compraId, pendente.corpo, pendente.chave)
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: (resultado) => {
          this.limparPendente();
          this.exibirRevisao({
            ...compra,
            resumo: { ...compra.resumo, situacao: resultado.situacao },
            versao: resultado.versao,
            recebidaEmUtc: resultado.recebidaEmUtc,
            recebidaPorId: resultado.recebidaPorId,
          });
          this.sucesso.set('Compra recebida. O estoque foi atualizado.');
        },
        error: (erro) => {
          const status = erro instanceof HttpErrorResponse ? erro.status : 0;
          if ((status === 400 && !reenvio) || status === 404) this.limparPendente();
          this.erro.set(
            this.mensagem(
              erro,
              status === 409
                ? 'O recebimento está em conflito. Confira a situação antes de reenviar.'
                : 'Não foi possível confirmar o resultado. Confira a situação antes de reenviar.',
            ),
          );
        },
      });
  }

  private exibirRevisao(compra: DetalheCompra): void {
    this.editor.set(null);
    this.confirmarCancelamento.set(false);
    this.revisao.set(compra);
    this.validades.set({});
    this.politicas.set(null);
    this.compras.update((lista) =>
      lista.map((item) => (item.id === compra.resumo.id ? compra.resumo : item)),
    );
    afterNextRender(
      () => this.elemento.nativeElement.querySelector<HTMLElement>('#recebimento-titulo')?.focus(),
      { injector: this.injector },
    );
  }

  private chaveStorage(ator: string): string {
    return `xaquiamuni:compras:recebimento:v1:${ator}`;
  }

  private limparPendente(): void {
    const ator = this.administrador();
    if (ator) this.storage.remover(this.chaveStorage(ator));
    this.pendente.set(null);
    this.podeReenviar.set(false);
  }

  protected fechar(): void {
    if (this.tentativa?.incerta) {
      this.erro.set(
        'Repita o envio com os mesmos dados para confirmar o resultado antes de fechar.',
      );
      return;
    }
    this.editor.set(null);
    this.revisao.set(null);
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
      if (erro.status === 401) {
        void this.router.navigateByUrl('/admin/login');
        return 'Sua sessão expirou. Entre novamente.';
      }
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
