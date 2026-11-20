import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, catchError, finalize, of, switchMap, tap } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { OperacaoPendenteStore } from '../../core/admin-estoque/operacao-pendente.store';
import { ProdutosAcabadosService } from '../../core/admin-estoque/produtos-acabados.service';
import { ProdutosService } from '../../core/admin-produtos/produtos.service';
import {
  canaisVenda,
  meiosVenda,
  CanalVenda,
  DetalheVenda,
  MeioVenda,
  RecebimentoInput,
  ResumoVenda,
  TentativaVenda,
  VendasExternasService,
} from '../../core/vendas-externas.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { decimalEscalado, decimalTexto, exibirReais } from '../compras/compra-decimais';
import { dataCivilValida } from '../compras/recebimento-pendente';

interface Opcao {
  id: string;
  produtoId: string;
  nome: string;
  preco: string;
}
interface Linha {
  opcao: Opcao;
  quantidade: number | null;
  preco: string;
  desconto: string;
}
type SemChave<T> = T extends TentativaVenda ? Omit<T, 'chave'> : never;
const limite = 9223372036854775807n;
const uuid = /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i;
const hoje = () => {
  const data = new Date();
  return [data.getFullYear(), data.getMonth() + 1, data.getDate()]
    .map((n) => String(n).padStart(2, '0'))
    .join('-');
};
const centavos = (valor: string) => decimalEscalado(valor, 2, 17);

@Component({
  imports: [FormsModule, RouterLink, AdminWorkspaceComponent],
  templateUrl: './admin-vendas-externas.component.html',
})
export class AdminVendasExternasComponent {
  private readonly api = inject(VendasExternasService);
  private readonly auth = inject(AdminAuthService);
  private readonly estoque = inject(ProdutosAcabadosService);
  private readonly produtos = inject(ProdutosService);
  private readonly storage = inject(OperacaoPendenteStore);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private chaveStorage = '';

  protected readonly canais = canaisVenda;
  protected readonly meios = meiosVenda;
  protected readonly estados = ['Pendente', 'Parcial', 'Paga', 'Cancelada'];
  protected readonly lista = signal<ResumoVenda[]>([]);
  protected readonly detalhe = signal<DetalheVenda | null>(null);
  protected readonly nova = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly sessao = signal(false);
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');
  protected readonly falhaLista = signal(false);
  protected readonly pendente = signal<TentativaVenda | null>(null);
  protected readonly armazenamentoInvalido = signal(false);
  protected readonly reenviavel = signal(false);
  protected readonly confirmaDescarte = signal(false);
  protected readonly pagina = signal(1);
  protected filtroCanal = '';
  protected filtroSituacao = '';
  protected readonly opcoes = signal<Opcao[]>([]);
  protected selecionada = '';
  protected readonly saldos = signal<Record<string, number>>({});
  protected readonly linhas = signal<Linha[]>([]);
  protected readonly totais = computed(() => {
    let total = 0n;
    const subtotais = this.linhas().map((linha) => {
      const preco = centavos(linha.preco),
        desconto = centavos(linha.desconto);
      const quantidade = linha.quantidade;
      if (
        preco === null ||
        desconto === null ||
        preco > limite ||
        desconto > limite ||
        quantidade === null ||
        !Number.isInteger(quantidade) ||
        quantidade < 1 ||
        quantidade > 2147483647
      )
        return null;
      const bruto = preco * BigInt(quantidade);
      if (bruto > limite || desconto > bruto) return null;
      const subtotal = bruto - desconto;
      total += subtotal;
      return subtotal;
    });
    return { subtotais, total: subtotais.includes(null) || total > limite ? null : total };
  });
  protected canal: CanalVenda = 'Presencial';
  protected descricao = '';
  protected dataVenda = hoje();
  protected comprador = '';
  protected vencimento = '';
  protected pagamento: 'pendente' | 'parcial' | 'integral' = 'pendente';
  protected valor = '';
  protected meio: MeioVenda = 'Pix';
  protected dataRecebimento = hoje();
  protected acao: 'receber' | 'cancelar' | null = null;
  protected confirmado = false;
  protected motivo = '';

