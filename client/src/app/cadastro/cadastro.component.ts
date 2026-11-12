import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { RouterLink } from '@angular/router';
import { CadastroService, RegisterClientRequest } from './cadastro.service';
import {
  brazilianCpfValidator,
  brazilianEmailValidator,
  brazilianPhoneValidator,
  brazilianUfValidator,
  cepValidator,
  identityPasswordValidator,
  normalizedTextValidator,
  optionalNormalizedTextValidator,
  normalizeRegistrationDigits,
  passwordsMatchValidator,
  registrationPasswordRequirements,
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
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-cadastro',
  styleUrl: './cadastro.component.css',
  templateUrl: './cadastro.component.html',
})
export class CadastroComponent {
  protected readonly passwordRequirements = registrationPasswordRequirements;
  protected readonly submitted = signal(false);
  protected readonly passwordVisible = signal(false);
  protected readonly confirmationVisible = signal(false);
  protected readonly registering = signal(false);
  protected readonly successMessage = signal('');
  protected readonly errorMessage = signal('');
  protected readonly serverErrors = signal<Record<string, string>>({});

  constructor(private readonly cadastroService: CadastroService) {
    this.form.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.serverErrors.set({}));
  }

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
      validators: [Validators.required, identityPasswordValidator],
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

  protected togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  protected toggleConfirmationVisibility(): void {
    this.confirmationVisible.update((visible) => !visible);
  }

  protected submit(): void {
    if (this.registering()) {
      return;
    }

    this.submitted.set(true);
    this.successMessage.set('');
    this.errorMessage.set('');
    this.serverErrors.set({});
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }

    const formValue = this.form.getRawValue();
    const request: RegisterClientRequest = {
      nomeCompleto: formValue.nomeCompleto,
      cpf: normalizeRegistrationDigits(formValue.cpf),
      email: formValue.email,
      telefone: normalizeRegistrationDigits(formValue.telefone),
      senha: formValue.senha,
      enderecoInicial: {
        cep: normalizeRegistrationDigits(formValue.enderecoInicial.cep),
        uf: formValue.enderecoInicial.uf,
        cidade: formValue.enderecoInicial.cidade,
        bairro: formValue.enderecoInicial.bairro,
        logradouro: formValue.enderecoInicial.logradouro,
        numero: formValue.enderecoInicial.numero,
        complemento: formValue.enderecoInicial.complemento,
      },
    };

    this.registering.set(true);
    this.cadastroService.register(request)
      .pipe(finalize(() => this.registering.set(false)))
      .subscribe({
        next: () => {
          this.form.reset();
          this.passwordVisible.set(false);
          this.confirmationVisible.set(false);
          this.submitted.set(false);
          this.successMessage.set(
            'Cadastro criado. Você poderá continuar usando a conta após as próximas etapas de acesso.',
          );
        },
        error: (error: unknown) => this.handleRegistrationError(error),
      });
  }

  protected serverFieldError(field: string): string | null {
    return this.serverErrors()[field] ?? null;
  }

  private handleRegistrationError(error: unknown): void {
    if (!(error instanceof HttpErrorResponse)) {
      this.errorMessage.set('Não foi possível concluir o cadastro. Tente novamente.');
      return;
    }

    if (error.status === 400) {
      const fieldErrors = this.readFieldErrors(error.error);
      this.serverErrors.set(fieldErrors);
      this.errorMessage.set(
        Object.keys(fieldErrors).length > 0
          ? 'Revise os campos indicados e tente novamente.'
          : 'Não foi possível validar os dados. Revise o formulário e tente novamente.',
      );
      return;
    }

    if (error.status === 409) {
      this.errorMessage.set(
        'Não foi possível concluir o cadastro com os dados informados. Verifique os dados e tente novamente.',
      );
      return;
    }

    this.errorMessage.set('O serviço está indisponível no momento. Tente novamente.');
  }

  private readFieldErrors(body: unknown): Record<string, string> {
    if (typeof body !== 'object' || body === null || !('errors' in body)) {
      return {};
    }

    const errors = body.errors;
    if (typeof errors !== 'object' || errors === null) {
      return {};
    }

    const fieldMap: Record<string, string> = {
      NomeCompleto: 'nomeCompleto',
      Cpf: 'cpf',
      Email: 'email',
      Telefone: 'telefone',
      Senha: 'senha',
      'EnderecoInicial.Cep': 'cep',
      'EnderecoInicial.Uf': 'uf',
      'EnderecoInicial.Cidade': 'cidade',
      'EnderecoInicial.Bairro': 'bairro',
      'EnderecoInicial.Logradouro': 'logradouro',
      'EnderecoInicial.Numero': 'numero',
      'EnderecoInicial.Complemento': 'complemento',
    };
    const fieldErrors: Record<string, string> = {};

    for (const [serverField, controlName] of Object.entries(fieldMap)) {
      const messages = (errors as Record<string, unknown>)[serverField];
      if (Array.isArray(messages)) {
        const safeMessage = messages.find(
          (message): message is string => typeof message === 'string',
        );
        if (safeMessage) {
          fieldErrors[controlName] = safeMessage;
        }
      }
    }
    return fieldErrors;
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
