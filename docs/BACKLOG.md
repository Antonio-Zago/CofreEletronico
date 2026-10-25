# Backlog — Cofre Eletrônico

Escopo: **Backend .NET 8**, **App Flutter** e **Firmware ESP32**, com broker **Mosquitto (MQTT)**, banco **PostgreSQL** e pagamento **Pix via Mercado Pago**.
O site de gerenciamento está fora do escopo por enquanto: as funções de administrador ficam disponíveis como endpoints `/admin` da API.
Detalhes técnicos (arquitetura, contrato MQTT, modelo de dados, fluxo) em [ARQUITETURA.md](ARQUITETURA.md). Planilha equivalente: [Backlog.xlsx](Backlog.xlsx).

**Prioridade:** P1 = MVP obrigatório · P2 = importante · P3 = desejável. **Sprints** de 2 semanas. **Pts** = story points (1 = poucas horas, 3 = alguns dias).

**Resumo:** 9 épicos · 22 user stories · 94 tarefas · 189 pontos.

## Roadmap

| Sprint | Foco | Pts |
|---|---|---|
| 1 | Fundação (E0), auth (US-03), compra de hardware e PoC MQTT (T-1901, T-2001) | 27 |
| 2 | Estantes/cofres (US-04/05), MQTT no backend (US-06), montagem da estante, telas de login | 38 |
| 3 | Firmware MQTT (US-20), locação (US-07), telas de mapa e estante | 37 |
| 4 | Pix e abertura (US-07/08), SignalR, telas de pagamento e locação ativa | 30 |
| 5 | Encerrar/estender, notificações, admin/monitoramento, deploy | 34 |
| 6 | Segurança (incl. rotação da chave dos segredos HMAC), E2E com hardware, piloto | 23 |

**Pontos por componente:** Infra 14 · Backend 94 · Mobile 35 · Firmware 22 · Docs 5 · Hardware 7 · QA 12

## Rastreabilidade com o Backlog1 original

| Item original | Onde está agora |
|---|---|
| US: cofre seguro para guardar pertences | E2, E4 (US-07/08), E7 |
| US: pagamento do tempo de uso pelo app | US-07, US-15 |
| US: plataforma admin (cadastro, monitoramento, exclusão) | US-04, US-11 (via API; site fora do escopo) |
| Montar o layout da estante com cofres | T-1902 |
| Montar placa com microcontrolador e relé | T-1903 |
| Conectar o microcontrolador ao backend | US-06, US-20 |
| Desenvolver telas do app mobile | T-1301 e E6 |
| Diagrama do banco de dados | T-0201 |
| Desenhar o fluxo da aplicação | T-0203 |
| Estudar MQTT e broker com ESP32 | T-2001 |
| Comprar ESP32 | T-1901 |

## E0 — Fundação e infraestrutura (Sprint 1)

### US-01 · P1

**Como dev, quero o ambiente local pronto para desenvolver backend, app e firmware.**

Critérios de aceite:
- [ ] `docker compose up` sobe PostgreSQL e Mosquitto funcionando
- [ ] Backend, app e firmware compilam a partir do README
- [ ] CI executa em todo push/PR

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0101 | Estruturar monorepo: backend/, mobile/, firmware/, docs/, infra/ | Infra | P1 | 1 | 1 | Concluído |
| T-0102 | docker-compose com PostgreSQL, Mosquitto (mosquitto.conf, passwd, ACL) e pgAdmin | Infra | P1 | 1 | 2 | Em andamento |
| T-0103 | Criar solução .NET 8 (Api, Application, Domain, Infrastructure) + projeto de testes xUnit | Backend | P1 | 1 | 2 | Concluído |
| T-0104 | Criar projeto Flutter (features, Riverpod, go_router, dio, flavors dev/prod) | Mobile | P1 | 1 | 2 | A fazer |
| T-0105 | Criar projeto PlatformIO para ESP32 (Arduino, PubSubClient, ArduinoJson) | Firmware | P1 | 1 | 1 | A fazer |
| T-0106 | CI no GitHub Actions: dotnet build/test, flutter analyze/test, pio run | Infra | P2 | 1 | 3 | A fazer |
| T-0107 | .gitignore, .editorconfig e README com instruções de setup | Docs | P1 | 1 | 1 | A fazer |

