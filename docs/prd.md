# PRD — Xaquiamuni Produtos Naturais

**Plataforma web de comércio eletrônico e gestão de produção artesanal**

| Campo               | Informação                                                                                                                                                                                                                 |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Projeto             | Xaquiamuni — E-commerce e Gestão Artesanal                                                                                                                                                                                 |
| Versão do documento | 0.28 — F10.8.2, consolidação do MVP validado |
| Data | 20/11/2026 |
| Status | MVP validado em ambiente local isolado na F10.7.8. Homologação para uso real pendente. |
| Contexto            | Trabalho de Conclusão de Curso — Academia do Programador                                                                                                                                                                   |
| Fonte principal     | Entrevista com a proprietária (53 respostas, cinco esclarecimentos complementares e rodada posterior de decisões sobre cadastro, entregas, estoque e banco), logomarca e capturas do Instagram fornecidas pelo solicitante |
| Escopo              | Primeira entrega demonstrável e planejamento de evolução, com limites e pendências explícitos                                                                                                                              |

> **Regra de leitura:** “Confirmado” significa declarado pela proprietária ou pelo solicitante; “Implementado” significa comportamento verificado no código e coberto por testes; “Proposto” significa sugestão ainda sujeita a aprovação; “Pendente” exige decisão ou validação antes de ativar o fluxo afetado. Critérios de aceite descrevem verificações; resultados só são afirmados quando há evidência.

## 1. Resumo executivo

Desenvolver uma plataforma web responsiva para a **Xaquiamuni Produtos Naturais**, empreendimento de Vacaria–RS que fabrica sabonetes e velas artesanais desde 2021. A solução terá uma loja virtual para clientes comprarem **somente produtos disponíveis** e um painel administrativo para organizar catálogo, matérias-primas, fichas técnicas, ordens de produção, estoque, pedidos e operações do negócio.

O diferencial do produto será conectar a venda online à **organização da produção artesanal**, apontada pela proprietária como a principal necessidade. A solução deverá apoiar o planejamento de lotes, a reserva de matérias-primas, o cálculo da capacidade de produção e a atualização consistente de produtos acabados.

O MVP reúne loja com retirada e PIX manual, compras, fabricação, estoque, vendas externas, Financeiro e Agenda. Pagamento integrado, logística externa e análise avançada permanecem condicionados.

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
| Kits | Kits mistos de compra de matérias-primas disponíveis. Kits de produtos vendidos permanecem futuros. |
| Pagamento | Pix, dinheiro e cartão são meios declarados na operação da loja. O site implementa Pix manual. A proprietária deseja avaliar confirmação automática em etapa futura. |
| Frete                              | Correios para regiões atendidas; entrega própria em Vacaria por R$ 10 e retirada local sem frete, com abrangência exata a confirmar            |
| Cadastro do cliente                | Conta obrigatória antes da compra, com nome completo, CPF, e-mail, telefone e endereço completo com CEP                                        |
| Confirmação de e-mail              | Cadastro cria conta não confirmada e enfileira solicitação inicial; link válido por 24 horas; reenvio genérico com cooldown e limite por conta |
| Banco PJ                           | Sicredi; disponibilidade de API Pix e condições contratuais não verificadas                                                                    |
| Rastreabilidade | Variantes têm receitas e estoque próprios. A rotina trabalha com saldos consolidados. Lotes internos, datas e condições históricas são preservados. |
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

### 5.1 Núcleo prioritário da primeira entrega

1. **Contas e acesso:** cadastro, login com confirmação obrigatória para cliente, recuperação de senha e sessões de cliente e administrador. Funcionários têm papel de base, com permissões por tarefa pendentes.
2. **Catálogo e vitrine:** cadastro administrativo de produtos, categorias, preços, fotos, situação de publicação e consulta pública de itens disponíveis.
3. **Carrinho e pedidos:** carrinho local, checkout para retirada, reserva no pedido, PIX manual e histórico do titular.
4. **Estoque de produtos acabados:** entradas e saídas comuns sem seleção de lote, saldos consolidados e histórico. Ajustes técnicos permanecem disponíveis.
5. **Matérias-primas, fornecedores e compras:** unidades-base, apresentações geridas em Compras, conversões, recebimento integral, saldos, mínimos e reservas.
6. **Fichas técnicas:** vínculo entre produto e quantidades necessárias de cada matéria-prima.
7. **Ordens de produção:** planejamento, reserva de insumos, acompanhamento, conclusão com entrada de produtos acabados e cancelamento com liberação de reservas.
8. **Operação mobile-first:** telas de pedidos, estoque e produção. Painel consolidado permanece parcial.
9. **Vendas externas:** ADMIN registra a venda com baixa transacional e recebimentos integrais ou parciais.

10. **Financeiro e Agenda:** lançamentos, pagamentos a fornecedores, movimentações e visão geral de caixa. Calendário administrativo, com cadastro, edição e exclusão lógica de compromissos.

O núcleo foi validado localmente. A seção 13.3 distingue entregas parciais e pendências.

### 5.2 Expansões comerciais e de gestão — propostas condicionadas

- **Pix integrado:** geração de cobrança e confirmação automática por provedor, após escolha de fornecedor, custos e contrato.
- **Frete e entregas:** cálculo de Correios, entrega local e retirada, conforme regras de preço, abrangência e prazos a confirmar.
- **Financeiro avançado:** valoração, margem, lucro e conciliação bancária.
- **Análise de estoque:** mais vendidos, consumo de insumos, cobertura, alertas e sugestões de mínimo; sugestões não alteram estoque automaticamente.
- **Agenda ampliada:** recorrência, sincronização externa e lembretes.
- **Notificações:** avisos de novos pedidos e prazos, dependendo do canal escolhido.

As expansões acima dependem de decisão explícita. O fluxo de retirada local e Pix manual já integra o recorte entregue.

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

A administradora cadastra produtos, fornecedores e matérias-primas, gerencia apresentações em Compras e registra linhas simples ou kits mistos. Confere os materiais antes do recebimento integral, que dispensa entrada de validade na rotina. O rascunho não altera o estoque.

### J-02 — Planejar produção

A administradora escolhe uma receita ativa vinculada à variante e informa a quantidade planejada. A prévia mostra necessidades, saldos livres e faltas. O início revalida e reserva todos os materiais ou rejeita a ordem inteira.

### J-03 — Executar e concluir produção

A administradora informa a quantidade produzida e o consumo real de todos os materiais, dentro das reservas. A conclusão libera as sobras e dá entrada no lote acabado como Vendável na mesma operação. A ordem em andamento também pode ser cancelada, liberando reservas sem baixa física.

### J-04 — Comprar produto disponível

O cliente escolhe produtos disponíveis, autentica-se ou cria conta, revisa o carrinho e finaliza para retirada local. O servidor revalida preços e estoque e cria o pedido com a reserva. O cliente pode reencontrar o pedido e consultar, em **Meus pedidos**, as instruções de PIX manual quando habilitadas.

### J-05 — Atender pedido

A administradora confere o recebimento do Pix, confirma o pagamento, inicia a preparação e libera a retirada. A confirmação da entrega presencial baixa as unidades reservadas. O histórico preserva valores, quantidades e eventos.

### J-06 — Registrar venda externa

A administradora registra itens, canal, valores e recebimentos de uma venda externa. A baixa de estoque e a venda são gravadas na mesma transação. Pagamentos parciais deixam saldo a receber. O cancelamento exige ausência de recebimentos e reversão segura do estoque.

