# PRD — Xaquiamuni Produtos Naturais

**Plataforma web de comércio eletrônico e gestão de produção artesanal**

| Campo | Informação |
| --- | --- |
| Projeto | Xaquiamuni — E-commerce e Gestão Artesanal |
| Versão do documento | 0.2 — revisão do levantamento e decisões comerciais |
| Data | 11/11/2026 |
| Status | **Revisado; regras operacionais propostas aguardam validação e integrações aguardam viabilidade** |
| Contexto | Trabalho de Conclusão de Curso — Academia do Programador |
| Fonte principal | Entrevista com a proprietária (53 respostas, cinco esclarecimentos complementares e rodada posterior de decisões sobre cadastro, entregas, estoque e banco), logomarca e capturas do Instagram fornecidas pelo solicitante |
| Escopo | Primeira entrega demonstrável e planejamento de evolução, com limites e pendências explícitos |

> **Regra de leitura:** “Confirmado” significa declarado pela proprietária ou pelo solicitante; “Proposto” significa sugestão de produto ainda sujeita a aprovação; “Pendente” exige decisão antes da implementação do fluxo afetado. Critérios de aceite descrevem testes futuros, não funcionalidades já implementadas.

## 1. Resumo executivo

Desenvolver uma plataforma web responsiva para a **Xaquiamuni Produtos Naturais**, empreendimento de Vacaria–RS que fabrica sabonetes e velas artesanais desde 2021. A solução terá uma loja virtual para clientes comprarem **somente produtos disponíveis** e um painel administrativo para organizar catálogo, matérias-primas, fichas técnicas, ordens de produção, estoque, pedidos e operações do negócio.

O diferencial do produto será conectar a venda online à **organização da produção artesanal**, apontada pela proprietária como a principal necessidade. A solução deverá apoiar o planejamento de lotes, a reserva de matérias-primas, o cálculo da capacidade de produção e a atualização consistente de produtos acabados.

Pagamentos integrados, entregas, financeiro, análises e agenda compõem a visão de produto, mas sua abrangência na entrega do TCC depende de decisões operacionais e da viabilidade das integrações. Não presumir automações ou serviços externos já contratados.

## 2. Contexto e problema

### 2.1 Negócio observado

- **Confirmado:** marca Xaquiamuni Produtos Naturais, em atividade desde 2021; fabricação própria de sabonetes artesanais, incluindo sabonetes energizados, e velas artesanais. O *Sabonete para Descarrego* foi apontado como o produto mais vendido.
- **Confirmado:** vendas atualmente por Instagram, Facebook, WhatsApp, presencialmente e em feiras; atendimento principalmente por WhatsApp.
- **Confirmado:** controle de entradas e saídas pelo aplicativo **Trienta**, complementado por anotações; existe catálogo de produtos, valores e descrições nesse aplicativo. A plataforma Xaquiamuni deverá substituí-lo integralmente quando estiver pronta.
- **Confirmado:** produtos prontos e produção sob encomenda; prazo médio declarado de aproximadamente duas semanas, dependente de materiais e quantidade. Entretanto, **o site não deve aceitar compra sem estoque pronto disponível**.
- **Confirmado:** a proprietária conhece o consumo de matérias-primas por produto e deseja apurar capacidade produtiva, custos e margens.
- **Confirmado:** utiliza principalmente celular e há outra pessoa que ajudará na operação com acesso limitado.
- **Confirmado:** envios pelos Correios para RS e SC, entregas próprias na região de Vacaria e retirada presencial para clientes de Vacaria.

### 2.2 Problema a resolver

O planejamento de produção, os pedidos e os controles operacionais estão distribuídos entre aplicativo e anotações. A proprietária indicou **organização de produções** como a melhoria mais importante. A hipótese de produto é que centralizar fichas técnicas, reservas de insumos, ordens de produção e estoque de produtos acabados reduza esquecimentos, conflitos de materiais e retrabalho. **Esses benefícios ainda não foram medidos.**

### 2.3 Decisões confirmadas

