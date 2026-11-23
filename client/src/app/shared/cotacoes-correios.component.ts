import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AdminWorkspaceComponent } from '../admin/admin-workspace.component';
import { tarifaEmCentavos } from '../admin/admin-atendimento.component';
import { CotacoesCorreiosService, DetalheCotacao, erroPostal, estadosCotacao, PaginaCotacoes, RevisaoPostal, VolumePostal } from '../core/cotacoes-correios.service';
import { formatarCentavos } from '../public-store/carrinho.service';
import { CheckoutService } from '../public-store/checkout.service';

const novaProposta = () => ({
  servico: '', frete: '', prazoDias: null as number | null, validade: '', pesoTotalGramas: null as number | null,
  volumes: [] as (Omit<VolumePostal, 'pesoGramas'> & { pesoGramas: number | null })[],
});

@Component({
  imports: [DatePipe, NgTemplateOutlet, FormsModule, RouterLink, AdminWorkspaceComponent],
  templateUrl: './cotacoes-correios.component.html',
  styles: `
    :host { display: block; }
    .painel { max-width: 68rem; margin-inline: auto; padding: 1rem; color: var(--admin-text, var(--brand-ink)); }
    .cartao { border: 1px solid var(--admin-border, var(--brand-line, currentColor)); border-radius: .75rem;
      background: var(--admin-surface, var(--brand-surface)); padding: 1rem; min-width: 0; overflow-wrap: anywhere; }
    .painel h1 { font-size: 1.65rem; font-weight: 700; }
    .painel h2 { font-size: 1.2rem; font-weight: 650; }
    .painel input, .painel select, .painel button { border: 1px solid var(--admin-border, var(--brand-line, currentColor));
      border-radius: .4rem; padding: .45rem .7rem; min-height: 2.75rem; max-width: 100%; }
    .painel input, .painel select { display: block; width: 100%; background: var(--admin-field, var(--brand-surface));
      border-color: var(--admin-control, var(--brand-control-line)); }
    .painel button:disabled { opacity: .65; }
    .painel a { color: var(--admin-action-text, var(--brand-link)); text-decoration: underline; text-underline-offset: .2em; }
    .painel :focus-visible { outline: 2px solid var(--admin-focus, var(--brand-focus, currentColor)); outline-offset: 3px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CotacoesCorreiosComponent {
  private readonly api = inject(CotacoesCorreiosService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  protected readonly checkout = inject(CheckoutService);
  protected readonly admin = this.route.snapshot.data['admin'] === true;
  protected readonly base = this.admin ? '/admin/cotacoes-correios' : '/cotacoes-correios';
  protected readonly estados = estadosCotacao;
  protected readonly reais = formatarCentavos;
  protected readonly id = signal<string | null>(null);
  protected readonly pagina = signal<PaginaCotacoes | null>(null);
  protected readonly detalhe = signal<DetalheCotacao | null>(null);
  protected readonly revisao = signal<RevisaoPostal | null>(null);
  protected readonly erro = signal('');
  protected readonly sucesso = signal('');
  protected readonly ocupado = signal(false);
  protected readonly carregando = signal(false);
  protected readonly incerta = signal(false);
  protected filtro = '';
  protected motivo = '';
  protected proposta = novaProposta();
  private etag: string | null = null;
  private leitura = 0;
  private tentativa?: { id: string; acao: string; corpo: object; etag: string; chave: string };

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params => {
      this.id.set(params.get('id'));
      this.detalhe.set(null);
      this.revisao.set(null);
      this.proposta = novaProposta();
      this.motivo = '';
      this.sucesso.set('');
      this.tentativa = undefined;
      this.incerta.set(false);
      this.checkout.atualizarTentativa();
      void this.carregar();
    });
  }

  protected async carregar(pagina = 1, manterErro = false) {
    const leitura = ++this.leitura;
    const id = this.id();
    this.carregando.set(true);
    if (!manterErro) this.erro.set('');
    try {
      if (id) {
        const resposta = await firstValueFrom(this.api.detalhe(this.admin, id));
        if (leitura !== this.leitura || this.destroy.destroyed) return;
        this.etag = resposta.headers.get('ETag');
        this.detalhe.set(resposta.body);
      } else {
        const resposta = await firstValueFrom(this.api.listar(this.admin, pagina, this.filtro));
        if (leitura === this.leitura && !this.destroy.destroyed) this.pagina.set(resposta);
      }
    } catch (falha) {
      if (leitura === this.leitura && !this.destroy.destroyed) {
        this.detalhe.set(null);
        this.pagina.set(null);
        this.erro.set(erroPostal(falha));
      }
    } finally {
      if (leitura === this.leitura) this.carregando.set(false);
    }
  }

  protected adicionarVolume() {
    this.proposta.volumes.push({ pesoGramas: null, comprimentoCm: null, larguraCm: null, alturaCm: null });
  }

  protected publicar() {
    const { servico, frete, prazoDias, validade, pesoTotalGramas, volumes } = this.proposta;
    const freteCentavos = tarifaEmCentavos(frete);
    const validaAte = validade ? new Date(validade) : null;
    if (!servico.trim() || servico.trim().length > 60 || freteCentavos === null || prazoDias === null || !Number.isInteger(prazoDias) || prazoDias < 1 ||
        (validaAte && !Number.isFinite(validaAte.getTime())) ||
        (pesoTotalGramas !== null && (!Number.isInteger(pesoTotalGramas) || pesoTotalGramas < 1)) ||
        (pesoTotalGramas === null && !volumes.length) ||
        (pesoTotalGramas !== null && volumes.length > 0 && volumes.reduce((peso, v) => peso + (v.pesoGramas ?? 0), 0) !== pesoTotalGramas) ||
        volumes.some(v => {
          const dimensoes = [v.comprimentoCm, v.larguraCm, v.alturaCm];
          return v.pesoGramas === null || !Number.isInteger(v.pesoGramas) || v.pesoGramas < 1 ||
            (!dimensoes.every(x => x === null) && !dimensoes.every(x => x !== null && Number.isFinite(x) && x > 0));
        })) {
      this.erro.set('Informe serviço, frete, prazo e peso total ou por volume. Se informar ambos, os pesos devem coincidir. Informe as três dimensões de cada volume ou deixe as três em branco.');
      return;
    }
    void this.operar('propostas', {
      servico: servico.trim(), freteCentavos, prazoDias, validaAteUtc: validaAte?.toISOString() ?? null,
      pesoTotalGramas, volumes: volumes.map(v => ({ ...v })),
    }, `Publicar ${servico.trim()}, frete de ${formatarCentavos(freteCentavos)} e prazo de ${prazoDias} dia(s)?`);
  }

  protected decidir(acao: 'aceitar' | 'recusar' | 'cancelar', revisao: number | null) {
    const corpo = acao === 'aceitar' || (!this.admin && acao === 'recusar')
      ? { revisao, motivo: this.motivo.trim() || null }
      : { motivo: this.motivo.trim() || null };
    void this.operar(acao, corpo, `Confirma a ação de ${acao} esta cotação?`);
  }

  protected async operar(acao?: string, corpo?: object, confirmacao?: string) {
    const id = this.id();
    if (!id || this.ocupado() || this.carregando()) return;
    if (!this.tentativa && acao && corpo && this.etag && window.confirm(confirmacao)) {
      this.tentativa = { id, acao, corpo, etag: this.etag, chave: crypto.randomUUID() };
    }
    const tentativa = this.tentativa;
    if (!tentativa || tentativa.id !== id) return;
    this.ocupado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    try {
      await firstValueFrom(this.api.operar(this.admin, id, tentativa.acao, tentativa.corpo, tentativa.etag, tentativa.chave));
      if (this.id() !== id || this.destroy.destroyed) return;
      this.tentativa = undefined;
      this.incerta.set(false);
      this.sucesso.set('Cotação atualizada.');
      this.revisao.set(null);
      this.proposta = novaProposta();
      await this.carregar();
    } catch (falha) {
      if (this.id() !== id || this.destroy.destroyed) return;
      const status = (falha as { status?: number }).status;
      const incerta = !status || status >= 500 || status === 408 || status === 401;
      this.incerta.set(incerta);
      if (!incerta) this.tentativa = undefined;
      this.erro.set(incerta ? 'Resultado incerto. Use Recuperar operação para repetir a tentativa original.' : erroPostal(falha));
      if (status === 409) await this.carregar(1, true);
    } finally {
      this.ocupado.set(false);
    }
  }

  protected async revisar(manterErro = false) {
    const id = this.id();
    if (!id || this.ocupado()) return;
    this.revisao.set(null);
    this.ocupado.set(true);
    if (!manterErro) this.erro.set('');
    try {
      const revisao = await firstValueFrom(this.api.revisar(id));
      if (this.id() === id && !this.destroy.destroyed) this.revisao.set(revisao);
    } catch (falha) {
      if (this.id() === id && !this.destroy.destroyed) {
        this.erro.set(erroPostal(falha));
        await this.carregar(1, true);
      }
    } finally {
      this.ocupado.set(false);
    }
  }

  protected async finalizar(recuperar: boolean) {
    const id = this.id();
    const token = this.revisao()?.conversaoCorreiosId;
    if (!id || this.ocupado() || this.checkout.enviando() || (recuperar ? this.checkout.pendente()?.postal?.cotacaoId !== id : !token)) return;
    if (!recuperar && !window.confirm('Confirmar o total exibido e criar o pedido para pagamento por PIX?')) return;
    this.erro.set('');
    const resultado = await this.checkout.enviar([], recuperar, undefined, token ? { token, cotacaoId: id } : undefined);
    if (!resultado || this.id() !== id || this.destroy.destroyed) return;
    if (resultado.estado === 'sucesso') {
      await this.router.navigate(['/pedidos', resultado.pedido.id], { info: resultado.pedido });
    } else if (resultado.estado === 'preco_alterado') {
      this.erro.set('Os preços mudaram. Confira o novo total e confirme novamente.');
      await this.revisar(true);
    } else if (resultado.estado === 'login') {
      await this.router.navigate(['/login'], { queryParams: { retorno: 'cotacoes-correios', cotacao: id } });
    } else if (resultado.estado === 'erro') {
      this.erro.set(resultado.mensagem);
      if (!this.checkout.pendente()) {
        this.revisao.set(null);
        await this.carregar(1, true);
      }
    }
  }
}