### J-07 — Consultar gestão

A administradora consulta estoque, produção e pedidos, acompanha movimentações, fluxo do período e posição atual e registra lançamentos ou pagamentos a fornecedores. Na Agenda, organiza compromissos por dia, com horário opcional.

### J-08 — Recuperar e redefinir senha

Uma pessoa não autenticada solicita recuperação informando o e-mail. A interface sempre apresenta uma confirmação genérica para um e-mail válido, sem revelar se a conta existe ou é elegível. Somente uma conta com e-mail confirmado pode gerar uma mensagem de recuperação. A pessoa abre o link temporário, informa e confirma uma nova senha e, após a redefinição bem-sucedida, entra novamente usando a nova senha; sessões anteriores deixam de ser válidas.

## 7. Requisitos funcionais

**Convenção:** os RF descrevem escopo entregue e evolução. Itens parciais, condicionados e futuros estão identificados nas regras e na seção 13.3.

### 7.1 Identidade e permissões

- **RF-01 — Autenticar usuários:** permitir acesso de administrador e cliente, exigir autenticação para operações privadas e encerrar a sessão. O acesso por tarefa de funcionários permanece futuro.
- **RF-02 — Proteger acesso:** cliente consulta apenas seus próprios recursos. As operações administrativas exigem `ADMIN`. A matriz de permissões por tarefa permanece pendente.
- **RF-03 — Cadastrar cliente:** exigir conta antes da compra, com nome completo, CPF, e-mail, telefone e endereço completo (CEP, UF, cidade, bairro, logradouro, número e complemento opcional); validar formato/dígitos do CPF e proteger os dados. A conta nasce não confirmada e a solicitação inicial de confirmação é enfileirada no cadastro. Após cadastro bem-sucedido, a interface apresenta estado de conclusão e ações explícitas para login e confirmação, sem autenticar ou reenviar automaticamente. A necessidade de dados adicionais pelo provedor/logística deve ser reavaliada.

### 7.2 Catálogo e estoque

- **RF-04 — Gerir produtos:** cadastrar, editar, listar e despublicar produtos com nome, descrição, categoria, preço, fotos e tipo de produção; permitir variantes que alterem estoque, ficha técnica e rastreabilidade, com identidade própria quando necessário. No cadastro administrativo, oferecer produto simples (uma versão e um preço) ou produto com opções, sem persistir essa escolha como tipo distinto. Produtos simples podem ter peso e volume líquidos opcionais; a interface aceita g/kg e ml/L e envia valores canônicos sem arredondamento. O catálogo pode definir características por produto — como peso, volume, cor, fragrância, tamanho ou material — e registrar somente as combinações de valores efetivamente comercializadas. Cada variante mantém identidade, estado, unidade comercial e preço próprios. As unidades comerciais são `UN`, `KIT`, `PAR`, `PCT`, `CX` e `CJ`; composição de kits permanece fora do escopo atual. A interface administrativa reconstrói a edição com IDs persistidos, preserva variantes e oferece referências temporárias para cadastrar todo o agregado em uma única operação. A API e o formulário administrativo suportam características, medidas e combinações. O domínio e o SQL Server modelam metadados e vínculos de imagens; upload, armazenamento e galeria administrativa foram implementados nas F3.4.3.2–F3.4.4.1. A API pública de consulta é F3.6.1, a homepage/listagem é F3.6.2 e a página pública individual é F3.6.3.1. Carrinho e checkout integram a entrega comercial atual.
- **RF-05 — Consultar vitrine:** exibir produtos publicados, preços, medidas, combinações reais e disponibilidade por opção. Produto publicado esgotado pode continuar visível. A compra revalida o saldo no servidor.
- **RF-06 — Gerir insumos:** cadastrar materiais com unidade-base, mínimo opcional e saldos consolidados. Registrar movimentos auditáveis e preservar políticas históricas. Gerir apresentações em Compras. O cadastro não gera saldo.
- **RF-07 — Gerir estoque acabado:** controlar quantidades inteiras por variante e rastreabilidade interna por lote. Entradas comuns dispensam seleção de lote, fabricação informada e validade. Ajustes por posição ficam em áreas avançadas.
- **RF-08 — Controlar saldos, reservas e disponibilidade:** distinguir físico, utilizável, reservado e livre. Reservas produtivas comprometem insumos e reservas de pedidos comprometem lotes acabados. O histórico e os saldos são atualizados de forma atômica nas operações de estoque.

- **RF-25 — Gerir fornecedores e compras (implementado):** cadastrar, editar e inativar fornecedores. Registrar compras em rascunho com linhas simples e kits mistos. Confirmar o recebimento integral para registrar entradas vinculadas à compra e preservar valores conhecidos e desconhecidos.

### 7.3 Produção artesanal

- **RF-09 — Gerir receitas:** vincular cada receita a uma variante, com rendimento esperado e matérias-primas em unidade-base. Revisões são versionadas e preservam ordens existentes.
- **RF-10 — Calcular necessidade e capacidade:** a prévia calcula o consumo para a quantidade informada e indica faltas após reservas. A consulta da capacidade máxima fabricável permanece pendente.
- **RF-11 — Iniciar ordem:** selecionar a receita e a quantidade planejada, registrar sua versão e reservar integralmente os materiais elegíveis.
- **RF-12 — Acompanhar ordem:** consultar ordens em andamento, concluídas e canceladas, com materiais, histórico e aviso de reserva inelegível. Ordens novas preservam os nomes do produto e da variante. Ordens anteriores exibem os nomes atuais com aviso.
- **RF-13 — Concluir produção:** registrar rendimento e consumo reais, liberar sobras e adicionar o lote acabado como Vendável numa única operação. A disponibilidade pública depende de publicação, validade e saldo livre.
- **RF-14 — Cancelar produção:** permitir cancelamento de ordem em andamento sem consumo registrado, com liberação integral das reservas e histórico preservado.

### 7.4 Compra e pedidos

- **RF-15 — Gerir carrinho:** adicionar/remover itens, ajustar quantidades e recalcular subtotal, sem garantir estoque apenas pela presença no carrinho.
- **RF-16 — Criar pedido:** revalidar cliente, preço, disponibilidade e total no servidor para retirada local, preservando itens e valores históricos.
- **RF-17 — Controlar estoque do pedido:** reservar ao criar o pedido, liberar ao cancelar ou expirar e realizar a baixa física na entrega presencial, impedindo comprometer a mesma unidade duas vezes.
- **RF-18 — Acompanhar pedidos:** permitir consulta de pedidos próprios pelo cliente e gestão por usuários autorizados; distinguir estados de pedido, pagamento e entrega.
- **RF-19 — Registrar andamento:** iniciar preparação, liberar retirada e confirmar entrega presencial, preservando autoria e histórico. Envio e rastreamento são futuros.
- **RF-26 — Registrar vendas externas (implementado):** permitir ao ADMIN registrar itens, canal, valores e recebimentos parciais ou integrais, com baixa de estoque atômica e sem gerar pedido do site.

### 7.5 Recursos complementares e condicionados

