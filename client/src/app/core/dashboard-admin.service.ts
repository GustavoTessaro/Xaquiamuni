import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { PeriodoFinanceiro } from './financeiro.service';

export interface OperacaoDashboard {
  pixNoPrazo: number;
  pixVencido: number;
  aPreparar: number;
  emPreparacao: number;
  prontosParaRetirada: number;
  producoesEmAndamento: number;
  comprasRascunho: number;
}

export interface EstoqueDashboard {
  materiasPrimasEmAlerta: number;
  produtosDisponiveis: number;
}

export interface CompromissoDashboard {
  data: string;
  horarioInicial: string | null;
  titulo: string;
}

@Injectable({ providedIn: 'root' })
export class DashboardAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/admin/dashboard';
  periodo: PeriodoFinanceiro = { dataInicial: '', dataFinal: '' };

  operacao() {
    return this.http.get<OperacaoDashboard>(this.base + '/operacao');
  }

  estoque() {
    return this.http.get<EstoqueDashboard>(this.base + '/estoque');
  }

  agenda() {
    return this.http.get<CompromissoDashboard[]>(this.base + '/agenda');
  }
}
