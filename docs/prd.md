# PRD — Xaquiamuni Produtos Naturais

**Plataforma web de comércio eletrônico e gestão de produção artesanal**

| Campo               | Informação                                                                                                                                                                                                                 |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Projeto             | Xaquiamuni — E-commerce e Gestão Artesanal                                                                                                                                                                                 |
| Versão do documento | 0.14 — consolidação do planejamento F4.0                                                                                                                                                                                       |
| Data                | 16/11/2026                                                                                                                                                                                                                 |
| Status              | **F2 e F3.1–F3.6.4 implementadas; gate final F3.7 aprovado; F4.0 planejada; F4.1 é a próxima etapa de implementação. Nenhuma funcionalidade F4 está implementada.**                                                        |
| Contexto            | Trabalho de Conclusão de Curso — Academia do Programador                                                                                                                                                                   |
| Fonte principal     | Entrevista com a proprietária (53 respostas, cinco esclarecimentos complementares e rodada posterior de decisões sobre cadastro, entregas, estoque e banco), logomarca e capturas do Instagram fornecidas pelo solicitante |
| Escopo              | Primeira entrega demonstrável e planejamento de evolução, com limites e pendências explícitos                                                                                                                              |

> **Regra de leitura:** “Confirmado” significa declarado pela proprietária ou pelo solicitante; “Implementado” significa comportamento verificado no código e coberto por testes; “Proposto” significa sugestão ainda sujeita a aprovação; “Pendente” exige decisão ou validação antes de ativar o fluxo afetado. Critérios de aceite descrevem verificações; resultados só são afirmados quando há evidência.

## 1. Resumo executivo

Desenvolver uma plataforma web responsiva para a **Xaquiamuni Produtos Naturais**, empreendimento de Vacaria–RS que fabrica sabonetes e velas artesanais desde 2021. A solução terá uma loja virtual para clientes comprarem **somente produtos disponíveis** e um painel administrativo para organizar catálogo, matérias-primas, fichas técnicas, ordens de produção, estoque, pedidos e operações do negócio.

O diferencial do produto será conectar a venda online à **organização da produção artesanal**, apontada pela proprietária como a principal necessidade. A solução deverá apoiar o planejamento de lotes, a reserva de matérias-primas, o cálculo da capacidade de produção e a atualização consistente de produtos acabados.

Pagamentos integrados, entregas, financeiro, análises e agenda compõem a visão de produto, mas sua abrangência na entrega do TCC depende de decisões operacionais e da viabilidade das integrações. Não presumir automações ou serviços externos já contratados.

Na F2 foram implementados cadastro de clientes com CPF protegido, perfil e endereço; login, logout e sessão autenticada; gate explícito que impede o login de `CLIENTE` não confirmado após credenciais corretas; confirmação e reenvio de e-mail; recuperação e redefinição de senha com invalidação das sessões anteriores; e estados de interface de cadastro concluído, confirmação pendente e sessão ativa. As solicitações de e-mail usam Outbox e processamento assíncrono. O gate final aprovou 638 testes backend e 139 frontend (777 no total), sem falhas ou ignorados, e os builds backend e frontend. Testes manuais anteriores validaram cadastro, bloqueio pré-confirmação, confirmação, reenvio e recuperação com Resend real em ambientes isolados; isso não valida implantação nem operação em produção. `RequireConfirmedEmail` global permanece `false`; o gate específico para `CLIENTE` está ativo. Resend e worker continuam desabilitados por padrão, e os requisitos de produção permanecem pendentes.

## 2. Contexto e problema

### 2.1 Negócio observado

- **Confirmado:** marca Xaquiamuni Produtos Naturais, em atividade desde 2021; fabricação própria de sabonetes artesanais, incluindo sabonetes energizados, e velas artesanais. O _Sabonete para Descarrego_ foi apontado como o produto mais vendido.
- **Confirmado:** vendas atualmente por Instagram, Facebook, WhatsApp, presencialmente e em feiras; atendimento principalmente por WhatsApp.
- **Confirmado:** controle de entradas e saídas pelo aplicativo **Trienta**, complementado por anotações; existe catálogo de produtos, valores e descrições nesse aplicativo. A plataforma Xaquiamuni deverá substituí-lo integralmente quando estiver pronta.
- **Confirmado:** produtos prontos e produção sob encomenda; prazo médio declarado de aproximadamente duas semanas, dependente de materiais e quantidade. Entretanto, **o site não deve aceitar compra sem estoque pronto disponível**.
- **Confirmado:** a proprietária conhece o consumo de matérias-primas por produto e deseja apurar capacidade produtiva, custos e margens.
- **Confirmado:** utiliza principalmente celular e há outra pessoa que ajudará na operação com acesso limitado.
- **Confirmado:** envios pelos Correios para RS e SC, entregas próprias na região de Vacaria e retirada presencial para clientes de Vacaria.

### 2.2 Problema a resolver

O planejamento de produção, os pedidos e os controles operacionais estão distribuídos entre aplicativo e anotações. A proprietária indicou **organização de produções** como a melhoria mais importante. A hipótese de produto é que centralizar fichas técnicas, reservas de insumos, ordens de produção e estoque de produtos acabados reduza esquecimentos, conflitos de materiais e retrabalho. **Esses benefícios ainda não foram medidos.**

### 2.3 Decisões confirmadas

| Tema                               | Decisão                                                                                                                                        |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Compra no site                     | Apenas unidades prontas e disponíveis; sem venda online de produto esgotado                                                                    |
| Produção                           | Reservar matérias-primas já no planejamento                                                                                                    |
| Equipe                             | Funcionários com acesso limitado às tarefas autorizadas                                                                                        |
| Canais registrados automaticamente | Pedidos do site; vendas de outros canais serão lançadas manualmente no painel, sem integração automática com esses canais                      |
| Dados existentes                   | Catálogo de produtos, preços e descrições no Trienta; substituição total planejada, sem importação automática confirmada                       |
| Dispositivo prioritário            | Celular                                                                                                                                        |
| Produtos                           | Fabricação própria; sabonetes e velas; sem personalização de pedidos na operação atual                                                         |
| Kits                               | Interesse futuro, não operação atual                                                                                                           |
| Pagamento                          | Pix, dinheiro e cartão aceitos atualmente; desejo de Pix online com confirmação automática                                                     |
| Frete                              | Correios para regiões atendidas; entrega própria em Vacaria por R$ 10 e retirada local sem frete, com abrangência exata a confirmar            |
| Cadastro do cliente                | Conta obrigatória antes da compra, com nome completo, CPF, e-mail, telefone e endereço completo com CEP                                        |
| Confirmação de e-mail              | Cadastro cria conta não confirmada e enfileira solicitação inicial; link válido por 24 horas; reenvio genérico com cooldown e limite por conta |
| Banco PJ                           | Sicredi; disponibilidade de API Pix e condições contratuais não verificadas                                                                    |
| Rastreabilidade                    | Variações dos produtos podem exigir fichas técnicas/estoques distintos; registrar fabricação e validade por lote                               |
| Precificação                       | Considerar matérias-primas, embalagem, mão de obra, frete, taxas e despesas, distinguindo margem e lucro                                       |

## 3. Objetivos e indicadores de sucesso