- **RF-20 — Integrar Pix (condicionado):** gerar cobrança pelo provedor escolhido e confirmar somente após verificação confiável do estado junto ao provedor; tratar notificações repetidas e falhas.
- **RF-21 — Calcular frete (condicionado):** oferecer, para clientes elegíveis de Vacaria, retirada local sem frete ou entrega própria por R$ 10; para demais localidades atendidas, calcular Correios conforme CEP, peso, dimensões e disponibilidade de integração. Confirmar abrangência, endereço de retirada e regras de contingência.
- **RF-22 — Registrar financeiro (núcleo implementado):** registrar e cancelar lançamentos manuais, pagar compras e consultar Movimentações e Visão geral. O fluxo reúne PIX confirmado, recebimentos de vendas externas, pagamentos de compras e lançamentos manuais ativos. A posição atual de pendências independe do período. Compras de total desconhecido ficam **Valor a conferir**. Recebimento físico e consumo de produção não movimentam caixa. Valoração, margem e lucro permanecem futuros.
- **RF-23 — Analisar estoque (condicionado):** mostrar itens críticos, consumo, giro e sugestões de mínimo fundamentadas em histórico suficiente, sem alteração automática de mínimos.
- **RF-24 — Gerir agenda e notas (núcleo implementado):** calendário mensal ADMIN, com compromissos de dia inteiro ou com horário, cadastro, edição e exclusão lógica. Versão e idempotência protegem escritas. Conflitos exigem conferência da situação antes de nova escrita. Recorrência e alertas externos permanecem futuros.
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
- **RN-03 — Reserva no início:** iniciar exige saldo livre integral para todos os materiais. A reserva reduz o saldo livre sem reduzir o físico. A prévia é informativa.
- **RN-04 — Capacidade produtiva (pendente):** a capacidade máxima pretendida é limitada pelo menor quociente inteiro entre saldo livre e consumo por unidade. A prévia atual informa necessidades e faltas para uma quantidade escolhida.
- **RN-05 — Histórico da ficha:** mudanças futuras de ficha técnica não alteram silenciosamente quantidades e custos capturados por ordens existentes.
- **RN-06 — Conclusão consistente:** cada material exige consumo real positivo e dentro da reserva, sem pressupor proporção com o rendimento. A conclusão baixa os insumos, libera sobras e dá entrada ao acabado Vendável atomicamente. Reenvio da mesma conclusão não duplica estoque. Cura, quarentena e aprovação não integram esse fluxo.
- **RN-07 — Reserva de produto acabado:** carrinho não reserva. O pedido aguarda pagamento por 24 horas. Pagamento confirmado dentro do prazo mantém a reserva até retirada. A baixa física ocorre na entrega presencial. Cancelamento autorizado ou expiração de pedido não pago libera o compromisso.
- **RN-08 — Vendas externas:** a venda registrada por ADMIN baixa estoque. Somente recebimentos efetivos entram no caixa. O cancelamento exige ausência de recebimento e reversão segura nas posições de origem.
- **RN-09 — Preço histórico:** pedido preserva preço unitário, descontos, frete e total aplicados no momento da confirmação; edição de catálogo não altera compras anteriores.
- **RN-10 — Valores monetários:** usar precisão decimal adequada, regras explícitas de arredondamento e moeda BRL; não usar ponto flutuante binário para cálculos financeiros.
- **RN-11 — Precificação declarada:** a proprietária informa dividir o custo unitário por `0,3` para margem-alvo de 70%, ou `0,4` para 60%. O custo deverá considerar, quando aplicáveis, matérias-primas, embalagem, mão de obra, frete, taxas e despesas. Distinguir custos por unidade, custos fixos rateados, margem bruta e lucro líquido; validar o método de rateio antes da implementação.
- **RN-12 — Prazos:** produção sob encomenda leva em média duas semanas segundo a proprietária, mas não gera autorização para venda sem estoque nem prazo garantido para todas as ordens.
- **RN-13 — Permissões:** autorização deve ser verificada no servidor para cada operação protegida; ocultar botão na interface não constitui proteção suficiente.
- **RN-14 — Pagamento:** a loja pode configurar instruções de Pix manual. A configuração aceita chave aleatória, e-mail, telefone ou CNPJ. A confirmação é administrativa, após conferência do recebimento. Cobrança integrada e confirmação automática permanecem condicionadas à escolha e homologação do provedor.
- **RN-15 — Entrega:** o checkout atual oferece retirada local. Entrega própria em Vacaria e Correios para RS/SC permanecem no escopo futuro, conforme decisão operacional e integração. Endereço e horários de retirada precisam de confirmação antes do uso real.
- **RN-16 — Cadastro e CPF:** compra exige conta com CPF validado, nome, telefone, e-mail e endereço completo; dados pessoais devem ser protegidos e utilizados para finalidades justificadas.
- **RN-17 — Lotes e validade:** novas operações comuns dispensam validade. Lotes datados legados mantêm elegibilidade até o fim do dia de vencimento. Alocação automática prioriza lotes datados elegíveis, depois lotes sem validade em ordem determinística. Vencidos, danificados, bloqueados ou em quarentena mantêm suas restrições.
- **RN-18 — Cancelamentos e exceções:** cancelar antes do consumo libera a reserva e preserva o histórico. Uma posição reservada inelegível não pode ser consumida. Conclusão parcial, remanejamento automático de lote e reembolso de pedido pago não fazem parte do fluxo atual.
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
- **RN-37 — Referências de imagens:** o SQL Server guarda somente metadados e chave de armazenamento opaca e única; não guarda bytes, Base64, caminho local absoluto ou URL temporária assinada. O nome original é metadado não confiável e não deve ser usado como caminho nem renderizado sem codificação apropriada. As operações de upload validam e processam o conteúdo. As URLs públicas servem o conteúdo sem revelar o caminho de armazenamento.
- **RN-38 — Unidades físicas e conversões de estoque:** unidades físicas são g, kg, ml, L e `un`, separadas das unidades comerciais e medidas descritivas do catálogo. Unidade-base é g, ml ou `un`; conversões exatas padrão são permitidas somente dentro da mesma dimensão (kg/g e L/ml). A fundação F4.1.1 representa quantidade-base com `decimal(18,6)`, intervalo de zero a `999999999999.999999`, validado após conversão sem arredondamento; contagem é inteira e não negativa. F4.1.2 implementa no domínio formas de compra pertencentes ao insumo, com fator positivo exato normalizado à unidade-base, quantidade inteira positiva de embalagens e snapshot imutável do fator aplicado na conversão. Movimentações conservam quantidade original, unidade de origem e fator aplicado. Cadastro de insumo não cria saldo.
- **RN-39 — Lotes de insumos:** a política histórica conserva o saldo agregado ou por lote. Entradas e recebimentos comuns de materiais rastreados geram lote interno sem validade, mesmo com exigência cadastrada no legado. Saldos vencidos ou bloqueados ficam no físico e fora do utilizável.
- **RN-40 — Custos de aquisição:** entradas de insumos podem registrar custo total e custo por unidade-base, preservados historicamente por entrada/lote. Custo desconhecido é diferente de custo zero; estoque inicial pode não ter custo conhecido. Precisão técnica implementada: total BRL `decimal(18,2)`; custo unitário derivado `decimal(38,14)`, midpoint away from zero a 14 casas. Total e quantidade-base originais são fonte de verdade. Custo médio ponderado, valoração definitiva, custo completo de produção e margem não estão definidos.
- **RN-41 — Lotes de produto acabado:** quantidades inteiras são vinculadas à variante e ao lote interno. O sistema registra identificação e fabricação dos novos lotes na rotina comum, com validade ausente. Datas e condições históricas são preservadas.
- **RN-42 — Movimentações e ajustes:** alterar saldo exige movimento auditável e atômico. Contagens com diferença gravam ajuste e exigem versão da posição; diferença zero não gera movimento. Reversões são compensatórias e limitadas pela integridade do saldo. Movimentos vinculados a compras e produção não admitem estorno avulso. Saída administrativa automática de acabados usa FEFO; operações de insumos que exigem posição continuam identificando-a.
- **RN-43 — Saldos e reservas:** saldo físico, utilizável, reservado e livre são distintos. A reserva vincula origem e posição de estoque e não baixa o físico. A disponibilidade é apurada por posição elegível, descontando os compromissos aplicáveis. Catálogo e carrinho não criam reservas.
- **RN-44 — Mínimos e autorização de estoque:** estoque mínimo é configurável por insumo e por variante de produto acabado. Consultas internas F4.2.5 classificam sobre saldo disponível: zero é `SemEstoque` (precedência mesmo sem mínimo ou com mínimo zero); com mínimo configurado, saldo positivo menor ou igual ao mínimo é `AbaixoOuNoMinimo` e acima é `Normal`; saldo positivo sem mínimo é `MinimoNaoConfigurado`. Alertas administrativos futuros usam saldo disponível, descontando reservas e quantidades inelegíveis. Operações administrativas de estoque na F4 são restritas a `ADMIN`.
- **RN-45 — Disponibilidade pública (F4.6):** um produto publicado pode continuar visível esgotado. A disponibilidade é verdadeira se ao menos uma variante ativa tiver saldo livre em lote elegível. O detalhe informa a disponibilidade de cada variante. A consulta pública não divulga quantidades, custos, lotes ou reservas e o checkout revalida o saldo.