### US-02 · P1

**Como time, quero as decisões técnicas documentadas.**

Critérios de aceite:
- [ ] DER atualizado com o modelo revisado
- [ ] Contrato MQTT documentado em docs/ARQUITETURA.md
- [ ] Fluxograma mostra locação → pagamento → abertura

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0201 | Revisar DER conforme modelo de dados revisado e atualizar DER.drawio | Docs | P1 | 1 | 2 | Concluído |
| T-0202 | Documentar contrato MQTT (tópicos, payloads, QoS, segurança) | Docs | P1 | 1 | 1 | A fazer |
| T-0203 | Atualizar Fluxograma.drawio com fluxo de locação/pagamento/abertura | Docs | P1 | 1 | 1 | A fazer |

## E1 — Backend: usuários e autenticação (Sprint 1-2)

### US-03 · P1

**Como usuário, quero me cadastrar e fazer login no app.**

Critérios de aceite:
- [ ] Cadastro com e-mail único, senha forte e CPF válido
- [ ] Login retorna access token (JWT) e refresh token
- [ ] Endpoints protegidos retornam 401 sem token e 403 sem papel adequado

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0301 | Configurar ASP.NET Identity + EF Core/Npgsql + migrations iniciais | Backend | P1 | 1 | 3 | Concluído |
| T-0302 | Endpoints POST /auth/registro, /auth/login, /auth/refresh, /auth/logout | Backend | P1 | 1 | 3 | Concluído |
| T-0303 | JWT com papéis Usuario/Admin e seed do admin inicial | Backend | P1 | 1 | 2 | Concluído |
| T-0304 | Validação (FluentValidation) e erros padronizados (ProblemDetails) | Backend | P1 | 2 | 2 | A fazer |
| T-0305 | GET/PUT /usuarios/me | Backend | P1 | 2 | 1 | A fazer |
| T-0306 | Recuperação de senha por e-mail | Backend | P2 | 5 | 3 | A fazer |
| T-0307 | Testes de integração de auth (Testcontainers Postgres) | Backend | P1 | 2 | 2 | A fazer |

## E2 — Backend: estantes e cofres (Sprint 2)

### US-04 · P1

**Como administrador, quero cadastrar e gerenciar estantes e cofres via API.**

Critérios de aceite:
- [ ] Admin cria estante e recebe device_id, credenciais MQTT e segredo HMAC
- [ ] Admin cadastra cofres com número, canal do relé e tamanho
- [ ] Usuário comum recebe 403 nas rotas /admin

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0401 | Entidades Estante, Cofre, Tarifa + migrations | Backend | P1 | 2 | 2 | Concluído |
| T-0402 | CRUD /admin/estantes (gera device_id, credenciais MQTT e segredo HMAC) | Backend | P1 | 2 | 3 | Concluído |
| T-0403 | CRUD /admin/estantes/{id}/cofres (número, canal do relé, tamanho) | Backend | P1 | 2 | 2 | Concluído |
| T-0404 | CRUD /admin/tarifas (valor por hora, tempo mínimo por tamanho) | Backend | P1 | 2 | 2 | A fazer |
| T-0405 | Colocar cofre em manutenção / reativar | Backend | P2 | 2 | 1 | Concluído |
| T-0406 | Políticas de autorização: rotas /admin apenas para Admin | Backend | P1 | 2 | 1 | Concluído |

### US-05 · P1

**Como usuário, quero ver estantes próximas e cofres disponíveis.**

