import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminAuthService } from '../core/admin-auth/admin-auth.service';
import { AdminWorkspaceComponent } from './admin-workspace.component';

const chave = 'xaquiamuni:admin:tema';

describe('Tema no cabeçalho ADMIN', () => {
  beforeEach(() => {
    localStorage.removeItem(chave);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AdminAuthService, useValue: {} }],
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
});