- **RN-46 — Compras de matérias-primas:** o recebimento integral registra as entradas de uma vez, sem exigir validade na rotina. Apresentações, fatores e quantidades são preservados no histórico. O custo conjunto de kits permanece sem rateio implícito. Recebimento físico e pagamento são operações distintas.

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
| **CA-01** | RF-01–03, RNF-01–02 | Cliente só acessa pedidos próprios. A API administrativa exige `ADMIN` e recusa funcionário e cliente. Credenciais inválidas são rejeitadas. Permissões por tarefa para funcionário permanecem futuras. |
| **CA-02** | RF-04–05, RN-01        | Produto publicado com 3 unidades permite até 3 unidades disponíveis; produto zerado não pode ser comprado; despublicado não aparece como comprável.                                   |
| **CA-03** | RF-06–08, RN-02, RN-38 | Entradas e ajustes preservam histórico; kg/g e L/ml convertem dentro da mesma dimensão; massa e volume não são somados nem convertidos genericamente.                                 |
| **CA-04** | RF-09–10, RN-04 | **Parcial:** a prévia calcula necessidades para a quantidade escolhida. A consulta de capacidade máxima permanece futura. Exemplo de aceite futuro: 100 g e 5 ml por unidade, com 850 g e 30 ml livres, resultam em 6 unidades. |
| **CA-05** | RF-11, RN-03           | Duas ordens concorrentes não reservam mais material que o saldo disponível; criação rejeitada não deixa reserva parcial.                                                              |
| **CA-06** | RF-13–14, RN-05–06     | Conclusão válida consome a reserva e aumenta estoque acabado uma vez; repetição não duplica; cancelamento permitido libera reserva remanescente.                                      |
| **CA-07** | RF-15–17, RN-07        | Carrinho não garante saldo; duas tentativas simultâneas sobre a última unidade não resultam em duas compras confirmadas.                                                              |
| **CA-08** | RF-16–19, RN-09        | Pedido registra preço e quantidade originais; mudança posterior no catálogo não modifica histórico; cliente vê somente seus pedidos.                                                  |
| **CA-09** | RF-06–08, RF-26, RN-08 | Venda baixa estoque uma vez. Recebimentos parciais reduzem a pendência e entram no caixa pelos valores efetivos. Cancelamento somente sem recebimento e com reversão segura. |
| **CA-10** | RF-20, RN-14           | **Se integração aprovada:** notificação válida confirma cobrança correspondente; notificação duplicada não duplica pagamento/baixa; comprovante enviado não confirma automaticamente. |
| **CA-11** | RF-21, RN-15           | **Condicionado:** para endereço elegível em Vacaria, checkout oferece retirada sem frete ou entrega local de R$ 10; para Correios, preço e prazo dependem de cotação válida antes da confirmação.       |
| **CA-12** | RF-22–25 | Conciliar movimentações com as origens, fluxo do período e posição atual. Validar compra recebida e Agenda com conflito de versão recuperável. Sugestões de mínimo e alertas externos seguem futuros. |
| **CA-13** | RNF-04–05, RNF-10      | Jornadas principais são utilizáveis no celular, com campos identificados, foco e estados de erro compreensíveis.                                                                      |
| **CA-14** | RN-10–11, RNF-07 | **Parcial:** valores de venda e caixa preservam centavos e histórico. Preço sugerido, margem e lucro aguardam regras de custeio. |

| **CA-15** | RF-03, RN-16 | Cadastro exige conta, CPF válido, nome, e-mail, telefone e endereço completo; cliente não acessa CPF/endereço de outro cliente. |
| **CA-16** | RF-07, RN-17 | Lotes preservam fabricação e validade; lote vencido não compõe estoque vendável. |
| **CA-17** | RF-17, RN-07 | Carrinho não reserva; ao finalizar pedido, reserva acontece uma vez; cancelamento libera somente reserva remanescente. O pedido não pago expira em 24 horas. A baixa física ocorre na entrega presencial. |
| **CA-18** | RF-03, RF-27–28, RN-21–28 | Cadastro deixa a conta não confirmada e cria uma solicitação inicial; link expira em 24 horas; reenvio não enumera contas, respeita cooldown/limite, evita duplicação e preserva histórico ao recuperar `DeadLetter`; credenciais corretas de `CLIENTE` não confirmado resultam em HTTP `403`/`EMAIL_NOT_CONFIRMED`, sem sessão. |
| **CA-19** | RF-29–30, RN-29–32 | Solicitação válida sempre apresenta resposta genérica; apenas conta confirmada pode receber recuperação; link de 1 hora redefine a senha uma única vez, invalida sessões anteriores e exige novo login. |
| **CA-20** | RF-31, RNF-04–05 | Os cinco campos de senha começam ocultos; controles independentes mostram/ocultam sem alterar valor, validação ou submeter formulário; acessibilidade por teclado e foco visível; limpeza/sucesso restaura ocultação. |
| **CA-21** | RF-04, RN-34–35 | Medidas opcionais persistem em unidades canônicas; características físicas são a fonte da medida derivada; combinações incompletas são permitidas, combinações duplicadas são rejeitadas mesmo sob concorrência e registros legados mantêm seus dados sem inferência de medidas ou características. |
| **CA-22** | RF-04, RN-36–37 | Migration aditiva preserva produtos publicados legados sem imagens; domínio exige imagem principal para nova publicação, mantém até cinco imagens e impede vínculo imagem-variante entre produtos; nenhuma imagem ou URL de arquivo é criada automaticamente. |
| **CA-23** | RF-06, RN-38 | Conversões físicas compatíveis são exatas e normalizadas em unidade-base; quantidade-base obedece a `decimal(18,6)` sem arredondamento. Contagem fracionada, quantidade negativa e conversão entre dimensões são rejeitadas. Formas de compra são tratadas na F4.1.2. |
| **CA-24** | RF-06, RN-39 | Insumo sem lotes mantém saldo agregado; insumo controlado mantém saldo por lote; FEFO escolhe lote elegível com vencimento mais próximo; vencido/bloqueado permanece físico, mas não utilizável. |
| **CA-25** | RF-06, RN-40 | Entrada distingue custo desconhecido de custo zero e preserva custo histórico; saldo inicial sem custo conhecido é aceito; nenhum método de valorização não aprovado é inferido. |
| **CA-26** | RF-07, RN-41 | Entrada comum gera lote interno único para variante existente e quantidade inteira, com validade ausente. Datas e condições legadas permanecem intactas. |
| **CA-27** | RF-06–08, RN-42–44 | Ajuste compara contagem física com saldo, identificando lote quando aplicável; repetição idempotente não duplica movimento; saldo e histórico são atômicos e saldo físico não fica negativo. Mínimos alertam pelo disponível. |
| **CA-28** | RF-05, RN-45 | Produto publicado segue visível quando esgotado. Disponibilidade por produto e variante, sem divulgar quantidade, custo, lote ou reserva. |

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
| Receita / versão / material | Variante, rendimento, matérias-primas e quantidades exatas da versão aplicada. |
| Ordem de produção | Variante, nomes históricos quando disponíveis, quantidade planejada, versão de receita, estado, reservas, consumo e rendimento real. |
| Reserva / movimentação                | Origem, quantidade, unidade, motivo, instante e responsável                              |
| Carrinho / pedido / item              | Cliente, produto, quantidade, preços capturados, estados e totais                        |
| Venda externa / item / recebimento | Canal, itens, baixa de estoque, valores, recebimentos e saldo pendente. |
| Pagamento                             | Pedido, método, estado, referência do provedor e eventos, se integrado                   |
| Entrega                               | Modalidade, endereço, frete, rastreamento e estado                                       |
| Fornecedor / compra | Origem, linhas simples e kits mistos, quantidades, conversões, custos conhecidos e vínculos com recebimentos. |
| Financeiro / Agenda | Lançamentos manuais, pagamentos de compras, projeção de caixa e compromissos administrativos. |
| Auditoria                             | Ator, ação, instante e referência da operação                                            |

