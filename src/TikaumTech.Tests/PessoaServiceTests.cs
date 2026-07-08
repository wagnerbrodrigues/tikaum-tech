using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class PessoaServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ApplicationDbContext _db;
    private readonly PessoaService _svc;

    public PessoaServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new PessoaService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task ListarAsync_RetornaTodasOrdenadasPorNome_QuandoSemBusca()
    {
        _db.Pessoas.AddRange(
            new Pessoa { Nome = "Beatriz" },
            new Pessoa { Nome = "Ana" });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync();

        Assert.Equal(["Ana", "Beatriz"], resultado.Select(p => p.Nome));
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("11999")]
    [InlineData("111.222.333-44")]
    public async Task ListarAsync_FiltraPorNomeCelularOuCpf_CaseInsensitive(string busca)
    {
        _db.Pessoas.Add(new Pessoa { Nome = "Ana Silva", Telefone = "11999999999", Cpf = "111.222.333-44" });
        _db.Pessoas.Add(new Pessoa { Nome = "Bruno Costa", Telefone = "11888888888", Cpf = "555.666.777-88" });
        await _db.SaveChangesAsync();

        var resultado = await _svc.ListarAsync(busca);

        Assert.Single(resultado);
        Assert.Equal("Ana Silva", resultado[0].Nome);
    }

    [Fact]
    public async Task ListarAsync_CpfDistingueHomonimos()
    {
        // Dois clientes com o mesmo nome — o CPF é o desempate (TIKAUM_SPEC.md §5)
        _db.Pessoas.Add(new Pessoa { Nome = "João Souza", Cpf = "111.222.333-44" });
        _db.Pessoas.Add(new Pessoa { Nome = "João Souza", Cpf = "999.888.777-66" });
        await _db.SaveChangesAsync();

        var porNome = await _svc.ListarAsync("João Souza");
        var porCpf = await _svc.ListarAsync("999.888.777-66");

        Assert.Equal(2, porNome.Count);
        Assert.Single(porCpf);
        Assert.Equal("999.888.777-66", porCpf[0].Cpf);
        Assert.Contains("999.888.777-66", porCpf[0].Identificacao);
    }

    [Fact]
    public async Task CriarAsync_PersisteEDefineCriadoEm()
    {
        var pessoa = new Pessoa { Nome = "Carla" };

        await _svc.CriarAsync(pessoa);

        var salva = await _db.Pessoas.FindAsync(pessoa.Id);
        Assert.NotNull(salva);
        Assert.True(salva!.CriadoEm > DateTime.MinValue);
    }

    [Fact]
    public async Task AtualizarAsync_PersisteAlteracoes()
    {
        var pessoa = new Pessoa { Nome = "Diego" };
        await _svc.CriarAsync(pessoa);

        pessoa.Telefone = "11977776666";
        await _svc.AtualizarAsync(pessoa);

        var atualizada = await _db.Pessoas.FindAsync(pessoa.Id);
        Assert.Equal("11977776666", atualizada!.Telefone);
    }

    [Fact]
    public async Task AtualizarAsync_Persiste_QuandoRecebeCopiaComMesmoIdDaInstanciaRastreada()
    {
        // Cenário real do circuito Blazor Server: a listagem deixa a entidade rastreada
        // no DbContext compartilhado e o diálogo de edição salva uma CÓPIA com o mesmo Id.
        // Com Update(cópia) isso lançava "another instance ... is already being tracked".
        var pessoa = new Pessoa { Nome = "Julia" };
        await _svc.CriarAsync(pessoa);   // instância original fica rastreada

        var copia = new Pessoa { Id = pessoa.Id, Nome = "Julia Atualizada", CriadoEm = pessoa.CriadoEm };
        await _svc.AtualizarAsync(copia);

        var atualizada = await _db.Pessoas.FindAsync(pessoa.Id);
        Assert.Equal("Julia Atualizada", atualizada!.Nome);
    }

    [Fact]
    public async Task AtualizarAsync_LancaExcecao_QuandoPessoaNaoExiste()
    {
        var fantasma = new Pessoa { Id = 999, Nome = "Não Existe" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AtualizarAsync(fantasma));
    }

    [Fact]
    public async Task DeletarAsync_LancaExcecao_QuandoPessoaTemVendas()
    {
        var pessoa = new Pessoa { Nome = "Elaine" };
        await _svc.CriarAsync(pessoa);
        _db.Vendas.Add(new Venda { PessoaId = pessoa.Id, ValorTotal = 100 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeletarAsync(pessoa.Id));

        Assert.NotNull(await _db.Pessoas.FindAsync(pessoa.Id));
    }

    [Fact]
    public async Task DeletarAsync_Remove_QuandoPessoaNaoTemVendas()
    {
        var pessoa = new Pessoa { Nome = "Fabio" };
        await _svc.CriarAsync(pessoa);

        await _svc.DeletarAsync(pessoa.Id);

        Assert.Null(await _db.Pessoas.FindAsync(pessoa.Id));
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoCpfInvalido()
    {
        var pessoa = new Pessoa { Nome = "Gustavo", Cpf = "111.111.111-11" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(pessoa));
    }

    [Fact]
    public async Task CriarAsync_Persiste_QuandoCpfValido()
    {
        var pessoa = new Pessoa { Nome = "Helena", Cpf = "529.982.247-25" };

        await _svc.CriarAsync(pessoa);

        Assert.NotNull(await _db.Pessoas.FindAsync(pessoa.Id));
    }

    [Fact]
    public async Task AtualizarAsync_LancaExcecao_QuandoCpfInvalido()
    {
        var pessoa = new Pessoa { Nome = "Igor" };
        await _svc.CriarAsync(pessoa);
        pessoa.Cpf = "123.456.789-00";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AtualizarAsync(pessoa));
    }

    [Fact]
    public async Task ConsultasDeLeitura_NaoRastreiamEntidadesNoContexto()
    {
        // Leituras de exibição não devem anexar entidades ao DbContext do circuito —
        // era a origem do conflito de tracking na edição (a outra metade da correção).
        var pessoa = new Pessoa { Nome = "Larissa" };
        await _svc.CriarAsync(pessoa);
        _db.Vendas.Add(new Venda { PessoaId = pessoa.Id, ValorTotal = 50 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _svc.ListarAsync();
        await _svc.ObterAsync(pessoa.Id);
        await _svc.HistoricoAsync(pessoa.Id);

        Assert.Empty(_db.ChangeTracker.Entries());
    }
}
