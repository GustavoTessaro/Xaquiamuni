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
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { OperacaoPendenteStore } from '../../core/admin-estoque/operacao-pendente.store';
import {
  AgendaService,
  ComandoAgenda,
  Compromisso,
  TentativaAgenda,
  rotaAgenda,
} from '../../core/agenda.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { dataCivilValida } from '../compras/recebimento-pendente';
import { hojeCivil, mesAgenda, rotuloDia } from './calendario-agenda';

const uuid = /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i;
const horaCurta = /^([01]\d|2[0-3]):[0-5]\d$/;
const horaApi = /^([01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?$/;
const limites = { titulo: 120, descricao: 2000, categoria: 80 };
const pontual = (hora: string) => (hora.length === 8 ? hora + '.0000000' : hora.padEnd(16, '0'));

function lerTentativa(texto: string): TentativaAgenda | null {
  const t = JSON.parse(texto) as TentativaAgenda;
  if (!t || typeof t.chave !== 'string' || !uuid.test(t.chave)) return null;
  if (t.operacao === 'excluir') {
    return t.metodo === 'DELETE' &&
      uuid.test(t.id) &&
      uuid.test(t.versao) &&
      t.corpo === null &&
      t.url === rotaAgenda + '/' + t.id + '?versao=' + t.versao
      ? t
      : null;
  }
  if (t.operacao !== 'criar' && t.operacao !== 'editar') return null;
  const c = t.corpo;
  if (
    !c ||
    typeof c !== 'object' ||
    Array.isArray(c) ||
    Object.keys(c).length !== 7 ||
    typeof c.titulo !== 'string' ||
    !c.titulo.trim() ||
    c.titulo.length > limites.titulo ||
    (c.descricao !== null &&
      (typeof c.descricao !== 'string' || c.descricao.length > limites.descricao)) ||
    (c.categoria !== null &&
      (typeof c.categoria !== 'string' || c.categoria.length > limites.categoria)) ||
    typeof c.data !== 'string' ||
    !dataCivilValida(c.data) ||
    (c.horarioInicial !== null &&
      (typeof c.horarioInicial !== 'string' || !horaApi.test(c.horarioInicial))) ||
    (c.horarioFinal !== null &&
      (typeof c.horarioFinal !== 'string' ||
        !horaApi.test(c.horarioFinal) ||
        !c.horarioInicial ||
        pontual(c.horarioFinal) <= pontual(c.horarioInicial)))
  )
    return null;
  if (t.operacao === 'criar')
    return t.metodo === 'POST' && t.url === rotaAgenda && t.corpo.versao === null ? t : null;
  return t.metodo === 'PUT' &&
    uuid.test(t.id) &&
    uuid.test(t.corpo.versao) &&
    t.url === rotaAgenda + '/' + t.id
    ? t
    : null;
}

@Component({
  imports: [AdminWorkspaceComponent, FormsModule],
  templateUrl: './admin-agenda.component.html',
  styleUrl: './admin-agenda.component.css',
})
export class AdminAgendaComponent {
  private readonly api = inject(AgendaService);
  private readonly auth = inject(AdminAuthService);
  private readonly store = inject(OperacaoPendenteStore);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly injector = inject(Injector);
  private readonly tituloEditor = viewChild<ElementRef<HTMLElement>>('tituloEditor');
  private chaveStorage = '';

  protected readonly limites = limites;
  protected agora = hojeCivil();
  protected readonly semana = [
    'Segunda',
    'Terça',
    'Quarta',
    'Quinta',
    'Sexta',
    'Sábado',
    'Domingo',
  ];
  protected dia = this.agora;
  protected mes = mesAgenda(this.dia)!;
  protected porDia = new Map<string, Compromisso[]>();
  protected carregado = false;
  protected ocupado = false;
  protected erro = '';
  protected sucesso = '';
  protected pendente: TentativaAgenda | null = null;
  protected tentativaAusente = false;
  protected semPayload = false;
  protected confirmarDescarte = false;

  protected modo: 'criar' | 'editar' | 'ver' | 'excluir' | null = null;
  protected atual: Compromisso | null = null;
  protected titulo = '';
  protected descricao = '';
  protected categoria = '';
  protected data = this.dia;
  protected inicio = '';
  protected fim = '';
  protected conflito = false;

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
          this.chaveStorage = 'xaquiamuni:agenda:v1:' + sessao.userId;
          try {
            const guardada = this.store.ler(this.chaveStorage);
            if (guardada) {
              this.pendente = lerTentativa(guardada);
              this.semPayload = !this.pendente;
            }
          } catch {
            this.semPayload = true;
          }
          if (this.pendente) this.conferir();
          else this.carregar();
          this.cdr.markForCheck();
        },
        error: (erro) => {
          this.erro = this.mensagem(erro);
          this.cdr.markForCheck();
        },
      });
  }

  protected carregar(preservar = false): void {
    if (this.ocupado || !this.chaveStorage) return;
    this.carregado = false;
    this.executar(
      this.api.listar(this.mes.inicio, this.mes.fim),
      (itens) => {
        const agrupados = new Map<string, Compromisso[]>();
        for (const item of itens) {
          const dia = agrupados.get(item.data) ?? [];
          dia.push(item);
          agrupados.set(item.data, dia);
        }
        this.porDia = agrupados;
        this.carregado = true;
      },
      undefined,
      preservar,
    );
  }

  protected navegar(delta: number): void {
    const destino = mesAgenda(this.mes.inicio, delta);
    if (!destino || this.ocupado || this.pendente) return;
    this.agora = hojeCivil();
    this.irPara(delta === 0 ? this.agora : destino.inicio);
    this.carregar();
  }

  protected selecionar(data: string): void {
    this.dia = data;
    this.modo = null;
  }

  protected novo(): void {
    this.atual = null;
    this.titulo = this.descricao = this.categoria = this.inicio = this.fim = '';
    this.data = this.dia;
    this.conflito = false;
    this.modo = 'criar';
    this.focarEditor();
  }

  protected abrir(id: string, modo: 'editar' | 'ver' | 'excluir'): void {
    this.executar(this.api.obter(id), (item) => {
      this.atual = item;
      this.modo = modo;
      this.conflito = false;
      this.titulo = item.titulo;
      this.descricao = item.descricao ?? '';
      this.categoria = item.categoria ?? '';
      this.data = item.data;
      this.inicio = item.horarioInicial?.slice(0, 5) ?? '';
      this.fim = item.horarioFinal?.slice(0, 5) ?? '';
      this.focarEditor();
    });
  }

  protected salvar(): void {
    if (this.modo !== 'criar' && this.modo !== 'editar') return;
    if (
      !this.titulo.trim() ||
      this.titulo.trim().length > limites.titulo ||
      this.descricao.trim().length > limites.descricao ||
      this.categoria.trim().length > limites.categoria ||
      !dataCivilValida(this.data)
    ) {
      this.erro = 'Confira o título, os limites dos campos e a data.';
      return;
    }
    const inicio = this.horaParaApi(this.inicio, this.atual?.horarioInicial);
    const fim = this.horaParaApi(this.fim, this.atual?.horarioFinal);
    if (
      (this.inicio && !inicio) ||
      (this.fim && !fim) ||
      (fim && (!inicio || pontual(fim) <= pontual(inicio)))
    ) {
      this.erro = 'Informe um horário inicial e um horário final posterior no mesmo dia.';
      return;
    }
    const corpo = {
      titulo: this.titulo.trim(),
      descricao: this.descricao.trim() || null,
      categoria: this.categoria.trim() || null,
      data: this.data,
      horarioInicial: inicio,
      horarioFinal: fim,
    };
    if (this.modo === 'criar')
      this.preparar({
        operacao: 'criar',
        metodo: 'POST',
        url: rotaAgenda,
        corpo: { ...corpo, versao: null },
      });
    else if (this.atual) {
      const { id, versao } = this.atual;
      this.preparar({
        operacao: 'editar',
        metodo: 'PUT',
        url: rotaAgenda + '/' + id,
        id,
        corpo: { ...corpo, versao },
      });
    }
  }

  protected excluir(): void {
    if (this.modo !== 'excluir' || !this.atual) return;
    const { id, versao } = this.atual;
    this.preparar({
      operacao: 'excluir',
      metodo: 'DELETE',
      url: rotaAgenda + '/' + id + '?versao=' + versao,
      id,
      versao,
      corpo: null,
    });
  }

  protected conferir(): void {
    const tentativa = this.pendente;
    if (!tentativa || this.ocupado) return;
    this.tentativaAusente = false;
    this.executar(
      this.api.tentativa(tentativa.chave),
      (resposta) => {
        if (
          tentativa.operacao !== resposta.operacao ||
          (tentativa.operacao !== 'criar' && tentativa.id !== resposta.recursoId)
        ) {
          this.erro = 'A chave corresponde a outro compromisso ou operação.';
          return;
        }
        this.concluir(resposta.resultado, tentativa.operacao);
      },
      (erro) => {
        if (erro instanceof HttpErrorResponse && erro.status === 404) {
          this.erro = '';
          this.tentativaAusente = true;
          this.carregar(true);
        }
      },
    );
  }

  protected reenviar(): void {
    if (!this.pendente || !this.tentativaAusente || this.ocupado) return;
    try {
      const texto = this.store.ler(this.chaveStorage);
      if (texto && JSON.stringify(lerTentativa(texto)) === JSON.stringify(this.pendente)) {
        this.enviar(this.pendente, true);
        return;
      }
    } catch {
      /* A tentativa não pode mais ser restaurada. */
    }
    this.semPayload = true;
    this.pendente = null;
    this.tentativaAusente = false;
  }

  protected descartar(): void {
    if (this.confirmarDescarte && this.limpar()) this.carregar();
  }

  private horaParaApi(hora: string, original?: string | null): string | null {
    if (!horaCurta.test(hora)) return null;
    return original?.slice(0, 5) === hora ? original : hora + ':00';
  }

  private focarEditor(): void {
    afterNextRender(() => this.tituloEditor()?.nativeElement.focus(), { injector: this.injector });
  }

  private preparar(comando: ComandoAgenda): void {
    if (this.ocupado || this.conflito || this.pendente || this.semPayload || !this.chaveStorage)
      return;
    const chave = this.store.criarChaveIdempotencia();
    if (!chave || !this.store.salvar(this.chaveStorage, { ...comando, chave })) {
      this.erro = 'Não foi possível guardar a tentativa. A operação não foi enviada.';
      return;
    }
    this.pendente = { ...comando, chave };
    this.enviar(this.pendente, false);
  }

  private enviar(tentativa: TentativaAgenda, reenvio: boolean): void {
    this.tentativaAusente = false;
    this.executar(
      this.api.enviar(tentativa),
      (item) => this.concluir(item, tentativa.operacao),
      (erro) => {
        if (
          !reenvio &&
          erro instanceof HttpErrorResponse &&
          [400, 404, 409].includes(erro.status)
        ) {
          this.limpar();
          if (tentativa.operacao !== 'criar' && (erro.status === 409 || erro.status === 404))
            this.conflito = true;
          if (erro.status === 404) this.carregar(true);
        }
      },
    );
  }

  private concluir(item: Compromisso, operacao: ComandoAgenda['operacao']): void {
    if (!this.limpar()) return;
    this.modo = null;
    this.atual = null;
    this.conflito = false;
    this.irPara(item.data);
    this.sucesso = operacao === 'excluir' ? 'Compromisso excluído.' : 'Compromisso salvo.';
    this.carregar(true);
  }

  private limpar(): boolean {
    if (!this.store.remover(this.chaveStorage)) {
      this.erro = 'Não foi possível limpar a tentativa nesta aba.';
      return false;
    }
    this.pendente = null;
    this.semPayload = this.tentativaAusente = this.confirmarDescarte = false;
    return true;
  }

  private irPara(data: string): void {
    this.dia = data;
    this.mes = mesAgenda(data)!;
    this.modo = null;
  }

  private executar<T>(
    fonte: Observable<T>,
    receber: (valor: T) => void,
    falhar?: (erro: unknown) => void,
    preservar = false,
  ): void {
    if (this.ocupado) return;
    this.ocupado = true;
    if (!preservar) this.erro = this.sucesso = '';
    fonte.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (dados) => {
        this.ocupado = false;
        receber(dados);
        this.cdr.markForCheck();
      },
      error: (erro) => {
        this.ocupado = false;
        this.erro = this.mensagem(erro);
        falhar?.(erro);
        this.cdr.markForCheck();
      },
    });
  }

  private mensagem(erro: unknown): string {
    if (erro instanceof HttpErrorResponse) {
      if (erro.status === 401) {
        void this.router.navigateByUrl('/admin/login');
        return 'Sua sessão expirou.';
      }
      if (erro.status === 403) return 'Acesso negado. Confira sua sessão administrativa.';
      if (erro.status === 400)
        return erro.error?.errors?.agenda?.[0] ?? 'Confira os dados do compromisso.';
      if (erro.status === 404) return 'Compromisso não encontrado.';
      if (erro.status === 409)
        return 'A operação entrou em conflito. Consulte os dados atuais antes de tentar novamente.';
      if (erro.status === 503 || erro.status === 0)
        return 'Serviço indisponível ou conexão interrompida.';
    }
    return 'Não foi possível confirmar a operação. Consulte a agenda antes de tentar novamente.';
  }

  protected rotuloDia = rotuloDia;
  protected horario(item: Compromisso): string {
    return !item.horarioInicial
      ? 'Dia inteiro'
      : item.horarioInicial.slice(0, 5) +
          (item.horarioFinal ? ' a ' + item.horarioFinal.slice(0, 5) : '');
  }
}
