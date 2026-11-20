import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AgendaService, Compromisso, rotaAgenda, TentativaAgenda } from '../../core/agenda.service';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { AdminAgendaComponent } from './admin-agenda.component';
import { mesAgenda } from './calendario-agenda';

const id = '11111111-1111-4111-8111-111111111111';
const chave = '22222222-2222-4222-8222-222222222222';
const versao = '33333333-3333-4333-8333-333333333333';
const storage = 'xaquiamuni:agenda:v1:admin';
const dados = {
  titulo: 'Feira',
  descricao: 'Levar material',
  categoria: 'Eventos',
  data: '2026-12-16',
  horarioInicial: null,
  horarioFinal: null,
};
const compromisso: Compromisso = {
  ...dados,
  id,
  versao,
  criadoPorId: id,
  criadoEmUtc: '2026-12-15T10:00:00Z',
  atualizadoPorId: id,
  atualizadoEmUtc: '2026-12-15T10:00:00Z',
  excluidoPorId: null,
  excluidoEmUtc: null,
};
const comandos: TentativaAgenda[] = [
  { operacao: 'criar', metodo: 'POST', url: rotaAgenda, corpo: { ...dados, versao: null }, chave },
  {
    operacao: 'editar',
    metodo: 'PUT',
    url: rotaAgenda + '/' + id,
    id,
    corpo: { ...dados, versao },
    chave,
  },
  {
    operacao: 'excluir',
    metodo: 'DELETE',
    url: rotaAgenda + '/' + id + '?versao=' + versao,
    id,
    versao,
    corpo: null,
    chave,
  },
];