  constructor() {
    this.auth
      .getCurrentSession()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (sessao) => {
          if (!sessao) {
            void this.router.navigateByUrl('/admin/login');
            return;
          }
          this.sessao.set(true);
          this.chaveStorage = `xaquiamuni:vendas-externas:v1:${sessao.userId}`;
          try {
            const texto = this.storage.ler(this.chaveStorage);
            if (texto) {
              const dados = JSON.parse(texto) as TentativaVenda;
              if (
                !dados ||
                typeof dados.chave !== 'string' ||
                !uuid.test(dados.chave) ||
                !dados.corpo ||
                !['criar', 'receber', 'cancelar'].includes(dados.tipo) ||
                (dados.tipo !== 'criar' && (!uuid.test(dados.id) || !uuid.test(dados.versao)))
              )
                throw new Error('Tentativa inválida');
              this.pendente.set(dados);
            }
          } catch {
            this.armazenamentoInvalido.set(true);
          }
          if (this.pendente()) this.conferir();
          else this.carregar();
        },
        error: (erro) => this.erro.set(this.mensagem(erro, 'Não foi possível confirmar a sessão.')),
      });
  }

  protected carregar(pagina = this.pagina()): void {
    this.falhaLista.set(false);
    this.executar(
      this.api.listar(this.filtroCanal, this.filtroSituacao, pagina),
      (vendas) => {
        this.lista.set(vendas);
        this.pagina.set(pagina);
      },
      () => this.falhaLista.set(true),
    );
  }

  protected abrir(id: string): void {
    this.executar(this.api.detalhe(id), (venda) => this.mostrar(venda));
  }

  protected voltar(): void {
    this.detalhe.set(null);
    this.nova.set(false);
    this.acao = null;
    this.carregar();
  }

  protected novaVenda(): void {
    if (this.pendente() || this.armazenamentoInvalido()) return;
    this.executar(this.produtos.listar(), (produtos) => {
      this.opcoes.set(
        produtos.flatMap((p) =>
          p.variantes
            .filter((v) => v.ativa)
            .map((v) => {
              const preco = centavos(String(v.precoVenda));
              return {
                id: v.id,
                produtoId: p.id,
                nome: p.nome + ' · ' + v.identificacao,
                preco:
                  preco !== null && preco <= BigInt(Number.MAX_SAFE_INTEGER)
                    ? decimalTexto(preco, 2).replace('.', ',')
                    : '',
              };
            }),
        ),
      );
      this.selecionada = '';
      this.linhas.set([]);
      this.saldos.set({});
      this.canal = 'Presencial';
      this.descricao = this.comprador = this.vencimento = this.valor = '';
      this.dataVenda = this.dataRecebimento = hoje();
      this.pagamento = 'pendente';
      this.meio = 'Pix';
      this.nova.set(true);
    });
  }

  protected adicionar(): void {
    const opcao = this.opcoes().find((o) => o.id === this.selecionada);
    if (!opcao || this.linhas().length >= 50 || this.linhas().some((l) => l.opcao.id === opcao.id))
      return;
    this.linhas.update((linhas) => [
      ...linhas,
      { opcao, quantidade: 1, preco: opcao.preco, desconto: '0' },
    ]);
    this.selecionada = '';
    this.estoque
      .consultarPosicao(opcao.id)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (posicao) =>
          this.saldos.update((saldos) => ({ ...saldos, [opcao.id]: posicao.saldoDisponivel })),
        error: () => {
          /* A estimativa é opcional. O servidor confere estoque ao gravar. */
        },
      });
  }

  protected jaAdicionada(id: string): boolean {
    return this.linhas().some((linha) => linha.opcao.id === id);
  }

  protected atualizar(id: string, alteracao: Partial<Omit<Linha, 'opcao'>>): void {
    this.linhas.update((linhas) =>
      linhas.map((l) => (l.opcao.id === id ? { ...l, ...alteracao } : l)),
    );
  }

  protected remover(id: string): void {
    this.linhas.update((linhas) => linhas.filter((l) => l.opcao.id !== id));
  }

  protected saldoEstimado(): bigint | null {
    const total = this.totais().total;
    if (total === null) return null;
    if (this.pagamento === 'integral') return 0n;
    const recebido = this.pagamento === 'parcial' ? centavos(this.valor) : 0n;
    return recebido === null || recebido > total ? null : total - recebido;
  }

  protected salvar(): void {
    const total = this.totais().total;
    if (
      !total ||
      !dataCivilValida(this.dataVenda) ||
      (this.vencimento &&
        (!dataCivilValida(this.vencimento) || this.vencimento < this.dataVenda)) ||
      (this.canal === 'Outro' && !this.descricao.trim())
    ) {
      this.erro.set(
        'Confira canal, datas, quantidades, preços e descontos. O total deve ser positivo.',
      );
      return;
    }
    const valor = this.pagamento === 'integral' ? total : centavos(this.valor);
    const inicial =
      this.pagamento === 'pendente' ? null : this.validarRecebimento(valor, total, this.dataVenda);
    if (
      this.pagamento !== 'pendente' &&
      (!inicial || (this.pagamento === 'parcial' && valor === total))
    ) {
      this.erro.set(
        'Informe data e valor válidos. Para pagamento parcial, use um valor menor que o total.',
      );
      return;
    }
    if ((!inicial || valor !== total) && !this.comprador.trim()) {
      this.erro.set('Informe o nome ou a referência do comprador para registrar o saldo pendente.');
      return;
    }
    this.preparar({
      tipo: 'criar',
      corpo: {
        canal: this.canal,
        descricaoCanal: this.canal === 'Outro' ? this.descricao.trim() : null,
        dataVenda: this.dataVenda,
        comprador: this.comprador.trim() || null,
        vencimento: this.vencimento || null,
        itens: this.linhas().map((l) => ({
          produtoId: l.opcao.produtoId,
          varianteId: l.opcao.id,
          quantidade: l.quantidade!,
          precoUnitarioCentavos: centavos(l.preco)!.toString(),
          descontoCentavos: centavos(l.desconto)!.toString(),
        })),
        recebimentoInicial: inicial,
      },
    });
  }

  protected abrirAcao(tipo: 'receber' | 'cancelar'): void {
    this.acao = tipo;
    this.confirmado = false;
    this.valor = this.motivo = '';
    this.meio = 'Pix';
    this.dataRecebimento = hoje();
  }

  protected confirmarAcao(): void {
    const venda = this.detalhe();
    if (!venda || venda.resumo.situacao !== 'Registrada' || !this.confirmado) return;
    const { id, versao, saldoCentavos, dataVenda } = venda.resumo;
    if (this.acao === 'receber') {
      const corpo = this.validarRecebimento(centavos(this.valor), BigInt(saldoCentavos), dataVenda);
      if (!corpo) {
        this.erro.set('Confira o valor, o saldo e a data do recebimento.');
        return;
      }
      this.preparar({ tipo: 'receber', id, versao, corpo });
    } else if (this.acao === 'cancelar' && !venda.recebimentos.length && this.motivo.trim()) {
      this.preparar({ tipo: 'cancelar', id, versao, corpo: { motivo: this.motivo.trim() } });
    }
  }

  private validarRecebimento(
    valor: bigint | null,
    limite: bigint,
    dataVenda: string,
  ): RecebimentoInput | null {
    return valor !== null &&
      valor > 0n &&
      valor <= limite &&
      dataCivilValida(this.dataRecebimento) &&
      this.dataRecebimento >= dataVenda
      ? { valorCentavos: valor.toString(), data: this.dataRecebimento, meio: this.meio }
      : null;
  }

  private preparar(dados: SemChave<TentativaVenda>): void {
    if (this.ocupado() || !this.sessao() || this.pendente() || this.armazenamentoInvalido()) return;
    const chave = this.storage.criarChaveIdempotencia();
    const tentativa = chave ? ({ ...dados, chave } as TentativaVenda) : null;
    if (!tentativa || !this.storage.salvar(this.chaveStorage, tentativa)) {
      this.erro.set('Não foi possível guardar a tentativa nesta aba. A operação não foi enviada.');
      return;
    }
    this.pendente.set(tentativa);
    this.enviar(tentativa, false);
  }

  protected conferir(): void {
    const tentativa = this.pendente();
    if (!tentativa || this.ocupado()) return;
    this.reenviavel.set(false);
    const resultado = this.api.tentativa(tentativa.chave, tentativa.tipo).pipe(
      catchError((erro) => {
        if (erro instanceof HttpErrorResponse && erro.status === 404) return of(null);
        throw erro;
      }),
    );
    this.executar(
      resultado.pipe(
        switchMap((confirmada) => {
          if (confirmada)
            return this.api.detalhe(confirmada.id).pipe(tap((venda) => this.concluir(venda)));
          if (tentativa.tipo === 'criar') {
            this.reenviavel.set(true);
            return of(null);
          }
          return this.api.detalhe(tentativa.id).pipe(
            tap((venda) => {
              this.mostrar(venda);
              if (
                venda.resumo.versao === tentativa.versao &&
                venda.resumo.situacao === 'Registrada'
              ) {
                this.reenviavel.set(true);
              } else {
                this.erro.set(
                  'A venda mudou. Confira o hist?rico antes de descartar a tentativa. O reenvio foi bloqueado.',
                );
              }
            }),
          );
        }),
      ),
      () => {},
    );
  }

  protected reenviar(): void {
    const tentativa = this.pendente();
    if (tentativa && this.reenviavel() && !this.ocupado()) this.enviar(tentativa, true);
  }

  protected descartar(): void {
    if (this.ocupado() || !this.confirmaDescarte() || !this.limparTentativa()) return;
    this.armazenamentoInvalido.set(false);
    this.voltar();
  }

  private enviar(tentativa: TentativaVenda, reenvio: boolean): void {
    this.reenviavel.set(false);
    let confirmada = false;
    this.executar(
      this.api.enviar(tentativa).pipe(
        switchMap((resposta) => {
          confirmada = true;
          return this.api.detalhe(resposta.id);
        }),
      ),
      (venda) => this.concluir(venda),
      (erro) => {
        if (
          !confirmada &&
          !reenvio &&
          erro instanceof HttpErrorResponse &&
          erro.status === 400 &&
          erro.error?.errors?.venda
        )
          this.limparTentativa();
      },
    );
  }

  private concluir(venda: DetalheVenda): void {
    this.limparTentativa();
    this.mostrar(venda);
    this.sucesso.set('Operação confirmada. O histórico foi atualizado.');
  }

  private limparTentativa(): boolean {
    if (!this.storage.remover(this.chaveStorage)) {
      this.erro.set(
        'Não foi possível remover a tentativa guardada. Confira o histórico antes de continuar.',
      );
      return false;
    }
    this.pendente.set(null);
    this.reenviavel.set(false);
    this.confirmaDescarte.set(false);
    return true;
  }

  private mostrar(venda: DetalheVenda): void {
    this.nova.set(false);
    this.detalhe.set(venda);
    this.acao = null;
  }

  private executar<T>(
    request: Observable<T>,
    resultado: (valor: T) => void,
    falha?: (erro: unknown) => void,
  ): void {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    request
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.ocupado.set(false)),
      )
      .subscribe({
        next: resultado,
        error: (erro) => {
          falha?.(erro);
          this.erro.set(
            this.mensagem(
              erro,
              'Não foi possível confirmar a operação. Confira a situação e tente novamente.',
            ),
          );
        },
      });
  }

  private mensagem(erro: unknown, padrao: string): string {
    if (!(erro instanceof HttpErrorResponse)) return padrao;
    if (erro.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return 'Sua sessão expirou.';
    }
    if (erro.status === 403) return 'Acesso negado. Confira sua sessão administrativa.';
    if (erro.status === 404) return 'Venda não encontrada.';
    if (erro.status === 503)
      return 'O serviço está temporariamente indisponível. Confira a situação antes de reenviar.';
    const detalhe = erro.status === 409 ? erro.error?.detail : erro.error?.errors?.venda?.[0];
    return typeof detalhe === 'string' ? detalhe : padrao;
  }

  protected brl(valor: string | bigint | null): string {
    return valor === null ? '—' : exibirReais(BigInt(valor));
  }

  protected civil(valor: string): string {
    return valor.split('-').reverse().join('/');
  }
  protected rotuloMeio(meio: MeioVenda): string {
    return meio === 'Cartao' ? 'Cartão' : meio;
  }
  protected baixasDoItem(venda: DetalheVenda, id: string) {
    return venda.baixas.filter((b) => b.itemId === id);
  }
}
