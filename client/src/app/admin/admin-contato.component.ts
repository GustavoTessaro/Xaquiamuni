import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AtendimentoService, ContatoLojaAdmin } from '../core/atendimento.service';
import { linkWhatsApp, normalizarEmailComercial } from '../public-store/contato-loja.component';
import { AdminWorkspaceComponent } from './admin-workspace.component';

type Canal = 'whatsapp' | 'email';
type Envio = { canal: Canal; corpo: object; chave: string };

@Component({
  selector: 'app-admin-contato',
  imports: [AdminWorkspaceComponent, FormsModule],
  templateUrl: './admin-contato.component.html',
  styleUrl: './admin-atendimento.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminContatoComponent {
  private readonly api = inject(AtendimentoService);
  private readonly salvo = signal<ContatoLojaAdmin | null>(null);
  protected readonly rascunho = signal<ContatoLojaAdmin | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly conflito = signal(false);
  protected readonly tentativa = signal<Envio | null>(null);
  protected readonly erro = signal('');
  protected readonly erroEmail = signal('');
  protected readonly erroWhatsApp = signal('');
  protected readonly sucesso = signal('');
  protected readonly pendente = computed(() => JSON.stringify(this.salvo()) !== JSON.stringify(this.rascunho()));
  protected readonly previewEmail = computed(() => {
    const dados = this.rascunho();
    return dados?.emailComercialHabilitado ? normalizarEmailComercial(dados.emailComercial) : null;
  });
  protected readonly previewWhatsApp = computed(() => {
    const dados = this.rascunho();
    return dados?.whatsAppConfirmado && dados.whatsAppHabilitado &&
      linkWhatsApp((dados.whatsApp ?? '').replace(/[ ().-]/g, '')) ? dados.whatsApp : null;
  });

  constructor() { void this.recarregar(); }

  protected alterar(patch: Partial<ContatoLojaAdmin>): void {
    this.rascunho.update(dados => dados && { ...dados, ...patch });
    this.erroEmail.set('');
    this.erroWhatsApp.set('');
  }

  protected async recarregar(): Promise<void> {
    if (this.ocupado() || this.tentativa()) return;
    if (this.pendente() && !window.confirm('Descartar as alterações ainda não salvas e carregar a configuração atual?')) return;
    this.ocupado.set(true);
    this.erro.set('');
    try {
      const atual = await firstValueFrom(this.api.contatoAdmin());
      this.salvo.set(atual);
      this.rascunho.set(atual);
      this.conflito.set(false);
      this.sucesso.set('');
      this.erroEmail.set('');
      this.erroWhatsApp.set('');
    } catch {
      this.erro.set('Não foi possível carregar o contato da loja. Tente novamente.');
    } finally { this.ocupado.set(false); }
  }

  protected salvar(canal: Canal): void {
    const dados = this.rascunho();
    if (!dados || this.ocupado() || this.conflito() || this.tentativa()) return;
    const revisao = dados.revisao;
    let corpo: object;
    if (canal === 'email') {
      const endereco = normalizarEmailComercial(dados.emailComercial);
      if (!endereco && (dados.emailComercial?.replace(/^ +| +$/g, '') || dados.emailComercialHabilitado)) {
        this.erroEmail.set('Informe um e-mail comercial válido para salvar.');
        return;
      }
      corpo = { endereco, habilitado: dados.emailComercialHabilitado, revisao };
    } else {
      if (dados.whatsAppHabilitado && (!dados.whatsAppConfirmado || !dados.whatsApp?.trim())) {
        this.erroWhatsApp.set('Confira e confirme o número antes de publicá-lo.');
        return;
      }
      if (dados.whatsAppHabilitado && !window.confirm('Publicar este número de WhatsApp na loja?')) return;
      corpo = { numero: dados.whatsApp?.trim() || null, confirmado: dados.whatsAppConfirmado,
        habilitado: dados.whatsAppHabilitado, revisao };
    }
    void this.enviar({ canal, corpo, chave: crypto.randomUUID() });
  }

  protected async enviar(tentativa: Envio): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.tentativa.set(tentativa);
    this.erro.set('');
    this.sucesso.set('');
    const email = tentativa.canal === 'email';
    try {
      const atual = await firstValueFrom(this.api.escrever<ContatoLojaAdmin>('PUT',
        email ? '/contato/email' : '/whatsapp', tentativa.corpo, tentativa.chave));
      const patch = email
        ? { emailComercial: atual.emailComercial, emailComercialHabilitado: atual.emailComercialHabilitado }
        : { whatsApp: atual.whatsApp, whatsAppConfirmado: atual.whatsAppConfirmado, whatsAppHabilitado: atual.whatsAppHabilitado };
      const revisao = atual.revisao;
      this.rascunho.update(dados => dados && { ...dados, ...patch, revisao });
      this.salvo.update(dados => dados && { ...dados, ...patch, revisao });
      this.tentativa.set(null);
      this.sucesso.set(email ? 'E-mail comercial salvo.' : 'WhatsApp salvo.');
    } catch (falha) {
      if (falha instanceof HttpErrorResponse && falha.status >= 400 && falha.status < 500 && falha.status !== 408) {
        this.tentativa.set(null);
        if (falha.status === 409) {
          this.conflito.set(true);
          this.erro.set('A configuração foi alterada. Recarregue os dados antes de salvar novamente.');
        } else {
          (email ? this.erroEmail : this.erroWhatsApp).set(
            falha.error?.errors?.atendimento?.[0] || 'Não foi possível salvar. Confira os dados e tente novamente.');
        }
      } else {
        this.erro.set('Não foi possível confirmar o salvamento. Use Recuperar salvamento para verificar a mesma tentativa.');
      }
    } finally { this.ocupado.set(false); }
  }
}