| Objetivo                          | Como verificar                                                                 | Meta da primeira entrega                                    |
| --------------------------------- | ------------------------------------------------------------------------------ | ----------------------------------------------------------- |
| Organizar produção                | Criar, acompanhar, concluir e cancelar ordens de produção em cenários de teste | Fluxos de aceite aprovados; melhoria real a medir em piloto |
| Evitar uso duplicado de insumos   | Tentar reservar o mesmo saldo em duas ordens simultâneas                       | Nenhuma reserva acima do saldo disponível                   |
| Conhecer capacidade produtiva     | Comparar cálculo de unidades possíveis com ficha técnica e saldo livre         | Resultado correto nos cenários definidos                    |
| Evitar venda de item indisponível | Simular duas compras concorrentes da última unidade                            | Nunca confirmar duas vendas da mesma unidade                |
| Simplificar a compra              | Percorrer catálogo, carrinho, identificação e pedido no celular                | Jornada demonstrável; usabilidade a validar com clientes    |
| Proteger operação                 | Exercitar permissões de administrador, funcionário e cliente                   | Nenhum acesso indevido nos cenários de aceite               |
| Preservar dados financeiros       | Recalcular totais históricos com valores capturados no pedido                  | Concordância nos cenários de aceite                         |

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

1. **Contas e acesso:** cadastro, autenticação por cookie e sessões de cliente, administrador e funcionário; confirmação obrigatória para login de `CLIENTE`; confirmação/reenvio de e-mail e recuperação/redefinição de senha. Permissões administrativas detalhadas e demais operações de negócio continuam futuras.
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

### J-08 — Recuperar e redefinir senha

Uma pessoa não autenticada solicita recuperação informando o e-mail. A interface sempre apresenta uma confirmação genérica para um e-mail válido, sem revelar se a conta existe ou é elegível. Somente uma conta com e-mail confirmado pode gerar uma mensagem de recuperação. A pessoa abre o link temporário, informa e confirma uma nova senha e, após a redefinição bem-sucedida, entra novamente usando a nova senha; sessões anteriores deixam de ser válidas.

## 7. Requisitos funcionais

**Convenção:** os RF abaixo descrevem o escopo-alvo da seção 5.1; os RF condicionados estão identificados separadamente. Detalhes não confirmados são explicitados como pendências e não devem ser decididos silenciosamente na implementação.

### 7.1 Identidade e permissões

- **RF-01 — Autenticar usuários:** permitir acesso de administrador, funcionário e cliente; exigir autenticação para operações privadas e encerrar sessão com segurança.
- **RF-02 — Proteger acesso:** cliente consulta apenas seus dados e pedidos; funcionário acessa somente operações autorizadas; administrador configura permissões. A matriz fina de permissões está pendente.
- **RF-03 — Cadastrar cliente:** exigir conta antes da compra, com nome completo, CPF, e-mail, telefone e endereço completo (CEP, UF, cidade, bairro, logradouro, número e complemento opcional); validar formato/dígitos do CPF e proteger os dados. A conta nasce não confirmada e a solicitação inicial de confirmação é enfileirada no cadastro. Após cadastro bem-sucedido, a interface apresenta estado de conclusão e ações explícitas para login e confirmação, sem autenticar ou reenviar automaticamente. A necessidade de dados adicionais pelo provedor/logística deve ser reavaliada.

### 7.2 Catálogo e estoque

- **RF-04 — Gerir produtos:** cadastrar, editar, listar e despublicar produtos com nome, descrição, categoria, preço, fotos e tipo de produção; permitir variantes que alterem estoque, ficha técnica e rastreabilidade, com identidade própria quando necessário. No cadastro administrativo, oferecer produto simples (uma versão e um preço) ou produto com opções, sem persistir essa escolha como tipo distinto. Produtos simples podem ter peso e volume líquidos opcionais; a interface aceita g/kg e ml/L e envia valores canônicos sem arredondamento. O catálogo pode definir características por produto — como peso, volume, cor, fragrância, tamanho ou material — e registrar somente as combinações de valores efetivamente comercializadas. Cada variante mantém identidade, estado, unidade comercial e preço próprios. As unidades comerciais são `UN`, `KIT`, `PAR`, `PCT`, `CX` e `CJ`; composição de kits permanece fora do escopo atual. A interface administrativa reconstrói a edição com IDs persistidos, preserva variantes e oferece referências temporárias para cadastrar todo o agregado em uma única operação. A API e o formulário administrativo suportam características, medidas e combinações. O domínio e o SQL Server modelam metadados e vínculos de imagens; upload, armazenamento e galeria administrativa foram implementados nas F3.4.3.2–F3.4.4.1. A API pública de consulta é F3.6.1, a homepage/listagem é F3.6.2 e a página pública individual é F3.6.3.1; compra permanece futura.
- **RF-05 — Consultar vitrine:** exibir produtos publicados, preços, medidas e combinações reais na listagem e na página pública individual. A disponibilidade real será integrada na F4.6; não haverá botão de compra, carrinho ou checkout na F4.
- **RF-06 — Gerir insumos:** cadastrar matérias-primas, embalagens e outros insumos com unidade-base física, formas de compra específicas, mínimo configurável e política de lote/validade configurável; registrar entradas, saídas e ajustes auditáveis. Criar o cadastro não gera saldo.
- **RF-07 — Gerir estoque acabado:** controlar quantidades comerciais inteiras por `ProdutoVariante` existente e por lote; registrar identificação, fabricação e validade, entradas, saídas, perdas, descartes e ajustes. Variantes existentes começam sem saldo implícito; lotes vencidos, danificados ou em quarentena não podem ser vendidos.
- **RF-08 — Controlar saldos, reservas e disponibilidade:** distinguir saldo físico, utilizável, reservado e disponível; impedir saldos negativos, reservas duplicadas ou superiores ao disponível; manter saldo operacional e histórico na mesma transação. As operações internas de reserva, liberação e consumo serão preparadas sem implementar ordens, pedidos, checkout ou expiração de pedidos na F4.

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
- **RF-17 — Controlar estoque do pedido:** a reserva de produto acabado ocorrerá ao finalizar o pedido, não no carrinho; cancelar libera somente a quantidade ainda reservada. Prazo de pagamento/expiração e momento da baixa física após pagamento continuam pendentes para a fase de pedidos; impedir venda concorrente da última unidade.
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

### 7.6 Confirmação de e-mail — F2 implementada

- **RF-27 — Confirmar e-mail:** permitir a confirmação anônima da conta por link contendo token de confirmação com validade de 24 horas. O cadastro cria a conta não confirmada e enfileira automaticamente a solicitação inicial, sem iniciar uma sessão. O login de `CLIENTE` com credenciais corretas permanece bloqueado até a confirmação.
- **RF-28 — Reenviar confirmação:** permitir que o cliente solicite novo e-mail pela página de confirmação e pelos pontos de entrada do cadastro e login. O login apresenta estado de confirmação pendente com ação de reenvio; o link redundante para a página de confirmação aparece apenas no formulário normal de login. Para e-mails válidos, a resposta pública não revela existência, elegibilidade ou estado interno da conta; cooldown, limite por conta e deduplicação seguem RN-23 a RN-27.

### 7.7 Recuperação de senha — implementada

- **RF-29 — Solicitar recuperação:** permitir que uma pessoa não autenticada solicite recuperação informando o e-mail. Para e-mail válido, a resposta genérica não revela existência, confirmação, elegibilidade, cooldown, limite ou estado interno da solicitação. Somente contas com e-mail confirmado podem criar mensagem de recuperação.
- **RF-30 — Redefinir senha:** oferecer página de redefinição por link temporário; aceitar a nova senha somente após validar o token de Identity e as regras de senha. Após sucesso, invalidar sessões anteriores e exigir novo login explícito.
- **RF-31 — Mostrar ou ocultar senha:** disponibilizar controle de olho independente para senha e confirmação nas telas de login, cadastro e redefinição. Os cinco campos começam ocultos, preservam valor e validação ao alternar, e voltam a ocultos quando o formulário é limpo ou o respectivo fluxo é concluído.

