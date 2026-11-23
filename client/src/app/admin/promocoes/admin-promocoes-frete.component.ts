import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Observable, catchError, finalize, map, of, startWith, switchMap } from 'rxjs';
import { formatarCentavos } from '../../public-store/carrinho.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';
import { converterData, emCentesimos, paraDataLocal, textoDecimal } from './formato-promocao';
import { CampanhaFrete, ConfiguracaoFrete, ModalidadeFrete, PromocoesFreteService, TipoDescontoFrete } from './promocoes-frete.service';

interface Formulario {
  nome: string;
  tipo: TipoDescontoFrete;
  valor: string;
  modalidades: ModalidadeFrete[];
  subtotal: string;
  inicio: string;
  fim: string;
}

const novoFormulario = (): Formulario => ({
  nome: '', tipo: 'Percentual', valor: '', modalidades: [], subtotal: '0,00', inicio: '', fim: '',
});

@Component({
  selector: 'app-admin-promocoes-frete',
  imports: [AdminWorkspaceComponent, DatePipe, FormsModule],
  templateUrl: './admin-promocoes-frete.component.html',
  styleUrl: './admin-promocoes.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPromocoesFreteComponent {
  private readonly api = inject(PromocoesFreteService);
  private readonly destroy = inject(DestroyRef);
  private readonly revisaoLista = signal(0);
  protected readonly pagina = signal(1);
  private readonly consultaLista = computed(() => ({ pagina: this.pagina(), revisao: this.revisaoLista() }));
  protected readonly lista = toSignal(toObservable(this.consultaLista).pipe(
    switchMap(({ pagina }) => this.api.listar(pagina).pipe(
      map(dados => ({ estado: 'pronto', dados }) as const),
      startWith({ estado: 'carregando' } as const),
      catchError(erro => of({ estado: 'erro', mensagem: this.mensagem(erro, 'Não foi possível carregar as campanhas.') } as const)),
    )),
  ), { initialValue: { estado: 'carregando' } as const });

  protected readonly formulario = signal<Formulario | null>(null);
  protected readonly atual = signal<CampanhaFrete | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly conflito = signal(false);
  protected readonly erro = signal('');
  protected readonly aviso = signal('');
  protected readonly moeda = formatarCentavos;
  protected readonly tipos = { Percentual: 'Percentual', ValorFixo: 'Valor fixo', FreteGratis: 'Frete grátis' };
  protected readonly modalidades = { EntregaPropria: 'Entrega própria', CorreiosManual: 'Correios manual' };
  protected readonly escolhas: ModalidadeFrete[] = ['EntregaPropria', 'CorreiosManual'];

  protected recarregar(): void {
    this.revisaoLista.update(n => n + 1);
  }

  protected criar(): void {
    this.cancelar();
    this.aviso.set('');
    this.formulario.set(novoFormulario());
  }

  protected cancelar(): void {
    this.formulario.set(null);
    this.atual.set(null);
    this.erro.set('');
    this.conflito.set(false);
  }

  protected abrir(id: string): void {
    if (this.ocupado()) return;
    this.aviso.set('');
    this.erro.set('');
    this.ocupado.set(true);
    this.api.consultar(id).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.ocupado.set(false))).subscribe({
      next: campanha => {
        this.atual.set(campanha);
        this.conflito.set(false);
        this.formulario.set({
          nome: campanha.nome, tipo: campanha.tipo, valor: textoDecimal(campanha.valor),
          modalidades: campanha.modalidades, subtotal: textoDecimal(campanha.subtotalMinimoCentavos),
          inicio: paraDataLocal(campanha.inicioUtc), fim: paraDataLocal(campanha.fimUtc),
        });
      },
      error: e => this.erro.set(this.mensagem(e, 'Não foi possível consultar a campanha.')),
    });
  }

  protected atualizar<K extends keyof Formulario>(campo: K, valor: Formulario[K]): void {
    this.formulario.update(f => f && ({ ...f, [campo]: valor }));
  }

  protected marcarModalidade(f: Formulario, modalidade: ModalidadeFrete, marcada: boolean): void {
    this.atualizar('modalidades', marcada ? [...f.modalidades, modalidade] : f.modalidades.filter(m => m !== modalidade));
  }

  protected salvar(f: Formulario): void {
    if (this.ocupado() || this.conflito()) return;
    const atual = this.atual();
    const valor = f.tipo === 'FreteGratis' ? '0' : emCentesimos(f.valor, f.tipo === 'ValorFixo');
    const subtotal = emCentesimos(f.subtotal, true, true);
    const inicioUtc = converterData(f.inicio, atual?.inicioUtc);
    const fimUtc = converterData(f.fim, atual?.fimUtc);
    this.erro.set('');
    if (!f.nome.trim() || f.nome.trim().length > 120) return this.erro.set('Informe um nome de até 120 caracteres.');
    if (!valor || (f.tipo === 'Percentual' && BigInt(valor) > 10_000n))
      return this.erro.set('Informe um valor positivo com até duas casas decimais. O percentual máximo é 100%.');
    if (!subtotal) return this.erro.set('Informe um subtotal mínimo válido, a partir de zero.');
    if (!f.modalidades.length) return this.erro.set('Selecione pelo menos uma modalidade.');
    if (inicioUtc === undefined || fimUtc === undefined || (inicioUtc && fimUtc && Date.parse(inicioUtc) >= Date.parse(fimUtc)))
      return this.erro.set('Informe horários válidos e um término posterior ao início.');
    const dados: ConfiguracaoFrete = { nome: f.nome.trim(), tipo: f.tipo, valor, modalidades: f.modalidades,
      subtotalMinimoCentavos: subtotal, inicioUtc, fimUtc };
    this.gravar(this.api.salvar(dados, atual), 'Campanha salva.');
  }

  protected alterarStatus(campanha: CampanhaFrete): void {
    if (this.ocupado() || this.formulario() || this.conflito()) return;
    this.gravar(this.api.alterarStatus(campanha), campanha.ativa ? 'Campanha inativada.' : 'Campanha ativada.');
  }

  protected recarregarAposConflito(): void {
    const atual = this.atual();
    if (atual) this.abrir(atual.id);
    else this.cancelar();
    this.recarregar();
  }

  protected desconto(campanha: CampanhaFrete): string {
    if (campanha.tipo === 'FreteGratis') return 'Integral';
    return campanha.tipo === 'ValorFixo' ? this.moeda(campanha.valor) : textoDecimal(campanha.valor) + '%';
  }

  private gravar(envio: Observable<CampanhaFrete>, mensagem: string): void {
    this.ocupado.set(true);
    this.erro.set('');
    this.aviso.set('');
    envio.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.ocupado.set(false))).subscribe({
      next: () => {
        this.cancelar();
        this.aviso.set(mensagem);
        this.pagina.set(1);
        this.recarregar();
      },
      error: e => {
        this.conflito.set(e instanceof HttpErrorResponse && e.status === 409);
        this.erro.set(this.mensagem(e, 'Não foi possível confirmar a operação. Consulte a lista antes de repetir.'));
      },
    });
  }

  private mensagem(erro: unknown, alternativa: string): string {
    if (!(erro instanceof HttpErrorResponse)) return alternativa;
    if (erro.status === 401 || erro.status === 403) return 'Entre novamente como administradora.';
    if (erro.status === 409) return 'A campanha mudou. Recarregue os dados antes de editar.';
    if (erro.status === 400 && erro.error?.errors) return Object.values(erro.error.errors).flat().join(' ');
    return erro.status < 500 && erro.error?.detail ? erro.error.detail : alternativa;
  }
}