O esquema físico, índices, migrações e contratos serão definidos no TDD, não neste PRD.

### 11.2 Integrações

| Integração        | Situação                                                    | Dependência                                                                                                     |
| ----------------- | ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Trienta           | **Substituição total planejada, sem integração automática** | Catálogo, preços e descrições disponíveis; conferir exportação ou migrar manualmente, validar saldos            |
| Pix / Sicredi | Pix manual implementado, com confirmação administrativa. Sicredi é o banco PJ declarado. | Integração e confirmação automática dependem de escolha e homologação de provedor. |
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

| ID original | Resultado                                                                                                                                                                                                                                                                                                                                                                                               |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P-01        | Entrega acadêmica informada para **30/11/2026**; solicitante acompanhará necessidade de ajuste de escopo.                                                                                                                                                                                                                                                                                               |
| P-02        | Perfis limitados confirmados; **matriz fina de permissões adiada** até após o administrador.                                                                                                                                                                                                                                                                                                            |
| P-03        | **Conta e CPF obrigatórios**, com nome completo, e-mail, telefone e endereço completo com CEP.                                                                                                                                                                                                                                                                                                          |
| P-06        | Banco PJ identificado: **Sicredi**; **provedor/API Pix ainda pendente**.                                                                                                                                                                                                                                                                                                                                |
| P-07        | **Vacaria: entrega própria R$ 10 ou retirada local sem frete**; CEPs, horários e local exato pendentes.                                                                                                                                                                                                                                                                                                 |
| P-11        | Variações afetam ficha técnica/estoque; lotes internos e FEFO preservados. A exigência operacional inicial de validade foi superada na F10.7.5–F10.7.6.                                                                                                                                                                                                           |
| P-12        | **Vendas externas serão registradas manualmente no sistema**, com estoque e financeiro; não apenas ajustes sem origem.                                                                                                                                                                                                                                                                                  |
| P-13        | Trienta contém catálogo, valores e descrições; **substituição total** confirmada; exportação não comprovada.                                                                                                                                                                                                                                                                                            |
| P-14        | Precificação deve considerar embalagem, mão de obra, frete, taxas e despesas; entradas de insumos preservam custo de aquisição, sem método definitivo de valorização.                                                                                                                                                                                                                                   |
| P-16        | **Proposta inicial:** alertas internos no painel; canais adicionais pendentes.                                                                                                                                                                                                                                                                                                                          |
| P-19        | Gate de confirmação no login de clientes implementado e validado: credenciais corretas de `CLIENTE` não confirmado resultam em HTTP `403` com código `EMAIL_NOT_CONFIRMED`; `RequireConfirmedEmail` global permanece `false`.                                                                                                                                                                           |
| F4.0        | Planejamento de estoque aprovado: unidades físicas, lotes/validade, custos, saldos/reservas e disponibilidade pública. F4.0 é documental; implementação incremental consta em F4.1 e etapas futuras.                                                                                                                                                                                                    |
| F4.1.1      | Fundação de domínio implementada: dimensões, unidades físicas padrão, conversões exatas para unidades-base e `QuantidadeEstoque` limitada a `decimal(18,6)`; 14 testes focados e build backend aprovados.                                                                                                                                                                                               |
| F4.1.2      | Domínio de insumos, formas de compra específicas e snapshots de conversão implementados; 36 testes novos, 50 focados incluindo F4.1.1. Sem saldo ou persistência; build backend e diff check validados nesta subetapa.                                                                                                                                                                                  |
| F4.2.1      | Persistência de `InsumoEstoque` e `FormaCompraInsumo` implementada em migration aditiva e validada em testes SQL na fixture descartável; sem aplicação em banco persistente.                                                                                                                                                                                                                            |
| F4.2.2      | **Implementada:** domínio e persistência de lotes de insumos e saldos agregados ou por lote; sem movimentos, entradas ou custos. Migration validada somente em SQL descartável.                                                                                                                                                                                                                         |
| F4.2.3      | **Implementada e aprovada:** entradas iniciais e aquisições atualizam saldo e ledger append-only na mesma transação; snapshots de formas de compra e custos históricos são preservados. Testes focados e migration executados somente na fixture descartável.                                                                                                                                           |
| F4.2.4      | **Implementada e aprovada:** saídas manuais, perdas, descartes, contagem física e estornos compensatórios de insumos; sem reservas, seleção automática de lote, API ou UI. Migration `20261116182802_ImplementarSaidasAjustesEEstornosInsumo`; locks por responsável/chave e insumo, `rowversion` para contagem e idempotência de contagem sem diferença; validação SQL somente em fixture descartável. |
| F4.2.5      | **Implementada e aprovada:** consultas internas paginadas de insumos e movimentos, com saldos físico/utilizável/disponível, posições por lote, mínimo e formas de compra. Sem escrita ou alteração de schema; reserva exposta como zero até existir módulo próprio.                                                                                                                                     |
| F4.2.6      | **Implementada e aprovada:** API administrativa de insumos, formas de compra, posições, lotes, movimentos e operações de estoque. Restrita a `ADMIN`, com antifalsificação nas escritas; sem reservas ou migration. Calendário de validade configurável com proposta técnica `America/Sao_Paulo`, ainda pendente de aprovação da proprietária.                                                          |

### 13.2 Pendências operacionais e técnicas