| Tema | Decisão |
| --- | --- |
| Compra no site | Apenas unidades prontas e disponíveis; sem venda online de produto esgotado |
| Produção | Reservar matérias-primas já no planejamento |
| Equipe | Funcionários com acesso limitado às tarefas autorizadas |
| Canais registrados automaticamente | Pedidos do site; vendas de outros canais serão lançadas manualmente no painel, sem integração automática com esses canais |
| Dados existentes | Catálogo de produtos, preços e descrições no Trienta; substituição total planejada, sem importação automática confirmada |
| Dispositivo prioritário | Celular |
| Produtos | Fabricação própria; sabonetes e velas; sem personalização de pedidos na operação atual |
| Kits | Interesse futuro, não operação atual |
| Pagamento | Pix, dinheiro e cartão aceitos atualmente; desejo de Pix online com confirmação automática |
| Frete | Correios para regiões atendidas; entrega própria em Vacaria por R$ 10 e retirada local sem frete, com abrangência exata a confirmar |
| Cadastro do cliente | Conta obrigatória antes da compra, com nome completo, CPF, e-mail, telefone e endereço completo com CEP |
| Banco PJ | Sicredi; disponibilidade de API Pix e condições contratuais não verificadas |
| Rastreabilidade | Variações dos produtos podem exigir fichas técnicas/estoques distintos; registrar fabricação e validade por lote |
| Precificação | Considerar matérias-primas, embalagem, mão de obra, frete, taxas e despesas, distinguindo margem e lucro |

## 3. Objetivos e indicadores de sucesso

| Objetivo | Como verificar | Meta da primeira entrega |
| --- | --- | --- |
| Organizar produção | Criar, acompanhar, concluir e cancelar ordens de produção em cenários de teste | Fluxos de aceite aprovados; melhoria real a medir em piloto |
| Evitar uso duplicado de insumos | Tentar reservar o mesmo saldo em duas ordens simultâneas | Nenhuma reserva acima do saldo disponível |
| Conhecer capacidade produtiva | Comparar cálculo de unidades possíveis com ficha técnica e saldo livre | Resultado correto nos cenários definidos |
| Evitar venda de item indisponível | Simular duas compras concorrentes da última unidade | Nunca confirmar duas vendas da mesma unidade |
| Simplificar a compra | Percorrer catálogo, carrinho, identificação e pedido no celular | Jornada demonstrável; usabilidade a validar com clientes |
| Proteger operação | Exercitar permissões de administrador, funcionário e cliente | Nenhum acesso indevido nos cenários de aceite |
| Preservar dados financeiros | Recalcular totais históricos com valores capturados no pedido | Concordância nos cenários de aceite |

Não há baseline de tempo economizado, faturamento, volume de pedidos, disponibilidade ou desempenho. Não inventar metas quantitativas antes de medir.

## 4. Público-alvo e stakeholders

- **Cliente:** pessoa que consulta produtos e realiza compras online; deve acessar somente seus próprios pedidos e dados.
- **Administrador (proprietária):** gerencia produtos, preços, estoque, produção, equipe, pedidos, configurações e indicadores.
- **Funcionário:** executa exclusivamente operações autorizadas pelo administrador; permissões específicas ainda serão definidas.
- **Proprietária:** principal validadora das regras de fabricação, custos e rotina operacional; concordou em participar dos testes.
- **Solicitante/desenvolvedor:** responsável pelo TCC e pela condução do projeto, documentação e implementação.
- **Professor/banca:** avaliam o projeto acadêmico; critérios formais específicos não foram fornecidos.

## 5. Escopo e prioridades

### 5.1 Núcleo prioritário da primeira entrega — proposta para validação

1. **Contas e acesso:** autenticação de cliente, administrador e funcionário; proteção das operações por perfil/permissão.
2. **Catálogo e vitrine:** cadastro administrativo de produtos, categorias, preços, fotos, situação de publicação e consulta pública de itens disponíveis.
3. **Carrinho e pedidos:** seleção de quantidades, validação de disponibilidade, conta de cliente obrigatória, endereço ou modalidade de entrega e criação de pedido.
4. **Estoque de produtos acabados:** entradas, saídas, ajustes auditáveis, lotes com fabricação/validade e disponibilidade real para venda online.
5. **Estoque de matérias-primas:** unidades coerentes, entradas, ajustes, mínimo cadastrado, saldo físico e saldo reservado.
6. **Fichas técnicas:** vínculo entre produto e quantidades necessárias de cada matéria-prima.
7. **Ordens de produção:** planejamento, reserva de insumos, acompanhamento, conclusão com entrada de produtos acabados e cancelamento com liberação de reservas.
8. **Painel operacional mobile-first:** visão de pedidos, produção pendente, materiais em falta e produtos críticos.
9. **Registro de vendas externas:** lançamento manual de vendas de feira, WhatsApp, Instagram e presencial, com movimentação de estoque e registro financeiro correspondente, sem criar pedido online fictício.

Esses itens formam o **escopo-alvo proposto**, não uma garantia de implementação completa no prazo acadêmico. O recorte mínimo para apresentação será validado após definição de regras pendentes e planejamento técnico.

### 5.2 Operação comercial e gestão — proposta condicionada