## 8. Regras de negócio

- **RN-01 — Venda somente com disponibilidade:** quantidade comprável não excede saldo acabado disponível; produto esgotado não admite compra online nem encomenda pelo checkout.
- **RN-02 — Estoques e unidades distintas:** estoque de insumos e de produtos acabados são separados. Insumos usam unidade-base física de massa (g), volume (ml) ou contagem inteira (un); unidades comerciais do catálogo não são unidades físicas de consumo. Não somar dimensões incompatíveis nem converter massa em volume genericamente.
- **RN-03 — Reserva no planejamento:** quando a etapa de produção for implementada, ordem efetivamente planejada reserva insumos; a soma das reservas ativas não pode exceder o saldo disponível do insumo.
- **RN-04 — Capacidade produtiva:** para uma ficha técnica válida, a capacidade é limitada pelo menor quociente inteiro entre saldo livre do insumo e consumo por unidade; faltas e ficha incompleta devem ser identificadas, não ocultadas.
- **RN-05 — Histórico da ficha:** mudanças futuras de ficha técnica não alteram silenciosamente quantidades e custos capturados por ordens existentes.
- **RN-06 — Conclusão consistente:** a movimentação de insumos e a entrada de produtos acabados de uma ordem concluída não podem ocorrer parcialmente; reenvio da conclusão não duplica estoque.
- **RN-07 — Reserva de produto acabado:** carrinho não reserva. A reserva acontece ao finalizar o pedido; cancelamentos liberam somente o que ainda estiver reservado. Prazo de pagamento/expiração e momento da baixa física após pagamento permanecem pendentes para a fase de pedidos.
- **RN-08 — Vendas externas:** vendas feitas fora do site serão lançadas manualmente no painel; cada lançamento efetivo deve atualizar estoque, histórico e financeiro sem atribuir origem fictícia a pedido online. Não há sincronização automática com canais externos.
- **RN-09 — Preço histórico:** pedido preserva preço unitário, descontos, frete e total aplicados no momento da confirmação; edição de catálogo não altera compras anteriores.
- **RN-10 — Valores monetários:** usar precisão decimal adequada, regras explícitas de arredondamento e moeda BRL; não usar ponto flutuante binário para cálculos financeiros.
- **RN-11 — Precificação declarada:** a proprietária informa dividir o custo unitário por `0,3` para margem-alvo de 70%, ou `0,4` para 60%. O custo deverá considerar, quando aplicáveis, matérias-primas, embalagem, mão de obra, frete, taxas e despesas. Distinguir custos por unidade, custos fixos rateados, margem bruta e lucro líquido; validar o método de rateio antes da implementação.
- **RN-12 — Prazos:** produção sob encomenda leva em média duas semanas segundo a proprietária, mas não gera autorização para venda sem estoque nem prazo garantido para todas as ordens.
- **RN-13 — Permissões:** autorização deve ser verificada no servidor para cada operação protegida; ocultar botão na interface não constitui proteção suficiente.
- **RN-14 — Pagamento:** uma compra não pode ser marcada como paga apenas por declaração do cliente ou imagem de comprovante. Integração automática exige validação pelo provedor; procedimentos manuais ainda dependem de decisão.
- **RN-15 — Entrega:** envios declarados para RS/SC via Correios; para Vacaria, oferecer retirada local sem frete ou entrega própria por R$ 10, escolhida no checkout. Delimitação por CEP, endereço/horários de retirada, prazos e exceções ainda precisam de confirmação.
- **RN-16 — Cadastro e CPF:** compra exige conta com CPF validado, nome, telefone, e-mail e endereço completo; dados pessoais devem ser protegidos e utilizados para finalidades justificadas.
- **RN-17 — Lotes e validade:** lotes de produto acabado registram fabricação e validade; são elegíveis até o fim da data de vencimento no calendário de negócio da loja e ficam indisponíveis no dia seguinte. Vencidos, danificados e em quarentena não são vendáveis, mas permanecem no saldo físico até movimento apropriado. A seleção futura utiliza FEFO. Lotes de insumo e exigência de validade são configuráveis por insumo. A seleção determinística de lotes sem validade e alterações da configuração com histórico/saldo exigem detalhamento técnico.
- **RN-18 — Cancelamentos e produção parcial (proposta):** cancelamentos de pedidos e ordens devem preservar histórico, liberar apenas reservas remanescentes e não desfazer consumos já efetivados; produção parcial registra rendimento real. Transições detalhadas e estornos dependem de validação.
- **RN-19 — Trienta:** o novo sistema substituirá o Trienta na operação final; migração inicial poderá ser manual, mediante conferência dos dados e saldos, sem presumir API ou exportação.
- **RN-20 — Banco e Pix:** Sicredi é o banco da conta PJ, mas não equivale à escolha ou aprovação de provedor de pagamentos. Confirmar disponibilidade de integração, homologação, custos e conciliação antes de ativar cobrança automática.
- **RN-21 — Estado inicial da conta:** o cadastro cria a conta com e-mail não confirmado e enfileira a solicitação inicial na mesma operação transacional do usuário e do perfil; enfileirar não significa que o e-mail foi enviado.
- **RN-22 — Validade da confirmação:** o token do link de confirmação é válido por 24 horas; token inválido ou expirado não confirma a conta.
- **RN-23 — Privacidade do reenvio:** solicitações com e-mail válido recebem resposta pública genérica equivalente para contas elegíveis e não elegíveis; não revelar existência da conta, confirmação, cooldown, limites ou estado da Outbox.
- **RN-24 — Cooldown:** respeitar intervalo mínimo de 2 minutos entre solicitações da mesma conta, considerando a solicitação mais recente e eventual conclusão posterior do processamento.
- **RN-25 — Limite por conta:** permitir no máximo 5 solicitações em janela móvel de uma hora; a solicitação inicial do cadastro conta nesse limite.
- **RN-26 — Deduplicação:** não criar solicitação adicional enquanto houver mensagem de confirmação `Pending` ou `Processing` para a conta.
- **RN-27 — Recuperação de DeadLetter:** uma mensagem `DeadLetter` pode ser substituída por nova solicitação quando cooldown e limite permitirem; preservar a mensagem antiga como histórico e liberar sua chave ativa junto à criação da nova mensagem, atomicamente.
- **RN-28 — Confirmação obrigatória para login:** `RequireConfirmedEmail` global permanece `false`. O login aplica um gate explícito somente a `CLIENTE`: depois de validar as credenciais, conta não confirmada recebe HTTP `403` com código `EMAIL_NOT_CONFIRMED` e não inicia sessão. `ADMIN` e `FUNCIONARIO` não são abrangidos por esse gate específico.
- **RN-29 — Elegibilidade de recuperação:** somente contas com e-mail confirmado podem gerar solicitação de recuperação; contas não elegíveis recebem a mesma resposta pública genérica.
- **RN-30 — Token de recuperação:** o link de redefinição tem validade de 1 hora; tokens inválidos, expirados ou já utilizados não redefinem a senha.
- **RN-31 — Solicitações de recuperação:** respeitar cooldown mínimo de 2 minutos, no máximo 5 solicitações aceitas por conta em janela móvel de 1 hora e deduplicação enquanto houver mensagem `Pending` ou `Processing`. Esses limites são independentes dos limites de reenvio de confirmação.
- **RN-32 — Redefinição segura:** alteração da senha e cancelamento transacional de mensagens de recuperação obsoletas devem ser atômicos; redefinição bem-sucedida invalida sessões anteriores pelo `SecurityStamp`, exigindo novo login.
- **RN-33 — Envio e produção:** enfileirar uma mensagem não comprova sua entrega. Envio real de confirmação, reenvio e recuperação foi validado manualmente com Resend em ambiente isolado; isso não comprova prontidão de produção. Antes de ativar em produção, configurar e validar worker, Resend, domínio verificado, remetente, gestão de credenciais, URL pública HTTPS e Data Protection.
- **RN-34 — Unidade, preço e medidas físicas do catálogo:** unidades comerciais válidas são `UN`, `KIT`, `PAR`, `PCT`, `CX` e `CJ`. `KIT` nesta etapa identifica apenas a unidade comercial; não representa composição, estoque de componentes nem ficha técnica de kit. Preços são informados em BRL com duas casas decimais. Peso líquido é opcional e canônico em gramas; volume líquido é opcional e canônico em mililitros. Medidas devem ser positivas, ter até três casas decimais e caber no limite persistido. Quando uma variante usa característica física de peso ou volume, o valor numérico canônico da característica é a fonte de verdade; medidas diretas não podem divergir.
- **RN-35 — Características, combinações e legado:** características e seus valores pertencem ao produto e são normalizados para impedir duplicatas equivalentes. Uma variante pode associar no máximo um valor a cada característica; somente combinações explicitamente cadastradas são comercializadas, sem exigir o produto cartesiano. Combinações iguais no mesmo produto são rejeitadas, inclusive por integridade persistente sob concorrência. Variantes legadas sem características permanecem sem assinatura de combinação; nenhum peso, volume ou característica será inferido a partir de nomes ou identificações antigos. Valores legados da unidade de venda são preservados na migration; as seis unidades comerciais são validadas pelo domínio nas novas operações.
- **RN-36 — Imagens de produto (F3.4.1):** cada produto pode ter até cinco imagens no total, em uma galeria geral compartilhável com as opções. A primeira imagem é principal; quando houver imagens, o agregado mantém exatamente uma principal e uma ordem de exibição por imagem. Uma imagem pode ser vinculada a várias variantes do mesmo produto, sem duplicar o arquivo nem ocupar novas posições; a variante sem imagem específica utiliza a galeria geral. Produtos não publicados podem não ter imagens. Nova publicação exige ao menos uma imagem principal; a última imagem de um produto publicado não pode ser removida sem despublicá-lo antes. A migration não cria imagens fictícias nem altera produtos publicados legados sem imagens; eles permanecem intactos, mas não podem ser publicados novamente sem imagem. A futura vitrine deverá tratar essa condição sem correção automática de dados. Imagens não indicam disponibilidade de estoque.
- **RN-37 — Referências de imagens:** o SQL Server guarda somente metadados e chave de armazenamento opaca e única; não guarda bytes, Base64, caminho local absoluto ou URL temporária assinada. O nome original é metadado não confiável e não deve ser usado como caminho nem renderizado sem codificação apropriada. As validações de conteúdo, armazenamento e URLs pertencem a etapas futuras; a F3.4.1 não implementa upload, endpoint ou interface.
- **RN-38 — Unidades físicas e conversões de estoque:** cada insumo possui unidade-base de g, ml ou unidade inteira (`un`). Conversões exatas são permitidas somente entre unidades compatíveis, incluindo kg/g e L/ml. Formas de compra são configuráveis por insumo e suas conversões não são universais. Uma movimentação conserva quantidade original, unidade de origem e fator aplicado. Quantidades físicas usam `decimal`, nunca `float`/`double`, com até seis casas decimais; limites e precisão SQL serão detalhados na F4.1. Contagens não admitem fração. Cadastro de insumo não cria saldo.
- **RN-39 — Lotes de insumos:** o controle de lote é configurável por insumo; a exigência de validade também é configurável. Sem controle de lote, o saldo é agregado; com controle, o saldo é por lote. Registrar identificação, data de entrada, fornecedor quando informado e validade quando exigida. FEFO seleciona primeiro o lote elegível que vence antes. Lotes vencidos ou bloqueados contam no físico, mas não podem ser usados. Perda/descarte gera movimento auditável.
- **RN-40 — Custos de aquisição:** entradas de insumos podem registrar custo total e custo por unidade-base, preservados historicamente por entrada/lote. Custo desconhecido é diferente de custo zero; estoque inicial pode não ter custo conhecido. Custo médio ponderado, valorização definitiva, custo completo de produção e margem não estão definidos. Precisão monetária/de custo unitário e arredondamento serão definidos na etapa técnica correspondente, sem arredondamento prematuro.
- **RN-41 — Lotes de produto acabado:** estoque acabado é vinculado por FK à `ProdutoVariante`, inclusive à variante interna `Padrão`; quantidades são inteiras por variante e lote. Cada lote exige identificação única gerada de modo seguro sob concorrência, fabricação e validade. Pode haver prazo padrão por produto ou variante, calculado para cada novo lote e substituível pela validade real informada; mudanças futuras não alteram lotes existentes. Não há prazo universal para sabonetes ou velas. Produtos/variantes F3 começam sem saldo implícito; KIT não tem composição nesta fase.
- **RN-42 — Movimentações e ajustes:** toda alteração de saldo gera movimento append-only com quantidade, unidade, instante, responsável, motivo, origem e identificador de operação idempotente. Prever estoque inicial, entradas/saídas manuais, perdas, descartes, ajustes de inventário e estornos compensatórios vinculados ao original. Ajuste recebe quantidade física contada; o servidor calcula a diferença. Em estoque por lote, o ajuste identifica o lote. Não editar/excluir movimentos, permitir saldo físico negativo nem atualizar saldo sem atualizar o histórico na mesma transação. Compras, produção e vendas automáticas ficam para as etapas correspondentes.
- **RN-43 — Reservas e disponibilidade interna:** saldo físico, utilizável, reservado e disponível são distintos; disponível é físico utilizável menos reservas válidas. Reserva compromete disponibilidade sem baixar o físico, identifica insumo ou lote, quantidade, origem, estado e expiração quando aplicável. Operações de reservar, liberar e consumir reserva são consistentes e idempotentes. Não criar reservas ao navegar no catálogo. Reservar materiais no planejamento e liberar reservas em cancelamentos ocorrerá nas fases próprias; consumo de materiais e entrada de acabado ao concluir produção deverão ser atômicos. Política de reserva de pedido: checkout/finalização, não carrinho; prazo PIX e baixa física permanecem pendentes. Tratamento de reserva quando lote vence, é bloqueado ou danificado será definido para evitar dupla subtração.
- **RN-44 — Mínimos e autorização de estoque:** estoque mínimo é configurável por insumo e por variante de produto acabado. Alertas administrativos usam saldo disponível, descontando reservas e quantidades inelegíveis. Operações administrativas de estoque na F4 são restritas a `ADMIN`.
- **RN-45 — Disponibilidade pública (F4.6):** publicação e disponibilidade são independentes; produtos publicados que atendem às regras F3 permanecem visíveis mesmo esgotados. Produto tem disponibilidade quando pelo menos uma variante ativa tem saldo disponível; detalhes apresentam estado por variante, sem trocar automaticamente a variante padrão. Cliente pode selecionar outra variante disponível. A vitrine apresenta somente `Disponível`, `Últimas unidades` ou `Esgotado`; faixa inicial de 1 a 3 unidades para `Últimas unidades` é parâmetro a confirmar no detalhamento F4.6. DTOs públicos não expõem quantidades exatas, custos, lotes ou reservas. O backend exclui lotes vencidos, bloqueados, danificados e quantidades reservadas; consultas refletem movimentações e elegibilidade sem promessa de push em tempo real. F4 não implementa compra, carrinho ou checkout.

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

