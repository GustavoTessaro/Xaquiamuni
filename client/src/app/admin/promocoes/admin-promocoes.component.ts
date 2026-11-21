import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, firstValueFrom, map, of, startWith, switchMap, timer } from 'rxjs';
import { ProdutosService } from '../../core/admin-produtos/produtos.service';
import { formatarCentavos } from '../../public-store/carrinho.service';
import { formatarOff } from '../../public-store/preco-exibido.component';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { ConfiguracaoPromocao, ConsultaPrevia, OperacaoPromocao, PreviaPromocao, Promocao, PromocoesService } from './promocoes.service';

type Resultado<T> = { estado: 'carregando' | 'inicial' } | { estado: 'erro'; mensagem: string } | { estado: 'pronto'; dados: T };
type Formulario = Omit<ConfiguracaoPromocao, 'inicioUtc' | 'fimUtc'> & { inicio: string; fim: string };
const vazio: Formulario = { produtoId: '', tipo: 'Percentual', escopo: 'TodasVariantes', valor: '', inicio: '', fim: '', variantesIds: [] };

@Component({
  selector: 'app-admin-promocoes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, DatePipe, FormsModule],
  templateUrl: './admin-promocoes.component.html',
  styleUrl: './admin-promocoes.component.css',
})
export class AdminPromocoesComponent {
  protected readonly api = inject(PromocoesService);
  private readonly produtosApi = inject(ProdutosService);
  private readonly injector = inject(Injector);
  private readonly tituloFormulario = viewChild<ElementRef<HTMLElement>>('tituloFormulario');
  protected readonly filtro = signal('');
  protected readonly pagina = signal(1);
  private readonly revisaoLista = signal(0);
  private readonly pedidoLista = computed(() => ({ produto: this.filtro(), pagina: this.pagina(), revisao: this.revisaoLista() }));
  protected readonly lista = toSignal(toObservable(this.pedidoLista).pipe(
    switchMap(({ produto, pagina }) => this.api.listar(produto, pagina).pipe(
      map(dados => ({ estado: 'pronto', dados }) as const),
      startWith({ estado: 'carregando' } as const),
      catchError(e => of({ estado: 'erro', mensagem: mensagem(e, 'Falha ao listar promoções.') } as const)),
    )),
  ), { initialValue: { estado: 'carregando' } as const });
  protected readonly produtos = toSignal(this.produtosApi.listar().pipe(
    map(dados => ({ estado: 'pronto', dados }) as const),
    catchError(e => of({ estado: 'erro', mensagem: mensagem(e, 'Falha ao carregar produtos.') } as const)),
  ));
  protected readonly formulario = signal<Formulario | null>(null);
  protected readonly atual = signal<Promocao | null>(null);
  protected readonly carregandoDetalhe = signal(false);
  protected readonly erro = signal('');
  protected readonly aviso = signal('');
  protected readonly desatualizada = signal(false);
  protected readonly desativando = signal(false);
  protected readonly moeda = formatarCentavos;
  protected readonly off = formatarOff;
  protected readonly varianteOpcoes = computed(() =>
    this.produtos()?.estado === 'pronto' ? this.produtosProntos().find(p => p.id === this.formulario()?.produtoId)?.variantes ?? [] : []);
  protected readonly ocupado = computed(() => this.carregandoDetalhe() || this.api.gravando() || !!this.api.pendente());
  private readonly revisaoPrevia = signal(0);

  protected readonly validacao = computed<{ configuracao?: ConfiguracaoPromocao; erro?: string }>(() => {
    const f = this.formulario();
    if (!f || !f.produtoId) return {};
    const valor = emCentesimos(f.valor, f.tipo === 'PrecoFixo');
    if (!valor || (f.tipo === 'Percentual' && BigInt(valor) >= 10_000n))
      return { erro: f.tipo === 'Percentual' ? 'Informe um desconto maior que 0 e menor que 100%, com até duas casas decimais.' :
        'Informe um preço positivo em reais, com até duas casas decimais.' };
    if (f.escopo === 'VariantesSelecionadas' && !f.variantesIds.length)
      return { erro: 'Selecione pelo menos uma variante.' };
    const atual = this.atual();
    const inicioUtc = converterData(f.inicio, atual?.inicioUtc);
    const fimUtc = converterData(f.fim, atual?.fimUtc);
    if (inicioUtc === undefined || fimUtc === undefined)
      return { erro: 'Informe uma data e um horário válidos.' };
    if (inicioUtc && fimUtc && Date.parse(inicioUtc) >= Date.parse(fimUtc))
      return { erro: 'O término deve ser posterior ao início.' };
    return { configuracao: { produtoId: f.produtoId, tipo: f.tipo, escopo: f.escopo, valor, inicioUtc, fimUtc,
      variantesIds: f.escopo === 'VariantesSelecionadas' ? [...f.variantesIds].sort() : [] } };
  });