- **Pix integrado:** geração de cobrança e confirmação automática por provedor, após escolha de fornecedor, custos e contrato.
- **Frete e entregas:** cálculo de Correios, entrega local e retirada, conforme regras de preço, abrangência e prazos a confirmar.
- **Financeiro:** registro de receitas provenientes de pedidos online e vendas externas lançadas manualmente, despesas, custos e visão de resultado estimado.
- **Análise de estoque:** mais vendidos, consumo de insumos, cobertura, alertas e sugestões de mínimo; sugestões não alteram estoque automaticamente.
- **Fornecedores:** cadastro e histórico básico de compras de materiais.
- **Agenda e notas:** compromissos, prazos de produção/entrega, lembretes e anotações.
- **Notificações:** avisos de novos pedidos e prazos, dependendo do canal escolhido.
- **Histórico de compras do cliente:** consulta das próprias compras.

A inclusão de cada recurso no TCC será decidida explicitamente. Não tratá-los como implementados nem obrigatórios por simples menção neste documento.

### 5.3 Fora do escopo inicial ou não autorizado

- Marketplace com múltiplos vendedores, comissões ou lojas independentes.
- Venda de produto esgotado, pré-venda e encomenda paga diretamente pelo site sem unidade pronta.
- Personalização livre de produtos pelo cliente.
- Integração automática com Trienta, Instagram, Facebook ou WhatsApp.
- Sincronização automática com vendas de canais externos; lançamentos manuais serão registrados como **vendas externas**, distintos dos pedidos online.
- Kits/combos, cupons sofisticados, programa de fidelidade e recomendações automatizadas avançadas, até aprovação específica.
- Emissão fiscal automática, cálculo tributário ou comprovação de conformidade regulatória de cosméticos.
- Integração com transportadoras além dos Correios sem decisão posterior.

## 6. Jornadas dos usuários

### J-01 — Preparar a operação

Administrador autentica-se, cadastra produtos e preços, matérias-primas, unidades, saldos e fichas técnicas. Cadastra funcionário e atribui permissões autorizadas. Dados inválidos não são gravados; ausência de cadastros é apresentada claramente.

### J-02 — Planejar produção

Administrador ou funcionário autorizado escolhe produto e quantidade. O sistema calcula necessidades da ficha técnica, compara com saldos **não reservados**, informa faltas e permite criar uma ordem somente conforme regra de insuficiência aprovada. Uma ordem efetivamente planejada reserva materiais de modo consistente.

### J-03 — Executar e concluir produção

Usuário autorizado inicia a ordem, registra quantidades realmente produzidas e eventuais divergências conforme regra futura. A conclusão consome as reservas aplicáveis e adiciona as unidades concluídas ao estoque de produtos acabados em operação consistente. Cancelamento libera reservas não consumidas.

### J-04 — Comprar produto disponível

Cliente navega pelo catálogo, escolhe quantidades, autentica-se ou cria conta, informa endereço e modalidade de recebimento, revisa preço/frete e envia pedido. O sistema revalida disponibilidade no servidor. O pagamento e a confirmação do pedido dependem da política comercial e integração definidas na seção 11.

### J-05 — Atender pedido

Administrador ou funcionário autorizado consulta pedidos, identifica o estado de pagamento e executa separação, envio/entrega ou retirada. Histórico mantém valores, quantidades e eventos do pedido. Falha ou cancelamento não pode liberar ou descontar unidades duas vezes.

### J-06 — Registrar venda externa

Administrador ou funcionário autorizado registra manualmente produto, quantidade, canal e valores da venda fora do site. A operação movimenta estoque e registra a receita correspondente uma única vez, sem criar um pedido online fictício. Se houver divergência, a correção deve preservar auditoria.

### J-07 — Consultar gestão

Administrador consulta saldos físicos e disponíveis, materiais reservados, ordens em andamento e pedidos online. Indicadores avançados e financeiro dependem do escopo aprovado.

## 7. Requisitos funcionais

**Convenção:** os RF abaixo descrevem o escopo-alvo da seção 5.1; os RF condicionados estão identificados separadamente. Detalhes não confirmados são explicitados como pendências e não devem ser decididos silenciosamente na implementação.

### 7.1 Identidade e permissões

- **RF-01 — Autenticar usuários:** permitir acesso de administrador, funcionário e cliente; exigir autenticação para operações privadas e encerrar sessão com segurança.
- **RF-02 — Proteger acesso:** cliente consulta apenas seus dados e pedidos; funcionário acessa somente operações autorizadas; administrador configura permissões. A matriz fina de permissões está pendente.
- **RF-03 — Cadastrar cliente:** exigir conta antes da compra, com nome completo, CPF, e-mail, telefone e endereço completo (CEP, UF, cidade, bairro, logradouro, número e complemento opcional); validar formato/dígitos do CPF, proteger os dados e prever recuperação segura de acesso. A necessidade de dados adicionais pelo provedor/logística deve ser reavaliada.