| ID        | Referências            | Cenário verificável                                                                                                                                                                   |
| --------- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **CA-01** | RF-01–03, RNF-01–02    | Cliente não acessa pedido de outro; funcionário não executa operação proibida mesmo chamando a API diretamente; credenciais inválidas são rejeitadas.                                 |
| **CA-02** | RF-04–05, RN-01        | Produto publicado com 3 unidades permite até 3 unidades disponíveis; produto zerado não pode ser comprado; despublicado não aparece como comprável.                                   |
| **CA-03** | RF-06–08, RN-02, RN-38 | Entradas e ajustes preservam histórico; kg/g e L/ml convertem dentro da mesma dimensão; massa e volume não são somados nem convertidos genericamente.                                  |
| **CA-04** | RF-09–10, RN-04        | Ficha com 100 g de base e 5 ml de essência por unidade, saldos livres 850 g e 30 ml, resulta em capacidade de 6 unidades.                                                             |
| **CA-05** | RF-11, RN-03           | Duas ordens concorrentes não reservam mais material que o saldo disponível; criação rejeitada não deixa reserva parcial.                                                              |
| **CA-06** | RF-13–14, RN-05–06     | Conclusão válida consome a reserva e aumenta estoque acabado uma vez; repetição não duplica; cancelamento permitido libera reserva remanescente.                                      |
| **CA-07** | RF-15–17, RN-07        | Carrinho não garante saldo; duas tentativas simultâneas sobre a última unidade não resultam em duas compras confirmadas.                                                              |
| **CA-08** | RF-16–19, RN-09        | Pedido registra preço e quantidade originais; mudança posterior no catálogo não modifica histórico; cliente vê somente seus pedidos.                                                  |
| **CA-09** | RF-06–08, RF-26, RN-08 | Venda externa lançada manualmente reduz saldo vendável e registra receita uma única vez, sem criar pedido online fictício; lançamento repetido não duplica movimentações.             |
| **CA-10** | RF-20, RN-14           | **Se integração aprovada:** notificação válida confirma cobrança correspondente; notificação duplicada não duplica pagamento/baixa; comprovante enviado não confirma automaticamente. |
| **CA-11** | RF-21, RN-15           | Para endereço elegível em Vacaria, checkout oferece retirada sem frete ou entrega local de R$ 10; para Correios, preço e prazo dependem de cotação válida antes da confirmação.       |
| **CA-12** | RF-22–25               | **Se módulos aprovados:** indicadores conferem com dados de origem, sugestões não alteram mínimo e alertas não são prometidos sem canal configurado.                                  |
| **CA-13** | RNF-04–05, RNF-10      | Jornadas principais são utilizáveis no celular, com campos identificados, foco e estados de erro compreensíveis.                                                                      |
| **CA-14** | RN-10–11, RNF-07       | Cálculo de preço/margem usa valores monetários consistentes e distingue margem de materiais de lucro líquido; histórico não muda com novos custos.                                    |

