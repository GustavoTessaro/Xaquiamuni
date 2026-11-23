import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, forkJoin } from 'rxjs';
import { AtendimentoService, CidadeEntrega, ConfiguracaoAtendimento } from '../core/atendimento.service';
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
  imports: [AdminWorkspaceComponent, FormsModule],
  templateUrl: './admin-atendimento.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminAtendimentoComponent {
  private readonly api = inject(AtendimentoService);
  protected readonly config = signal<ConfiguracaoAtendimento | null>(null);
  protected readonly cidades = signal<CidadeEntrega[]>([]);
  protected readonly cidade = signal({ ...cidadeNova });
  protected readonly ocupado = signal(false);
  protected readonly revisar = signal(false);
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');
  protected readonly reais = formatarCentavos;
  private tentativa?: { assinatura: string; chave: string };

  constructor() { void this.recarregar(); }

  protected async recarregar(): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    try {
      await this.carregar();
      this.erro.set('');
      this.tentativa = undefined;
    } catch {
      this.erro.set('Não foi possível carregar as configurações de atendimento.');
      this.revisar.set(true);
    } finally {
      this.ocupado.set(false);
    }
  }

  private async carregar(): Promise<void> {
    const [config, cidades] = await firstValueFrom(forkJoin([this.api.configuracao(), this.api.cidades()]));
    this.config.set(config);
    this.cidades.set(cidades);
    this.cidade.set({ ...cidadeNova });
    this.revisar.set(false);
  }

  protected editarCidade(cidade?: CidadeEntrega): void {
    if (!cidade) { this.cidade.set({ ...cidadeNova }); return; }
    const valor = BigInt(cidade.tarifaCentavos);
    this.cidade.set({ ...cidade, tarifa: `${valor / 100n},${String(valor % 100n).padStart(2, '0')}` });
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

  protected salvarCidade(): void {
    const { id, codigoIbge, nome, uf, tarifa, ativa, revisao } = this.cidade();
    const tarifaCentavos = tarifaEmCentavos(tarifa);
    if (tarifaCentavos === null) { this.erro.set('Informe uma tarifa não negativa em reais, com até duas casas decimais.'); return; }
    void this.gravar(id ? 'PUT' : 'POST', id ? '/cidades/' + id : '/cidades', {
      codigoIbge, nome, uf, tarifaCentavos, ativa, ...(id && { revisao }),
    });
  }

  protected excluir(cidade: CidadeEntrega): void {
    if (window.confirm(`Excluir ${cidade.nome}/${cidade.uf} das cidades atendidas?`))
      void this.gravar('DELETE', `/cidades/${cidade.id}?revisao=${cidade.revisao}`, null);
  }

  private async gravar(metodo: 'POST' | 'PUT' | 'DELETE', rota: string, corpo: object | null): Promise<void> {
    if (this.ocupado() || this.revisar()) return;
    const assinatura = JSON.stringify([metodo, rota, corpo]);
    if (this.tentativa && this.tentativa.assinatura !== assinatura) {
      this.erro.set('Consulte os dados atuais antes de enviar uma operação diferente.');
      return;
    }
    this.ocupado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    this.tentativa ??= { assinatura, chave: crypto.randomUUID() };
    try {
      await firstValueFrom(this.api.escrever(metodo, rota, corpo, this.tentativa.chave));
      this.tentativa = undefined;
      this.revisar.set(true);
      this.sucesso.set('Alteração gravada.');
      await this.carregar();
    } catch (falha) {
      const status = falha instanceof HttpErrorResponse ? falha.status : 0;
      if (status >= 400 && status < 500) this.tentativa = undefined;
      if (status === 409 || status === 404) this.revisar.set(true);
      this.erro.set((falha instanceof HttpErrorResponse && (falha.error?.detail || falha.error?.title)) ||
        (this.revisar() ? 'Recarregue os dados antes de continuar.' :
          'Não foi possível confirmar o resultado. Repita a mesma operação ou recarregue os dados.'));
    } finally {
      this.ocupado.set(false);
    }
  }
}