### 7.2 Catálogo e estoque

- **RF-04 — Gerir produtos:** cadastrar, editar, listar e despublicar produtos com nome, descrição, categoria, preço, fotos e tipo de produção; permitir variantes que alterem estoque, ficha técnica e rastreabilidade, com identidade própria quando necessário.
- **RF-05 — Consultar vitrine:** exibir produtos publicados e disponíveis, preço e quantidade vendável quando adotada essa apresentação; produtos sem saldo não podem ser comprados.
- **RF-06 — Gerir matérias-primas:** cadastrar nome, unidade física, saldo e mínimo; registrar entradas e ajustes com motivo, preservando histórico.
- **RF-07 — Gerir estoque acabado:** registrar quantidades físicas por lote, fabricação, validade, reservas para pedidos e movimentações de entrada, saída e ajuste; impedir saldo disponível negativo e venda de lote vencido.
- **RF-08 — Controlar disponibilidade:** calcular saldo disponível como saldo físico menos reservas válidas; validar concorrência em compras e reservas.

### 7.3 Produção artesanal

- **RF-09 — Gerir fichas técnicas:** associar a cada produto matérias-primas e quantidades por unidade ou lote, com unidades compatíveis; permitir revisão sem alterar retroativamente ordens já planejadas.
- **RF-10 — Calcular necessidade e capacidade:** calcular consumo previsto para uma quantidade de produtos e a quantidade fabricável limitada pelo insumo mais restritivo, considerando reservas existentes.
- **RF-11 — Planejar ordem:** registrar produto, quantidade, prazo, ficha técnica aplicada e insumos requeridos; reservar materiais no planejamento sem permitir dupla reserva.
- **RF-12 — Acompanhar ordem:** exibir estados e histórico; transições e regras de edição devem ser aprovadas antes da implementação.
- **RF-13 — Concluir produção:** registrar quantidade concluída, consumir materiais conforme política aprovada e acrescentar unidades acabadas; evitar conclusão duplicada e atualizações parciais.
- **RF-14 — Cancelar produção:** liberar materiais ainda reservados; tratamento de ordens já iniciadas e perdas permanece pendente.

### 7.4 Compra e pedidos

- **RF-15 — Gerir carrinho:** adicionar/remover itens, ajustar quantidades e recalcular subtotal, sem garantir estoque apenas pela presença no carrinho.
- **RF-16 — Criar pedido:** validar dados de cliente, preço, disponibilidade, endereço/modalidade de entrega e total no servidor; registrar valores e itens históricos.
- **RF-17 — Controlar estoque do pedido:** reservar ou baixar unidades de modo consistente conforme estado do pedido e política de expiração/cancelamento a definir; impedir venda concorrente da última unidade.
- **RF-18 — Acompanhar pedidos:** permitir consulta de pedidos próprios pelo cliente e gestão por usuários autorizados; distinguir estados de pedido, pagamento e entrega.
- **RF-19 — Registrar andamento:** permitir atualizar preparação, envio/entrega e rastreamento quando aplicável, preservando autoria e histórico.
- **RF-26 — Registrar vendas externas:** permitir lançamento manual de vendas de outros canais com itens, quantidades, valores, forma de pagamento e origem; atualizar estoque e financeiro de modo consistente, com auditoria e proteção contra duplicidade.

### 7.5 Recursos condicionados

- **RF-20 — Integrar Pix (condicionado):** gerar cobrança pelo provedor escolhido e confirmar somente após verificação confiável do estado junto ao provedor; tratar notificações repetidas e falhas.
- **RF-21 — Calcular frete (condicionado):** oferecer, para clientes elegíveis de Vacaria, retirada local sem frete ou entrega própria por R$ 10; para demais localidades atendidas, calcular Correios conforme CEP, peso, dimensões e disponibilidade de integração. Confirmar abrangência, endereço de retirada e regras de contingência.
- **RF-22 — Registrar financeiro (condicionado):** apresentar receitas efetivas de pedidos online e vendas externas lançadas, além de despesas registradas; distinguir faturamento, custos, margem e lucro estimado, evitando lançamento duplicado de receita.
- **RF-23 — Analisar estoque (condicionado):** mostrar itens críticos, consumo, giro e sugestões de mínimo fundamentadas em histórico suficiente, sem alteração automática de mínimos.
- **RF-24 — Gerir agenda e notas (condicionado):** registrar compromissos, prazos e anotações, com alertas apenas quando canal e mecanismo forem definidos.
- **RF-25 — Gerir fornecedores (condicionado):** cadastrar fornecedores e associá-los a aquisições de materiais, respeitando dados necessários à operação.

