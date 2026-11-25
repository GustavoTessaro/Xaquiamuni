import { mensagensCep } from './consulta-cep.service';
import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, of, switchMap, timeout } from 'rxjs';
import type { ItemCompra, PrecoAtualPedido } from '../public-store/pedidos.service';
import type { DescontoFrete, EnderecoEntrega } from './atendimento.service';
import { AuthService } from './auth/auth.service';
import { CsrfService } from './security/csrf.service';

export const estadosCotacao = {
  AguardandoAnalise: 'Aguardando cotação',
  PropostaDisponivel: 'Aguardando resposta do cliente',
  Aceita: 'Proposta aceita',
  Recusada: 'Proposta recusada',
  Expirada: 'Proposta expirada',
  Cancelada: 'Solicitação cancelada',
  ConvertidaEmPedido: 'Solicitação convertida em pedido',
};
type Estado = keyof typeof estadosCotacao;
export interface ValoresPropostaPostal {
  freteCentavos: string;
  subtotalProdutosCentavos: string | null;
  totalCentavos: string | null;
  descontoFrete: DescontoFrete | null;
}
export interface ResumoCotacao {
  valores?: ValoresPropostaPostal | null;
  id: string;
  situacao: Estado;
  criadaEmUtc: string;
  revisao: number | null;
  validaAteUtc: string | null;
}
export interface VolumePostal {
  pesoGramas: number;
  comprimentoCm: number | null;
  larguraCm: number | null;
  alturaCm: number | null;
}
interface ItemCotacao extends ItemCompra { nomeProduto: string; nomeVariante: string }
export interface ReferenciaPostal {
  solicitacaoId: string;
  revisao: number;
  servico: string;
  prazoDias: number;
  publicadaEmUtc: string;
  validaAteUtc: string;
}
export interface PropostaPostal extends Omit<ReferenciaPostal, 'solicitacaoId'> {
  valores?: ValoresPropostaPostal | null;
  freteCentavos: string;
  pesoTotalGramas: number | null;
  volumes: VolumePostal[];
}
export interface DetalheCotacao {
  resumo: ResumoCotacao;
  itens: ItemCotacao[];
  endereco: EnderecoEntrega;
  propostas: PropostaPostal[];
  revisaoAceita: number | null;
  pedidoConvertidoId: string | null;
}
export interface RevisaoPostal {
  descontoFrete?: DescontoFrete | null;
  proposta: ReferenciaPostal;
  endereco: EnderecoEntrega;
  itens: (ItemCotacao & { disponivel: boolean; preco: PrecoAtualPedido | null })[];
  freteCentavos: string;
  subtotalProdutosCentavos: string | null;
  totalCentavos: string | null;
  conversaoCorreiosId: string | null;
}
export interface PaginaCotacoes { itens: ResumoCotacao[]; totalItens: number; pagina: number; itensPorPagina: number }
export interface SolicitacaoPostal {
  itens: Pick<ItemCompra, 'produtoId' | 'produtoVarianteId' | 'quantidade'>[];
  endereco: EnderecoEntrega;
}