Critérios de aceite:
- [ ] Lista de estantes ordenada por distância da minha localização
- [ ] Cada cofre mostra status (livre/ocupado), tamanho e preço
- [ ] Estantes offline aparecem como indisponíveis

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0501 | GET /estantes?lat=&lng=&raio= ordenado por distância | Backend | P1 | 2 | 2 | A fazer |
| T-0502 | GET /estantes/{id}/cofres com disponibilidade e preço | Backend | P1 | 2 | 2 | A fazer |
| T-0503 | Sinalizar estantes offline como indisponíveis | Backend | P2 | 3 | 1 | A fazer |

## E3 — Backend: integração MQTT (Sprint 2-3)

### US-06 · P1

**Como sistema, quero enviar comandos aos ESP32 e receber confirmações com segurança.**

Critérios de aceite:
- [ ] Comando publicado em cofre/{estanteId}/cmd com commandId, expiração e assinatura HMAC
- [ ] Ack correlacionado em até 10 s; sem ack retorna timeout
- [ ] Status online/offline da estante atualizado por LWT/heartbeat
- [ ] Todo comando e resultado fica registrado em eventos_cofre

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0601 | MqttService (MQTTnet) como IHostedService com reconexão automática e TLS | Backend | P1 | 2 | 3 | A fazer |
| T-0602 | Publicar comando com commandId, expiração e assinatura HMAC | Backend | P1 | 2 | 2 | A fazer |
| T-0603 | Assinar ack e correlacionar com comando pendente (TaskCompletionSource + timeout) | Backend | P1 | 3 | 3 | A fazer |
| T-0604 | Assinar estado e online → atualizar status de cofre/estante e heartbeat | Backend | P1 | 3 | 2 | A fazer |
| T-0605 | Persistir comandos/eventos em eventos_cofre | Backend | P1 | 3 | 1 | A fazer |
| T-0606 | Job que marca estante offline sem heartbeat | Backend | P2 | 3 | 1 | A fazer |
| T-0607 | Testes com Mosquitto (Testcontainers) + simulador de ESP32 em console .NET | Backend | P1 | 3 | 3 | A fazer |
| T-0608 | ACL do Mosquitto: cada ESP32 só acessa seus tópicos; usuário próprio para o backend | Infra | P1 | 3 | 2 | A fazer |

## E4 — Backend: locação e pagamento Pix (Sprint 3-5)

### US-07 · P1

**Como usuário, quero alugar um cofre por um período e pagar via Pix no app.**

Critérios de aceite:
- [ ] Ao criar a locação o cofre fica reservado e um Pix (QR + copia-e-cola) é gerado
- [ ] Dois usuários não conseguem reservar o mesmo cofre ao mesmo tempo
- [ ] Webhook do Mercado Pago ativa a locação de forma idempotente
- [ ] Pix não pago em N minutos cancela a reserva e libera o cofre
- [ ] A reserva só é feita se a estante e o cofre estiverem ativos, conferido na própria escrita condicional (fecha a corrida com a desativação da estante, T-0402)

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0701 | Entidades Locacao e Pagamento + máquina de estados da locação | Backend | P1 | 3 | 3 | A fazer |
| T-0702 | POST /locacoes com reserva do cofre e controle de concorrência | Backend | P1 | 3 | 3 | A fazer |
| T-0703 | Integrar SDK Mercado Pago: criar pagamento Pix, retornar QR code e copia-e-cola | Backend | P1 | 4 | 3 | A fazer |
| T-0704 | POST /webhooks/mercadopago: validar assinatura, consultar pagamento, ativar locação (idempotente) | Backend | P1 | 4 | 3 | A fazer |
| T-0705 | Job: cancelar reserva/Pix não pago após N minutos e liberar cofre | Backend | P1 | 4 | 2 | A fazer |
| T-0706 | GET /locacoes/minhas (ativas e histórico) e GET /locacoes/{id} | Backend | P1 | 4 | 1 | A fazer |
| T-0707 | Testes com credenciais sandbox do Mercado Pago | Backend | P1 | 4 | 2 | A fazer |

### US-08 · P1

**Como usuário, quero abrir meu cofre pelo app durante a locação.**

