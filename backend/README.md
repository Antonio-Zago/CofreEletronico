# Backend

API .NET 8 do Cofre Eletrônico (ASP.NET Core, EF Core + Npgsql, Identity + JWT, MQTTnet, SignalR, Serilog).

Estrutura prevista (criada na T-0103):

```
src/CofreEletronico.Api             # endpoints, auth, Swagger, hub SignalR
src/CofreEletronico.Application     # casos de uso, DTOs, validação
src/CofreEletronico.Domain          # entidades e regras (estados de locação e cofre)
src/CofreEletronico.Infrastructure  # EF Core, MQTT, Mercado Pago, jobs
tests/                              # xUnit + Testcontainers
tools/EspSimulator                  # simulador de ESP32 para dev sem hardware
```

Responsável: agente `backend-dotnet`. Detalhes em [../docs/ARQUITETURA.md](../docs/ARQUITETURA.md).
