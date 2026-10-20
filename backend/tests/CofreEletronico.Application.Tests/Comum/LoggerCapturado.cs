using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Comum;

/// <summary>Logger em memória: guarda nível e mensagem formatada para os testes conferirem o que foi logado.</summary>
internal sealed class LoggerCapturado<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Mensagem)> Registros { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Registros.Add((logLevel, formatter(state, exception)));

    public string Tudo => string.Join("\n", Registros.Select(r => r.Mensagem));
}