Critérios de aceite:
- [ ] Só o dono da locação ativa e vigente consegue abrir
- [ ] Retorno distingue sucesso, estante offline, timeout e falha mecânica
- [ ] Cada abertura fica auditada

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0801 | POST /locacoes/{id}/abrir: valida dono, vigência e estante online → envia comando e aguarda ack | Backend | P1 | 4 | 3 | A fazer |
| T-0802 | Rate limit de aberturas e registro de auditoria | Backend | P1 | 4 | 1 | A fazer |
| T-0803 | Mapear falhas (offline, timeout, erro do ESP32) em respostas claras | Backend | P1 | 4 | 1 | A fazer |

### US-09 · P2

**Como usuário, quero encerrar ou estender minha locação.**

Critérios de aceite:
- [ ] Encerrar abre o cofre uma última vez e o libera
- [ ] Estender gera novo Pix e prorroga o fim após pagamento
- [ ] Locações vencidas são marcadas como expiradas e o usuário é notificado

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-0901 | POST /locacoes/{id}/encerrar (abre uma última vez e libera o cofre) | Backend | P2 | 5 | 2 | A fazer |
| T-0902 | POST /locacoes/{id}/estender (novo Pix) | Backend | P2 | 5 | 2 | A fazer |
| T-0903 | Job de expiração + definir política para itens não retirados | Backend | P2 | 5 | 2 | A fazer |

### US-10 · P2

**Como usuário, quero ser notificado sobre pagamento e fim da locação.**

Critérios de aceite:
- [ ] App recebe em tempo real a confirmação de pagamento e o resultado da abertura
- [ ] Push 15 min antes do fim da locação

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1001 | Hub SignalR para pagamento confirmado e resultado de abertura | Backend | P2 | 4 | 2 | A fazer |
| T-1002 | Push via Firebase Cloud Messaging (pagamento aprovado, aviso de fim) | Backend | P2 | 5 | 3 | A fazer |

## E5 — Backend: administração e operação (Sprint 5)

### US-11 · P2

**Como administrador, quero monitorar estantes e locações.**

Critérios de aceite:
- [ ] Visão de status de cada estante (online, ocupação, último heartbeat)
- [ ] Consulta de locações e eventos com filtros
- [ ] Abertura de emergência registrada com o admin responsável

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1101 | GET /admin/estantes/status (online, ocupação, último heartbeat) | Backend | P2 | 5 | 2 | A fazer |
| T-1102 | GET /admin/locacoes e GET /admin/eventos com filtros e paginação | Backend | P2 | 5 | 2 | A fazer |
| T-1103 | POST /admin/cofres/{id}/abrir (abertura de emergência auditada) | Backend | P2 | 5 | 1 | A fazer |
| T-1104 | Relatório de faturamento por período | Backend | P3 | 6 | 2 | A fazer |

### US-12 · P2

**Como time, quero a API observável e publicada.**

Critérios de aceite:
- [ ] /health reporta banco e broker
- [ ] API, Postgres e Mosquitto rodam em produção com TLS
- [ ] Backup diário do banco

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1201 | Logs estruturados (Serilog) e health checks (/health com DB e MQTT) | Backend | P2 | 5 | 2 | A fazer |
| T-1202 | Dockerfile da API + compose de produção (Mosquitto com TLS) | Infra | P2 | 5 | 2 | A fazer |
| T-1203 | Deploy em VPS/nuvem com HTTPS e secrets via variáveis de ambiente | Infra | P2 | 5 | 3 | A fazer |
| T-1204 | Backup automatizado do banco | Infra | P2 | 6 | 1 | A fazer |

## E6 — App Flutter (Sprint 2-5)

### US-13 · P1

**Como usuário, quero me cadastrar, fazer login e manter a sessão no app.**

