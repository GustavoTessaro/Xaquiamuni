import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../core/auth/auth.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { StoreLayoutComponent } from './store-layout.component';

describe('tema da loja pública', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [StoreLayoutComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { obterSessao: () => of(null) } },
        { provide: CatalogoPublicoService, useValue: { listarCategorias: () => of([]) } },
      ],
    });
  });

  it.each([null, 'original', 'natal', 'black-friday', 'pascoa', 'ano-novo', 'dia-das-maes', 'tema-inexistente'])(
    'mantém navegação e resolve o tema para resposta %s',
    (chave) => {
      const fixture = TestBed.createComponent(StoreLayoutComponent);
      fixture.detectChanges();
      const layout = fixture.nativeElement as HTMLElement;
      expect(layout.dataset['theme']).toBe('original');
      const req = TestBed.inject(HttpTestingController).expectOne('/api/loja/tema');
      if (chave === null) req.flush(null, { status: 503, statusText: 'Unavailable' });
      else req.flush({ chave, revisao: 2 });
      fixture.detectChanges();
      expect(layout.dataset['theme']).toBe(chave === null || chave === 'tema-inexistente' ? 'original' : chave);
      const sazonal = !!chave && chave !== 'original' && chave !== 'tema-inexistente';
      expect(!!layout.style.getPropertyValue('--tema-painel')).toBe(sazonal);
      expect(layout.querySelector('main router-outlet')).not.toBeNull();
      expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
      TestBed.inject(HttpTestingController).verify();
    },
  );
  it('revalida ao voltar para a aba e remove os tokens ao restaurar o Original', () => {
    const fixture = TestBed.createComponent(StoreLayoutComponent);
    const http = TestBed.inject(HttpTestingController);
    const layout = fixture.nativeElement as HTMLElement;
    http.expectOne('/api/loja/tema').flush({ chave: 'natal', revisao: 1 });
    fixture.detectChanges();
    expect(layout.dataset['theme']).toBe('natal');

    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      document.dispatchEvent(new Event('visibilitychange'));
      http.expectOne('/api/loja/tema').flush({ chave: 'original', revisao: 2 });
      fixture.detectChanges();
      expect(layout.dataset['theme']).toBe('original');
      expect(layout.style.getPropertyValue('--tema-painel')).toBe('');
      http.verify();
    } finally {
      visibility.mockRestore();
    }
  });

});
