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
import { Router, RouterLink } from '@angular/router';
import { Observable, catchError, finalize, of, switchMap } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { OperacaoPendenteStore } from '../../core/admin-estoque/operacao-pendente.store';
import {
  DetalheOrdem,
  OrdensProducaoService,
  PreviaProducao,
  ResumoOrdem,
} from '../../core/ordens-producao.service';
import { ReceitasProducaoService, ResumoReceita } from '../../core/receitas-producao.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { decimalEscalado } from '../compras/compra-decimais';
import {
  lerTentativaProducao,
  quantidadePlanejadaValida,
  TentativaProducao,
} from './tentativa-producao';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, FormsModule, DatePipe, RouterLink],
  templateUrl: './admin-producao.component.html',
})
export class AdminProducaoComponent {
  private readonly api = inject(OrdensProducaoService);
  private readonly receitasApi = inject(ReceitasProducaoService);
  private readonly auth = inject(AdminAuthService);
  private readonly storage = inject(OperacaoPendenteStore);
  private readonly destroy = inject(DestroyRef);
  private readonly router = inject(Router);
  private ator: string | null = null;

  protected readonly lista = signal<ResumoOrdem[]>([]);
  protected readonly receitas = signal<ResumoReceita[]>([]);
  protected readonly detalhe = signal<DetalheOrdem | null>(null);
  protected readonly nova = signal(false);
  protected readonly receitaId = signal('');
  protected readonly quantidade = signal<number | null>(null);
  protected readonly previa = signal<PreviaProducao | null>(null);
  protected readonly filtro = signal('');
  protected readonly confirmarCancelamento = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly falhaLista = signal(false);
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');
  protected readonly pendente = signal<TentativaProducao | null>(null);
  protected readonly podeReenviar = signal(false);
  protected readonly visiveis = computed(() =>
    this.lista().filter((o) => !this.filtro() || o.situacao === this.filtro()),
  );
  protected readonly podeIniciar = computed(() => {
    const previa = this.previa();
    return previa !== null && previa.materiais.every((m) => decimalEscalado(m.falta, 6, 12) === 0n);
  });

