import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ProdutosAcabadosService } from '../../core/admin-estoque/produtos-acabados.service';
import { ConcluirProducao, DetalheOrdem } from '../../core/ordens-producao.service';
import { decimalEscalado, decimalTexto } from '../compras/compra-decimais';
import { quantidadePlanejadaValida } from './tentativa-producao';

@Component({
  selector: 'app-concluir-producao',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './concluir-producao.component.html',
})
export class ConcluirProducaoComponent {
  private readonly estoque = inject(ProdutosAcabadosService);
  private readonly destroy = inject(DestroyRef);
  readonly ordem = input.required<DetalheOrdem>();
  readonly bloqueado = input(false);
  readonly confirmar = output<ConcluirProducao>();
  readonly voltar = output<void>();
  protected readonly politica = signal<{ prazo: number | null } | null>(null);
  protected readonly erroPolitica = signal(false);
  protected readonly produzida = signal<number | null>(null);
  protected readonly fabricacao = signal('');
  protected readonly validade = signal('');
  protected readonly conferido = signal(false);
  protected readonly erroCampos = signal(false);
  private readonly titulo = viewChild.required<ElementRef<HTMLElement>>('titulo');
  protected readonly valores = signal<Record<string, string>>({});
  protected readonly linhas = computed(() =>
    this.ordem().materiais.map((material) => {
      const casas = material.unidade === 'Unidade' ? 0 : 6;
      const reserva = decimalEscalado(material.reservado, 6, 12);
      const valor =
        this.valores()[material.insumoId] ??
        (reserva === null ? '' : decimalTexto(reserva, 6).replace('.', ','));
      const consumo = decimalEscalado(valor, casas, 12);
      const quantidade = consumo === null ? null : decimalEscalado(valor, 6, 12);
      const valido =
        reserva !== null && quantidade !== null && quantidade > 0n && quantidade <= reserva;
      const unidade =
        material.unidade === 'Grama' ? 'g' : material.unidade === 'Mililitro' ? 'ml' : 'un';
      return {
        ...material,
        valor,
        unidadeRotulo: unidade,
        valido,
        quantidade: valido ? decimalTexto(quantidade, 6) : '',
        sobra: valido ? decimalTexto(reserva - quantidade, 6).replace('.', ',') : '',
      };
    }),
  );

  constructor() {
    afterNextRender(() => this.titulo().nativeElement.focus());
  }

  ngOnInit(): void {
    const agora = new Date();
    this.fabricacao.set(
      [agora.getFullYear(), agora.getMonth() + 1, agora.getDate()]
        .map((parte) => String(parte).padStart(2, '0'))
        .join('-'),
    );
    this.carregarPolitica();
  }

  protected carregarPolitica(): void {
    this.erroPolitica.set(false);
    this.estoque
      .consultarPolitica(this.ordem().varianteId)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (politica) => this.politica.set({ prazo: politica.prazoValidadePadraoDias }),
        error: () => this.erroPolitica.set(true),
      });
  }

  protected alterar(id: string, valor: string): void {
    this.valores.update((valores) => ({ ...valores, [id]: valor }));
    this.conferido.set(false);
  }

  protected emitir(): void {
    const quantidade = this.produzida();
    const politica = this.politica();
    const linhas = this.linhas();
    if (this.bloqueado() || !this.conferido()) return;
    if (
      !quantidadePlanejadaValida(quantidade) ||
      !politica ||
      (politica.prazo === null && !this.validade()) ||
      !linhas.length ||
      linhas.some((linha) => !linha.valido)
    ) {
      this.erroCampos.set(true);
      return;
    }
    this.erroCampos.set(false);
    this.confirmar.emit({
      versao: this.ordem().resumo.versaoOrdem,
      quantidadeProduzida: quantidade,
      consumos: linhas.map(({ insumoId, unidade, quantidade }) => ({
        insumoId,
        unidadeBase: unidade,
        quantidade,
      })),
      dataFabricacao: this.fabricacao() || null,
      dataValidade: this.validade() || null,
    });
  }
}