| **CA-15** | RF-03, RN-16 | Cadastro exige conta, CPF válido, nome, e-mail, telefone e endereço completo; cliente não acessa CPF/endereço de outro cliente. |
| **CA-16** | RF-07, RN-17 | Lotes preservam fabricação e validade; lote vencido não compõe estoque vendável. |
| **CA-17** | RF-17, RN-07 | Carrinho não reserva; ao finalizar pedido, reserva acontece uma vez; cancelamento libera somente reserva remanescente. Prazo de expiração e baixa aguardam decisão da fase de pedidos. |
| **CA-18** | RF-03, RF-27–28, RN-21–28 | Cadastro deixa a conta não confirmada e cria uma solicitação inicial; link expira em 24 horas; reenvio não enumera contas, respeita cooldown/limite, evita duplicação e preserva histórico ao recuperar `DeadLetter`; credenciais corretas de `CLIENTE` não confirmado resultam em HTTP `403`/`EMAIL_NOT_CONFIRMED`, sem sessão. |
| **CA-19** | RF-29–30, RN-29–32 | Solicitação válida sempre apresenta resposta genérica; apenas conta confirmada pode receber recuperação; link de 1 hora redefine a senha uma única vez, invalida sessões anteriores e exige novo login. |
| **CA-20** | RF-31, RNF-04–05 | Os cinco campos de senha começam ocultos; controles independentes mostram/ocultam sem alterar valor, validação ou submeter formulário; acessibilidade por teclado e foco visível; limpeza/sucesso restaura ocultação. |
| **CA-21** | RF-04, RN-34–35 | Medidas opcionais persistem em unidades canônicas; características físicas são a fonte da medida derivada; combinações incompletas são permitidas, combinações duplicadas são rejeitadas mesmo sob concorrência e registros legados mantêm seus dados sem inferência de medidas ou características. |
| **CA-22** | RF-04, RN-36–37 | Migration aditiva preserva produtos publicados legados sem imagens; domínio exige imagem principal para nova publicação, mantém até cinco imagens e impede vínculo imagem-variante entre produtos; nenhuma imagem ou URL de arquivo é criada automaticamente. |
| **CA-23** | RF-06, RN-38 | Conversões físicas compatíveis são exatas e guardam quantidade original, unidade e fator; contagem fracionada e conversão genérica entre massa e volume são rejeitadas. |
| **CA-24** | RF-06, RN-39 | Insumo sem lotes mantém saldo agregado; insumo controlado mantém saldo por lote; FEFO escolhe lote elegível com vencimento mais próximo; vencido/bloqueado permanece físico, mas não utilizável. |
| **CA-25** | RF-06, RN-40 | Entrada distingue custo desconhecido de custo zero e preserva custo histórico; saldo inicial sem custo conhecido é aceito; nenhum método de valorização não aprovado é inferido. |
| **CA-26** | RF-07, RN-41 | Lote acabado vincula variante existente, mantém quantidade inteira, validade aplicada ao lote e identificador único mesmo sob concorrência; alterar prazo padrão não altera lotes anteriores. |
| **CA-27** | RF-06–08, RN-42–44 | Ajuste compara contagem física com saldo, identificando lote quando aplicável; repetição idempotente não duplica movimento; saldo e histórico são atômicos e saldo físico não fica negativo. Mínimos alertam pelo disponível. |
| **CA-28** | RF-05, RN-45 | Na F4.6, produto publicado segue visível esgotado; disponibilidade é por variante ativa, com apenas os três estados públicos e sem quantidade, custo, lote ou reserva no DTO. |

Os números dos cenários são **exemplos de teste a executar**, não resultados de execução.

## 11. Dados e integrações

### 11.1 Entidades conceituais

