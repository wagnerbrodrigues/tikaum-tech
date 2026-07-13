using System.Text;

namespace TikaumTech.Services;

/// <summary>
/// Log em arquivo (data/logs/tikaum_AAAA-MM-DD.log) sem dependência externa. Existe porque
/// na máquina do estúdio o app roda por Tarefa Agendada, sem console visível — sem isso,
/// um erro fatal de inicialização (ex.: migration) é invisível e vira só "o sistema não
/// abre" (relato de 2026-07-13). Escrita síncrona (AppendAllText) de propósito: o volume
/// de log é baixo e o objetivo é a última linha estar no disco mesmo quando o processo
/// morre logo em seguida.
/// </summary>
public sealed class ArquivoLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly object _escrita = new();

    public ArquivoLoggerProvider(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        LimparAntigos();
    }

    public ILogger CreateLogger(string categoryName) => new ArquivoLogger(this, categoryName);

    public void Dispose() { }

    internal void Escrever(string linha)
    {
        try
        {
            lock (_escrita)
                File.AppendAllText(CaminhoDoDia, linha + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging nunca derruba nem trava o app (disco cheio, pasta removida etc.)
        }
    }

    private string CaminhoDoDia => Path.Combine(_dir, $"tikaum_{DateTime.Today:yyyy-MM-dd}.log");

    // Retenção: 30 dias bastam para diagnóstico e o diretório não cresce sem limite
    private void LimparAntigos()
    {
        try
        {
            foreach (var arquivo in Directory.GetFiles(_dir, "tikaum_*.log"))
                if (File.GetLastWriteTime(arquivo) < DateTime.Today.AddDays(-30))
                    File.Delete(arquivo);
        }
        catch
        {
            // Melhor manter logs antigos do que impedir o app de subir
        }
    }
}

internal sealed class ArquivoLogger(ArquivoLoggerProvider provider, string categoria) : ILogger
{
    // Framework (Microsoft/System) só a partir de Warning; código do app desde Information
    private LogLevel NivelMinimo =>
        categoria.StartsWith("Microsoft", StringComparison.Ordinal) ||
        categoria.StartsWith("System", StringComparison.Ordinal)
            ? LogLevel.Warning
            : LogLevel.Information;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= NivelMinimo;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {categoria}: {formatter(state, exception)}";
        if (exception is not null) linha += Environment.NewLine + exception;
        provider.Escrever(linha);
    }
}
