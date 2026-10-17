using CofreEletronico.Application;
using CofreEletronico.Infrastructure;
using CofreEletronico.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);


builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Sem a string de conexão "Cofre", só loga um aviso e a Api sobe sem banco.
    await app.Services.AplicarMigrationsAsync();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/ping", () => Results.Ok("pong"))
    .WithName("Ping")
    .WithOpenApi();

app.Run();

// Exposto para testes de integração (WebApplicationFactory) e de arquitetura.
public partial class Program;
