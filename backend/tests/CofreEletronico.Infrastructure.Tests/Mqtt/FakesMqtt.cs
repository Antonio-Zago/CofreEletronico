using System.Net.Sockets;
using CofreEletronico.Application.Mqtt.Portas;
using CofreEletronico.Infrastructure.Mqtt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace CofreEletronico.Infrastructure.Tests.Mqtt;

/// <summary>Adaptador MQTT em memória: simula falhas de conexão, quedas, recusas e entrega de mensagens.</summary>
internal sealed class AdaptadorMqttFalso : IAdaptadorMqtt
{
    private readonly object _trava = new();
    private int _emAndamento;

    public bool Conectado { get; private set; }

    public event Func<DesconexaoMqtt, Task>? Desconectado;

    public event Func<MensagemMqtt, Task>? MensagemRecebida;

    /// <summary>Quantas das próximas tentativas de conexão falham (conexão recusada).</summary>
    public int FalhasRestantes { get; set; }

    /// <summary>Erro lançado nas falhas de conexão (padrão: conexão recusada).</summary>
    public Func<Exception> ErroConexao { get; set; } = () => new SocketException((int)SocketError.ConnectionRefused);

    /// <summary>Se definido, a conexão fica pendente até ele completar.</summary>
    public TaskCompletionSource? PortaoConexao { get; set; }

    public int TentativasConexao { get; private set; }

    public int Conexoes { get; private set; }

    public int MaximoConcorrente { get; private set; }

    public int Desconexoes { get; private set; }

    public HashSet<string> FiltrosRecusados { get; } = [];

    public Exception? ErroPublicacao { get; set; }

    public List<(int Conexao, string Filtro)> Assinaturas { get; } = [];

    public List<(string Topico, byte[] Payload)> Publicacoes { get; } = [];

    public async Task ConectarAsync(CancellationToken ct)
    {
        lock (_trava)
        {
            TentativasConexao++;
            _emAndamento++;
            MaximoConcorrente = Math.Max(MaximoConcorrente, _emAndamento);
        }

        try
        {
            if (PortaoConexao is { } portao)
                await portao.Task.WaitAsync(ct);

            lock (_trava)
            {
                if (FalhasRestantes > 0)
                {
                    FalhasRestantes--;
                    throw ErroConexao();
                }

                Conectado = true;
                Conexoes++;
            }
        }
        finally
        {
            lock (_trava)
                _emAndamento--;
        }
    }

    public Task AssinarAsync(string filtro, CancellationToken ct)
    {
        lock (_trava)
        {
            if (FiltrosRecusados.Contains(filtro))
                throw new MqttAssinaturaRecusadaException(filtro, "NotAuthorized");
            Assinaturas.Add((Conexoes, filtro));
        }

        return Task.CompletedTask;
    }

    public Task PublicarAsync(string topico, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (ErroPublicacao is not null)
            throw ErroPublicacao;
        lock (_trava)
            Publicacoes.Add((topico, payload.ToArray()));
        return Task.CompletedTask;
    }

    public Task DesconectarAsync(CancellationToken ct)
    {
        lock (_trava)
        {
            Conectado = false;
            Desconexoes++;
        }

        return Task.CompletedTask;
    }

    public List<string> FiltrosDaConexao(int conexao)
    {
        lock (_trava)
            return Assinaturas.Where(a => a.Conexao == conexao).Select(a => a.Filtro).ToList();
    }

    public Task SimularQuedaAsync(string motivo = "queda simulada", bool sessaoAssumida = false)
    {
        lock (_trava)
            Conectado = false;
        return Desconectado?.Invoke(new DesconexaoMqtt(motivo, sessaoAssumida)) ?? Task.CompletedTask;
    }

    public Task EntregarAsync(MensagemMqtt mensagem) => MensagemRecebida?.Invoke(mensagem) ?? Task.CompletedTask;
}

/// <summary>FakeTimeProvider que conta os timers criados (cada Task.Delay cria um).</summary>
internal sealed class RelogioMqtt : FakeTimeProvider
{
    private int _timers;

    public int TimersCriados => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        return base.CreateTimer(callback, state, dueTime, period);
    }
}

/// <summary>Logger em memória (thread-safe).</summary>
internal sealed class LoggerMqtt<T> : ILogger<T>
{
    private readonly List<(LogLevel Nivel, string Mensagem, Exception? Erro)> _registros = [];

    public IReadOnlyList<(LogLevel Nivel, string Mensagem, Exception? Erro)> Registros
    {
        get
        {
            lock (_registros)
                return _registros.ToList();
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_registros)
            _registros.Add((logLevel, formatter(state, exception), exception));
    }

    public int Contar(LogLevel nivel, string trecho) =>
        Registros.Count(r => r.Nivel == nivel && r.Mensagem.Contains(trecho, StringComparison.Ordinal));

    public string Tudo => string.Join("\n", Registros.Select(r => r.Mensagem));
}

internal static class Esperar
{
    /// <summary>Espera (em tempo real, até 5 s) a condição ficar verdadeira.</summary>
    public static async Task AteAsync(Func<bool> condicao, string descricao)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
                throw new TimeoutException("Condição não atingida: " + descricao);
            await Task.Delay(10);
        }
    }
}
