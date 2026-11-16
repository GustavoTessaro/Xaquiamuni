import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminProdutoImagem, ImagensProdutoService } from './imagens-produto.service';

describe('ImagensProdutoService', () => {
  let service: ImagensProdutoService;
  let http: HttpTestingController;

  const image: AdminProdutoImagem = {
    id: 'image-1',
    produtoId: 'product-1',
    nomeOriginal: 'lavanda.png',
    tipoConteudo: 'image/webp',
    tamanhoBytes: 1200,
    larguraPixels: 800,
    alturaPixels: 600,
    ordemExibicao: 0,
    principal: true,
    varianteIds: ['variant-1'],
    criadoEmUtc: '2026-11-16T03:00:00+00:00',
    conteudoUrl: '/api/admin/produtos/product-1/imagens/image-1/conteudo',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ImagensProdutoService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: { cookie: 'XSRF-TOKEN=cookie-token' } },
      ],
    });

    service = TestBed.inject(ImagensProdutoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('lists, consults, and streams content through the administrative routes', () => {
    let listed: AdminProdutoImagem[] | undefined;
    let selected: AdminProdutoImagem | undefined;
    let content: Blob | undefined;

    service.listar(image.produtoId).subscribe((value) => (listed = value));
    service.consultar(image.produtoId, image.id).subscribe((value) => (selected = value));
    service.conteudo(image.produtoId, image.id).subscribe((value) => (content = value));

    const listRequest = http.expectOne('/api/admin/produtos/product-1/imagens');
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([image]);
    const getRequest = http.expectOne('/api/admin/produtos/product-1/imagens/image-1');
    expect(getRequest.request.method).toBe('GET');
    getRequest.flush(image);
    const contentRequest = http.expectOne('/api/admin/produtos/product-1/imagens/image-1/conteudo');
    expect(contentRequest.request.method).toBe('GET');
    expect(contentRequest.request.responseType).toBe('blob');
    contentRequest.flush(new Blob(['webp'], { type: 'image/webp' }));

    expect(listed).toEqual([image]);
    expect(selected).toEqual(image);
    expect(content?.type).toBe('image/webp');
  });

  it('sends one multipart file with repeated variantIds and leaves the boundary to the browser', () => {
    const file = new File(['image'], 'photo.png', { type: 'image/png' });
    let response: AdminProdutoImagem | undefined;
    service
      .adicionar('product-1', file, ['variant-1', 'variant-2'])
      .subscribe((value) => (response = value));

    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    csrf.flush({ requestToken: 'image-upload-token' });

    const request = http.expectOne('/api/admin/produtos/product-1/imagens');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('image-upload-token');
    expect(request.request.headers.has('Content-Type')).toBe(false);
    expect(request.request.body).toBeInstanceOf(FormData);
    const formData = request.request.body as FormData;
    expect(formData.getAll('arquivo')).toHaveLength(1);
    expect(formData.get('arquivo')).toBeInstanceOf(File);
    expect(formData.getAll('varianteIds')).toEqual(['variant-1', 'variant-2']);
    request.flush(image, { status: 201, statusText: 'Created' });
    expect(response).toEqual(image);
  });

  it('uses the existing individual mutation routes and antiforgery contract', () => {
    expectMutation(
      service.definirPrincipal('product-1', 'image-1'),
      'PUT',
      '/api/admin/produtos/product-1/imagens/image-1/principal',
      null,
    );
    expectMutation(
      service.reordenar('product-1', ['image-2', 'image-1']),
      'PUT',
      '/api/admin/produtos/product-1/imagens/ordem',
      { imagemIds: ['image-2', 'image-1'] },
    );
    expectMutation(
      service.associarVariante('product-1', 'image-1', 'variant-1'),
      'PUT',
      '/api/admin/produtos/product-1/imagens/image-1/variantes/variant-1',
      null,
    );
    expectMutation(
      service.desassociarVariante('product-1', 'image-1', 'variant-1'),
      'DELETE',
      '/api/admin/produtos/product-1/imagens/image-1/variantes/variant-1',
      null,
    );
  });

  it('distinguishes confirmed physical cleanup from a completed logical removal response', () => {
    let cleanupPending: boolean | undefined;
    service.remover('product-1', 'image-1').subscribe((response) => {
      cleanupPending = response.body?.limpezaFisicaConcluida;
    });
    const csrf = http.expectOne('/api/auth/csrf');
    csrf.flush({ requestToken: 'image-delete-token' });
    const request = http.expectOne('/api/admin/produtos/product-1/imagens/image-1');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('image-delete-token');
    request.flush(
      {
        removidaDoCatalogo: true,
        limpezaFisicaConcluida: false,
        mensagem: 'A exclusão física está pendente.',
      },
      { status: 200, statusText: 'OK' },
    );
    expect(cleanupPending).toBe(false);

    let confirmedStatus: number | undefined;
    service.remover('product-1', 'image-1').subscribe((response) => {
      confirmedStatus = response.status;
    });
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'confirmed-delete-token' });
    const confirmedRequest = http.expectOne('/api/admin/produtos/product-1/imagens/image-1');
    confirmedRequest.flush(null, { status: 204, statusText: 'No Content' });
    expect(confirmedStatus).toBe(204);
  });

  function expectMutation(
    operation: ReturnType<ImagensProdutoService['definirPrincipal']>,
    method: string,
    url: string,
    body: unknown,
  ): void {
    let response: AdminProdutoImagem[] | undefined;
    operation.subscribe((value) => (response = value));
    const csrf = http.expectOne('/api/auth/csrf');
    csrf.flush({ requestToken: 'mutation-token' });
    const request = http.expectOne(url);
    expect(request.request.method).toBe(method);
    expect(request.request.body).toEqual(body);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('mutation-token');
    request.flush([image]);
    expect(response).toEqual([image]);
  }
});