@Injectable({ providedIn: 'root' })
export class CotacoesCorreiosService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly janela = inject(DOCUMENT).defaultView;
  private readonly auth = inject(AuthService);

  consultarEnderecoPadrao() {
    return this.http.get<{ endereco: EnderecoEntrega | null }>('/api/clientes/me/endereco-padrao')
      .pipe(timeout(15_000));
  }

  private rota(admin: boolean) { return admin ? '/api/admin/cotacoes-correios' : '/api/cotacoes-correios'; }

  listar(admin: boolean, pagina: number, situacao: string) {
    return this.http.get<PaginaCotacoes>(this.rota(admin), {
      params: { pagina, ...(situacao && { situacao }) },
    }).pipe(timeout(30_000));
  }

  detalhe(admin: boolean, id: string) {
    return this.http.get<DetalheCotacao>(this.rota(admin) + '/' + encodeURIComponent(id), {
      observe: 'response',
    }).pipe(timeout(30_000));
  }

  async prepararSolicitacao(corpo: SolicitacaoPostal) {
    const { requestToken } = await firstValueFrom(this.csrf.getRequestToken().pipe(timeout(15_000)));
    const sessao = await firstValueFrom(this.auth.obterSessao().pipe(timeout(15_000)));
    if (!sessao) return null;
    const janela = this.janela;
    if (!janela) return null;
    const hash = await janela.crypto.subtle.digest('SHA-256',
      new TextEncoder().encode(JSON.stringify([sessao.userId, corpo])));
    const registro = 'xaquiamuni:solicitacao-correios:' +
      Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('');
    let chave = janela.crypto.randomUUID() as string;
    try {
      const anterior = janela.sessionStorage.getItem(registro);
      if (anterior && /^[0-9a-f-]{36}$/i.test(anterior)) chave = anterior;
      janela.sessionStorage.setItem(registro, chave);
    } catch {
      // A recuperação continua possível na página enquanto a tentativa estiver em memória.
    }
    return { conta: sessao.userId, registro, chave, requestToken };
  }

  concluirSolicitacao(registro: string) {
    try {
      this.janela?.sessionStorage.removeItem(registro);
    } catch {
      // A chave original permanece segura para replay.
    }
  }

  solicitar(corpo: SolicitacaoPostal, chave: string, requestToken?: string) {
    return this.post<ResumoCotacao>(this.rota(false), corpo, { 'Idempotency-Key': chave }, requestToken);
  }

  preverProposta(id: string, corpo: object, versao: string) {
    return this.post<ResumoCotacao>(this.rota(true) + '/' + encodeURIComponent(id) + '/propostas/previa',
      corpo, { 'If-Match': versao });
  }

  operar(admin: boolean, id: string, acao: string, corpo: object, versao: string, chave: string) {
    return this.post<ResumoCotacao>(this.rota(admin) + '/' + encodeURIComponent(id) + '/' + acao,
      corpo, { 'If-Match': versao, 'Idempotency-Key': chave });
  }

  revisar(id: string) {
    return this.post<RevisaoPostal>('/api/checkout/cotacoes-correios/' + encodeURIComponent(id) + '/revisar', null, {});
  }

  private post<T>(rota: string, corpo: object | null, headers: Record<string, string>, token?: string) {
    return (token ? of({ requestToken: token }) : this.csrf.getRequestToken()).pipe(
      switchMap(({ requestToken }) => this.http.post<T>(rota, corpo, {
        headers: { ...headers, 'X-XSRF-TOKEN': requestToken },
      })),
      timeout(30_000),
    );
  }
}

export function erroPostal(erro: unknown) {
  const falha = erro as { status?: number; error?: { codigo?: string } };
  const mensagens: Record<string, string> = {
    cotacao_manual_desabilitada: 'A loja não está recebendo solicitações dos Correios.',
    solicitacao_duplicada: 'Já existe uma solicitação em aberto com os mesmos itens e destino. Consulte Minhas cotações.',
    ...mensagensCep,
    destino_nao_validado: 'Confira o endereço. O CEP precisa corresponder ao município informado.',
    proposta_invalida: 'Confira serviço, frete, prazo, validade e peso da proposta.',
    revisao_desatualizada: 'A versão da proposta mudou. Confira a revisão atual antes de decidir.',
    cotacao_correios_expirada: 'A proposta expirou. Solicite uma nova cotação.',
    cotacao_correios_convertida: 'Esta cotação já foi convertida. Consulte Meus pedidos.',
    itens_indisponiveis: 'Há itens sem estoque suficiente. As quantidades da cotação foram preservadas.',
    preco_alterado: 'Os preços mudaram. Confira os valores atualizados antes de confirmar novamente.',
  };
  return mensagens[falha.error?.codigo ?? ''] ??
    (falha.status === 409 ? 'A cotação mudou ou a ação deixou de estar disponível. Confira a situação atual.' :
      falha.status === 404 ? 'Cotação indisponível.' :
      falha.status === 429 ? 'Limite de solicitações atingido. Aguarde antes de tentar novamente.' :
      falha.status === 400 || falha.status === 422 ? 'Confira os dados informados e tente novamente.' :
      'Não foi possível concluir a solicitação. Tente novamente.');
}