describe('AdminAgendaComponent', () => {
  const api = { listar: vi.fn(), obter: vi.fn(), tentativa: vi.fn(), enviar: vi.fn() };
  const sessao = vi.fn();
  let fixture: ComponentFixture<AdminAgendaComponent>;
  beforeEach(() => {
    vi.resetAllMocks();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 11, 16, 23, 45));
    sessionStorage.removeItem(storage);
    sessao.mockReturnValue(of({ userId: 'admin' }));
    api.listar.mockReturnValue(of([compromisso]));
    api.obter.mockReturnValue(of(compromisso));
    api.tentativa.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    api.enviar.mockReturnValue(of(compromisso));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AgendaService, useValue: api },
        { provide: AdminAuthService, useValue: { getCurrentSession: sessao } },
      ],
    });
  });
  afterEach(() => {
    sessionStorage.removeItem(storage);
    vi.useRealTimers();
  });
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function render() {
    fixture = TestBed.createComponent(AdminAgendaComponent);
    await atualizar();
  }
  function texto() {
    return fixture.nativeElement.textContent as string;
  }
  function botao(nome: string) {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === nome,
    )!;
  }
  async function clicar(nome: string) {
    expect(botao(nome)).toBeDefined();
    botao(nome).click();
    await atualizar();
  }
  async function campo(nome: string, valor: string) {
    const campo = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '[name="' + nome + '"]',
    )!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  }

  it('consulta o mês civil, marca compromissos e seleciona dias', async () => {
    await render();
    expect(api.listar).toHaveBeenCalledWith('2026-12-01', '2026-12-31');
    expect(texto()).toContain('Dia inteiro');
    const marcador = fixture.nativeElement.querySelector(
      '[aria-label="16/12/2026, 1 compromisso"]',
    );
    expect(marcador.querySelector('.marcador')).not.toBeNull();
    await clicar('17');
    expect(texto()).toContain('Compromissos de 17/12/2026');
    expect(texto()).toContain('Nenhum compromisso');
    await clicar('Próximo mês');
    expect(api.listar).toHaveBeenLastCalledWith('2027-01-01', '2027-01-31');
    await clicar('Mês anterior');
    expect(api.listar).toHaveBeenLastCalledWith('2026-12-01', '2026-12-31');
    await clicar('Hoje');
    expect(texto()).toContain('Compromissos de 16/12/2026');
  });

  it.each([false, true])('cadastra compromisso com horário? %s', async (horario) => {
    await render();
    await clicar('Novo compromisso');
    await campo('titulo', 'Levar material');
    if (horario) {
      await campo('inicio', '08:30');
      await campo('fim', '09:00');
    }
    await clicar('Salvar compromisso');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({
        metodo: 'POST',
        url: rotaAgenda,
        chave: expect.stringMatching(/^[a-f0-9-]{36}$/),
        corpo: expect.objectContaining({
          data: dados.data,
          versao: null,
          horarioInicial: horario ? '08:30:00' : null,
          horarioFinal: horario ? '09:00:00' : null,
        }),
      }),
    );
    expect(api.listar).toHaveBeenCalledTimes(2);
    expect(sessionStorage.getItem(storage)).toBeNull();
  });

  it('rejeita horário final sem início ou anterior ao inicial', async () => {
    await render();
    await clicar('Novo compromisso');
    await campo('titulo', 'Feira');
    await campo('fim', '08:00');
    await clicar('Salvar compromisso');
    expect(texto()).toContain('horário final posterior');
    await campo('inicio', '09:00');
    await clicar('Salvar compromisso');
    expect(api.enviar).not.toHaveBeenCalled();
  });

  it('usa a versão consultada, bloqueia conflito e permite recarregar os dados', async () => {
    api.enviar.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    await render();
    await clicar('Editar');
    await campo('titulo', 'Feira atualizada');
    await clicar('Salvar compromisso');
    expect(api.obter).toHaveBeenCalledWith(id);
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({
        metodo: 'PUT',
        corpo: expect.objectContaining({ versao, titulo: 'Feira atualizada' }),
      }),
    );
    expect(texto()).toContain('entrou em conflito');
    expect(botao('Salvar compromisso').matches(':disabled')).toBe(true);
    await clicar('Recarregar compromisso');
    expect(api.obter).toHaveBeenCalledTimes(2);
  });

  it('só exclui após confirmação e observa a versão na URL', async () => {
    await render();
    await clicar('Excluir');
    expect(api.enviar).not.toHaveBeenCalled();
    expect(texto()).toContain('Confirma a exclusão');
    api.listar.mockReturnValue(of([]));
    await clicar('Confirmar exclusão');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({
        metodo: 'DELETE',
        url: rotaAgenda + '/' + id + '?versao=' + versao,
        versao,
        corpo: null,
      }),
    );
    expect(texto()).toContain('Compromisso excluído');
    expect(texto()).toContain('Nenhum compromisso');
  });

  it.each(comandos)('recupera $operacao sem escrita automática', async (comando) => {
    sessionStorage.setItem(storage, JSON.stringify(comando));
    api.tentativa.mockReturnValue(
      of({ operacao: comando.operacao, recursoId: id, resultado: compromisso }),
    );
    await render();
    expect(api.tentativa).toHaveBeenCalledWith(chave);
    expect(api.enviar).not.toHaveBeenCalled();
    expect(api.listar).toHaveBeenCalledWith('2026-12-01', '2026-12-31');
    expect(sessionStorage.getItem(storage)).toBeNull();
  });

  it('trava duplo clique e reenvia DELETE com chave, URL e versão idênticas após recarga', async () => {
    const resposta = new Subject<Compromisso>();
    api.enviar.mockReturnValueOnce(resposta);
    await render();
    await clicar('Excluir');
    botao('Confirmar exclusão').click();
    botao('Confirmar exclusão').click();
    expect(api.enviar).toHaveBeenCalledTimes(1);
    expect(texto()).toContain('Feira');
    resposta.error(new HttpErrorResponse({ status: 0 }));
    const tentativa = api.enviar.mock.calls[0][0];
    fixture.destroy();
    await render();
    expect(api.tentativa).toHaveBeenCalledWith(tentativa.chave);
    expect(api.enviar).toHaveBeenCalledTimes(1);
    await clicar('Reenviar tentativa');
    expect(api.enviar).toHaveBeenLastCalledWith(tentativa);
  });

  it('mantém a tentativa e bloqueia reenvio se a consulta falhar', async () => {
    sessionStorage.setItem(storage, JSON.stringify(comandos[0]));
    api.tentativa.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    await render();
    expect(botao('Reenviar tentativa').disabled).toBe(true);
    expect(texto()).toContain('Serviço indisponível');
    expect(sessionStorage.getItem(storage)).not.toBeNull();
  });

  it('bloqueia payload perdido e ignora a tentativa de outra conta', async () => {
    sessionStorage.setItem(storage, JSON.stringify(comandos[0]));
    await render();
    sessionStorage.removeItem(storage);
    await clicar('Reenviar tentativa');
    expect(api.enviar).not.toHaveBeenCalled();
    expect(texto()).toContain('reenvio foi bloqueado');
    fixture.destroy();
    sessionStorage.setItem(storage, JSON.stringify(comandos[0]));
    sessao.mockReturnValue(of({ userId: 'outro-admin' }));
    await render();
    expect(api.tentativa).toHaveBeenCalledTimes(1);
  });

  it('redireciona sem sessão e não consulta agenda', async () => {
    sessao.mockReturnValue(of(null));
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await render();
    expect(navegar).toHaveBeenCalledWith('/admin/login');
    expect(api.listar).not.toHaveBeenCalled();
  });
});

describe('calendário civil', () => {
  it('calcula meses de diferentes durações, fevereiro bissexto e semana iniciada na segunda', () => {
    expect(mesAgenda('2024-02-15')?.fim).toBe('2024-02-29');
    expect(mesAgenda('2026-02-15')?.fim).toBe('2026-02-28');
    expect(mesAgenda('2026-11-15')?.fim).toBe('2026-11-30');
    expect(mesAgenda('2026-12-15')?.fim).toBe('2026-12-31');
    expect(mesAgenda('2026-06-15')?.espacos).toHaveLength(0);
    expect(mesAgenda('2026-11-15')?.espacos).toHaveLength(6);
    expect(mesAgenda('2027-01-01', -1)?.inicio).toBe('2026-12-01');
  });
});
