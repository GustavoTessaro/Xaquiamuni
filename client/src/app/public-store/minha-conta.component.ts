import { ConsultaCepComponent } from '../shared/consulta-cep.component';
import { formatarCep } from '../core/consulta-cep.service';
import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription, fromEvent } from 'rxjs';
import { PerfilClienteService, type PerfilClienteVersionado } from '../core/perfil-cliente.service';

type Estado =
  | { tipo: 'carregando' | 'oculto' | 'sessao' | 'negado' | 'erro' }
  | { tipo: 'pronto'; perfil: PerfilClienteVersionado };

const enderecoVazio = { cep: '', uf: '', cidade: '', bairro: '', logradouro: '', numero: '', complemento: '' };

@Component({
  selector: 'app-minha-conta',
  imports: [RouterLink, ReactiveFormsModule, ConsultaCepComponent],
  templateUrl: './minha-conta.component.html',
  styleUrl: './minha-conta.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MinhaContaComponent {
  protected readonly formatarCep = formatarCep;
  private readonly perfil = inject(PerfilClienteService);
  private readonly documento = inject(DOCUMENT);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly destroyRef = inject(DestroyRef);
  private consulta?: Subscription;
  private gravacao?: Subscription;

  protected readonly estado = signal<Estado>({ tipo: 'carregando' });
  protected readonly editando = signal<'perfil' | 'endereco' | null>(null);
  protected readonly salvando = signal(false);
  protected readonly conflito = signal(false);
  protected readonly aviso = signal('');
  protected readonly erro = signal('');
  protected readonly erros = signal<Record<string, string[]>>({});
  protected readonly dados = this.fb.group({ nomeCompleto: ['', Validators.required], telefone: ['', Validators.required] });
  protected readonly camposEndereco = [
    { chave: 'cep', rotulo: 'CEP', autocomplete: 'postal-code' },
    { chave: 'uf', rotulo: 'UF', autocomplete: 'address-level1' },
    { chave: 'cidade', rotulo: 'Cidade', autocomplete: 'address-level2' },
    { chave: 'bairro', rotulo: 'Bairro', autocomplete: 'address-level3' },
    { chave: 'logradouro', rotulo: 'Logradouro', autocomplete: 'address-line1' },
    { chave: 'numero', rotulo: 'Número', autocomplete: 'off' },
    { chave: 'complemento', rotulo: 'Complemento', autocomplete: 'address-line2' },
  ] as const;
  protected readonly endereco = this.fb.group({
    cep: ['', Validators.required], uf: ['', Validators.required], cidade: ['', Validators.required],
    bairro: ['', Validators.required], logradouro: ['', Validators.required], numero: ['', Validators.required],
    complemento: '',
  });

  constructor() {
    fromEvent(this.documento, 'visibilitychange').pipe(takeUntilDestroyed()).subscribe(() => this.recarregar());
    this.recarregar();
  }

  protected recarregar(): void {
    this.consulta?.unsubscribe();
    this.gravacao?.unsubscribe();
    this.salvando.set(false);
    this.cancelar();
    this.aviso.set('');
    if (this.documento.hidden) {
      this.estado.set({ tipo: 'oculto' });
      return;
    }

    this.estado.set({ tipo: 'carregando' });
    this.consulta = this.perfil.consultar().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: perfil => this.estado.set({ tipo: 'pronto', perfil }),
      error: erro => this.estado.set({ tipo: this.classificarErro(erro) }),
    });
  }

  protected editar(parte: 'perfil' | 'endereco', perfil: PerfilClienteVersionado): void {
    this.cancelar();
    this.aviso.set('');
    if (parte === 'perfil') {
      this.dados.reset({ nomeCompleto: perfil.nomeCompleto ?? '', telefone: perfil.telefone ?? '' });
    } else {
      this.endereco.reset(perfil.endereco ? { ...perfil.endereco, complemento: perfil.endereco.complemento ?? '' } : enderecoVazio);
    }
    this.editando.set(parte);
  }

  protected cancelar(): void {
    this.editando.set(null);
    this.erros.set({});
    this.erro.set('');
    this.conflito.set(false);
    this.dados.reset();
    this.endereco.reset();
  }

  protected salvar(perfil: PerfilClienteVersionado): void {
    const parte = this.editando();
    if (!parte || this.salvando() || this.conflito()) return;
    const formulario = parte === 'perfil' ? this.dados : this.endereco;
    formulario.markAllAsTouched();
    if (formulario.invalid) return;
    const versao = parte === 'perfil' ? perfil.versaoPerfil : perfil.versaoEndereco;
    if (!versao) return;

    this.erros.set({});
    this.erro.set('');
    this.salvando.set(true);
    const envio = parte === 'perfil'
      ? this.perfil.salvarPerfil(this.dados.getRawValue(), versao)
      : this.perfil.salvarEndereco(this.endereco.getRawValue(), versao);
    this.gravacao = envio.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: perfilAtual => {
        this.salvando.set(false);
        this.estado.set({ tipo: 'pronto', perfil: perfilAtual });
        this.cancelar();
        this.aviso.set('Alterações salvas.');
      },
      error: (erro: unknown) => {
        this.salvando.set(false);
        const tipo = this.classificarErro(erro);
        if (tipo !== 'erro') {
          this.cancelar();
          this.estado.set({ tipo });
          return;
        }

        if (erro instanceof HttpErrorResponse) {
          if (erro.status === 409 || erro.status === 412) {
            this.conflito.set(true);
            this.erro.set('Os dados mudaram. Recarregue os dados antes de editar novamente.');
            return;
          }
          if (erro.status === 400 && erro.error?.errors) {
            this.erros.set(erro.error.errors);
            this.erro.set('Confira os campos indicados e tente novamente.');
            return;
          }
          if (erro.status === 422) {
            this.erro.set('Os dados pessoais do cadastro estão indisponíveis.');
            return;
          }
        }
        this.erro.set('Não foi possível confirmar o salvamento. Tente novamente ou recarregue os dados.');
      },
    });
  }

  private classificarErro(erro: unknown): 'sessao' | 'negado' | 'erro' {
    return erro instanceof HttpErrorResponse && erro.status === 401 ? 'sessao'
      : erro instanceof HttpErrorResponse && erro.status === 403 ? 'negado' : 'erro';
  }
}
