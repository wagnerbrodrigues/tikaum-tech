using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class ServicoServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ApplicationDbContext _db;
    private readonly ServicoService _svc;

    public ServicoServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new ServicoService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task ListarAsync_RetornaApenasAtivos_PorPadrao()
    {
        _db.Servicos.AddRange(
            new Servico { Nome = "Tatuagem pequena", PrecoPadrao = 200, Ativo = true },
            new Servico { Nome = "Piercing antigo", PrecoPadrao = 80, Ativo = false });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync();

        Assert.Single(resultado);
        Assert.Equal("Tatuagem pequena", resultado[0].Nome);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorNome()
    {
        _db.Servicos.AddRange(
            new Servico { Nome = "Tatuagem grande", PrecoPadrao = 500 },
            new Servico { Nome = "Piercing nariz", PrecoPadrao = 100 });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync("tatuagem");

        Assert.Single(resultado);
        Assert.Equal("Tatuagem grande", resultado[0].Nome);
    }

    [Fact]
    public async Task AlternarAtivoAsync_InverteFlag()
    {
        var servico = new Servico { Nome = "Piercing", PrecoPadrao = 80, Ativo = true };
        await _svc.CriarAsync(servico);

        await _svc.AlternarAtivoAsync(servico.Id);
        Assert.False((await _db.Servicos.FindAsync(servico.Id))!.Ativo);
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoPrecoNegativo()
    {
        var servico = new Servico { Nome = "Piercing", PrecoPadrao = -5 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(servico));
    }

    [Fact]
    public async Task AtualizarAsync_Persiste_QuandoRecebeCopiaComMesmoIdDaInstanciaRastreada()
    {
        // Cenário real do circuito Blazor Server: a listagem deixa a entidade rastreada
        // no DbContext compartilhado e o diálogo de edição salva uma CÓPIA com o mesmo Id.
        // Com Update(cópia) isso lançava "another instance ... is already being tracked".
        var servico = new Servico { Nome = "Tatuagem média", PrecoPadrao = 400, Ativo = true };
        await _svc.CriarAsync(servico);   // instância original fica rastreada

        var copia = new Servico { Id = servico.Id, Nome = "Tatuagem média colorida", PrecoPadrao = 500, Ativo = true };
        await _svc.AtualizarAsync(copia);

        var atualizado = await _db.Servicos.FindAsync(servico.Id);
        Assert.Equal("Tatuagem média colorida", atualizado!.Nome);
        Assert.Equal(500, atualizado.PrecoPadrao);
    }

    [Fact]
    public async Task ConsultasDeLeitura_NaoRastreiamEntidadesNoContexto()
    {
        // Leituras de exibição não devem anexar entidades ao DbContext do circuito —
        // era a origem do conflito de tracking na edição (a outra metade da correção).
        var servico = new Servico { Nome = "Furo simples", PrecoPadrao = 60, Ativo = true };
        await _svc.CriarAsync(servico);
        _db.ChangeTracker.Clear();

        await _svc.ListarAsync();
        await _svc.ObterAsync(servico.Id);

        Assert.Empty(_db.ChangeTracker.Entries());
    }
}
