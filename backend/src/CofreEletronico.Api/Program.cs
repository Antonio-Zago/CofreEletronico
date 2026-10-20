using CofreEletronico.Api.Autenticacao;
using CofreEletronico.Api.Endpoints.Auth;
using CofreEletronico.Application;
using CofreEletronico.Infrastructure;
using CofreEletronico.Infrastructure.Autorizacao;
using CofreEletronico.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// JWT (Jwt:ChaveAssinatura obrigatória na subida; ver README), políticas de autorização
// (fallback = usuário autenticado; Politicas.Admin) e rate limit dos endpoints de /auth.
builder.Services.AddAutenticacaoJwt();
builder.Services.AddRateLimitAuth(builder.Configuration);

builder.Services.AddSwaggerComBearer();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Sem a string de conexão "Cofre", só loga um aviso e a Api sobe sem banco.
    await app.Services.AplicarMigrationsAsync();

    // Antes da autorização: o Swagger não passa pela FallbackPolicy.
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Todos os ambientes, depois das migrations. Sem string de conexão ou sem AdminInicial, só registra no log.
// Senha recusada pela política do Identity impede a subida.
await app.Services.GarantirAdminInicialAsync();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/ping", () => Results.Ok("pong"))
    .AllowAnonymous()
    .WithName("Ping")
    .WithOpenApi();

app.MapAuthEndpoints();

app.Run();

// Exposto para testes de integração (WebApplicationFactory) e de arquitetura.
public partial class Program;
