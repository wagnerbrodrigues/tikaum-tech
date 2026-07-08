using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class RelatorioServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ApplicationDbContext _db;
    private readonly RelatorioService _svc;
    private readonly VendaService _vendaSvc;

    public RelatorioServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new RelatorioService(_db);
        _vendaSvc = new VendaService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task GerarAsync_RetornaVendasNoPeriodo()
    {
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, new DateTime(2026, 1, 15), null, [
            new NovoItemVendaDto(null, null, "Item janeiro", 1, 200m)
        ]));
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, new DateTime(2026, 2, 10), null, [
            new NovoItemVendaDto(null, null, "Item fevereiro", 1, 300m)
        ]));

        var resultado = await _svc.GerarAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        Assert.Single(resultado.Vendas);
        Assert.Equal(200m, resultado.TotalGeral);
    }

    [Fact]
    public async Task GerarAsync_RetornaVazioForaDoPeriodo()
    {
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, new DateTime(2025, 12, 1), null, [
            new NovoItemVendaDto(null, null, "Ano passado", 1, 100m)
        ]));

        var resultado = await _svc.GerarAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        Assert.Empty(resultado.Vendas);
        Assert.Equal(0m, resultado.TotalGeral);
    }

    [Fact]
    public async Task GerarAsync_ExcluiVendasDisabledEDeleted()
    {
        var data = new DateTime(2026, 6, 10);
        var editada = await _vendaSvc.CriarAsync(new NovaVendaDto(null, data, null, [
            new NovoItemVendaDto(null, null, "Original", 1, 100m)
        ]));
        await _vendaSvc.EditarAsync(editada.Id, new NovaVendaDto(null, data, null, [
            new NovoItemVendaDto(null, null, "Corrigida", 1, 150m)
        ]));
        var excluida = await _vendaSvc.CriarAsync(new NovaVendaDto(null, data, null, [
            new NovoItemVendaDto(null, null, "Cancelada", 1, 999m)
        ]));
        await _vendaSvc.ExcluirAsync(excluida.Id);

        var resultado = await _svc.GerarAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        // Só a substituta ativa entra no relatório — disabled/deleted ficam de fora
        // de qualquer soma/contagem (TIKAUM_SPEC.md §5)
        Assert.Single(resultado.Vendas);
        Assert.Equal(150m, resultado.TotalGeral);
    }

    [Fact]
    public async Task GerarAsync_IncluiVendaExatamenteNoDiaFim()
    {
        var dataFim = new DateTime(2026, 6, 30);
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, dataFim, null, [
            new NovoItemVendaDto(null, null, "Ultimo dia", 1, 150m)
        ]));

        var resultado = await _svc.GerarAsync(new DateTime(2026, 6, 1), dataFim);

        Assert.Single(resultado.Vendas);
    }

    [Fact]
    public async Task GerarAsync_SubtotaisAgrupadosPorPessoa()
    {
        var pessoa = new Pessoa { Nome = "Ana Lima" };
        _db.Pessoas.Add(pessoa);
        await _db.SaveChangesAsync();

        var base_ = new DateTime(2026, 6, 1);
        await _vendaSvc.CriarAsync(new NovaVendaDto(pessoa.Id, base_, null, [
            new NovoItemVendaDto(null, null, "Tatuagem", 1, 500m)
        ]));
        await _vendaSvc.CriarAsync(new NovaVendaDto(pessoa.Id, base_.AddDays(1), null, [
            new NovoItemVendaDto(null, null, "Retoque", 1, 100m)
        ]));
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, base_.AddDays(2), null, [
            new NovoItemVendaDto(null, null, "Avulso", 1, 50m)
        ]));

        var resultado = await _svc.GerarAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        Assert.Equal(650m, resultado.TotalGeral);
        Assert.Equal(2, resultado.Subtotais.Count);

        var subtotalAna = resultado.Subtotais.First(s => s.NomePessoa == "Ana Lima");
        Assert.Equal(600m, subtotalAna.Total);
        Assert.Equal(2, subtotalAna.NumeroVendas);

        var subtotalAvulso = resultado.Subtotais.First(s => s.NomePessoa == "Avulsa");
        Assert.Equal(50m, subtotalAvulso.Total);
    }

    [Fact]
    public async Task GerarAsync_SubtotalOrdenadoPorTotalDecrescente()
    {
        var p1 = new Pessoa { Nome = "Rico" };
        var p2 = new Pessoa { Nome = "Pobre" };
        _db.Pessoas.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        var base_ = new DateTime(2026, 6, 1);
        await _vendaSvc.CriarAsync(new NovaVendaDto(p1.Id, base_, null, [
            new NovoItemVendaDto(null, null, "Tattoo grande", 1, 1000m)
        ]));
        await _vendaSvc.CriarAsync(new NovaVendaDto(p2.Id, base_, null, [
            new NovoItemVendaDto(null, null, "Piercing", 1, 80m)
        ]));

        var resultado = await _svc.GerarAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        Assert.Equal("Rico", resultado.Subtotais[0].NomePessoa);
        Assert.Equal("Pobre", resultado.Subtotais[1].NomePessoa);
    }

    [Fact]
    public async Task GerarAsync_FiltrarPorPessoa()
    {
        var p = new Pessoa { Nome = "Filtrada" };
        _db.Pessoas.Add(p);
        await _db.SaveChangesAsync();

        var base_ = new DateTime(2026, 6, 1);
        await _vendaSvc.CriarAsync(new NovaVendaDto(p.Id, base_, null, [
            new NovoItemVendaDto(null, null, "Da pessoa", 1, 200m)
        ]));
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, base_, null, [
            new NovoItemVendaDto(null, null, "Avulso", 1, 50m)
        ]));

        var resultado = await _svc.GerarAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), p.Id);

        Assert.Single(resultado.Vendas);
        Assert.Equal(200m, resultado.TotalGeral);
    }

    [Fact]
    public async Task ExportarExcel_RetornaBytesNaoVazios()
    {
        var resultado = await _svc.GerarAsync(DateTime.Today, DateTime.Today);

        var bytes = _svc.ExportarExcel(resultado, DateTime.Today, DateTime.Today);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task ExportarExcel_ComDados_RetornaPlanilhaValida()
    {
        await _vendaSvc.CriarAsync(new NovaVendaDto(null, DateTime.Today, null, [
            new NovoItemVendaDto(null, null, "Item teste", 2, 75m)
        ]));
        var resultado = await _svc.GerarAsync(DateTime.Today, DateTime.Today);

        var bytes = _svc.ExportarExcel(resultado, DateTime.Today, DateTime.Today);

        // Arquivo XLSX é um ZIP — começa com PK header
        Assert.Equal(0x50, bytes[0]); // 'P'
        Assert.Equal(0x4B, bytes[1]); // 'K'
    }

    [Fact]
    public async Task GerarAniversariantesAsync_FiltraPorMesEOrdenaPorDia()
    {
        _db.Pessoas.AddRange(
            new Pessoa { Nome = "Dia Vinte", DataNascimento = new DateTime(1990, 3, 20) },
            new Pessoa { Nome = "Dia Cinco", DataNascimento = new DateTime(1985, 3, 5) },
            new Pessoa { Nome = "Outro Mes", DataNascimento = new DateTime(1990, 4, 5) });
        await _db.SaveChangesAsync();

        var resultado = await _svc.GerarAniversariantesAsync(3);

        Assert.Equal(2, resultado.Count);
        Assert.Equal("Dia Cinco", resultado[0].Nome);
        Assert.Equal("Dia Vinte", resultado[1].Nome);
    }

    [Fact]
    public async Task GerarAniversariantesAsync_IgnoraClientesSemDataNascimento()
    {
        _db.Pessoas.Add(new Pessoa { Nome = "Sem Nascimento" });
        await _db.SaveChangesAsync();

        var resultado = await _svc.GerarAniversariantesAsync(DateTime.Today.Month);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GerarAniversariantesAsync_CalculaIdadeQueCompleta()
    {
        var anoNascimento = DateTime.Today.Year - 30;
        _db.Pessoas.Add(new Pessoa { Nome = "Trinta Anos", DataNascimento = new DateTime(anoNascimento, 7, 10) });
        await _db.SaveChangesAsync();

        var resultado = await _svc.GerarAniversariantesAsync(7);

        Assert.Equal(30, Assert.Single(resultado).IdadeCompleta);
    }

    // --- Mensagem de aniversário (TIKAUM_SPEC.md §6, 2026-07-06) ---

    [Fact]
    public void GerarMensagemAniversario_UsaPrimeiroNomeESugereBrindeOuDesconto()
    {
        var mensagem = RelatorioService.GerarMensagemAniversario("Carla Mendes Vieira");

        Assert.Contains("Olá, Carla!", mensagem);
        Assert.Contains("Feliz Aniversário", mensagem);
        Assert.Contains("desconto", mensagem);
        Assert.Contains("brinde", mensagem);
    }

    [Fact]
    public void GerarMensagemAniversario_NomeComUmaPalavraSo_NaoQuebra()
    {
        var mensagem = RelatorioService.GerarMensagemAniversario("Madonna");

        Assert.Contains("Olá, Madonna!", mensagem);
    }
}