Critérios de aceite:
- [ ] Cadastro e login com validação dos campos
- [ ] Sessão mantida entre aberturas do app (refresh automático)
- [ ] Tokens guardados em armazenamento seguro

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1301 | Design das telas: splash, login, cadastro, mapa, estante, pagamento, minhas locações, perfil | Mobile | P1 | 2 | 3 | A fazer |
| T-1302 | Telas de login e cadastro com validação | Mobile | P1 | 2 | 2 | A fazer |
| T-1303 | Camada HTTP (dio) com interceptor JWT + refresh e flutter_secure_storage | Mobile | P1 | 2 | 2 | A fazer |
| T-1304 | Fluxo de recuperação de senha | Mobile | P2 | 5 | 1 | A fazer |

### US-14 · P1

**Como usuário, quero encontrar uma estante e escolher um cofre.**

Critérios de aceite:
- [ ] Mapa mostra minha posição e estantes próximas
- [ ] Tela da estante mostra cofres livres/ocupados com preço
- [ ] Valor total calculado ao escolher a duração

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1401 | Tela de mapa (google_maps_flutter + geolocator) com estantes próximas | Mobile | P1 | 3 | 3 | A fazer |
| T-1402 | Lista de estantes por distância | Mobile | P2 | 3 | 1 | A fazer |
| T-1403 | Tela da estante: grade de cofres com status, tamanho e preço | Mobile | P1 | 3 | 2 | A fazer |
| T-1404 | Seleção de duração com cálculo do valor | Mobile | P1 | 3 | 1 | A fazer |

### US-15 · P1

**Como usuário, quero pagar via Pix no app.**

Critérios de aceite:
- [ ] QR code e copia-e-cola exibidos com contador de expiração
- [ ] App avança sozinho para a locação ativa ao confirmar o pagamento

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1501 | Tela de pagamento: QR code (qr_flutter), copia-e-cola e contador | Mobile | P1 | 4 | 2 | A fazer |
| T-1502 | Aguardar confirmação via SignalR (fallback polling) e navegar para a locação | Mobile | P1 | 4 | 2 | A fazer |

### US-16 · P1

**Como usuário, quero abrir meu cofre e acompanhar minha locação.**

Critérios de aceite:
- [ ] Tela mostra cofre, estante e tempo restante
- [ ] Botão Abrir pede confirmação e mostra o resultado (abrindo/aberto/erro)
- [ ] Histórico de locações disponível

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1601 | Tela Minha locação: cofre, estante, tempo restante e botão Abrir | Mobile | P1 | 4 | 2 | A fazer |
| T-1602 | Confirmação antes de abrir + feedback de abrindo/aberto/erro | Mobile | P1 | 4 | 1 | A fazer |
| T-1603 | Estender e encerrar locação | Mobile | P2 | 5 | 2 | A fazer |
| T-1604 | Histórico de locações | Mobile | P2 | 5 | 1 | A fazer |

### US-17 · P2

**Como usuário, quero receber notificações no celular.**

Critérios de aceite:
- [ ] Push de pagamento aprovado e de fim de locação chega no Android e iOS

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1701 | Integrar Firebase Messaging e enviar token do dispositivo ao backend | Mobile | P2 | 5 | 2 | A fazer |

### US-18 · P2

**Como time, quero o app testado e pronto para distribuição.**

Critérios de aceite:
- [ ] Fluxos principais cobertos por testes
- [ ] Build de release Android assinado

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1801 | Testes de widget e de integração dos fluxos principais | Mobile | P2 | 5 | 3 | A fazer |
| T-1802 | Ícone, splash, nome e permissões (localização, notificações) | Mobile | P2 | 5 | 1 | A fazer |
| T-1803 | Build de release Android (assinatura) e TestFlight iOS | Mobile | P3 | 6 | 2 | A fazer |

## E7 — Firmware ESP32 e hardware (Sprint 1-4)

### US-19 · P1

**Como time, quero o hardware da estante montado.**

Critérios de aceite:
- [ ] Estante física com cofres e fechaduras instaladas
- [ ] Placa aciona cada fechadura individualmente com segurança elétrica

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-1901 | Comprar ESP32, módulos relé, fechaduras solenoide 12 V, fonte e sensores de porta (reed switch) | Hardware | P1 | 1 | 1 | A fazer |
| T-1902 | Montar layout físico da estante com cofres | Hardware | P1 | 2 | 3 | A fazer |
| T-1903 | Montar placa: ESP32 + relés (optoacoplador/diodo de proteção) + entradas de sensor | Hardware | P1 | 2 | 3 | A fazer |