## 8. Regras de negócio

- **RN-01 — Venda somente com disponibilidade:** quantidade comprável não excede saldo acabado disponível; produto esgotado não admite compra online nem encomenda pelo checkout.
- **RN-02 — Estoques distintos:** estoque de matéria-prima e estoque de produto acabado são controlados separadamente. Não somar quantidades de unidades físicas incompatíveis (unidades, gramas, mililitros etc.).
- **RN-03 — Reserva no planejamento:** ordem de produção confirmada reserva insumos; a soma das reservas ativas de um insumo não pode exceder o saldo físico utilizável.
- **RN-04 — Capacidade produtiva:** para uma ficha técnica válida, a capacidade é limitada pelo menor quociente inteiro entre saldo livre do insumo e consumo por unidade; faltas e ficha incompleta devem ser identificadas, não ocultadas.
- **RN-05 — Histórico da ficha:** mudanças futuras de ficha técnica não alteram silenciosamente quantidades e custos capturados por ordens existentes.
- **RN-06 — Conclusão consistente:** a movimentação de insumos e a entrada de produtos acabados de uma ordem concluída não podem ocorrer parcialmente; reenvio da conclusão não duplica estoque.
- **RN-07 — Carrinho não reserva:** adicionar ao carrinho não garante disponibilidade. **Proposta para validação:** reservar unidades ao criar pedido aguardando pagamento, com expiração configurável; baixar definitivamente após confirmação válida e liberar a reserva no cancelamento/expiração, de forma idempotente. Prazo exato pendente.
- **RN-08 — Vendas externas:** vendas feitas fora do site serão lançadas manualmente no painel; cada lançamento efetivo deve atualizar estoque, histórico e financeiro sem atribuir origem fictícia a pedido online. Não há sincronização automática com canais externos.
- **RN-09 — Preço histórico:** pedido preserva preço unitário, descontos, frete e total aplicados no momento da confirmação; edição de catálogo não altera compras anteriores.
- **RN-10 — Valores monetários:** usar precisão decimal adequada, regras explícitas de arredondamento e moeda BRL; não usar ponto flutuante binário para cálculos financeiros.
- **RN-11 — Precificação declarada:** a proprietária informa dividir o custo unitário por `0,3` para margem-alvo de 70%, ou `0,4` para 60%. O custo deverá considerar, quando aplicáveis, matérias-primas, embalagem, mão de obra, frete, taxas e despesas. Distinguir custos por unidade, custos fixos rateados, margem bruta e lucro líquido; validar o método de rateio antes da implementação.
- **RN-12 — Prazos:** produção sob encomenda leva em média duas semanas segundo a proprietária, mas não gera autorização para venda sem estoque nem prazo garantido para todas as ordens.
- **RN-13 — Permissões:** autorização deve ser verificada no servidor para cada operação protegida; ocultar botão na interface não constitui proteção suficiente.
- **RN-14 — Pagamento:** uma compra não pode ser marcada como paga apenas por declaração do cliente ou imagem de comprovante. Integração automática exige validação pelo provedor; procedimentos manuais ainda dependem de decisão.
- **RN-15 — Entrega:** envios declarados para RS/SC via Correios; para Vacaria, oferecer retirada local sem frete ou entrega própria por R$ 10, escolhida no checkout. Delimitação por CEP, endereço/horários de retirada, prazos e exceções ainda precisam de confirmação.
- **RN-16 — Cadastro e CPF:** compra exige conta com CPF validado, nome, telefone, e-mail e endereço completo; dados pessoais devem ser protegidos e utilizados para finalidades justificadas.
- **RN-17 — Lotes e validade:** cada lote de produto acabado registra data de fabricação e validade; unidades vencidas não são vendáveis. Critério de escolha de lote e descarte ainda será definido.
- **RN-18 — Cancelamentos e produção parcial (proposta):** cancelamentos de pedidos e ordens devem preservar histórico, liberar apenas reservas remanescentes e não desfazer consumos já efetivados; produção parcial registra rendimento real. Transições detalhadas e estornos dependem de validação.
- **RN-19 — Trienta:** o novo sistema substituirá o Trienta na operação final; migração inicial poderá ser manual, mediante conferência dos dados e saldos, sem presumir API ou exportação.
- **RN-20 — Banco e Pix:** Sicredi é o banco da conta PJ, mas não equivale à escolha ou aprovação de provedor de pagamentos. Confirmar disponibilidade de integração, homologação, custos e conciliação antes de ativar cobrança automática.

