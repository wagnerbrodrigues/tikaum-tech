using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class ProdutoServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ApplicationDbContext _db;
    private readonly ProdutoService _svc;

    public ProdutoServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new ProdutoService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task ListarAsync_RetornaApenasAtivos_PorPadrao()
    {
        _db.Produtos.AddRange(
            new Produto { Nome = "Agulha", PrecoPadrao = 5, Ativo = true },
            new Produto { Nome = "Tinta descontinuada", PrecoPadrao = 20, Ativo = false });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync();

        Assert.Single(resultado);
        Assert.Equal("Agulha", resultado[0].Nome);
    }

    [Fact]
    public async Task ListarAsync_IncluiInativos_QuandoApenasAtivosFalse()
    {
        _db.Produtos.AddRange(
            new Produto { Nome = "Agulha", PrecoPadrao = 5, Ativo = true },
            new Produto { Nome = "Tinta descontinuada", PrecoPadrao = 20, Ativo = false });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync(apenasAtivos: false);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorNome()
    {
        _db.Produtos.AddRange(
            new Produto { Nome = "Agulha 3RL", PrecoPadrao = 5 },
            new Produto { Nome = "Luva", PrecoPadrao = 2 });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync("agulha");

        Assert.Single(resultado);
        Assert.Equal("Agulha 3RL", resultado[0].Nome);
    }

    [Fact]
    public async Task AlternarAtivoAsync_InverteFlag()
    {
        var produto = new Produto { Nome = "Agulha", PrecoPadrao = 5, Ativo = true };
        await _svc.CriarAsync(produto);

        await _svc.AlternarAtivoAsync(produto.Id);
        Assert.False((await _db.Produtos.FindAsync(produto.Id))!.Ativo);

        await _svc.AlternarAtivoAsync(produto.Id);
        Assert.True((await _db.Produtos.FindAsync(produto.Id))!.Ativo);
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoPrecoNegativo()
    {
        var produto = new Produto { Nome = "Agulha", PrecoPadrao = -1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(produto));
    }

    [Fact]
    public async Task AtualizarAsync_LancaExcecao_QuandoPrecoNegativo()
    {
        var produto = new Produto { Nome = "Agulha", PrecoPadrao = 5 };
        await _svc.CriarAsync(produto);
        produto.PrecoPadrao = -10;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AtualizarAsync(produto));
    }

    [Fact]
    public async Task AtualizarAsync_Persiste_QuandoRecebeCopiaComMesmoIdDaInstanciaRastreada()
    {
        // Cenário real do circuito Blazor Server: a listagem deixa a entidade rastreada
        // no DbContext compartilhado e o diálogo de edição salva uma CÓPIA com o mesmo Id.
        // Com Update(cópia) isso lançava "another instance ... is already being tracked".
        var produto = new Produto { Nome = "Pomada", PrecoPadrao = 30, Ativo = true };
        await _svc.CriarAsync(produto);   // instância original fica rastreada

        var copia = new Produto { Id = produto.Id, Nome = "Pomada Premium", PrecoPadrao = 45, Ativo = true };
        await _svc.AtualizarAsync(copia);

        var atualizado = await _db.Produtos.FindAsync(produto.Id);
        Assert.Equal("Pomada Premium", atualizado!.Nome);
        Assert.Equal(45, atualizado.PrecoPadrao);
    }

    [Fact]
    public async Task ConsultasDeLeitura_NaoRastreiamEntidadesNoContexto()
    {
        // Leituras de exibição não devem anexar entidades ao DbContext do circuito —
        // era a origem do conflito de tracking na edição (a outra metade da correção).
        var produto = new Produto { Nome = "Luva", PrecoPadrao = 10, Ativo = true };
        await _svc.CriarAsync(produto);
        _db.ChangeTracker.Clear();

        await _svc.ListarAsync();
        await _svc.ObterAsync(produto.Id);

        Assert.Empty(_db.ChangeTracker.Entries());
    }
}
