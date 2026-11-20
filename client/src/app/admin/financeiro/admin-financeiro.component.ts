import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { OperacaoPendenteStore } from '../../core/admin-estoque/operacao-pendente.store';
import {
  ComprasMateriasPrimasService,
  ResumoCompra,
} from '../../core/compras-materias-primas.service';
import {
  ComandoFinanceiro,
  CompraFinanceira,
  FinanceiroService,
  LancamentoFinanceiro,
  MeioFinanceiro,
  PagamentoFornecedor,
  TentativaFinanceira,
  meiosFinanceiros,
} from '../../core/financeiro.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { decimalEscalado, decimalTexto, exibirReais } from '../compras/compra-decimais';
import { dataCivilValida } from '../compras/recebimento-pendente';

const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
function hoje(): string {
  const data = new Date();
  return [data.getFullYear(), data.getMonth() + 1, data.getDate()]
    .map((n) => String(n).padStart(2, '0'))
    .join('-');
}

export function centavosFinanceiros(entrada: string): bigint | null {
  const texto = entrada.trim();
  if (!/^(?:\d+|\d{1,3}(?:\.\d{3})+)(?:,\d{1,2})?$/.test(texto)) return null;
  const valor = decimalEscalado(texto.replaceAll('.', ''), 2, 17);
  return valor !== null && valor > 0n && valor <= 9223372036854775807n ? valor : null;
}

