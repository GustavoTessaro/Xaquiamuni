import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ComprasMateriasPrimasService } from './compras-materias-primas.service';

it('envia recebimento com token CSRF e preserva chave e corpo', () => {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const http = TestBed.inject(HttpTestingController);
  const corpo = { versao: 'v1', validades: [{ componenteId: 'c1', dataValidade: '2026-12-01' }] };
  TestBed.inject(ComprasMateriasPrimasService).receber('compra', corpo, 'uuid').subscribe();

  http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf-teste' });
  const req = http.expectOne('/api/admin/compras-materias-primas/compra/receber');
  expect(req.request.method).toBe('POST');
  expect(req.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-teste');
  expect(req.request.headers.get('Idempotency-Key')).toBe('uuid');
  expect(req.request.body).toEqual(corpo);
  req.flush({});
  http.verify();
});