| ID       | Decisão pendente                                                                   | Direção proposta / dependência                                                                                                                                                                                                                                                                                                                                                              |
| -------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **P-04** | Prazo e baixa física do pedido | Resolvida para retirada local: prazo de 24 horas, confirmação administrativa dentro do prazo e baixa física na entrega. |
| **P-05** | Cancelamentos, reembolsos, pagamentos tardios e pedidos não pagos                  | Proposta: estados distintos, liberação idempotente de reservas e estorno conciliado com provedor.                                                                                                                                                                                                                                                                                           |
| **P-06** | API Pix direta do Sicredi ou intermediador; taxas e habilitação                    | Investigar documentação e disponibilidade real para conta PJ; não presumir integração.                                                                                                                                                                                                                                                                                                      |
| **P-07** | Entrega local, endereço e horários de retirada | Retirada local implementada. Confirmar endereço e horários para operação real. Taxa e abrangência da entrega própria ainda pendentes. |
| **P-08** | CEP de origem, dimensões, peso, modalidade Correios e contingência                 | Confirmar com proprietária e verificar serviços disponíveis.                                                                                                                                                                                                                                                                                                                                |
| **P-09** | Insuficiência e cancelamento da produção | Implementados início com reserva integral e cancelamento antes de consumo. Produção parcial permanece fora do recorte atual. |
| **P-10** | Consumo e rendimento real | Implementados no encerramento único. Regras para produção parcial permanecem futuras. |
| **P-11** | Seleção de lotes sem validade | Resolvida. A alocação automática prioriza posições datadas elegíveis e usa ordem determinística para as posições sem validade. |
| **P-13** | Possibilidade de exportação do Trienta e método de conferência dos saldos iniciais | Validar exportação; senão, cadastrar e conferir manualmente.                                                                                                                                                                                                                                                                                                                                |
| **P-15** | Escopo do MVP | Vendas Externas, Financeiro de caixa e Agenda administrativa foram entregues. As extensões condicionadas seguem na seção 5.2. |
| **P-16** | Notificações além dos alertas internos                                             | Definir necessidade de e-mail, WhatsApp ou push posteriormente.                                                                                                                                                                                                                                                                                                                             |
| **P-17** | Permissões por operação para funcionários                                          | Definir após o fluxo administrativo da proprietária.                                                                                                                                                                                                                                                                                                                                        |
| **P-18** | Regras de rateio de despesas e apresentação de margem/lucro                        | Validar com proprietária antes de automatizar sugestão de preços.                                                                                                                                                                                                                                                                                                                           |
| **P-20** | Ativação de produção dos fluxos de e-mail                                          | Confirmação, reenvio e recuperação foram enviados e validados manualmente com Resend em ambiente isolado; isso não constitui homologação de produção. Antes de ativar em produção, configurar e validar domínio verificado, remetente, gestão segura de segredos, URL pública HTTPS, Data Protection persistente/compartilhada, proxies confiáveis e ativação explícita do sender e worker. |
| **P-21** | Armazenamento de imagens em produção                                               | O filesystem local atende ao desenvolvimento; selecionar e configurar armazenamento durável/privado, credenciais, backup e entrega de conteúdo antes da implantação.                                                                                                                                                                                                                        |
| **P-22** | Informações institucionais públicas                                                | Confirmar contatos oficiais, URLs de redes sociais e informações de atendimento antes de publicar links ou compromissos comerciais; não inventar dados.                                                                                                                                                                                                                                     |
| **P-23** | Indicação de “Últimas unidades” | Opcional. A API atual informa disponibilidade booleana, sem faixa de estoque baixo nem atualização em tempo real. |
| **P-24** | Precisão SQL de custos                                                             | **Resolvida tecnicamente na F4.2.3:** total BRL `decimal(18,2)`; custo por unidade-base derivado em `decimal(38,14)`, arredondado a 14 casas com midpoint away from zero. Total e quantidade-base originais são fonte de verdade; esse custo unitário não define método de valorização.                                                                                                     |
| **P-25** | Prazo padrão de validade | Compatibilidade para operações legadas. A rotina de novas entradas, recebimentos e conclusão dispensa prazo padrão ou validade informada. |
| **P-26** | Calendário de validade de insumos                                                  | A API resolve uma data operacional centralizada por `Estoque:Calendario:TimeZoneId`, padrão técnico proposto `America/Sao_Paulo`; o lote é elegível durante toda a data de vencimento nessa zona. A proprietária ainda precisa aprovar a zona operacional antes do uso em produção; instantes de auditoria continuam UTC e datas históricas não são reescritas.                             |

**Regra de governança:** propostas acima são hipóteses de projeto aceitas como direção inicial pelo solicitante, mas ainda exigem detalhamento/validação antes de fluxos financeiros ou de estoque em produção.

### 13.3 Prioridades restantes

| Classe | Pendência e evidência |
| --- | --- |
| A — Necessária para uso real | Confirmar retirada e homologar e-mail, domínio, armazenamento durável, segredos, backup e restauração. |
| B — Parcial ou futura | Capacidade máxima, permissões por tarefa, painel operacional consolidado, conciliação tardia e reembolsos. |
| C — Condicionada ou opcional | PIX integrado, entrega própria, Correios, rastreamento, análise avançada, valoração, margem, lucro, recorrência da Agenda e kits vendidos. |

## 14. Encerramento da F2, F3 e situação atual

As subseções 14.1 a 14.4 registram os gates anteriores. As restrições e pendências atribuídas a fases históricas descrevem o estado na data da entrega. O estado vigente está na seção 14.5.

### 14.1 Resultado final da F2

- Cadastro, login/logout, sessão, gate de confirmação para `CLIENTE`, confirmação/reenvio e recuperação/redefinição de senha implementados.
- Outbox e envio assíncrono implementados; cadastro e recuperação, confirmação, reenvio e login pós-confirmação foram validados manualmente em ambientes isolados com Resend real.
- Gate final: **638 testes backend + 139 testes frontend = 777 aprovados**, sem falhas ou ignorados; builds backend e frontend aprovados.
- Os ajustes visuais da E4.4 foram validados por testes automatizados; a repetição manual da E4.5 foi dispensada. A F2.1.1 corrigiu somente testes backend; o gate frontend de 139 testes da F2.1 foi mantido.
- Os testes e envios reais ocorreram em ambientes isolados. Produção, domínio público, Azure e configuração operacional de e-mail não foram validados.

### 14.2 Próximos passos

1. **Registro histórico F4.5.6:** aquisição sem custo e replay foram verificados por HTTP/SQL. O movimento preservou custo `null`, físico de `2.123456` e um único registro após reenvio. O roteiro manual original foi superado pela demonstração isolada e pelos ensaios de navegador da F10.7.8.
2. O planejamento original da F4 tratava a vitrine, o checkout e a produção como fases posteriores. O recorte entregue e suas pendências estão nas seções 13.3 e 14.5.
3. Antes da implantação, resolver armazenamento de imagens em produção (**P-21**), dados institucionais públicos (**P-22**) e os requisitos operacionais de autenticação/e-mail aplicáveis.

### 14.3 Plano incremental da F4

