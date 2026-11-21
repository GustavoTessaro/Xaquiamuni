import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AdminAuthService } from '../core/admin-auth/admin-auth.service';
import { AdminWorkspaceComponent } from './admin-workspace.component';

const chave = 'xaquiamuni:admin:tema';

@Component({
  imports: [AdminWorkspaceComponent],
  template: `
    <app-admin-workspace activePage="finance">
      <p role="alert" class="bg-amber-50 text-amber-950">Vencimento</p>
      <table class="bg-white text-slate-900"><tbody><tr><td>R$ 19,90</td></tr></tbody></table>
      <label>Vencimento <input type="date" value="2026-11-21"></label>
    </app-admin-workspace>
  `,
})
class ConteudoProjetadoComponent {}

describe('Tema no cabeçalho ADMIN', () => {
  beforeEach(() => {
    localStorage.removeItem(chave);
    TestBed.configureTestingModule({
      providers: [provideRouter([
        { path: 'admin/financeiro', component: ConteudoProjetadoComponent },
        { path: 'admin/agenda', component: ConteudoProjetadoComponent },
      ]), { provide: AdminAuthService, useValue: {} }],
    });
  });
  afterEach(() => localStorage.removeItem(chave));

  function renderizar() {
    const fixture = TestBed.createComponent(AdminWorkspaceComponent);
    fixture.componentRef.setInput('activePage', 'overview');
    fixture.detectChanges();
    return fixture;
  }

  it('usa botão nativo focável ao lado de Sair e alterna imediatamente', () => {
    const fixture = renderizar();
    const shell = fixture.nativeElement.querySelector('.admin-shell') as HTMLElement;
    const controle = shell.querySelector<HTMLButtonElement>('.admin-theme-toggle')!;
    expect(shell.dataset['adminTheme']).toBe('light');
    expect(controle.type).toBe('button');
    expect(controle.tabIndex).toBe(0);
    controle.focus();
    expect(document.activeElement).toBe(controle);
    expect(controle.nextElementSibling?.textContent?.trim()).toBe('Sair');
    expect(controle.getAttribute('aria-label')).toBe('Ativar modo escuro');
    controle.click();
    fixture.detectChanges();
    expect(shell.dataset['adminTheme']).toBe('dark');
    expect(controle.getAttribute('aria-label')).toBe('Ativar modo claro');
    expect(localStorage.getItem(chave)).toBe('dark');
    expect(document.documentElement.hasAttribute('data-admin-theme')).toBe(false);
    expect(document.body.hasAttribute('data-admin-theme')).toBe(false);
    fixture.destroy();

    TestBed.resetTestingModule().configureTestingModule({
      providers: [provideRouter([]), { provide: AdminAuthService, useValue: {} }],
    });
    const recriado = renderizar();
    expect(recriado.nativeElement.querySelector('.admin-shell').dataset.adminTheme).toBe('dark');
  });
  it('mantém tabela, formulário e alerta no escopo ADMIN durante navegação', async () => {
    const harness = await RouterTestingHarness.create('/admin/financeiro');
    const shell = () => harness.routeNativeElement!.querySelector<HTMLElement>('.admin-shell')!;
    shell().querySelector<HTMLButtonElement>('.admin-theme-toggle')!.click();
    harness.detectChanges();
    for (const rota of ['/admin/financeiro', '/admin/agenda']) {
      await harness.navigateByUrl(rota);
      expect(shell().dataset['adminTheme']).toBe('dark');
      expect(shell().querySelector('[role="alert"]')?.textContent).toContain('Vencimento');
      expect(shell().querySelector('table')?.textContent).toContain('R$ 19,90');
      expect(shell().querySelector('input')?.value).toBe('2026-11-21');
    }
  });
});