### US-20 · P1

**Como sistema, quero que o ESP32 receba comandos MQTT e abra o cofre correto.**

Critérios de aceite:
- [ ] ESP32 reconecta sozinho ao Wi-Fi e ao broker
- [ ] Comando válido aciona o relé do cofre e publica ack
- [ ] Comando expirado, repetido ou com assinatura inválida é rejeitado
- [ ] No boot todos os relés ficam desligados

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-2001 | Estudar MQTT e broker com ESP32 (PoC com Mosquitto local) | Firmware | P1 | 1 | 2 | A fazer |
| T-2002 | Wi-Fi com reconexão e provisionamento de credenciais (WiFiManager) | Firmware | P2 | 3 | 2 | A fazer |
| T-2003 | MQTT com TLS, usuário/senha, LWT offline, publicação online + heartbeat | Firmware | P1 | 3 | 3 | A fazer |
| T-2004 | Assinar cmd e validar HMAC, expiração e commandId repetido | Firmware | P1 | 3 | 3 | A fazer |
| T-2005 | Mapear número do cofre → GPIO do relé e acionar pulso de abertura configurável | Firmware | P1 | 3 | 2 | A fazer |
| T-2006 | Publicar ack com resultado | Firmware | P1 | 3 | 1 | A fazer |
| T-2007 | Ler sensores de porta e publicar estado (retained) a cada mudança | Firmware | P2 | 4 | 2 | A fazer |
| T-2008 | Configuração (estanteId, segredo, mapa de cofres) em NVS, sem hardcode | Firmware | P1 | 4 | 2 | A fazer |
| T-2009 | Watchdog e estado seguro no boot (relés desligados) | Firmware | P1 | 4 | 1 | A fazer |
| T-2010 | Atualização OTA de firmware | Firmware | P3 | 6 | 3 | A fazer |

## E8 — Segurança e testes ponta a ponta (Sprint 5-6)

### US-21 · P1

**Como dono do negócio, quero garantir que só o locatário consiga abrir o cofre.**

Critérios de aceite:
- [ ] TLS em API e MQTT, ACLs e HMAC revisados
- [ ] Testes de replay, acesso indevido e webhook falso falham como esperado
- [ ] Tratamento de dados pessoais conforme LGPD

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-2101 | Revisão de segurança: TLS, ACLs, HMAC, rate limit e LGPD (dados pessoais, CPF) | QA | P1 | 6 | 3 | A fazer |
| T-2102 | Testes de abuso: replay de comando, cofre alheio, webhook falso | QA | P1 | 6 | 2 | A fazer |
| T-2103 | Rotação da chave de criptografia dos segredos HMAC (várias versões de chave, cifrar com a atual, decifrar pela versão do prefixo, comando de recifragem) | Backend | P2 | 6 | 3 | A fazer |

### US-22 · P1

**Como time, quero validar o fluxo completo com hardware real.**

Critérios de aceite:
- [ ] Fluxo cadastro → cofre → Pix sandbox → abrir → encerrar executado com sucesso
- [ ] Sistema se recupera de quedas de ESP32, Wi-Fi e broker

| Nº Tarefa | Tarefa | Componente | Prioridade | Sprint | Pts | Status |
|---|---|---|---|---|---|---|
| T-2201 | Roteiro E2E: cadastro → escolher cofre → Pix sandbox → abrir → encerrar | QA | P1 | 6 | 2 | A fazer |
| T-2202 | Testes de falha: ESP32 desligado, Wi-Fi caindo, broker reiniciando, Pix expirado | QA | P1 | 6 | 2 | A fazer |
| T-2203 | Piloto com 1 estante física | QA | P1 | 6 | 3 | A fazer |
