import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type CamposCep = { uf: string; cidade: string; bairro?: string; logradouro?: string };

export function formatarCep(valor: string): string {
  const digitos = valor.replace(/[^0-9]/g, '').slice(0, 8);
  return digitos.length > 5 ? digitos.slice(0, 5) + '-' + digitos.slice(5) : digitos;
}

export const mensagensCep: Record<string, string> = {
  cep_invalido: 'Informe um CEP com oito dígitos.',
  cep_nao_encontrado: 'CEP não encontrado. Confira o número informado.',
  cep_municipio_divergente: 'O CEP precisa corresponder ao município informado e à UF.',
  endereco_incompleto: 'Complete os campos obrigatórios do endereço.',
  cep_indisponivel: 'Consulta de CEP temporariamente indisponível. Tente novamente.',
};

@Injectable({ providedIn: 'root' })
export class ConsultaCepService {
  private readonly http = inject(HttpClient);

  consultar(cep: string) {
    return this.http.get<CamposCep>('/api/cep/' + encodeURIComponent(cep));
  }
}