| Etapa  | Escopo planejado                                                                                                                                                                                              | Dependências e aceite                                                                                                                                                                                                                                                                            |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| F4.0   | Planejamento e modelagem; concluída documentalmente após aprovação.                                                                                                                                           | Aprovação das regras, pendências explícitas e plano de testes; sem implementação, migrations ou dados de estoque.                                                                                                                                                                                |
| F4.1   | Fundação de unidades físicas, quantidades, insumos e formas de compra no domínio.                                                                                                                             | Executada incrementalmente em F4.1.1 e F4.1.2; saldo, lote e movimento são posteriores.                                                                                                                                                                                                          |
| F4.1.1 | Tipos de dimensão/unidade e quantidade-base com conversões padrão exatas.                                                                                                                                     | **Implementada:** `decimal(18,6)`, limites e validação pós-conversão; build backend e 14 testes focados passaram; catálogo inalterado.                                                                                                                                                           |
| F4.1.2 | **Implementada no domínio:** insumo e formas de compra específicas, fatores exatos por insumo e snapshot imutável da conversão.                                                                               | 36 novos testes aprovados (50 focados incluindo F4.1.1), build backend e `git diff --check`; sem DbSets, migration, saldo ou movimento.                                                                                                                                                          |
| F4.2.1 | **Implementada e aprovada:** persistência dos cadastros de insumo e formas de compra, constraints SQL e concorrência otimista por `rowversion`.                                                               | Migration aditiva validada com Up/Down/Up, 4 testes SQL descartáveis e 53 testes focados totais; nenhuma aplicação persistente.                                                                                                                                                                  |
| F4.2.2 | **Implementada e aprovada:** estrutura de domínio e persistência para lotes e saldos de insumos; validade de calendário, bloqueio e total físico derivado. Sem movimento/entrada ou saldo positivo implícito. | Migration aditiva validada com Up/Down/Up e testes SQL/unitários/modelo; nenhuma aplicação persistente.                                                                                                                                                                                          |
| F4.2.3 | **Implementada e aprovada:** estoque inicial e aquisição de insumos, custo de aquisição opcional e ledger append-only; sem reservas, produção ou pedidos.                                                     | Migration `20261116173714_RegistrarEntradasInsumos`, com trigger contra UPDATE/DELETE; lock transacional por chave idempotente e por insumo, `rowversion` para saldo, snapshots e rollback atômico. Build e 92 testes focados aprovados em ambiente descartável; sem aplicação persistente.      |
| F4.2.4 | **Implementada e aprovada:** saídas, perdas, descartes, ajustes por contagem e estornos compensatórios de insumos.                                                                                            | Migration `20261116182802_ImplementarSaidasAjustesEEstornosInsumo`; lock por responsável/chave e insumo, contagem protegida por `rowversion`, idempotência inclusive sem diferença. Sem API, UI, reservas ou FEFO automático; validação SQL somente descartável.                                 |
| F4.2.5 | **Implementada e aprovada:** consultas internas paginadas somente leitura para insumos, posições e ledger, com disponibilidade derivada e classificação de mínimo.                                            | Sem migration ou alteração de dados; filtros/ordenação no SQL; saldos reservados expostos como zero até existir reserva real.                                                                                                                                                                    |
| F4.2.6 | **Implementada e aprovada:** API administrativa para cadastros, consultas e operações de estoque de insumos.                                                                                                  | 17 rotas restritas a `ADMIN`; antifalsificação em escritas; responsável derivado da identidade autenticada; reaproveita transações, idempotência e `rowversion`; sem migration. A zona padrão `America/Sao_Paulo` é uma proposta configurável ainda não aprovada pela proprietária.              |
| F4.3   | **Implementada e concluída:** estoque administrativo de produtos acabados vinculado a variantes existentes, com lotes, datas, política por variante e ledger.                                                                                                               | Domínio, persistência, 12 rotas ADMIN e painel Angular; entradas, saídas, transferências de condição, contagens com versão e estornos. Escritas protegidas por antifalsificação; operações de estoque idempotentes. Sem reservas de pedidos, produção ou checkout. |
| F4.4   | Reservas tipadas, saldos disponíveis e seleção FEFO interna, ainda não implementados.                                                                                                                          | Dependem de F4.2–F4.3 e de decisões pendentes sobre lotes sem validade/bloqueados; testes futuros de concorrência, reserva/liberação/consumo idempotentes e saldo não negativo. Não implica FEFO automático para pedidos. |
| F4.5   | Painel ADMIN de estoque de insumos.                                                                                                                                                                            | Entrega incremental: F4.5.1 cadastro/formas; F4.5.2 entradas; F4.5.3 posição/lotes/histórico; F4.5.4 saídas, perdas, descartes, contagem física e estorno; F4.5.5 validação integrada; F4.5.6 verificação final. Revisão final pendente. |
| F4.6   | Exibição pública de disponibilidade por variante e no produto.                                                                                                                                                | Tecnicamente depende de F4.4 e do contrato público F3; segue a sequência após F4.5. Produtos publicados continuam visíveis esgotados; três estados sem expor quantidades/custos/lotes/reservas; validar limite de “Últimas unidades” e consistência com elegibilidade. Sem carrinho/compra/push. |
| F4.7   | Validação integrada, regressão, revisão operacional e encerramento documental.                                                                                                                                | F4.1–F4.6. Testes de regressão F2/F3 e cenários de concorrência SQL descartável; sem aplicação de migration em banco persistente não autorizado.                                                                                                                                                 |

A tabela acima registra o plano original da F4. O estado vigente da cadeia está na seção 14.5.

### 14.4 Situação das etapas da F3

| Etapa           | Situação ao encerramento                                                                                                                                                                                                                                                                                                                 |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| F3.1            | Acesso administrativo inicial, autenticação ADMIN e proteção das rotas implementados.                                                                                                                                                                                                                                                    |
| F3.2.1–F3.2.2   | Categorias dinâmicas: domínio, persistência, endpoints administrativos e interface implementados.                                                                                                                                                                                                                                        |
| F3.3.1–F3.3.6.3 | Produtos simples e variantes, características personalizáveis, combinações explícitas, medidas físicas canônicas opcionais, seis unidades comerciais e interface administrativa implementados; compatibilidade legada preservada.                                                                                                        |
| F3.4.1–F3.4.4.1 | Metadados, processamento e API de imagens, galeria administrativa e vínculos com variantes implementados; filesystem local disponível para desenvolvimento.                                                                                                                                                                              |
| F3.5.1          | Fotos principais e contagem da galeria integradas à listagem administrativa.                                                                                                                                                                                                                                                             |
| F3.6.1          | API pública de categorias, listagem paginada e detalhes de produtos publicados, com imagens e opções ativas, disponível sem autenticação.                                                                                                                                                                                                |
| F3.6.2–F3.6.2.2 | Homepage e catálogo Angular integrados à API, com busca, filtros, navegação e refinamentos responsivos validados localmente.                                                                                                                                                                                                             |
| F3.6.3.1–F3.6.4 | Página individual e integração de navegação/autenticação validadas; produto inexistente/não publicado recebe tratamento público adequado, sem exposição administrativa.                                                                                                                                                                  |
| F3.7            | Gate final aprovado: build backend, 803 testes backend (incluindo SQL Server descartável) e 271 testes Angular passaram, sem falhas ou ignorados; build de produção Angular aprovado. Permanecem avisos não impeditivos de orçamento CSS: `store-header.component.css` 5,10 kB / 4,00 kB e `store-home.component.css` 7,98 kB / 4,00 kB. |

### 14.5 Estado vigente da cadeia operacional

- **F8.3:** fornecedores, compras e kits mistos, recebimento integral e rastreabilidade de entradas.
- **F8.4:** receitas versionadas e ordens com prévia, reserva, cancelamento e conclusão, com entrada imediatamente vendável.
- **F8.5:** recuperação de criação de compras após resposta incerta, ensaio encadeado por API .NET/SQL descartável e estabilização do histórico. A F8.5.4 verificou as seis telas administrativas em Chrome real a 375 e 1280 px. A fabricação encadeada até a reserva no checkout foi validada pela API, em execução distinta da verificação visual.

