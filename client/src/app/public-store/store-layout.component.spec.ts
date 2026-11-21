import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
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

  it.each([null, 'original', 'natal', 'tema-inexistente'])(
    'mantém navegação e tema original para resposta %s',
    (chave) => {
      const fixture = TestBed.createComponent(StoreLayoutComponent);
      fixture.detectChanges();
      const layout = fixture.nativeElement as HTMLElement;
      expect(layout.dataset['theme']).toBe('original');
      const req = TestBed.inject(HttpTestingController).expectOne('/api/loja/tema');
      if (chave === null) req.flush(null, { status: 503, statusText: 'Unavailable' });
      else req.flush({ chave, revisao: 2 });
      fixture.detectChanges();
      expect(layout.dataset['theme']).toBe('original');
      expect(layout.querySelector('main router-outlet')).not.toBeNull();
      expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
      TestBed.inject(HttpTestingController).verify();
    },
  );
});
