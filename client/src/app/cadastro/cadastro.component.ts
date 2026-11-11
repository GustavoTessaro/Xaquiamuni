import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import {
  brazilianCpfValidator,
  brazilianEmailValidator,
  brazilianPhoneValidator,
  brazilianUfValidator,
  cepValidator,
  normalizedTextValidator,
  optionalNormalizedTextValidator,
  passwordsMatchValidator,
} from './registration.validators';

type AddressControls = {
  cep: FormControl<string>;
  uf: FormControl<string>;
  cidade: FormControl<string>;
  bairro: FormControl<string>;
  logradouro: FormControl<string>;
  numero: FormControl<string>;
  complemento: FormControl<string>;
};

type RegistrationControls = {
  nomeCompleto: FormControl<string>;
  cpf: FormControl<string>;
  telefone: FormControl<string>;
  email: FormControl<string>;
  senha: FormControl<string>;
  confirmacaoSenha: FormControl<string>;
  enderecoInicial: FormGroup<AddressControls>;
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  selector: 'app-cadastro',
  styleUrl: './cadastro.component.css',
  templateUrl: './cadastro.component.html',
})
export class CadastroComponent {
  protected readonly submitted = signal(false);
  protected readonly readyMessage = signal('');

  protected readonly form = new FormGroup<RegistrationControls>({
    nomeCompleto: new FormControl('', {
      nonNullable: true,
      validators: [normalizedTextValidator(150)],
    }),
    cpf: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, brazilianCpfValidator],
    }),
    telefone: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, brazilianPhoneValidator],
    }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [brazilianEmailValidator],
    }),
    senha: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)],
    }),
    confirmacaoSenha: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    enderecoInicial: new FormGroup<AddressControls>({
      cep: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, cepValidator],
      }),
      uf: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, brazilianUfValidator],
      }),
      cidade: new FormControl('', {
        nonNullable: true,
        validators: [normalizedTextValidator(100)],
      }),
      bairro: new FormControl('', {
        nonNullable: true,
        validators: [normalizedTextValidator(100)],
      }),
      logradouro: new FormControl('', {
        nonNullable: true,
        validators: [normalizedTextValidator(150)],
      }),
      numero: new FormControl('', {
        nonNullable: true,
        validators: [normalizedTextValidator(20)],
      }),
      complemento: new FormControl('', {
        nonNullable: true,
        validators: [optionalNormalizedTextValidator(100)],
      }),
    }),
  }, { validators: [passwordsMatchValidator] });

  protected get passwordsMatch(): boolean {
    return this.form.controls.senha.value === this.form.controls.confirmacaoSenha.value;
  }

  protected fieldInvalid(control: AbstractControl): boolean {
    return control.invalid && (control.touched || this.submitted());
  }

  protected submit(): void {
    this.submitted.set(true);
    this.readyMessage.set('');
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }

    this.readyMessage.set('Dados válidos e prontos para a próxima etapa. Nenhum cadastro foi enviado.');
  }

  protected maskCpf(event: Event): void {
    this.updateMaskedValue(event, digits => {
      const value = digits.slice(0, 11);
      if (value.length > 9) {
        return `${value.slice(0, 3)}.${value.slice(3, 6)}.${value.slice(6, 9)}-${value.slice(9)}`;
      }
      if (value.length > 6) {
        return `${value.slice(0, 3)}.${value.slice(3, 6)}.${value.slice(6)}`;
      }
      return value.length > 3 ? `${value.slice(0, 3)}.${value.slice(3)}` : value;
    });
  }

  protected maskPhone(event: Event): void {
    this.updateMaskedValue(event, digits => {
      let value = digits.slice(0, 13);
      const international = value.startsWith('55') && value.length > 11;
      if (international) {
        value = value.slice(2);
      }
      if (value.length <= 2) {
        return international ? `+55 (${value}` : value;
      }
      const areaCode = value.slice(0, 2);
      const number = value.slice(2);
      const formattedNumber = number.length > 4
        ? `${number.slice(0, number.length > 8 ? number.length - 4 : 4)}-${number.slice(number.length > 8 ? -4 : 4)}`
        : number;
      return `${international ? '+55 ' : ''}(${areaCode}) ${formattedNumber}`.trimEnd();
    });
  }

  protected maskCep(event: Event): void {
    this.updateMaskedValue(event, digits => {
      const value = digits.slice(0, 8);
      return value.length > 5 ? `${value.slice(0, 5)}-${value.slice(5)}` : value;
    }, true);
  }

  protected normalizeUf(event: Event): void {
    const input = event.target as HTMLInputElement;
    const control = this.form.controls.enderecoInicial.controls.uf;
    const value = input.value.toUpperCase().replace(/[^A-Z]/g, '').slice(0, 2);
    input.value = value;
    control.setValue(value);
  }

  private updateMaskedValue(
    event: Event,
    format: (digits: string) => string,
    isCep = false,
  ): void {
    const input = event.target as HTMLInputElement;
    const value = format(input.value.replace(/\D/g, ''));
    input.value = value;

    const control = isCep
      ? this.form.controls.enderecoInicial.controls.cep
      : input.name === 'cpf'
        ? this.form.controls.cpf
        : this.form.controls.telefone;
    control.setValue(value);
  }
}