  protected readonly consulta = computed<ConsultaPrevia | null>(() => {
    this.revisaoPrevia();
    const dados = this.validacao().configuracao;
    if (!dados) return null;
    const instanteUtc = dados.inicioUtc && Date.parse(dados.inicioUtc) > Date.now() ? dados.inicioUtc : undefined;
    return { configuracao: dados, substituiId: this.atual()?.id, instanteUtc };
  });

  private readonly respostaPrevia = toSignal(toObservable(this.consulta).pipe(
    switchMap(consulta => {
      if (!consulta) return of({ consulta, resultado: { estado: 'inicial' } as Resultado<PreviaPromocao> });
      return timer(300).pipe(
        switchMap(() => this.api.prever(consulta)),
        map(dados => ({ consulta, resultado: { estado: 'pronto', dados } as Resultado<PreviaPromocao> })),
        catchError(e => of({ consulta, resultado: { estado: 'erro', mensagem: mensagem(e, 'Falha ao calcular a prévia.') } as Resultado<PreviaPromocao> })),
      );
    }),
  ));
  protected readonly previa = computed<Resultado<PreviaPromocao>>(() => {
    const consulta = this.consulta();
    if (!consulta) return { estado: 'inicial' };
    const resposta = this.respostaPrevia();
    return resposta?.consulta === consulta ? resposta.resultado : { estado: 'carregando' };
  });
  protected readonly abrangidas = computed(() => {
    const previa = this.previa();
    return previa.estado === 'pronto' ? previa.dados.variantes.filter(v => v.abrangida) : [];
  });
  protected readonly revisaoValida = computed(() =>
    this.abrangidas().some(v => v.ativa) && this.abrangidas().every(v => !v.ativa || !v.precoIncompativel));
  protected readonly semConflitos = computed(() => {
    const previa = this.previa();
    return previa.estado === 'pronto' && previa.dados.conflitos.length === 0;
  });
  protected readonly alterada = computed(() => {
    const atual = this.atual();
    const configuracao = this.validacao().configuracao;
    return !!atual && (!configuracao || JSON.stringify(configuracao) !== JSON.stringify(configuracaoDe(atual)));
  });

  protected produtosProntos() {
    const lista = this.produtos();
    return lista?.estado === 'pronto' ? lista.dados : [];
  }

  protected nome(id: string): string {
    return this.produtosProntos().find(p => p.id === id)?.nome ?? 'Produto';
  }

  protected status(p: Promocao): string {
    if (!p.ativa) return 'Inativa';
    const agora = Date.now();
    if (p.fimUtc && Date.parse(p.fimUtc) <= agora) return 'Encerrada';
    if (p.inicioUtc && Date.parse(p.inicioUtc) > agora) return 'Agendada';
    return 'Ativa';
  }

  protected filtrar(id: string): void {
    this.pagina.set(1);
    this.filtro.set(id);
  }

  protected atualizarLista(): void {
    this.revisaoLista.update(v => v + 1);
  }

  protected atualizarPrevia(): void {
    this.revisaoPrevia.update(v => v + 1);
  }

  protected nova(produtoId = this.filtro()): void {
    this.atual.set(null);
    this.formulario.set({ ...vazio, produtoId });
    this.limparAvisos();
    this.focarFormulario();
  }

  protected async abrir(id: string): Promise<void> {
    if (this.ocupado()) return;
    this.carregandoDetalhe.set(true);
    this.erro.set('');
    try {
      this.exibir(await firstValueFrom(this.api.obter(id)));
      this.focarFormulario();
    } catch (e) {
      this.erro.set(mensagem(e, 'Falha ao abrir a promoção.'));
    } finally {
      this.carregandoDetalhe.set(false);
    }
  }

  protected alterar(campo: Partial<Formulario>): void {
    this.formulario.update(f => f ? { ...f, ...campo } : f);
    this.desativando.set(false);
  }

  protected selecionarVariante(id: string, selecionada: boolean): void {
    const ids = this.formulario()?.variantesIds ?? [];
    this.alterar({ variantesIds: selecionada ? [...ids, id] : ids.filter(v => v !== id) });
  }

