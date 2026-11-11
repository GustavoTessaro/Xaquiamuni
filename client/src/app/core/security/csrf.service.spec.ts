import { DOCUMENT } from '@angular/common';
import {
  HttpClient,
  provideHttpClient,
  withXsrfConfiguration,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CsrfService } from './csrf.service';

describe('CsrfService', () => {
  let service: CsrfService;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        CsrfService,
        provideHttpClient(
          withXsrfConfiguration({
            cookieName: 'XSRF-TOKEN',
            headerName: 'X-XSRF-TOKEN',
          }),
        ),
        provideHttpClientTesting(),
        {
          provide: DOCUMENT,
          useValue: { cookie: 'XSRF-TOKEN=test-request-token' },
        },
      ],
    });

    service = TestBed.inject(CsrfService);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
    TestBed.resetTestingModule();
  });

  it('gets the request token from the same-origin API endpoint', () => {
    let response: unknown;
    service.getRequestToken().subscribe((value) => (response = value));

    const request = httpTestingController.expectOne('/api/auth/csrf');
    expect(request.request.method).toBe('GET');
    request.flush({ requestToken: 'test-request-token' });

    expect(response).toEqual({ requestToken: 'test-request-token' });
  });

  it('adds the native XSRF header to a relative API POST using the cookie', () => {
    const http = TestBed.inject(HttpClient);
    http.post('/api/test', {}).subscribe();

    const request = httpTestingController.expectOne('/api/test');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('test-request-token');
    request.flush({});
  });
});
