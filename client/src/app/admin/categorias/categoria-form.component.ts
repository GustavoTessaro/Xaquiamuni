import { ChangeDetectionStrategy, Component, OnInit, input, output, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { AdminCategoria, CategoriaWriteRequest } from '../../core/admin-categorias/categorias.service';

type CategoriaFormControls = {
  nome: FormControl<string>;
  descricao: FormControl<string>;
  ordemExibicao: FormControl<number | null>;
};

function nonWhitespace(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { whitespace: true };
}

function integerValue(control: AbstractControl<number | null>): ValidationErrors | null {
  return control.value === null || Number.isInteger(control.value) ? null : { integer: true };
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  selector: 'app-categoria-form',
  styleUrl: './categoria-form.component.css',
  templateUrl: './categoria-form.component.html',
})
export class CategoriaFormComponent implements OnInit {
  readonly categoria = input<AdminCategoria | null>(null);
  readonly saving = input(false);
  readonly errorMessage = input('');
  readonly save = output<CategoriaWriteRequest>();
  readonly cancel = output<void>();
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup<CategoriaFormControls>({
    nome: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespace, Validators.maxLength(100)],
    }),
    descricao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
    ordemExibicao: new FormControl<number | null>(0, {
      validators: [Validators.required, Validators.min(0), integerValue],
    }),
  });

  ngOnInit(): void {
    const categoria = this.categoria();
    if (categoria) {
      this.form.setValue({
        nome: categoria.nome,
        descricao: categoria.descricao ?? '',
        ordemExibicao: categoria.ordemExibicao,
      });
    }
  }

  protected get heading(): string {
    return this.categoria() ? 'Editar categoria' : 'Nova categoria';
  }

  protected fieldError(field: keyof CategoriaFormControls): string {
    const control = this.form.controls[field];
    if (!control.invalid || (!control.touched && !this.submitted())) {
      return '';
    }

    if (field === 'nome') {
      if (control.hasError('required') || control.hasError('whitespace')) {
        return 'Informe o nome da categoria.';
      }
      if (control.hasError('maxlength')) {
        return 'O nome deve ter no máximo 100 caracteres.';
      }
    }

    if (field === 'descricao' && control.hasError('maxlength')) {
      return 'A descrição deve ter no máximo 500 caracteres.';
    }

    if (field === 'ordemExibicao') {
      if (control.hasError('required')) {
        return 'Informe a ordem de exibição.';
      }
      if (control.hasError('min')) {
        return 'A ordem não pode ser negativa.';
      }
      if (control.hasError('integer')) {
        return 'A ordem deve ser um número inteiro.';
      }
    }

    return 'Revise este campo.';
  }

  protected submit(): void {
    if (this.saving()) {
      return;
    }

    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }

    const { nome, descricao, ordemExibicao } = this.form.getRawValue();
    if (ordemExibicao === null) {
      return;
    }

    this.save.emit({
      nome,
      descricao,
      ordemExibicao,
    });
  }
}
