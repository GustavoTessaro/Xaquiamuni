import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, forkJoin, map, Subscription } from 'rxjs';
import { AtendimentoService, CidadeEntrega, ConfiguracaoAtendimento, EstadoMunicipios, MunicipioIbge } from '../core/atendimento.service';
import { formatarCentavos } from '../public-store/carrinho.service';
import { AdminWorkspaceComponent } from './admin-workspace.component';

const cidadeNova = { id: '', codigoIbge: '', nome: '', uf: '', tarifa: '', ativa: false, revisao: 0 };

export function tarifaEmCentavos(valor: string): string | null {
  const partes = /^(\d{1,19})(?:[,.](\d{1,2}))?$/.exec(valor.trim());
  if (!partes) return null;
  const centavos = BigInt(partes[1]) * 100n + BigInt((partes[2] ?? '').padEnd(2, '0'));
  return centavos <= 9223372036854775807n ? centavos.toString() : null;
}

@Component({
  selector: 'app-admin-atendimento',
  imports: [AdminWorkspaceComponent, FormsModule, RouterLink],
  templateUrl: './admin-atendimento.component.html',
  styleUrl: './admin-atendimento.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminAtendimentoComponent {
  private readonly api = inject(AtendimentoService);
  private readonly destroy = inject(DestroyRef);
  protected readonly config = signal<ConfiguracaoAtendimento | null>(null);
  protected readonly configSalva = signal<ConfiguracaoAtendimento | null>(null);
  protected readonly cidades = signal<CidadeEntrega[]>([]);
  protected readonly temCidadeAtiva = computed(() => this.cidades().some(item => item.ativa));
  protected readonly cidade = signal({ ...cidadeNova });
  protected readonly ocupado = signal(false);
  protected readonly revisar = signal(false);
  protected readonly cidadeComPedidos = signal<CidadeEntrega | null>(null);
  protected readonly exclusaoIncerta = signal<CidadeEntrega | null>(null);
  protected readonly erro = signal('');
  protected readonly aviso = signal('');
  protected readonly sucesso = signal('');
  protected readonly reais = formatarCentavos;
  protected readonly estados = signal<EstadoMunicipios[]>([]);
  protected readonly municipios = signal<MunicipioIbge[]>([]);
  protected readonly buscaMunicipio = signal('');
  protected readonly municipiosVisiveis = computed(() => {
    const busca = this.normalizar(this.buscaMunicipio());
    const selecionado = this.cidade().codigoIbge;
    return this.municipios().filter(item => item.codigoIbge === selecionado || this.normalizar(item.nome).includes(busca));
  });
  protected readonly carregandoCatalogo = signal(false);
  protected readonly erroCatalogo = signal('');
  protected readonly erroCidade = signal('');
  protected readonly erroCorreios = signal('');
  protected readonly municipioLegado = computed(() => {
    const cidade = this.cidade();
    return !!cidade.id && !!cidade.codigoIbge && !this.carregandoCatalogo() && !this.erroCatalogo() &&
      !this.municipios().some(item => item.codigoIbge === cidade.codigoIbge && item.nome === cidade.nome);
  });
  private consultaCatalogo?: Subscription;
  private tentativa?: { assinatura: string; chave: string };

  constructor() { void this.recarregar(); }

  protected async recarregar(): Promise<void> {
    if (this.ocupado()) return;
    if (this.exclusaoIncerta()) {
      this.erro.set('Recupere a exclusão pendente antes de recarregar os dados.');
      return;
    }
    this.ocupado.set(true);
    try {
      await this.carregar();
      this.erro.set('');
      this.aviso.set('');
      this.tentativa = undefined;
    } catch {
      this.erro.set('Não foi possível carregar as configurações de atendimento.');
      this.revisar.set(true);
    } finally {
      this.ocupado.set(false);
    }
  }

  private async carregar(rotaSalva?: string): Promise<void> {
    const [config, cidades] = await firstValueFrom(forkJoin([this.api.configuracao(), this.api.cidades()]));
    const rascunho = this.config();
    const proximo = { ...config };
    const salvo = this.configSalva();
    const revisaoEsperada = salvo ? salvo.revisao + (rotaSalva?.startsWith('/cidades') ? 0 : 1) : null;
    if (rotaSalva && rascunho && config.revisao === revisaoEsperada) {
      if (rotaSalva !== '/modalidades' && this.modalidadesPendentes(rascunho)) {
        const { retiradaHabilitada, entregaPropriaHabilitada, enderecoRetirada, instrucoesRetirada } = rascunho;
        Object.assign(proximo, { retiradaHabilitada, entregaPropriaHabilitada, enderecoRetirada, instrucoesRetirada });
      }
      if (rotaSalva !== '/correios-manual' && this.correiosPendentes(rascunho)) {
        const { cotacaoManualCorreiosHabilitada, validadePropostaCorreiosHoras } = rascunho;
        Object.assign(proximo, { cotacaoManualCorreiosHabilitada, validadePropostaCorreiosHoras });
      }
      if (rotaSalva !== '/whatsapp' && this.contatoPendente(rascunho)) {
        const { whatsApp, whatsAppConfirmado, whatsAppHabilitado } = rascunho;
        Object.assign(proximo, { whatsApp, whatsAppConfirmado, whatsAppHabilitado });
      }
    } else if (rotaSalva && rascunho && (this.modalidadesPendentes(rascunho) ||
      this.correiosPendentes(rascunho) || this.contatoPendente(rascunho))) {
      this.aviso.set('Outra sessão atualizou as configurações. Confira os dados recarregados antes de editar novamente.');
    }
    this.configSalva.set(config);
    this.config.set(proximo);
    this.cidades.set(cidades);
    if (!rotaSalva || rotaSalva.startsWith('/cidades')) this.editarCidade();
    this.cidadeComPedidos.set(null);
    this.exclusaoIncerta.set(null);
    this.revisar.set(false);
  }

  protected editarCidade(cidade?: CidadeEntrega): void {
    this.erroCidade.set('');
    this.buscaMunicipio.set('');
    if (cidade) {
      const valor = BigInt(cidade.tarifaCentavos);
      this.cidade.set({ ...cidade, tarifa: `${valor / 100n},${String(valor % 100n).padStart(2, '0')}` });
    } else this.cidade.set({ ...cidadeNova });
    if (cidade || !this.estados().length) this.carregarCatalogo();
    else {
      this.consultaCatalogo?.unsubscribe();
      this.carregandoCatalogo.set(false);
      this.erroCatalogo.set('');
      this.municipios.set([]);
    }
  }

  protected carregarCatalogo(): void {
    this.consultaCatalogo?.unsubscribe();
    this.carregandoCatalogo.set(true);
    this.erroCatalogo.set('');
    const uf = this.cidade().uf;
    this.municipios.set([]);
    const porNome = (a: { nome: string }, b: { nome: string }) => a.nome.localeCompare(b.nome, 'pt-BR');
    const consulta = uf
      ? this.api.municipios(uf).pipe(map(itens => this.municipios.set(itens.sort(porNome))))
      : this.api.estados().pipe(map(itens => this.estados.set(itens.sort(porNome))));
    this.consultaCatalogo = consulta.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => this.carregandoCatalogo.set(false),
      error: () => {
        this.erroCatalogo.set('Não foi possível carregar a lista de localidades.');
        this.carregandoCatalogo.set(false);
      },
    });
  }

  protected selecionarUf(uf: string): void {
    this.cidade.update(atual => ({ ...atual, uf, codigoIbge: '', nome: '' }));
    this.buscaMunicipio.set('');
    this.carregarCatalogo();
  }

  protected selecionarMunicipio(codigo: string): void {
    const municipio = this.municipios().find(item => item.codigoIbge === codigo);
    this.cidade.update(atual => ({ ...atual, codigoIbge: municipio?.codigoIbge ?? '', nome: municipio?.nome ?? '' }));
    this.erroCidade.set('');
  }

  private normalizar(texto: string): string {
    return texto.trim().normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase('pt-BR');
  }

  protected modalidadesPendentes(dados: ConfiguracaoAtendimento): boolean {
    const salvo = this.configSalva();
    return !!salvo && (dados.retiradaHabilitada !== salvo.retiradaHabilitada ||
      dados.entregaPropriaHabilitada !== salvo.entregaPropriaHabilitada ||
      dados.enderecoRetirada !== salvo.enderecoRetirada || dados.instrucoesRetirada !== salvo.instrucoesRetirada);
  }

  protected correiosPendentes(dados: ConfiguracaoAtendimento): boolean {
    const salvo = this.configSalva();
    return !!salvo && (dados.cotacaoManualCorreiosHabilitada !== salvo.cotacaoManualCorreiosHabilitada ||
      dados.validadePropostaCorreiosHoras !== salvo.validadePropostaCorreiosHoras);
  }

  protected contatoPendente(dados: ConfiguracaoAtendimento): boolean {
    const salvo = this.configSalva();
    return !!salvo && (dados.whatsApp !== salvo.whatsApp || dados.whatsAppConfirmado !== salvo.whatsAppConfirmado ||
      dados.whatsAppHabilitado !== salvo.whatsAppHabilitado);
  }

  protected salvarModalidades(config: ConfiguracaoAtendimento): void {
    const { retiradaHabilitada, entregaPropriaHabilitada, enderecoRetirada, instrucoesRetirada, revisao } = config;
    void this.gravar('PUT', '/modalidades', {
      retiradaHabilitada, entregaPropriaHabilitada, correiosHabilitados: false, enderecoRetirada, instrucoesRetirada, revisao,
    });
  }

  protected alterarNumero(config: ConfiguracaoAtendimento, numero: string): void {
    this.config.set({ ...config, whatsApp: numero, whatsAppConfirmado: false, whatsAppHabilitado: false });
  }

  protected salvarContato(config: ConfiguracaoAtendimento): void {
    if (this.ocupado() || this.revisar()) return;
    if (config.whatsAppHabilitado && (!config.whatsAppConfirmado || !config.whatsApp?.trim())) {
      this.erro.set('Confirme o número antes de publicar o contato.');
      return;
    }
    if (config.whatsAppHabilitado && !window.confirm('Publicar este número de WhatsApp na loja?')) return;
    void this.gravar('PUT', '/whatsapp', {
      numero: config.whatsApp?.trim() || null, confirmado: config.whatsAppConfirmado,
      habilitado: config.whatsAppHabilitado, revisao: config.revisao,
    });
  }

  protected salvarCorreios(config: ConfiguracaoAtendimento): void {
    if (this.ocupado() || this.revisar()) return;
    const validadeHoras = config.validadePropostaCorreiosHoras === undefined ? 48 : config.validadePropostaCorreiosHoras;
    this.erroCorreios.set('');
    if (!Number.isInteger(validadeHoras) || validadeHoras < 1 || validadeHoras > 168) {
      this.erroCorreios.set('Informe um número inteiro entre 1 e 168 horas.');
      return;
    }
    if (!window.confirm('Salvar as configurações de cotação manual dos Correios?')) return;
    void this.gravar('PUT', '/correios-manual', {
      habilitada: config.cotacaoManualCorreiosHabilitada ?? false, validadeHoras, revisao: config.revisao,
    });
  }

  protected salvarCidade(): void {
    if (this.ocupado() || this.revisar()) return;
    const { id, codigoIbge, nome, uf, tarifa, ativa, revisao } = this.cidade();
    const tarifaCentavos = tarifaEmCentavos(tarifa);
    this.erroCidade.set('');
    if (!codigoIbge || !uf || !nome) { this.erroCidade.set('Selecione o estado e o município.'); return; }
    if (tarifaCentavos === null) { this.erroCidade.set('Informe uma tarifa não negativa em reais, com até duas casas decimais.'); return; }
    void this.gravar(id ? 'PUT' : 'POST', id ? '/cidades/' + id : '/cidades', {
      codigoIbge, nome, uf, tarifaCentavos, ativa, ...(id && { revisao }),
    });
  }

  protected excluir(cidade: CidadeEntrega): void {
    if (this.ocupado() || this.revisar() || this.exclusaoIncerta()) return;
    if (window.confirm(`Excluir ${cidade.nome}/${cidade.uf}? A exclusão é permanente.`))
      this.enviarExclusao(cidade);
  }

  protected recuperarExclusao(cidade: CidadeEntrega): void {
    this.enviarExclusao(cidade);
  }

  protected desabilitarAtendimento(cidade: CidadeEntrega): void {
    this.alterarSituacaoCidade(cidade, false);
  }

  protected alterarSituacaoCidade(cidade: CidadeEntrega, ativa: boolean): void {
    if (this.ocupado() || this.revisar() || this.exclusaoIncerta()) return;
    const acao = ativa ? 'Habilitar' : 'Desabilitar';
    if (!window.confirm(`${acao} o atendimento em ${cidade.nome}/${cidade.uf}? Os pedidos existentes serão preservados.`)) return;
    const { codigoIbge, nome, uf, tarifaCentavos, revisao } = cidade;
    void this.gravar('PUT', '/cidades/' + cidade.id, { codigoIbge, nome, uf, tarifaCentavos, revisao, ativa });
  }

  private enviarExclusao(cidade: CidadeEntrega): void {
    void this.gravar('DELETE', `/cidades/${cidade.id}?revisao=${cidade.revisao}`, null, cidade);
  }

  private async gravar(metodo: 'POST' | 'PUT' | 'DELETE', rota: string, corpo: object | null, exclusao?: CidadeEntrega): Promise<void> {
    if (this.ocupado() || this.revisar()) return;
    const assinatura = JSON.stringify([metodo, rota, corpo]);
    if (this.tentativa && this.tentativa.assinatura !== assinatura) {
      this.erro.set('Consulte os dados atuais antes de enviar uma operação diferente.');
      return;
    }
    this.ocupado.set(true);
    this.erro.set('');
    this.aviso.set('');
    this.sucesso.set('');
    this.cidadeComPedidos.set(null);
    this.tentativa ??= { assinatura, chave: crypto.randomUUID() };
    try {
      await firstValueFrom(this.api.escrever(metodo, rota, corpo, this.tentativa.chave));
      this.tentativa = undefined;
      this.exclusaoIncerta.set(null);
      this.revisar.set(true);
      this.sucesso.set(exclusao ? 'Cidade excluída.' : rota === '/modalidades' ? 'Modalidades salvas.'
        : rota === '/correios-manual' ? 'Configuração dos Correios salva.'
        : rota === '/whatsapp' ? 'Contato salvo.' : 'Cidade salva.');
      await this.carregar(rota);
    } catch (falha) {
      const status = falha instanceof HttpErrorResponse ? falha.status : 0;
      const bloqueio = exclusao && falha instanceof HttpErrorResponse && status === 409 &&
        falha.error?.codigo === 'cidade_com_pedidos';
      if (status >= 400 && status < 500 && status !== 408) {
        this.tentativa = undefined;
        this.exclusaoIncerta.set(null);
      } else if (exclusao && this.tentativa) {
        this.exclusaoIncerta.set(exclusao);
      }
      if (bloqueio) this.cidadeComPedidos.set(exclusao);
      else if (status === 409 || status === 404) this.revisar.set(true);
      this.erro.set((falha instanceof HttpErrorResponse && (falha.error?.detail || falha.error?.errors?.atendimento?.[0] || falha.error?.title)) ||
        (this.revisar() ? 'Recarregue os dados antes de continuar.' :
          'Não foi possível confirmar o resultado. Repita a mesma operação ou recarregue os dados.'));
    } finally {
      this.ocupado.set(false);
    }
  }
}