## 9. Requisitos não funcionais

- **RNF-01 — Segurança:** autenticação e autorização em API; senhas protegidas; nenhuma credencial, token ou chave de pagamento exposta no frontend ou repositório.
- **RNF-02 — Privacidade:** coletar apenas dados necessários à compra e operação; limitar acesso a documentos, endereços e contatos; definir política de retenção e atendimento a solicitações antes de uso real.
- **RNF-03 — Integridade e concorrência:** reservas, consumo de insumos, entradas de produção, pedidos e pagamentos não podem duplicar ou ficar parcialmente aplicados sob repetição ou concorrência.
- **RNF-04 — Usabilidade mobile-first:** fluxos principais utilizáveis em celular, com estados de carregamento, vazio, erro e sucesso; interface em português brasileiro, valores em BRL e rótulos claros.
- **RNF-05 — Acessibilidade:** navegação por teclado, foco visível, contraste adequado, mensagens de erro associadas aos campos e não depender apenas de cores.
- **RNF-06 — Rastreabilidade:** registrar autor, instante, motivo e tipo das alterações críticas de estoque, produção, preços e pedidos, sem incluir segredos nos logs.
- **RNF-07 — Histórico:** preservar dados necessários para reconstruir ordens e pedidos, mesmo após edição/despublicação de cadastros.
- **RNF-08 — Resiliência de integrações:** falha temporária de pagamento ou frete não deve gerar confirmação indevida, cobrança duplicada ou perda de pedido; apresentar estado pendente/erro recuperável.
- **RNF-09 — Desempenho:** metas de tempo de resposta, usuários simultâneos e disponibilidade serão definidas após dimensionamento; não declarar desempenho não medido.
- **RNF-10 — Identidade visual:** respeitar a logomarca fornecida (traço dourado, fundo claro, linguagem elegante) e manter boa legibilidade e contraste; paleta adicional ainda sujeita a aprovação.

## 10. Critérios de aceite

| ID | Referências | Cenário verificável |
| --- | --- | --- |
| **CA-01** | RF-01–03, RNF-01–02 | Cliente não acessa pedido de outro; funcionário não executa operação proibida mesmo chamando a API diretamente; credenciais inválidas são rejeitadas. |
| **CA-02** | RF-04–05, RN-01 | Produto publicado com 3 unidades permite até 3 unidades disponíveis; produto zerado não pode ser comprado; despublicado não aparece como comprável. |
| **CA-03** | RF-06–08, RN-02 | Entradas e ajustes de matéria-prima preservam histórico; 2 kg e 200 ml não são somados como uma mesma quantidade. |
| **CA-04** | RF-09–10, RN-04 | Ficha com 100 g de base e 5 ml de essência por unidade, saldos livres 850 g e 30 ml, resulta em capacidade de 6 unidades. |
| **CA-05** | RF-11, RN-03 | Duas ordens concorrentes não reservam mais material que o saldo disponível; criação rejeitada não deixa reserva parcial. |
| **CA-06** | RF-13–14, RN-05–06 | Conclusão válida consome a reserva e aumenta estoque acabado uma vez; repetição não duplica; cancelamento permitido libera reserva remanescente. |
| **CA-07** | RF-15–17, RN-07 | Carrinho não garante saldo; duas tentativas simultâneas sobre a última unidade não resultam em duas compras confirmadas. |
| **CA-08** | RF-16–19, RN-09 | Pedido registra preço e quantidade originais; mudança posterior no catálogo não modifica histórico; cliente vê somente seus pedidos. |
| **CA-09** | RF-06–08, RF-26, RN-08 | Venda externa lançada manualmente reduz saldo vendável e registra receita uma única vez, sem criar pedido online fictício; lançamento repetido não duplica movimentações. |
| **CA-10** | RF-20, RN-14 | **Se integração aprovada:** notificação válida confirma cobrança correspondente; notificação duplicada não duplica pagamento/baixa; comprovante enviado não confirma automaticamente. |
| **CA-11** | RF-21, RN-15 | Para endereço elegível em Vacaria, checkout oferece retirada sem frete ou entrega local de R$ 10; para Correios, preço e prazo dependem de cotação válida antes da confirmação. |
| **CA-12** | RF-22–25 | **Se módulos aprovados:** indicadores conferem com dados de origem, sugestões não alteram mínimo e alertas não são prometidos sem canal configurado. |
| **CA-13** | RNF-04–05, RNF-10 | Jornadas principais são utilizáveis no celular, com campos identificados, foco e estados de erro compreensíveis. |
| **CA-14** | RN-10–11, RNF-07 | Cálculo de preço/margem usa valores monetários consistentes e distingue margem de materiais de lucro líquido; histórico não muda com novos custos. |