  constructor() {
    this.carregar();
    this.auth
      .getCurrentSession()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (sessao) => {
          this.ator = sessao?.userId ?? null;
          if (!this.ator) return;
          try {
            this.pendente.set(lerTentativaProducao(this.storage.ler(this.chaveStorage(this.ator))));
          } catch {
            // O formulário verifica se consegue guardar a tentativa antes de enviar.
          }
          if (this.pendente()) this.conferirPendente();
        },
        error: () => {
          /* Ações de escrita exigem sessão identificada. */
        },
      });
  }

  protected carregar(): void {
    this.falhaLista.set(false);
    this.api
      .listar()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (lista) => {
          const atual = this.detalhe()?.resumo;
          this.lista.set(lista.map((o) => (atual?.id === o.id ? atual : o)));
        },
        error: (erro: unknown) => {
          this.falhaLista.set(true);
          this.erro.set(this.mensagem(erro, 'Não foi possível carregar as ordens.'));
        },
      });
  }

  protected abrirNova(): void {
    if (this.pendente()) return;
    this.executar(
      this.receitasApi.listar(),
      (lista) => {
        this.receitas.set(lista.filter((r) => r.ativa));
        this.receitaId.set('');
        this.quantidade.set(null);
        this.previa.set(null);
        this.detalhe.set(null);
        this.nova.set(true);
      },
      'Não foi possível carregar as receitas.',
    );
  }

  protected prever(): void {
    const quantidade = this.quantidade();
    if (!this.receitaId() || !quantidadePlanejadaValida(quantidade)) {
      this.erro.set('Escolha uma receita e informe uma quantidade inteira positiva.');
      return;
    }
    this.previa.set(null);
    this.executar(
      this.api.prever(this.receitaId(), quantidade),
      (previa) => this.previa.set(previa),
      'Não foi possível consultar a disponibilidade.',
    );
  }

  protected iniciar(): void {
    const previa = this.previa(),
      quantidade = this.quantidade();
    if (
      this.ocupado() ||
      this.pendente() ||
      !previa ||
      !this.podeIniciar() ||
      !quantidadePlanejadaValida(quantidade)
    )
      return;
    this.preparar((chave) => ({
      tipo: 'inicio',
      chave,
      corpo: { receitaId: this.receitaId(), receitaVersaoId: previa.receitaVersaoId, quantidade },
    }));
  }

  protected cancelar(): void {
    const ordem = this.detalhe()?.resumo;
    if (
      this.ocupado() ||
      this.pendente() ||
      !this.confirmarCancelamento() ||
      ordem?.situacao !== 'EmAndamento'
    )
      return;
    this.preparar((chave) => ({ tipo: 'cancelamento', chave, ordemId: ordem.id }));
  }

  protected consultar(id: string): void {
    this.executar(
      this.api.obter(id),
      (ordem) => this.exibir(ordem),
      'Não foi possível abrir a ordem.',
    );
  }

  protected voltar(): void {
    this.nova.set(false);
    this.detalhe.set(null);
    this.confirmarCancelamento.set(false);
    this.erro.set('');
  }

  protected conferirPendente(): void {
    const pendente = this.pendente();
    if (!pendente || this.ocupado()) return;
    this.podeReenviar.set(false);
    const consulta =
      pendente.tipo === 'inicio'
        ? this.api.tentativa(pendente.chave).pipe(
            switchMap(({ ordemId }) => this.api.obter(ordemId)),
            catchError((e: unknown) => {
              if (e instanceof HttpErrorResponse && e.status === 404) return of(null);
              throw e;
            }),
          )
        : this.api.obter(pendente.ordemId);
    this.executar(
      consulta,
      (ordem) => {
        if (ordem) this.exibir(ordem);
        if (
          (pendente.tipo === 'inicio' && ordem) ||
          (pendente.tipo === 'cancelamento' && ordem?.resumo.situacao !== 'EmAndamento')
        ) {
          this.limpar();
          this.sucesso.set(
            ordem?.resumo.situacao === 'Cancelada'
              ? 'A ordem está cancelada.'
              : 'A ordem já foi localizada no servidor.',
          );
        } else {
          this.podeReenviar.set(true);
        }
      },
      'Não foi possível conferir a situação. A tentativa foi preservada.',
    );
  }

  protected reenviar(): void {
    const pendente = this.pendente();
    if (pendente && this.podeReenviar() && !this.ocupado()) this.enviar(pendente);
  }

  protected situacao(situacao: ResumoOrdem['situacao']): string {
    return situacao === 'EmAndamento'
      ? 'Em andamento'
      : situacao === 'Concluida'
        ? 'Concluída'
        : 'Cancelada';
  }

  protected medida(quantidade: string, unidade: string): string {
    const sigla = unidade === 'Grama' ? 'g' : unidade === 'Mililitro' ? 'ml' : 'un';
    return quantidade.replace(/(\.\d*?[1-9])0+$|\.0+$/, '$1').replace('.', ',') + ' ' + sigla;
  }

  protected motivoFalta(necessario: string, utilizavel: string, falta: string): string {
    const n = decimalEscalado(necessario, 6, 12);
    const u = decimalEscalado(utilizavel, 6, 22);
    if (decimalEscalado(falta, 6, 12) === 0n || n === null || u === null) return '';
    return u >= n ? 'Saldo comprometido por outras ordens.' : 'Estoque utilizável insuficiente.';
  }

  private preparar(construir: (chave: string) => TentativaProducao): void {
    const chave = this.storage.criarChaveIdempotencia();
    if (!chave || !this.ator) {
      this.erro.set('Não foi possível preparar a tentativa. Entre novamente no painel.');
      return;
    }
    const tentativa = construir(chave);
    if (!this.storage.salvar(this.chaveStorage(this.ator), tentativa)) {
      this.erro.set(
        'O navegador não permitiu guardar a tentativa. Habilite o armazenamento da sessão.',
      );
      return;
    }
    this.pendente.set(tentativa);
    this.enviar(tentativa);
  }

  private enviar(tentativa: TentativaProducao): void {
    this.podeReenviar.set(false);
    const post =
      tentativa.tipo === 'inicio'
        ? this.api.iniciar(tentativa.corpo, tentativa.chave)
        : this.api.cancelar(tentativa.ordemId, tentativa.chave);
    let confirmado = false;
    this.executar(
      post.pipe(
        switchMap(({ ordemId }) => {
          confirmado = true;
          return this.api.obter(ordemId);
        }),
      ),
      (ordem) => {
        this.limpar();
        this.exibir(ordem);
        this.sucesso.set(
          ordem.resumo.situacao === 'Cancelada'
            ? 'Produção cancelada. Reservas liberadas.'
            : ordem.resumo.situacao === 'EmAndamento'
              ? 'Produção iniciada. Materiais reservados.'
              : 'A ordem foi localizada no servidor.',
        );
        this.carregar();
      },
      'Não foi possível confirmar o resultado. Confira a situação antes de reenviar.',
      (erro) => {
        if (
          !confirmado &&
          erro instanceof HttpErrorResponse &&
          (erro.status === 404 ||
            (erro.status === 400 && erro.error?.errors?.producao) ||
            (erro.status === 409 && erro.error?.rejeicaoDefinitiva === true))
        ) {
          this.limpar();
          this.previa.set(null);
        }
      },
    );
  }

  private exibir(ordem: DetalheOrdem): void {
    this.nova.set(false);
    this.detalhe.set(ordem);
    this.confirmarCancelamento.set(false);
    this.lista.update((lista) => [ordem.resumo, ...lista.filter((o) => o.id !== ordem.resumo.id)]);
  }

  private limpar(): void {
    if (this.ator) this.storage.remover(this.chaveStorage(this.ator));
    this.pendente.set(null);
    this.podeReenviar.set(false);
  }

  private chaveStorage(ator: string): string {
    return `xaquiamuni:producao:tentativa:v1:${ator}`;
  }

  private executar<T>(
    request: Observable<T>,
    next: (valor: T) => void,
    msg: string,
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
        next,
        error: (erro: unknown) => {
          falha?.(erro);
          this.erro.set(this.mensagem(erro, msg));
        },
      });
  }

  private mensagem(erro: unknown, padrao: string): string {
    if (!(erro instanceof HttpErrorResponse)) return padrao;
    if (erro.status === 401) {
      void this.router.navigateByUrl('/admin/login');
      return 'Sua sessão expirou.';
    }
    if (erro.status === 403) return 'Acesso negado ou sessão inválida.';
    if (erro.status === 404) return 'Ordem ou receita não encontrada.';
    const detalhe = erro.status === 409 ? erro.error?.detail : erro.error?.errors?.producao?.[0];
    return typeof detalhe === 'string' ? detalhe : padrao;
  }
}