| Entidade                              | Dados e relações principais                                                              |
| ------------------------------------- | ---------------------------------------------------------------------------------------- |
| Usuário / perfil / permissão          | Identificação, credencial protegida, papel e autorizações                                |
| Cliente / endereço                    | Nome, CPF protegido, e-mail, telefone, endereços completos e pedidos próprios            |
| Produto / categoria / foto / variação | Descrição, preço, publicação, variações, estoque acabado e imagens                       |
| Lote de produto acabado               | Produto/variação, quantidade, data de fabricação, validade e movimentações               |
| Matéria-prima                         | Nome, unidade, mínimo, saldo e movimentos                                                |
| Ficha técnica / componente            | Produto, material, quantidade, unidade e versão aplicável                                |
| Ordem de produção                     | Produto, quantidade, prazo, estado, reservas, consumo e resultado                        |
| Reserva / movimentação                | Origem, quantidade, unidade, motivo, instante e responsável                              |
| Carrinho / pedido / item              | Cliente, produto, quantidade, preços capturados, estados e totais                        |
| Venda externa / item                  | Canal, produtos, quantidades, valores, pagamento, movimentações e receita correspondente |
| Pagamento                             | Pedido, método, estado, referência do provedor e eventos, se integrado                   |
| Entrega                               | Modalidade, endereço, frete, rastreamento e estado                                       |
| Fornecedor / compra                   | Origem e custo de materiais, se aprovado                                                 |
| Despesa / agenda / nota               | Registros administrativos, se aprovados                                                  |
| Auditoria                             | Ator, ação, instante e referência da operação                                            |

O esquema físico, índices, migrações e contratos serão definidos no TDD, não neste PRD.

### 11.2 Integrações

| Integração        | Situação                                                    | Dependência                                                                                                     |
| ----------------- | ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Trienta           | **Substituição total planejada, sem integração automática** | Catálogo, preços e descrições disponíveis; conferir exportação ou migrar manualmente, validar saldos            |
| Pix / Sicredi     | **Sicredi é banco PJ; provedor não definido**               | Verificar API Pix disponível para a conta, taxas, credenciais, webhooks, homologação e alternativa intermediada |
| Correios          | **Utilizados manualmente hoje**                             | Validar API/contrato, preços, CEP de origem, peso e medidas                                                     |
| WhatsApp          | **Canal de atendimento atual**                              | Link de contato pode ser simples; automação exige decisão própria                                               |
| Fotos de produtos | **Necessárias**                                             | Definir armazenamento, limites, tratamento e custos no TDD                                                      |

## 12. Riscos, premissas e dependências

| Tema                                | Risco e encaminhamento                                                                                                                                                                                                                        |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Prazo acadêmico                     | Data informada: 30/11/2026; acompanhamento e eventual ajuste de escopo serão decididos pelo solicitante, sem alterar silenciosamente os requisitos                                                                                            |
| Divergência de estoque entre canais | Vendas externas precisam ser lançadas manualmente com agilidade; enquanto não lançadas, a disponibilidade online pode estar desatualizada                                                                                                     |
| Reserva de produção                 | Reserva sem política de cancelamento/consumo pode imobilizar insumos indefinidamente; definir transições                                                                                                                                      |
| Pedido sem pagamento                | Reserva de produto pode expirar ou bloquear estoque; definir janela, cancelamento e recuperação                                                                                                                                               |
| Pix                                 | Integração depende de provedor, contrato, ambiente de testes e segurança; não prometer confirmação sem infraestrutura                                                                                                                         |
| Correios                            | Preço/prazo dependem de serviços e dados corretos; prever indisponibilidade e política alternativa                                                                                                                                            |
| Custos e precificação               | Fórmula informada não cobre necessariamente mão de obra, embalagem, taxas e despesas; validar demonstrativos                                                                                                                                  |
| Dados de cliente                    | CPF foi escolhido como obrigatório; justificar finalidade, restringir acesso, definir retenção e validar obrigações de privacidade antes do uso real                                                                                          |
| Produtos naturais                   | Não presumir certificação, segurança clínica ou alegações terapêuticas; conteúdo comercial precisa de validação da proprietária                                                                                                               |
| Fotografias e marca                 | Utilizar arquivos fornecidos/autorizados; preservar identidade da marca                                                                                                                                                                       |
| Testes com proprietária             | Fluxos de autenticação foram validados manualmente em ambientes isolados; ajustes visuais da E4.4 foram cobertos por testes automatizados e a repetição manual da E4.5 foi dispensada. Usabilidade das fases futuras ainda deve ser validada. |

## 13. Decisões encerradas e questões em aberto

### 13.1 Resoluções registradas nesta revisão

| ID original | Resultado                                                                                                                                                                                                                     |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P-01        | Entrega acadêmica informada para **30/11/2026**; solicitante acompanhará necessidade de ajuste de escopo.                                                                                                                     |
| P-02        | Perfis limitados confirmados; **matriz fina de permissões adiada** até após o administrador.                                                                                                                                  |
| P-03        | **Conta e CPF obrigatórios**, com nome completo, e-mail, telefone e endereço completo com CEP.                                                                                                                                |
| P-06        | Banco PJ identificado: **Sicredi**; **provedor/API Pix ainda pendente**.                                                                                                                                                      |
| P-07        | **Vacaria: entrega própria R$ 10 ou retirada local sem frete**; CEPs, horários e local exato pendentes.                                                                                                                       |
| P-11        | Variações afetam ficha técnica/estoque; lotes acabados exigem fabricação/validade, validade até o fim do dia, FEFO futuro; insumos podem configurar controle de lote e exigência de validade. |
| P-12        | **Vendas externas serão registradas manualmente no sistema**, com estoque e financeiro; não apenas ajustes sem origem.                                                                                                        |
| P-13        | Trienta contém catálogo, valores e descrições; **substituição total** confirmada; exportação não comprovada.                                                                                                                  |
| P-14        | Precificação deve considerar embalagem, mão de obra, frete, taxas e despesas; entradas de insumos preservam custo de aquisição, sem método definitivo de valorização.                                                        |
| P-16        | **Proposta inicial:** alertas internos no painel; canais adicionais pendentes.                                                                                                                                                |
| P-19        | Gate de confirmação no login de clientes implementado e validado: credenciais corretas de `CLIENTE` não confirmado resultam em HTTP `403` com código `EMAIL_NOT_CONFIRMED`; `RequireConfirmedEmail` global permanece `false`. |
| F4.0        | Planejamento de estoque aprovado: unidades físicas, conversões por insumo, lotes e validade configuráveis para insumos, custos históricos, lotes acabados, movimentos, reservas e disponibilidade pública na F4.6; nenhuma funcionalidade F4 implementada. |

### 13.2 Pendências operacionais e técnicas