| **CA-15** | RF-03, RN-16 | Cadastro exige conta, CPF válido, nome, e-mail, telefone e endereço completo; cliente não acessa CPF/endereço de outro cliente. |
| **CA-16** | RF-07, RN-17 | Lotes preservam fabricação e validade; lote vencido não compõe estoque vendável. |
| **CA-17** | RF-17, RN-07 | **Se política proposta aprovada:** pedido pendente reserva uma vez, expiração libera uma vez e confirmação concorrente não produz venda duplicada. |

Os números dos cenários são **exemplos de teste a executar**, não resultados de execução.

## 11. Dados e integrações

### 11.1 Entidades conceituais

| Entidade | Dados e relações principais |
| --- | --- |
| Usuário / perfil / permissão | Identificação, credencial protegida, papel e autorizações |
| Cliente / endereço | Nome, CPF protegido, e-mail, telefone, endereços completos e pedidos próprios |
| Produto / categoria / foto / variação | Descrição, preço, publicação, variações, estoque acabado e imagens |
| Lote de produto acabado | Produto/variação, quantidade, data de fabricação, validade e movimentações |
| Matéria-prima | Nome, unidade, mínimo, saldo e movimentos |
| Ficha técnica / componente | Produto, material, quantidade, unidade e versão aplicável |
| Ordem de produção | Produto, quantidade, prazo, estado, reservas, consumo e resultado |
| Reserva / movimentação | Origem, quantidade, unidade, motivo, instante e responsável |
| Carrinho / pedido / item | Cliente, produto, quantidade, preços capturados, estados e totais |
| Venda externa / item | Canal, produtos, quantidades, valores, pagamento, movimentações e receita correspondente |
| Pagamento | Pedido, método, estado, referência do provedor e eventos, se integrado |
| Entrega | Modalidade, endereço, frete, rastreamento e estado |
| Fornecedor / compra | Origem e custo de materiais, se aprovado |
| Despesa / agenda / nota | Registros administrativos, se aprovados |
| Auditoria | Ator, ação, instante e referência da operação |

O esquema físico, índices, migrações e contratos serão definidos no TDD, não neste PRD.

### 11.2 Integrações

| Integração | Situação | Dependência |
| --- | --- | --- |
| Trienta | **Substituição total planejada, sem integração automática** | Catálogo, preços e descrições disponíveis; conferir exportação ou migrar manualmente, validar saldos |
| Pix / Sicredi | **Sicredi é banco PJ; provedor não definido** | Verificar API Pix disponível para a conta, taxas, credenciais, webhooks, homologação e alternativa intermediada |
| Correios | **Utilizados manualmente hoje** | Validar API/contrato, preços, CEP de origem, peso e medidas |
| WhatsApp | **Canal de atendimento atual** | Link de contato pode ser simples; automação exige decisão própria |
| Fotos de produtos | **Necessárias** | Definir armazenamento, limites, tratamento e custos no TDD |

## 12. Riscos, premissas e dependências

| Tema | Risco e encaminhamento |
| --- | --- |
| Prazo acadêmico | Data informada: 30/11/2026; acompanhamento e eventual ajuste de escopo serão decididos pelo solicitante, sem alterar silenciosamente os requisitos |
| Divergência de estoque entre canais | Vendas externas precisam ser lançadas manualmente com agilidade; enquanto não lançadas, a disponibilidade online pode estar desatualizada |
| Reserva de produção | Reserva sem política de cancelamento/consumo pode imobilizar insumos indefinidamente; definir transições |
| Pedido sem pagamento | Reserva de produto pode expirar ou bloquear estoque; definir janela, cancelamento e recuperação |
| Pix | Integração depende de provedor, contrato, ambiente de testes e segurança; não prometer confirmação sem infraestrutura |
| Correios | Preço/prazo dependem de serviços e dados corretos; prever indisponibilidade e política alternativa |
| Custos e precificação | Fórmula informada não cobre necessariamente mão de obra, embalagem, taxas e despesas; validar demonstrativos |
| Dados de cliente | CPF foi escolhido como obrigatório; justificar finalidade, restringir acesso, definir retenção e validar obrigações de privacidade antes do uso real |
| Produtos naturais | Não presumir certificação, segurança clínica ou alegações terapêuticas; conteúdo comercial precisa de validação da proprietária |
| Fotografias e marca | Utilizar arquivos fornecidos/autorizados; preservar identidade da marca |
| Testes com proprietária | Há disposição para testar, mas nenhuma validação de interface ocorreu ainda |

## 13. Decisões encerradas e questões em aberto