- **F10.2–F10.6:** Vendas Externas, Financeiro e Agenda administrativa entregues.
- **F10.7:** estoque simplificado, apresentações em Compras e recuperação financeira e da Agenda estabilizadas. A regressão F10.7.8 foi aprovada em ambiente local isolado. Consulte a seção 8.1 do [TDD](tdd.md#81-cenários-críticos-de-aceite) para os resultados por camada.

## 15. Histórico de versões

| Versão | Data       | Alterações                                                                                                                                                                                                                                                                                                                                       |
| ------ | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0.3    | 11/11/2026 | Cadastro, confirmação e reenvio de e-mail da F2; envio real pendente.                                                                                                                                                                                                                                                                            |
| 0.4    | 12/11/2026 | Recuperação e redefinição de senha implementadas e validadas manualmente com SQL isolado/remetente simulado; documentados controles de visibilidade dos cinco campos e pendências de produção.                                                                                                                                                   |
| 0.5    | 14/11/2026 | F2 encerrada; gate final de 777 testes e builds aprovados; registro de validações manuais com Resend real em ambientes isolados; gate específico de confirmação para `CLIENTE`; F3 definida como próxima fase e branch reutilizável planejada após o commit documental.                                                                          |
| 0.6    | 15/11/2026 | Registradas as decisões de UX do cadastro de produtos, unidades comerciais `UN`/`KIT` e entrada de preço BRL; kits permanecem sem composição nesta etapa.                                                                                                                                                                                        |
| 0.7    | 15/11/2026 | F3.3.6.1: documentados o modelo persistente de características e combinações explícitas, medidas canônicas opcionais, seis unidades comerciais, integridade e compatibilidade legada; API, DTOs, interface, estoque e medidas logísticas permanecem fora desta subetapa.                                                                         |
| 0.8    | 15/11/2026 | F3.3.6.2–F3.3.6.3: API administrativa e interface para produtos simples, características físicas/personalizadas e combinações explícitas; edição preserva identidades e medidas legadas; vitrine, fotos, estoque, kits e medidas logísticas seguem futuros.                                                                                      |
| 0.9    | 15/11/2026 | F3.4.1 concluída: modelo de domínio e migration aditiva para metadados de imagens e vínculos com variantes; preservação de produtos publicados legados sem imagens; upload, armazenamento, API, interface e vitrine permanecem nas próximas etapas.                                                                                              |
| 0.10   | 16/11/2026 | F3.4.3.2–F3.4.4.1 e F3.5.1: API e galeria administrativa de fotos, além da miniatura/contagem na listagem com orientação para publicação; vitrine, estoque e produção permanecem futuros.                                                                                                                                                        |
| 0.11   | 16/11/2026 | F3.6.2: homepage pública responsiva, categorias e catálogo real com busca/filtro/paginação, acesso à conta e limites explícitos de compra, estoque, página individual e contatos.                                                                                                                                                                |
| 0.12   | 16/11/2026 | F3.6.3.1: página pública individual com galeria, seletores para combinações ativas reais, estados recuperáveis e retorno com filtros; compra, estoque e contatos confirmados permanecem fora do escopo.                                                                                                                                          |
| 0.13   | 16/11/2026 | Encerramento da F3.1–F3.6.4 e gate F3.7: 803 testes backend e 271 Angular aprovados, builds concluídos; registrados avisos CSS e pendências reais de produção e transição para F4 — Estoque.                                                                                                                                                     |
| 0.14   | 16/11/2026 | F4.0: consolidadas decisões de unidades e conversões, lotes/validade, custos de aquisição, movimentos, saldos/reservas, mínimos e disponibilidade pública F4.6; plano F4.1–F4.7 registrado. Nenhuma funcionalidade F4 implementada.                                                                                                              |
| 0.15   | 16/11/2026 | F4.1.1: registradas dimensões e unidades físicas separadas do catálogo, conversões exatas para unidades-base, precisão `decimal(18,6)` e limites; fundação de domínio implementada e validada sem persistência.                                                                                                                                  |
| 0.16   | 16/11/2026 | F4.1.2: domínio de insumos, formas de compra específicas, conversão exata com embalagens inteiras e snapshot do fator; testes focados aprovados, sem persistência ou saldo.                                                                                                                                                                      |
| 0.17   | 16/11/2026 | F4.2.1: persistência de insumos/formas de compra, migration aditiva, constraints e `rowversion`; validada com quatro testes SQL em bancos descartáveis, sem aplicação persistente.                                                                                                                                                               |
| 0.18   | 16/11/2026 | F4.2.2: lotes de insumos e estrutura de saldos agregado/por lote com `decimal(18,6)`, validade por data civil, bloqueio e concorrência otimista; migration e testes validados somente em SQL descartável.                                                                                                                                        |
| 0.19   | 16/11/2026 | F4.2.3: entradas iniciais/aquisições transacionais e idempotentes, ledger append-only, snapshots de compra e custo BRL opcional; migration e 92 testes focados validados em SQL descartável. Data UTC para elegibilidade e ausência de endpoints/UI registradas como pendência/limite.                                                           |
| 0.20   | 16/11/2026 | F4.2.4: saídas, perdas, descartes, ajustes por contagem com `rowversion` e estornos compensatórios; idempotência de contagens sem diferença, migration aditiva, constraints e triggers append-only; validação somente em SQL descartável.                                                                                                        |
| 0.21   | 16/11/2026 | F4.2.5: consultas internas read-only paginadas de insumos, posições e movimentos; saldos físico/utilizável/disponível derivados, classificação de estoque mínimo e data UTC explícita. Sem migration, endpoints ou frontend; validação SQL somente descartável.                                                                                  |
| 0.22   | 16/11/2026 | F4.2.6: API ADMIN de estoque de insumos para cadastros, formas de compra, consultas, entradas, saídas, contagens e estornos; antifalsificação e ator autenticado. Calendário operacional centralizado e configurável, com `America/Sao_Paulo` como proposta pendente de aprovação. Sem migration ou frontend; validação SQL somente descartável. |
| 0.23   | 16/11/2026 | F4.5.3: painel ADMIN Angular para posição agregada/por lote e histórico de movimentações paginado, com filtros da API, decimais preservados, datas civis e consultas somente GET. Sem alteração de backend ou schema.                                                                                                                            |
| 0.24   | 16/11/2026 | F4.5.4: painel ADMIN Angular para saídas, perdas, descartes, contagem física com versão do saldo e solicitação de estorno; antifalsificação, idempotência e decimais exatos; sem alteração de backend, schema ou banco persistente.                                                                                                                  |
| 0.25   | 16/11/2026 | F4.5.5: 56 testes Angular e 29 testes HTTP/SQL Server aprovados usando somente bases temporárias da fixture local; sem E2E de navegador ou API real conectada a Angular; pendente avaliação, F4.5 não encerrada.                                                                                                                                        |
| 0.26   | 16/11/2026 | F4.5.6: teste HTTP/SQL de aquisição sem custo, posição, histórico e replay idempotente aprovado; navegador Angular/API não executado por falta de harness isolado; roteiro manual e revisão final pendentes.                                                                                                                                                 |
| 0.27 | 19/11/2026 | F8.5.5: sincronização do fluxo de compras, fabricação, estoque e checkout com o comportamento validado. |
| 0.28 | 20/11/2026 | F10.8.2: consolidação do MVP validado até F10.7.8 e das pendências de uso real. |
