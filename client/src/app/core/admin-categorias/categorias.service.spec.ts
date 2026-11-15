import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CategoriasService, AdminCategoria, CategoriaWriteRequest } from './categorias.service';

describe('CategoriasService', () => {
  let service: CategoriasService;
  let http: HttpTestingController;

  const categoria: AdminCategoria = {
    id: '2dc5886e-80f7-4e88-b32f-5f5ffcab4414',
    nome: 'Sabonetes Artesanais',
    descricao: 'Fabricação própria',
    ativa: true,
    ordemExibicao: 0,
  };
  const writeRequest: CategoriaWriteRequest = {
    nome: categoria.nome,
    descricao: categoria.descricao,
    ordemExibicao: categoria.ordemExibicao,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        CategoriasService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: { cookie: 'XSRF-TOKEN=cookie-token' } },
      ],
    });

    service = TestBed.inject(CategoriasService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('lists categories and reads one category using the real API paths', () => {
    let list: AdminCategoria[] | undefined;
    let item: AdminCategoria | undefined;
    service.listar().subscribe((value) => (list = value));
    service.obter(categoria.id).subscribe((value) => (item = value));

    const listRequest = http.expectOne('/api/admin/categorias');
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([categoria]);
    const getRequest = http.expectOne(`/api/admin/categorias/${categoria.id}`);
    expect(getRequest.request.method).toBe('GET');
    getRequest.flush(categoria);

    expect(list).toEqual([categoria]);
    expect(item).toEqual(categoria);
  });

  it('gets the shared antiforgery request token before creating a category', () => {
    let created: AdminCategoria | undefined;
    service.criar(writeRequest).subscribe((value) => (created = value));

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    http.expectNone('/api/admin/categorias');
    csrf.flush({ requestToken: 'create-request-token' });

    const request = http.expectOne('/api/admin/categorias');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(writeRequest);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('create-request-token');
    request.flush(categoria, { status: 201, statusText: 'Created' });

    expect(created).toEqual(categoria);
  });

  it('gets the shared antiforgery request token before editing a category', () => {
    service.editar(categoria.id, writeRequest).subscribe();

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'edit-request-token' });
    const request = http.expectOne(`/api/admin/categorias/${categoria.id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(writeRequest);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('edit-request-token');
    request.flush(categoria);
  });

  it('gets the shared antiforgery request token before changing category status', () => {
    service.alterarStatus(categoria.id, { ativa: false }).subscribe();

    http.expectOne('/api/auth/csrf').flush({ requestToken: 'status-request-token' });
    const request = http.expectOne(`/api/admin/categorias/${categoria.id}/status`);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ ativa: false });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('status-request-token');
    request.flush({ ...categoria, ativa: false });
  });
});
