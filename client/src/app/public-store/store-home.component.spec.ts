import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { StoreHomeComponent } from './store-home.component';

describe('StoreHomeComponent', () => {
  let fixture: ComponentFixture<StoreHomeComponent>;

  it('shows distinct icons for known categories and a generic icon for future categories', async () => {
    await TestBed.configureTestingModule({
      imports: [StoreHomeComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogoPublicoService,
          useValue: {
            listarCategorias: vi.fn(() =>
              of([
                { id: 'soap-id', nome: 'Sabonetes Artesanais', descricao: null },
                { id: 'candle-id', nome: 'Velas Aromáticas Artesanais', descricao: null },
                { id: 'future-id', nome: 'Novidades', descricao: null },
              ]),
            ),
            listarProdutos: vi.fn(() =>
              of({
                itens: [],
                pagina: 1,
                itensPorPagina: 8,
                totalItens: 0,
                totalPaginas: 0,
              }),
            ),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreHomeComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.store-hero__art[aria-hidden="true"] img[alt=""]')
      ?.getAttribute('src')).toBe('/logo-xaquiamuni.jpeg');
    const icons = Array.from(element.querySelectorAll<HTMLElement>('[data-category-icon]'));
    expect(icons.map((icon) => icon.dataset['categoryIcon'])).toEqual([
      'soap',
      'candle',
      'generic',
    ]);
    expect(element.querySelectorAll('.category-card')).toHaveLength(3);
    expect((element.querySelector('.category-card') as HTMLAnchorElement).href).toContain(
      'soap-id',
    );
  });
});
