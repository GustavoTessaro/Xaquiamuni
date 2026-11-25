import { ConsultaCepComponent } from '../shared/consulta-cep.component';
import { CamposCep, formatarCep, mensagensCep } from '../core/consulta-cep.service';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { CotacoesCorreiosService, erroPostal, type SolicitacaoPostal } from '../core/cotacoes-correios.service';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { finalize, firstValueFrom } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AtendimentoService, EnderecoEntrega, ModalidadeEntrega, ModalidadesPublicas, OfertaEntrega } from '../core/atendimento.service';
import { FretePedidoComponent } from '../shared/frete-pedido.component';
import { DadosEntregaComponent } from '../shared/dados-entrega.component';
import { CarrinhoService, formatarCentavos } from './carrinho.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { CheckoutService } from './checkout.service';
import { itensCompraValidos, mesmosItensCompra } from './pedidos.service';
import { revalidarCarrinho } from './revalidar-carrinho';
import { PrecoExibidoComponent } from './preco-exibido.component';

const enderecoVazio: EnderecoEntrega = {
  cep: '', uf: '', cidade: '', bairro: '', logradouro: '', numero: '', complemento: null,
};

@Component({
  selector: 'app-checkout',
  imports: [RouterLink, PrecoExibidoComponent, FormsModule, DadosEntregaComponent, FretePedidoComponent, DatePipe, ConsultaCepComponent],
  templateUrl: './checkout.component.html',
  styleUrl: './compra.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckoutComponent {
  protected readonly mensagensDeEndereco = Object.values(mensagensCep);
  private readonly correios = inject(CotacoesCorreiosService);
  protected readonly cotacaoCriada = signal<string | null>(null);
  protected readonly solicitacaoIncerta = signal(false);
  private tentativaPostal?: { corpo: SolicitacaoPostal; chave: string; registro: string; conta: string };

  protected async solicitarCorreios(): Promise<void> {
    if (this.cotando() || this.checkout.enviando() || !this.modalidades()?.correiosCotacaoManual) return;
    const endereco = this.enderecoPostal();
    if (!this.tentativaPostal) {
      if (!/^\d{5}-?\d{3}$/.test(endereco.cep.trim()) ||
          this.camposEndereco.some(({ campo }) => campo !== 'complemento' && !endereco[campo]?.trim())) {
        this.erro.set('Preencha o endereço completo e informe um CEP válido.');
        return;
      }
      if (!window.confirm('Solicitar uma cotação para os itens e o endereço informados?')) return;
    }
    const corpo = this.tentativaPostal?.corpo ?? {
      endereco: { ...endereco },
      itens: this.itensCompra().map(({ produtoId, produtoVarianteId, quantidade }) =>
        ({ produtoId, produtoVarianteId, quantidade })),
    };
    this.cotando.set(true);
    this.erro.set('');
    try {
      const preparada = await this.correios.prepararSolicitacao(corpo);
      if (!preparada) {
        await this.router.navigate(['/login'], { queryParams: { retorno: 'checkout' } });
        return;
      }
      if (this.tentativaPostal && this.tentativaPostal.conta !== preparada.conta) {
        this.correios.concluirSolicitacao(preparada.registro);
        this.erro.set('Entre com a conta original para recuperar esta solicitação.');
        return;
      }
      const { requestToken, ...registro } = preparada;
      const tentativa = this.tentativaPostal ?? { corpo, ...registro };
      this.tentativaPostal = tentativa;
      const cotacao = await firstValueFrom(this.correios.solicitar(tentativa.corpo, tentativa.chave, requestToken));
      this.correios.concluirSolicitacao(tentativa.registro);
      this.cotacaoCriada.set(cotacao.id);
      this.tentativaPostal = undefined;
      this.solicitacaoIncerta.set(false);
    } catch (falha) {
      const { status, error } = falha as { status?: number; error?: { codigo?: string } };
      const incerta = !!this.tentativaPostal && error?.codigo !== 'cep_indisponivel'
        && (!status || status >= 500 || status === 408 || status === 401);
      if (!incerta && this.tentativaPostal) {
        this.correios.concluirSolicitacao(this.tentativaPostal.registro);
        this.tentativaPostal = undefined;
      }
      this.solicitacaoIncerta.set(incerta);
      this.erro.set(incerta ? 'Resultado incerto. Recupere a solicitação para conferir se ela foi registrada.' : erroPostal(falha));
      if (status === 401) await this.router.navigate(['/login'], { queryParams: { retorno: 'checkout' } });
    } finally {
      this.cotando.set(false);
    }
  }
  protected readonly carrinho = inject(CarrinhoService);
  protected readonly checkout = inject(CheckoutService);
  protected readonly formatarPreco = formatarCentavos;
  protected readonly erro = signal('');
  protected readonly consultando = signal(true);
  protected readonly precoMudou = signal(false);
  private readonly destroy = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly atendimento = inject(AtendimentoService);
  protected readonly modalidades = signal<ModalidadesPublicas | null>(null);
  protected readonly modalidade = signal<ModalidadeEntrega>('RetiradaLocal');
  protected readonly usarPadrao = signal(true);
  protected readonly endereco = signal<EnderecoEntrega>({ ...enderecoVazio });
  private readonly enderecoPostal = signal<EnderecoEntrega>({ ...enderecoVazio });
  protected readonly enderecoPostalCadastrado = signal<EnderecoEntrega | null>(null);
  protected readonly avisoEnderecoPostal = signal('');
  protected readonly enderecoFormulario = computed(() =>
    this.modalidade() === 'Correios' ? this.enderecoPostal() : this.endereco());
  protected readonly camposEndereco = [
    { campo: 'cep', rotulo: 'CEP', limite: 9 }, { campo: 'uf', rotulo: 'UF', limite: 2 },
    { campo: 'cidade', rotulo: 'Cidade', limite: 100 }, { campo: 'bairro', rotulo: 'Bairro', limite: 100 },
    { campo: 'logradouro', rotulo: 'Logradouro', limite: 150 }, { campo: 'numero', rotulo: 'Número', limite: 20 },
    { campo: 'complemento', rotulo: 'Complemento (opcional)', limite: 100 },
  ] as const;
  protected readonly cotando = signal(false);
  private readonly cotacao = signal<{ chave: string; oferta: OfertaEntrega } | null>(null);
  private timer?: ReturnType<typeof setTimeout>;
  protected readonly oferta = computed(() => {
    const cotacao = this.cotacao();
    return cotacao?.chave === this.chaveCotacao() ? cotacao.oferta : null;
  });
  protected readonly subtotalOferta = computed(() => {
    const oferta = this.oferta();
    return oferta ? (BigInt(oferta.totalCentavos) - BigInt(oferta.freteCentavos)).toString() : '0';
  });
  protected readonly itensCompra = computed(() =>
    this.carrinho.itens().map((item) => ({
      produtoId: item.produtoId,
      produtoVarianteId: item.varianteId,
      quantidade: item.quantidade,
      precoAceitoCentavos: item.precoCentavos,
    })),
  );
  protected readonly indisponivel = computed(() =>
    this.carrinho.itens().some((item) => item.disponivel === false),
  );
  protected readonly valido = computed(() => itensCompraValidos(this.itensCompra()));
  protected readonly carrinhoMudou = computed(() => {
    const pendente = this.checkout.pendente();
    return !!pendente && !mesmosItensCompra(this.itensCompra(), pendente.itens);
  });

  protected readonly itensOriginais = computed(() => {
    const atuais = new Map(this.carrinho.itens().map((item) => [item.varianteId, item]));
    return (this.checkout.pendente()?.itens ?? []).map((item) => ({
      ...item,
      atual: atuais.get(item.produtoVarianteId),
    }));
  });

  constructor() {
    effect(onCleanup => {
      if (this.modalidade() !== 'Correios') {
        this.enderecoPostalCadastrado.set(null);
        return;
      }
      this.avisoEnderecoPostal.set('Consultando endereço cadastrado…');
      const consulta = this.correios.consultarEnderecoPadrao().subscribe({
        next: ({ endereco }) => {
          this.enderecoPostalCadastrado.set(endereco);
          this.avisoEnderecoPostal.set(endereco ? '' : 'Endereço cadastrado ausente ou incompleto. Informe o destino abaixo.');
        },
        error: (falha: unknown) => {
          this.avisoEnderecoPostal.set(falha instanceof HttpErrorResponse && falha.status === 401
            ? 'Entre na sua conta para consultar o endereço cadastrado. Você também pode preencher o destino.'
            : 'Não foi possível consultar o endereço cadastrado. Informe o destino abaixo.');
        },
      });
      onCleanup(() => consulta.unsubscribe());
    });
    this.checkout.atualizarTentativa();
    void this.carregarModalidades();
    this.destroy.onDestroy(() => clearTimeout(this.timer));
    revalidarCarrinho(this.carrinho, inject(CatalogoPublicoService))
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.consultando.set(false)),
      )
      .subscribe((mudou) => {
        if (mudou) this.precoMudou.set(true);
      });
  }

  protected async carregarModalidades(): Promise<void> {
    try {
      const modalidades = await firstValueFrom(this.atendimento.modalidades().pipe(takeUntilDestroyed(this.destroy)));
      if (this.destroy.destroyed) return;
      this.modalidades.set(modalidades);
      if (!modalidades.retiradaLocal && modalidades.entregaPropria) this.modalidade.set('EntregaPropria');
      if (!modalidades.retiradaLocal && !modalidades.entregaPropria && modalidades.correiosCotacaoManual) this.modalidade.set('Correios');
      if (modalidades.retiradaLocal && !modalidades.entregaPropria) this.modalidade.set('RetiradaLocal');
    } catch {
      if (this.destroy.destroyed) return;
      this.modalidades.set(null);
      this.erro.set('Não foi possível consultar as modalidades. Atualize as opções para continuar.');
    }
  }

  protected alterarEndereco(campo: keyof EnderecoEntrega, valor: string): void {
    const endereco = this.modalidade() === 'Correios' ? this.enderecoPostal : this.endereco;
    endereco.update(atual => ({ ...atual, [campo]: campo === 'cep' ? formatarCep(valor) : valor }));
  }

  protected aplicarCep(campos: CamposCep): void {
    const endereco = this.modalidade() === 'Correios' ? this.enderecoPostal : this.endereco;
    endereco.update(atual => ({ ...atual, ...campos }));
  }

  protected escolherEnderecoPostal(cadastrado: boolean): void {
    if (this.cotando() || this.solicitacaoIncerta()) return;
    const endereco = cadastrado ? this.enderecoPostalCadastrado() : enderecoVazio;
    if (endereco) this.enderecoPostal.set({ ...endereco });
  }

  private chaveCotacao(): string {
    return JSON.stringify([this.itensCompra(), this.modalidade(), this.usarPadrao(), this.endereco()]);
  }

  protected async cotar(): Promise<void> {
    if (this.cotando() || this.consultando() || !this.valido() || this.indisponivel()) return;
    const modalidade = this.modalidade();
    const modalidades = this.modalidades();
    if (!modalidades || (modalidade === 'RetiradaLocal' ? !modalidades.retiradaLocal : !modalidades.entregaPropria)) return;
    const destino = modalidade === 'EntregaPropria' ? this.usarPadrao() ? 'padrao' : this.endereco() : undefined;
    this.erro.set('');
    this.cotacao.set(null);
    if (destino && destino !== 'padrao' &&
        (!/^\d{5}-?\d{3}$/.test(destino.cep.trim()) || !/^[a-z]{2}$/i.test(destino.uf.trim()) ||
         this.camposEndereco.some(({ campo }) => campo !== 'complemento' && !destino[campo]?.trim()))) {
      this.erro.set('Preencha o endereço completo, com CEP de oito dígitos e UF.');
      return;
    }
    const chave = this.chaveCotacao();
    this.cotando.set(true);
    try {
      const resultado = await firstValueFrom(this.atendimento.cotar(this.itensCompra(), destino).pipe(
        takeUntilDestroyed(this.destroy),
      ));
      if (this.destroy.destroyed || chave !== this.chaveCotacao()) return;
      this.carrinho.atualizarPrecos(resultado.precosAtuais);
      const oferta = resultado.ofertas.find(item => item.modalidade === modalidade);
      if (!oferta) {
        const motivos: Record<string, string> = {
          ...mensagensCep,
          destino_nao_validado: 'Não foi possível validar o CEP deste endereço. Confira os dados ou informe outro endereço.',
          municipio_nao_atendido: 'O CEP, a cidade e a UF devem corresponder a um município atendido.',
          itens_indisponiveis: 'Revise a disponibilidade dos itens do carrinho.',
        };
        this.erro.set(motivos[resultado.motivoEntregaPropriaIndisponivel ?? ''] ?? 'Não há cotação para esta modalidade.');
        return;
      }
      this.cotacao.set({ chave: this.chaveCotacao(), oferta });
      clearTimeout(this.timer);
      this.timer = setTimeout(() => {
        if (this.cotacao()?.oferta.id !== oferta.id) return;
        this.cotacao.set(null);
        if (!this.checkout.pendente()) this.erro.set('A cotação venceu. Consulte os valores novamente.');
      }, Math.max(0, Date.parse(oferta.validaAteUtc) - Date.now()));
    } catch (erro) {
      if (this.destroy.destroyed) return;
      if (erro instanceof HttpErrorResponse && erro.status === 401)
        await this.router.navigate(['/login'], { queryParams: { retorno: 'checkout' } });
      else this.erro.set('Não foi possível consultar o frete. Tente novamente.');
    } finally {
      this.cotando.set(false);
    }
  }

  protected async enviar(recuperar: boolean): Promise<void> {
    if (
      this.checkout.enviando() ||
      (!recuperar && (this.consultando() || this.indisponivel() || !this.valido()))
    ) {
      return;
    }
    this.erro.set('');
    const oferta = this.oferta();
    if (!recuperar && (!oferta || Date.parse(oferta.validaAteUtc) <= Date.now())) {
      this.erro.set('Consulte uma cotação válida antes de confirmar a compra.');
      return;
    }
    const resultado = await this.checkout.enviar(this.itensCompra(), recuperar, oferta?.id);
    if (this.destroy.destroyed || !resultado) return;
    switch (resultado.estado) {
      case 'login':
        await this.router.navigate(['/login'], { queryParams: { retorno: 'checkout' } });
        break;
      case 'erro':
        this.erro.set(resultado.mensagem);
        break;
      case 'preco_alterado':
      case 'oferta_alterada':
        this.precoMudou.set(true);
        this.cotacao.set(null);
        await this.carregarModalidades();
        await this.cotar();
        break;
      case 'sucesso':
        await this.router.navigate(['/pedidos', resultado.pedido.id], { info: resultado.pedido });
        break;
    }
  }
}
