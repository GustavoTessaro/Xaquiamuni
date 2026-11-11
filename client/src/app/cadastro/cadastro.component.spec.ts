import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CadastroComponent } from './cadastro.component';

describe('CadastroComponent', () => {
  let fixture: ComponentFixture<CadastroComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CadastroComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(CadastroComponent);
    fixture.detectChanges();
  });

  it('marks required fields and shows accessible errors after submit', () => {
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#nomeCompleto[aria-invalid="true"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#nomeCompleto-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#cpf-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#telefone-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#email-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#senha-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#confirmacaoSenha-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#cep-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#uf-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#cidade-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#bairro-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#logradouro-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#numero-error[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent.trim()).toBe('');
  });

  it('does not mark form ready when password confirmation differs', () => {
    fillValidFields(fixture, { confirmPassword: 'Senha diferente' });
    submit(fixture);

    expect(fixture.nativeElement.textContent).toContain('As senhas não coincidem.');
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent.trim()).toBe('');
  });

  it('shows only local ready feedback for valid data', () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    fillValidFields(fixture);
    submit(fixture);

    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent).toContain(
      'Nenhum cadastro foi enviado.',
    );
    expect(fetchSpy).not.toHaveBeenCalled();
    fetchSpy.mockRestore();
  });
});

function fillValidFields(
  fixture: ComponentFixture<CadastroComponent>,
  options: { confirmPassword?: string } = {},
): void {
  const values: Record<string, string> = {
    nomeCompleto: 'Ana Maria Silva',
    cpf: '52998224725',
    telefone: '11912345678',
    email: 'ana@example.com',
    senha: 'Senha forte sem regras extras',
    confirmacaoSenha: options.confirmPassword ?? 'Senha forte sem regras extras',
    cep: '95000000',
    uf: 'rs',
    cidade: 'Vacaria',
    bairro: 'Centro',
    logradouro: 'Rua das Flores',
    numero: '10',
    complemento: '',
  };

  for (const [id, value] of Object.entries(values)) {
    const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<CadastroComponent>): void {
  (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).click();
  fixture.detectChanges();
}