| ID       | Decisão pendente                                                                   | Direção proposta / dependência                                                                                                                                                                                                                                                                                                                                                              |
| -------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **P-04** | Prazo de pagamento/expiração e momento da baixa física do pedido                    | A reserva acontece ao finalizar o pedido; prazo, expiração, pagamento tardio e baixa física após confirmação aguardam definição na fase de pedidos.                                                                                                                                                                                                                                      |
| **P-05** | Cancelamentos, reembolsos, pagamentos tardios e pedidos não pagos                  | Proposta: estados distintos, liberação idempotente de reservas e estorno conciliado com provedor.                                                                                                                                                                                                                                                                                           |
| **P-06** | API Pix direta do Sicredi ou intermediador; taxas e habilitação                    | Investigar documentação e disponibilidade real para conta PJ; não presumir integração.                                                                                                                                                                                                                                                                                                      |
| **P-07** | Abrangência da taxa local de R$ 10, endereço e horários de retirada                | Confirmar com proprietária antes de ativar checkout real.                                                                                                                                                                                                                                                                                                                                   |
| **P-08** | CEP de origem, dimensões, peso, modalidade Correios e contingência                 | Confirmar com proprietária e verificar serviços disponíveis.                                                                                                                                                                                                                                                                                                                                |
| **P-09** | Ordens com insumos insuficientes, cancelamento após início e produção parcial      | Proposta: permitir rascunho sem reserva, mas confirmar planejamento apenas com reserva integral; registrar execução parcial. Detalhar na F5/F6.                                                                                                                                                                                                                                           |
| **P-10** | Momento de consumo e rendimento real                                               | Proposta: reservar no planejamento e consumir conforme execução/conclusão, com ajustes auditáveis. Validar com a proprietária antes da F6.                                                                                                                                                                                                                                                 |
| **P-11** | Lotes de insumo, lote sem validade, alteração de configuração com histórico/saldo, perdas e descarte | FEFO aprovado para lotes elegíveis; lote acabado vale até o fim da data de vencimento e fica inelegível no dia seguinte. Definir seleção determinística de lotes sem validade e impedir alteração inconsistente de configuração quando houver histórico/saldo na F4.2/F4.4. Validar perdas/descarte com a proprietária. |
| **P-13** | Possibilidade de exportação do Trienta e método de conferência dos saldos iniciais | Validar exportação; senão, cadastrar e conferir manualmente.                                                                                                                                                                                                                                                                                                                                |
| **P-15** | Quais módulos condicionados integrarão a entrega e em qual ordem                   | Definir conforme evolução do projeto, sem declarar implementações antecipadamente.                                                                                                                                                                                                                                                                                                          |
| **P-16** | Notificações além dos alertas internos                                             | Definir necessidade de e-mail, WhatsApp ou push posteriormente.                                                                                                                                                                                                                                                                                                                             |
| **P-17** | Permissões por operação para funcionários                                          | Definir após o fluxo administrativo da proprietária.                                                                                                                                                                                                                                                                                                                                        |
| **P-18** | Regras de rateio de despesas e apresentação de margem/lucro                        | Validar com proprietária antes de automatizar sugestão de preços.                                                                                                                                                                                                                                                                                                                           |
| **P-20** | Ativação de produção dos fluxos de e-mail                                          | Confirmação, reenvio e recuperação foram enviados e validados manualmente com Resend em ambiente isolado; isso não constitui homologação de produção. Antes de ativar em produção, configurar e validar domínio verificado, remetente, gestão segura de segredos, URL pública HTTPS, Data Protection persistente/compartilhada, proxies confiáveis e ativação explícita do sender e worker. |
| **P-21** | Armazenamento de imagens em produção                                               | O filesystem local atende ao desenvolvimento; selecionar e configurar armazenamento durável/privado, credenciais, backup e entrega de conteúdo antes da implantação.                                                                                                                                                                 |
| **P-22** | Informações institucionais públicas                                               | Confirmar contatos oficiais, URLs de redes sociais e informações de atendimento antes de publicar links ou compromissos comerciais; não inventar dados.                                                                                                                                                                               |
| **P-23** | Faixa de “Últimas unidades” e atualização da disponibilidade pública              | Proposta inicial de 1 a 3 unidades, sem expor quantidade exata; confirmar limite e estratégia de atualização no detalhamento da F4.6.                                                                                                                                                                                        |
| **P-24** | Precisão SQL de quantidades e custos                                              | Quantidades físicas até seis casas decimais aprovadas; mapear precisão/limites SQL, custo total e custo por unidade-base sem arredondamento prematuro na F4.1/F4.2.                                                                                                                                                             |
| **P-25** | Prazos padrão de validade por produto/variante                                     | O modelo pode definir um prazo padrão e aceitar validade real por lote; estabelecer origem e unidade do prazo no detalhamento da F4.3, sem validade universal.                                                                                                                                                                |

**Regra de governança:** propostas acima são hipóteses de projeto aceitas como direção inicial pelo solicitante, mas ainda exigem detalhamento/validação antes de fluxos financeiros ou de estoque em produção.

## 14. Encerramento da F2, F3 e situação atual

F2 — **Identidade e Autenticação** — e F3 — **Catálogo de Produtos** — estão concluídas. As entregas F3.1–F3.6.4 e o gate final F3.7 foram aprovados. F4.0 consolida regras e planejamento aprovados, mas não implementa funcionalidades nem altera schema ou dados persistentes. A próxima etapa de implementação é F4.1.

### 14.1 Resultado final da F2

- Cadastro, login/logout, sessão, gate de confirmação para `CLIENTE`, confirmação/reenvio e recuperação/redefinição de senha implementados.
- Outbox e envio assíncrono implementados; cadastro e recuperação, confirmação, reenvio e login pós-confirmação foram validados manualmente em ambientes isolados com Resend real.
- Gate final: **638 testes backend + 139 testes frontend = 777 aprovados**, sem falhas ou ignorados; builds backend e frontend aprovados.
- Os ajustes visuais da E4.4 foram validados por testes automatizados; a repetição manual da E4.5 foi dispensada. A F2.1.1 corrigiu somente testes backend; o gate frontend de 139 testes da F2.1 foi mantido.
- Os testes e envios reais ocorreram em ambientes isolados. Produção, domínio público, Azure e configuração operacional de e-mail não foram validados.

### 14.2 Próximos passos

1. Iniciar **F4.1 — Unidades físicas e domínio de estoque** após a revisão deste planejamento.
2. A integração da disponibilidade à vitrine está planejada para F4.6; não habilitar compra, carrinho ou checkout na F4. Decisões sobre prazo/baixa de pedidos (**P-04/P-05**), execução de produção (**P-09/P-10**), Correios e Pix continuam pendentes.
3. Antes da implantação, resolver armazenamento de imagens em produção (**P-21**), dados institucionais públicos (**P-22**) e os requisitos operacionais de autenticação/e-mail aplicáveis.

### 14.3 Plano incremental da F4

| Etapa | Escopo planejado | Dependências e aceite |
| ----- | ---------------- | --------------------- |
| F4.0 | Planejamento e modelagem; concluída documentalmente após aprovação. | Aprovação das regras, pendências explícitas e plano de testes; sem implementação, migrations ou dados de estoque. |
| F4.1 | Unidades físicas, conversões exatas e domínio comum de estoque. | Definir limites/precisão SQL. Testes de dimensão, fator, conversão exata, fração de `un`, overflow e preservação do catálogo F3. |
| F4.2 | Cadastro de insumos, formas de compra, entradas/saídas/ajustes, custos históricos e lote de insumo configurável. | F4.1. Testes de saldo inicial zero, custo desconhecido versus zero, lote opcional/obrigatório, ajuste por contagem, reversão, idempotência e transação ledger/saldo. |
| F4.3 | Estoque acabado por variante e lote, fabricação/validade e movimentos. | F4.1 para os tipos/unidades compartilhados; reutilizar o padrão transacional de F4.2. Testes de FK à variante existente, ID de lote único concorrente, quantidade inteira, validade copiada/sobrescrita e ausência de saldo implícito. |
| F4.4 | Saldos utilizáveis/disponíveis, reservas tipadas e FEFO interno. | F4.2–F4.3 e decisões pendentes de lote sem validade/bloqueio. Testes de concorrência na última unidade, reserva/liberação/consumo idempotentes, elegibilidade e nenhuma quantidade negativa. |
| F4.5 | Painel ADMIN para saldos, movimentos, mínimos e indicadores operacionais. | F4.2–F4.4. API autorizada em `ADMIN`, histórico e alertas baseados em disponível; testes de acesso negado, estados de UI e consistência com API. |
| F4.6 | Exibição pública de disponibilidade por variante e no produto. | Tecnicamente depende de F4.4 e do contrato público F3; segue a sequência após F4.5. Produtos publicados continuam visíveis esgotados; três estados sem expor quantidades/custos/lotes/reservas; validar limite de “Últimas unidades” e consistência com elegibilidade. Sem carrinho/compra/push. |
| F4.7 | Validação integrada, regressão, revisão operacional e encerramento documental. | F4.1–F4.6. Testes de regressão F2/F3 e cenários de concorrência SQL descartável; sem aplicação de migration em banco persistente não autorizado. |