  protected salvar(): void {
    const configuracao = this.validacao().configuracao;
    const atual = this.atual();
    if (!configuracao || !this.revisaoValida() || this.desatualizada() || (atual?.ativa && !this.semConflitos())) return;
    void this.executar(atual ? { tipo: 'editar', id: atual.id, versao: atual.versao, configuracao } :
      { tipo: 'criar', configuracao });
  }

  protected alterarStatus(ativa: boolean): void {
    const atual = this.atual();
    if (!atual || this.desatualizada() || ativa === atual.ativa ||
        (ativa && (this.alterada() || !this.revisaoValida() || !this.semConflitos()))) return;
    void this.executar({ tipo: 'status', id: atual.id, versao: atual.versao, ativa });
  }

  protected async executar(operacao?: OperacaoPromocao): Promise<void> {
    this.erro.set('');
    try {
      this.exibir(await this.api.gravar(operacao));
      this.atualizarLista();
      this.aviso.set('Promoção atualizada.');
    } catch (e) {
      this.desatualizada.set(e instanceof HttpErrorResponse && e.status === 409 && e.error?.codigo === 'versao_divergente');
      this.erro.set(mensagem(e, this.api.pendente() ?
        'O resultado ainda não foi confirmado. Repita a mesma tentativa.' : 'Falha ao salvar a promoção.'));
      if (e instanceof HttpErrorResponse && e.status === 409) this.atualizarPrevia();
    }
  }

  protected fechar(): void {
    this.formulario.set(null);
    this.atual.set(null);
    this.limparAvisos();
  }

  private exibir(p: Promocao): void {
    const { inicioUtc, fimUtc, ...dados } = configuracaoDe(p);
    this.atual.set(p);
    this.formulario.set({ ...dados, valor: textoDecimal(p.valor), inicio: paraDataLocal(inicioUtc), fim: paraDataLocal(fimUtc) });
    this.limparAvisos();
  }

  private limparAvisos(): void {
    this.erro.set('');
    this.aviso.set('');
    this.desatualizada.set(false);
    this.desativando.set(false);
  }

  private focarFormulario(): void {
    afterNextRender(() => this.tituloFormulario()?.nativeElement.focus(), { injector: this.injector });
  }
}

function configuracaoDe(p: Promocao): ConfiguracaoPromocao {
  return { produtoId: p.produtoId, tipo: p.tipo, escopo: p.escopo, valor: p.valor,
    inicioUtc: p.inicioUtc, fimUtc: p.fimUtc, variantesIds: [...p.variantesIds].sort() };
}

function emCentesimos(entrada: string, moeda: boolean): string | null {
  const valor = moeda ? entrada.trim().replace(/^R\$\s*/u, '') : entrada.trim();
  const normalizado = moeda && /^\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?$/.test(valor)
    ? valor.replaceAll('.', '') : valor;
  const partes = /^(\d{1,19})(?:[,.](\d{1,2}))?$/.exec(normalizado);
  if (!partes) return null;
  const inteiro = BigInt(partes[1] + (partes[2] ?? '').padEnd(2, '0'));
  return inteiro > 0n && inteiro <= 9_223_372_036_854_775_807n ? inteiro.toString() : null;
}

function textoDecimal(valor: string): string {
  return `${BigInt(valor) / 100n},${String(BigInt(valor) % 100n).padStart(2, '0')}`;
}

function paraDataLocal(utc: string | null | undefined): string {
  if (!utc) return '';
  const data = new Date(utc);
  const dois = (n: number) => String(n).padStart(2, '0');
  return `${data.getFullYear()}-${dois(data.getMonth() + 1)}-${dois(data.getDate())}T${dois(data.getHours())}:${dois(data.getMinutes())}`;
}

function converterData(local: string, original: string | null | undefined): string | null | undefined {
  if (!local) return null;
  if (original && local === paraDataLocal(original)) return original;
  const data = new Date(local);
  if (Number.isNaN(data.getTime())) return undefined;
  const utc = data.toISOString();
  return paraDataLocal(utc) === local ? utc : undefined;
}

function mensagem(e: unknown, alternativa: string): string {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 409 && e.error?.codigo === 'versao_divergente')
      return 'Outra alteração foi salva. Recarregue a promoção e confira os dados antes de editar novamente.';
    if (e.status === 401 || e.status === 403) return 'Entre novamente como administradora.';
    if (typeof e.error?.detail === 'string' && e.status < 500) return e.error.detail;
  }
  return alternativa;
}