### 13.1 Resoluções registradas nesta revisão

| ID original | Resultado |
| --- | --- |
| P-01 | Entrega acadêmica informada para **30/11/2026**; solicitante acompanhará necessidade de ajuste de escopo. |
| P-02 | Perfis limitados confirmados; **matriz fina de permissões adiada** até após o administrador. |
| P-03 | **Conta e CPF obrigatórios**, com nome completo, e-mail, telefone e endereço completo com CEP. |
| P-06 | Banco PJ identificado: **Sicredi**; **provedor/API Pix ainda pendente**. |
| P-07 | **Vacaria: entrega própria R$ 10 ou retirada local sem frete**; CEPs, horários e local exato pendentes. |
| P-11 | Variações afetam ficha técnica/estoque; **data de fabricação e validade por lote** necessárias. |
| P-12 | **Vendas externas serão registradas manualmente no sistema**, com estoque e financeiro; não apenas ajustes sem origem. |
| P-13 | Trienta contém catálogo, valores e descrições; **substituição total** confirmada; exportação não comprovada. |
| P-14 | Precificação deve considerar embalagem, mão de obra, frete, taxas e despesas. |
| P-16 | **Proposta inicial:** alertas internos no painel; canais adicionais pendentes. |

### 13.2 Pendências operacionais e técnicas

| ID | Decisão pendente | Direção proposta / dependência |
| --- | --- | --- |
| **P-04** | Prazo de pagamento e política de reserva/baixa de produtos acabados | Proposta: reservar na criação do pedido, expirar após prazo configurável, baixar após pagamento confirmado. |
| **P-05** | Cancelamentos, reembolsos, pagamentos tardios e pedidos não pagos | Proposta: estados distintos, liberação idempotente de reservas e estorno conciliado com provedor. |
| **P-06** | API Pix direta do Sicredi ou intermediador; taxas e habilitação | Investigar documentação e disponibilidade real para conta PJ; não presumir integração. |
| **P-07** | Abrangência da taxa local de R$ 10, endereço e horários de retirada | Confirmar com proprietária antes de ativar checkout real. |
| **P-08** | CEP de origem, dimensões, peso, modalidade Correios e contingência | Confirmar com proprietária e verificar serviços disponíveis. |
| **P-09** | Ordens com insumos insuficientes, cancelamento após início e produção parcial | Proposta: permitir rascunho sem reserva, mas confirmar planejamento apenas com reserva integral; registrar execução parcial. |
| **P-10** | Momento de consumo e rendimento real | Proposta: reservar no planejamento e consumir ao registrar execução/conclusão, com ajustes auditáveis. |
| **P-11** | Política de lotes: seleção, vencimento, perdas e descarte | Proposta: bloquear venda de vencidos; detalhar baixa por lote. |
| **P-13** | Possibilidade de exportação do Trienta e método de conferência dos saldos iniciais | Validar exportação; senão, cadastrar e conferir manualmente. |
| **P-15** | Quais módulos condicionados integrarão a entrega e em qual ordem | Definir conforme evolução do projeto, sem declarar implementações antecipadamente. |
| **P-16** | Notificações além dos alertas internos | Definir necessidade de e-mail, WhatsApp ou push posteriormente. |
| **P-17** | Permissões por operação para funcionários | Definir após o fluxo administrativo da proprietária. |
| **P-18** | Regras de rateio de despesas e apresentação de margem/lucro | Validar com proprietária antes de automatizar sugestão de preços. |

**Regra de governança:** propostas acima são hipóteses de projeto aceitas como direção inicial pelo solicitante, mas ainda exigem detalhamento/validação antes de fluxos financeiros ou de estoque em produção.

## 14. Próximos passos e passagem para TDD

1. Revisar esta versão 0.2, concentrando a validação nas políticas de pedido/pagamento (**P-04/P-05**) e execução de produção (**P-09/P-10**).
2. Definir o recorte de entrega acadêmica e aprovar ou adiar explicitamente cada módulo condicionado; verificar dados de Correios e viabilidade de Pix no Sicredi.
3. Atualizar regras, jornadas, requisitos e critérios de aceite de acordo com as decisões; registrar data e versão.
4. Só então marcar o PRD como **Validado para TDD**.
5. Elaborar `docs/tdd.md` com base na versão aprovada, detalhando arquitetura inspirada no NoteKeeper (Angular, ASP.NET Core, SQL Server), autenticação, persistência, transações, armazenamento de imagens, integração de pagamentos, testes e plano de implementação.

**Estado atual:** versão 0.2 revisada, com decisões confirmadas e propostas identificadas; nenhuma implementação, integração ou teste de aceite é afirmado neste arquivo.