As dependências são técnicas; detalhes de produção e pedidos, checkout, pagamentos, compras e fornecedores permanecem nas fases posteriores. Cada etapa deve preservar as funcionalidades F2/F3 e obter aprovação antes da fase seguinte.

### 14.4 Situação das etapas da F3

| Etapa | Situação ao encerramento |
| ----- | ------------------------ |
| F3.1 | Acesso administrativo inicial, autenticação ADMIN e proteção das rotas implementados. |
| F3.2.1–F3.2.2 | Categorias dinâmicas: domínio, persistência, endpoints administrativos e interface implementados. |
| F3.3.1–F3.3.6.3 | Produtos simples e variantes, características personalizáveis, combinações explícitas, medidas físicas canônicas opcionais, seis unidades comerciais e interface administrativa implementados; compatibilidade legada preservada. |
| F3.4.1–F3.4.4.1 | Metadados, processamento e API de imagens, galeria administrativa e vínculos com variantes implementados; filesystem local disponível para desenvolvimento. |
| F3.5.1 | Fotos principais e contagem da galeria integradas à listagem administrativa. |
| F3.6.1 | API pública de categorias, listagem paginada e detalhes de produtos publicados, com imagens e opções ativas, disponível sem autenticação. |
| F3.6.2–F3.6.2.2 | Homepage e catálogo Angular integrados à API, com busca, filtros, navegação e refinamentos responsivos validados localmente. |
| F3.6.3.1–F3.6.4 | Página individual e integração de navegação/autenticação validadas; produto inexistente/não publicado recebe tratamento público adequado, sem exposição administrativa. |
| F3.7 | Gate final aprovado: build backend, 803 testes backend (incluindo SQL Server descartável) e 271 testes Angular passaram, sem falhas ou ignorados; build de produção Angular aprovado. Permanecem avisos não impeditivos de orçamento CSS: `store-header.component.css` 5,10 kB / 4,00 kB e `store-home.component.css` 7,98 kB / 4,00 kB. |

**F4.0 — planejamento aprovado, ainda não implementado:** unidades físicas e conversões por insumo; rastreabilidade opcional de lote/validade para insumos; custos históricos; lotes acabados por variante; movimentos append-only; saldos transacionais e infraestrutura de reserva; painel ADMIN; disponibilidade pública na F4.6. Regras e critérios constam nas seções 7–10; a sequência está na seção 14.3 e as decisões pendentes na seção 13. Permanecem em aberto seleção de lotes sem validade, alteração de configuração de lote com saldo/histórico, parâmetros de precisão e validade padrão e o limite/atualização da indicação pública. Não foram criados código, migration, endpoint, saldo inicial ou dados de estoque.

**Estado atual:** PRD versão 0.14; F2, F3.1–F3.6.4 implementadas e gate final F3.7 aprovado; F4.0 planejada, nenhuma funcionalidade F4 implementada. O catálogo administrativo inclui características personalizáveis, combinações explícitas, medidas físicas canônicas, seis unidades comerciais e gerenciamento de fotos. A loja pública permite navegação anônima, pesquisa, filtros e paginação com os dados reais da API, além de uma página individual com galeria e seleção somente de combinações ativas efetivamente cadastradas; não oferece ações de compra nem indicação de estoque até F4.6. A apresentação de detalhes e fotos respeita os contratos públicos, sem inferir medidas a partir de textos. A homepage apresenta categorias retornadas pela API, composição institucional em CSS e links para login/cadastro existentes. Contatos oficiais, URLs de redes sociais e políticas de entrega/pagamento permanecem pendentes. O armazenamento de imagens no filesystem local não constitui solução de produção; armazenamento durável/privado precisa ser escolhido e configurado (**P-21**). A contagem explícita de opções não existe no DTO resumido; o selo de variações é exibido somente quando preço, unidade ou quantidade de medidas permite inferi-las. A listagem administrativa apresenta foto principal e quantidade pela resposta já carregada do produto, sem chamadas por item; a publicação continua exigindo categoria ativa, opção padrão ativa e foto principal, independente da disponibilidade de estoque. Edições mantêm IDs existentes; registros legados são preservados sem inferir medidas ou imagens. Produtos publicados legados sem imagens não são alterados pela migration. Composição de kits e medidas logísticas permanecem futuros. `RequireConfirmedEmail` global permanece `false`, com gate explícito para login de `CLIENTE` não confirmado, e requisitos de produção continuam pendentes.

## 15. Histórico de versões

| Versão | Data       | Alterações                                                                                                                                                                                                                                                               |
| ------ | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0.3    | 11/11/2026 | Cadastro, confirmação e reenvio de e-mail da F2; envio real pendente.                                                                                                                                                                                                    |
| 0.4    | 12/11/2026 | Recuperação e redefinição de senha implementadas e validadas manualmente com SQL isolado/remetente simulado; documentados controles de visibilidade dos cinco campos e pendências de produção.                                                                           |
| 0.5    | 14/11/2026 | F2 encerrada; gate final de 777 testes e builds aprovados; registro de validações manuais com Resend real em ambientes isolados; gate específico de confirmação para `CLIENTE`; F3 definida como próxima fase e branch reutilizável planejada após o commit documental.  |
| 0.6    | 15/11/2026 | Registradas as decisões de UX do cadastro de produtos, unidades comerciais `UN`/`KIT` e entrada de preço BRL; kits permanecem sem composição nesta etapa.                                                                                                                |
| 0.7    | 15/11/2026 | F3.3.6.1: documentados o modelo persistente de características e combinações explícitas, medidas canônicas opcionais, seis unidades comerciais, integridade e compatibilidade legada; API, DTOs, interface, estoque e medidas logísticas permanecem fora desta subetapa. |
| 0.8    | 15/11/2026 | F3.3.6.2–F3.3.6.3: API administrativa e interface para produtos simples, características físicas/personalizadas e combinações explícitas; edição preserva identidades e medidas legadas; vitrine, fotos, estoque, kits e medidas logísticas seguem futuros.              |
| 0.9    | 15/11/2026 | F3.4.1 concluída: modelo de domínio e migration aditiva para metadados de imagens e vínculos com variantes; preservação de produtos publicados legados sem imagens; upload, armazenamento, API, interface e vitrine permanecem nas próximas etapas. |
| 0.10   | 16/11/2026 | F3.4.3.2–F3.4.4.1 e F3.5.1: API e galeria administrativa de fotos, além da miniatura/contagem na listagem com orientação para publicação; vitrine, estoque e produção permanecem futuros. |
| 0.11   | 16/11/2026 | F3.6.2: homepage pública responsiva, categorias e catálogo real com busca/filtro/paginação, acesso à conta e limites explícitos de compra, estoque, página individual e contatos. |
| 0.12   | 16/11/2026 | F3.6.3.1: página pública individual com galeria, seletores para combinações ativas reais, estados recuperáveis e retorno com filtros; compra, estoque e contatos confirmados permanecem fora do escopo. |
| 0.13   | 16/11/2026 | Encerramento da F3.1–F3.6.4 e gate F3.7: 803 testes backend e 271 Angular aprovados, builds concluídos; registrados avisos CSS e pendências reais de produção e transição para F4 — Estoque. |
| 0.14   | 16/11/2026 | F4.0: consolidadas decisões de unidades e conversões, lotes/validade, custos de aquisição, movimentos, saldos/reservas, mínimos e disponibilidade pública F4.6; plano F4.1–F4.7 registrado. Nenhuma funcionalidade F4 implementada. |