@Component({
  imports: [AdminWorkspaceComponent, FormsModule, DatePipe],
  templateUrl: './admin-financeiro.component.html',
  styleUrl: './admin-financeiro.component.css',
})
export class AdminFinanceiroComponent {
  private readonly api = inject(FinanceiroService);
  private readonly comprasApi = inject(ComprasMateriasPrimasService);
  private readonly auth = inject(AdminAuthService);
  private readonly store = inject(OperacaoPendenteStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly injector = inject(Injector);
  private readonly tituloFormulario = viewChild<ElementRef<HTMLElement>>('tituloFormulario');
  private chaveStorage = '';

  protected secao: 'lancamentos' | 'compras' = 'lancamentos';
  protected lista: LancamentoFinanceiro[] = [];
  protected compras: ResumoCompra[] = [];
  protected compraSelecionada = '';
  protected compra: CompraFinanceira | null = null;
  protected detalhe: LancamentoFinanceiro | null = null;
  protected pagamentos: PagamentoFornecedor[] = [];
  protected pagina = 1;
  protected paginaPagamentos = 1;
  protected ocupado = false;
  protected carregado = false;
  protected erro = '';
  protected sucesso = '';
  protected pendente: TentativaFinanceira | null = null;
  protected armazenamentoInvalido = false;
  protected conferida = false;
  protected confirmarDescarte = false;
  protected formulario: 'criar' | 'cancelar' | 'pagar' | null = null;
  protected tipo: 'Entrada' | 'Saida' = 'Entrada';
  protected categoria = '';
  protected descricao = '';
  protected valor = '';
  protected data = hoje();
  protected meio: MeioFinanceiro = 'Pix';
  protected motivo = '';
  protected referencia = '';
  protected confirmacao = false;
  protected readonly meios = meiosFinanceiros;
  protected readonly situacoes = {
    Cancelada: 'Cancelada',
    ValorAConferir: 'Valor a conferir',
    Quitada: 'Quitada',
    Pendente: 'Pendente',
    Parcial: 'Parcialmente paga',
  };

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
          this.chaveStorage = 'xaquiamuni:financeiro:v1:' + sessao.userId;
          try {
            const texto = this.store.ler(this.chaveStorage);
            if (texto) {
              const tentativa = JSON.parse(texto) as TentativaFinanceira;
              if (
                !tentativa ||
                !uuid.test(tentativa.chave) ||
                !tentativa.corpo ||
                !['criar', 'cancelar', 'pagar'].includes(tentativa.tipo) ||
                (tentativa.tipo !== 'criar' && !uuid.test(tentativa.id))
              ) {
                this.armazenamentoInvalido = true;
              } else {
                this.pendente = tentativa;
              }
            }
          } catch {
            this.armazenamentoInvalido = true;
          }
          const compraId = this.route.snapshot.queryParamMap.get('compra');
          if (this.pendente) this.conferir();
          else if (compraId && uuid.test(compraId)) {
            this.secao = 'compras';
            this.abrirCompra(compraId);
          } else this.carregar();
          this.cdr.markForCheck();
        },
        error: (erro) => {
          this.erro = this.mensagem(erro);
          this.cdr.markForCheck();
        },
      });
  }

  protected mudarSecao(secao: 'lancamentos' | 'compras'): void {
    this.secao = secao;
    this.detalhe = this.compra = null;
    this.compraSelecionada = '';
    this.formulario = null;
    this.carregar();
  }

  protected carregar(pagina = this.pagina): void {
    this.carregado = false;
    if (this.secao === 'compras') {
      this.executar(this.comprasApi.listar(), (compras) => {
        this.compras = compras;
        this.carregado = true;
      });
    } else {
      this.executar(this.api.lancamentos(pagina), (lista) => {
        this.lista = lista;
        this.pagina = pagina;
        this.carregado = true;
      });
    }
  }

  protected abrirLancamento(id: string): void {
    this.executar(this.api.lancamento(id), (dados) => {
      this.detalhe = dados;
    });
  }

  protected voltarLancamentos(): void {
    this.detalhe = null;
    this.formulario = null;
    this.carregar();
  }

  protected abrirCompra(id = this.compraSelecionada, pagina = 1, preservarMensagem = false): void {
    if (!id) return;
    this.compraSelecionada = id;
    this.executar(
      forkJoin({ compra: this.api.compra(id), pagamentos: this.api.pagamentos(id, pagina) }),
      (dados) => {
        this.compra = dados.compra;
        this.pagamentos = dados.pagamentos;
        this.paginaPagamentos = pagina;
      },
      undefined,
      preservarMensagem,
    );
  }

  protected abrirFormulario(tipo: 'criar' | 'cancelar' | 'pagar'): void {
    this.formulario = tipo;
    this.tipo = 'Entrada';
    this.categoria = this.descricao = this.valor = this.motivo = this.referencia = '';
    this.meio = 'Pix';
    this.data = hoje();
    this.confirmacao = false;
    afterNextRender(() => this.tituloFormulario()?.nativeElement.focus(), {
      injector: this.injector,
    });
  }

  protected preencherSaldo(): void {
    if (this.compra?.saldoCentavos)
      this.valor = decimalTexto(BigInt(this.compra.saldoCentavos), 2).replace('.', ',');
  }

  protected salvar(): void {
    if (this.ocupado || this.pendente || this.armazenamentoInvalido || !this.chaveStorage) return;
    let comando: ComandoFinanceiro;
    if (this.formulario === 'cancelar') {
      if (!this.detalhe || !this.confirmacao || !this.motivo.trim()) {
        this.erro = 'Informe o motivo e confirme o cancelamento.';
        return;
      }
      comando = { tipo: 'cancelar', id: this.detalhe.id, corpo: { motivo: this.motivo.trim() } };
    } else {
      const valor = centavosFinanceiros(this.valor);
      if (valor === null || !dataCivilValida(this.data)) {
        this.erro = 'Informe um valor positivo, com até duas casas decimais, e uma data válida.';
        return;
      }
      const movimento = { valorCentavos: valor.toString(), data: this.data, meio: this.meio };
      if (this.formulario === 'pagar') {
        if (!this.compra || !this.podePagar() || valor > BigInt(this.compra.saldoCentavos!)) {
          this.erro = 'O valor deve ser menor ou igual ao saldo da compra.';
          return;
        }
        comando = {
          tipo: 'pagar',
          id: this.compra.compraId,
          corpo: { ...movimento, referencia: this.referencia.trim() || null },
        };
      } else {
        if (!this.categoria.trim() || !this.descricao.trim()) {
          this.erro = 'Informe a categoria e a descrição.';
          return;
        }
        comando = {
          tipo: 'criar',
          corpo: {
            ...movimento,
            tipo: this.tipo,
            categoria: this.categoria.trim(),
            descricao: this.descricao.trim(),
          },
        };
      }
    }
    const chave = this.store.criarChaveIdempotencia();
    if (!chave || !this.store.salvar(this.chaveStorage, { ...comando, chave })) {
      this.erro = 'Não foi possível guardar a tentativa nesta aba. A operação não foi enviada.';
      return;
    }
    this.pendente = { ...comando, chave };
    this.enviar(this.pendente, false);
  }

  protected podePagar(): boolean {
    return (
      !!this.compra &&
      this.compra.situacaoFinanceira !== 'Cancelada' &&
      this.compra.saldoCentavos !== null &&
      BigInt(this.compra.saldoCentavos) > 0n
    );
  }

  protected conferir(): void {
    const tentativa = this.pendente;
    if (!tentativa || this.ocupado) return;
    this.conferida = false;
    if (tentativa.tipo === 'pagar') {
      this.secao = 'compras';
      this.compraSelecionada = tentativa.id;
      this.executar(
        forkJoin({
          compra: this.api.compra(tentativa.id),
          pagamentos: this.api.pagamentos(tentativa.id, 1),
        }),
        (dados) => {
          this.compra = dados.compra;
          this.pagamentos = dados.pagamentos;
          this.paginaPagamentos = 1;
          this.conferida = true;
        },
      );
    } else {
      this.secao = 'lancamentos';
      if (tentativa.tipo === 'cancelar') {
        this.executar(this.api.lancamento(tentativa.id), (dados) => {
          this.detalhe = dados;
          this.conferida = true;
        });
      } else {
        this.executar(this.api.lancamentos(1), (dados) => {
          this.lista = dados;
          this.pagina = 1;
          this.carregado = true;
          this.conferida = true;
        });
      }
    }
  }

  protected reenviar(): void {
    if (this.pendente && this.conferida) this.enviar(this.pendente, true);
  }

  protected descartar(): void {
    if (this.confirmarDescarte && this.limpar()) {
      this.formulario = null;
      if (this.secao === 'compras' && this.compraSelecionada) this.abrirCompra();
      else this.carregar();
    }
  }

  private enviar(tentativa: TentativaFinanceira, reenvio: boolean): void {
    this.conferida = false;
    this.executar(
      this.api.enviar(tentativa),
      (dados) => {
        this.limpar();
        this.formulario = null;
        this.sucesso = 'Operação confirmada pelo servidor.';
        if (tentativa.tipo === 'pagar') {
          this.abrirCompra(tentativa.id, 1, true);
        } else if ('tipo' in dados) {
          this.detalhe = dados;
        }
      },
      (falha) => {
        if (
          falha instanceof HttpErrorResponse &&
          !reenvio &&
          [400, 404, 409].includes(falha.status)
        ) {
          this.limpar();
          if (tentativa.tipo === 'pagar' && falha.status === 409) {
            this.formulario = null;
            this.abrirCompra(tentativa.id, 1, true);
          }
        }
      },
    );
  }

  private limpar(): boolean {
    if (!this.store.remover(this.chaveStorage)) {
      this.erro = 'Não foi possível remover a tentativa guardada nesta aba.';
      return false;
    }
    this.pendente = null;
    this.armazenamentoInvalido = this.conferida = this.confirmarDescarte = false;
    return true;
  }

  private executar<T>(
    request: Observable<T>,
    proximo: (dados: T) => void,
    falha?: (erro: unknown) => void,
    preservarMensagem = false,
  ): void {
    if (this.ocupado) return;
    this.ocupado = true;
    if (!preservarMensagem) this.erro = this.sucesso = '';
    request.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (dados) => {
        this.ocupado = false;
        proximo(dados);
        this.cdr.markForCheck();
      },
      error: (erro) => {
        this.ocupado = false;
        this.erro = this.mensagem(erro);
        falha?.(erro);
        this.cdr.markForCheck();
      },
    });
  }

  private mensagem(erro: unknown): string {
    if (!(erro instanceof HttpErrorResponse))
      return 'Não foi possível concluir a consulta ou operação.';
    if (erro.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return 'Sua sessão expirou.';
    }
    if (erro.status === 403)
      return 'Acesso negado. Confira a sessão administrativa e tente novamente.';
    if (erro.status === 404) return 'Registro não encontrado.';
    if (erro.status === 503 || erro.status === 0)
      return 'Serviço indisponível ou conexão interrompida. Confira o estado antes de tentar novamente.';
    if (erro.status === 400)
      return erro.error?.errors?.financeiro?.[0] ?? 'Confira os dados informados.';
    if (erro.status === 409)
      return erro.error?.detail ?? 'Os dados mudaram. Confira a situação atual.';
    return 'Não foi possível confirmar a operação. Consulte o estado no servidor.';
  }

  protected moeda(valor: string | null): string {
    return valor === null ? 'Valor a conferir' : exibirReais(BigInt(valor));
  }
  protected civil(valor: string): string {
    return valor.split('-').reverse().join('/');
  }
  protected rotuloMeio(meio: MeioFinanceiro): string {
    return meio === 'Cartao' ? 'Cartão' : meio === 'Transferencia' ? 'Transferência' : meio;
  }
}
