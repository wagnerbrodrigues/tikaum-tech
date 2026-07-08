using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;

namespace TikaumTech.Services;

public record SubtotalPessoa(string NomePessoa, int NumeroVendas, decimal Total);

public record RelatorioResultado(
    List<Venda> Vendas,
    List<SubtotalPessoa> Subtotais,
    decimal TotalGeral);

public record Aniversariante(
    int PessoaId,
    string Nome,
    int Dia,
    DateTime DataNascimento,
    int IdadeCompleta,
    string? Celular);

public class RelatorioService(ApplicationDbContext db)
{
    public async Task<RelatorioResultado> GerarAsync(
        DateTime inicio, DateTime fim, int? pessoaId = null)
    {
        // Relatórios só enxergam vendas ativas — disabled/deleted ficam no banco
        // apenas para auditoria (TIKAUM_SPEC.md §5)
        var query = db.Vendas
            .Include(v => v.Pessoa)
            .Include(v => v.Itens).ThenInclude(i => i.Produto)
            .Include(v => v.Itens).ThenInclude(i => i.Servico)
            .Where(v => v.Status == VendaStatus.Active)
            .Where(v => v.DataHora >= inicio && v.DataHora < fim.AddDays(1))
            .AsQueryable();

        if (pessoaId.HasValue)
            query = query.Where(v => v.PessoaId == pessoaId.Value);

        // Venda só registra a data (hora 00:00) — Id desempata vendas do mesmo dia
        var vendas = await query
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();

        var subtotais = vendas
            .GroupBy(v => v.PessoaId)
            .Select(g => new SubtotalPessoa(
                g.First().Pessoa?.Nome ?? "Avulsa",
                g.Count(),
                g.Sum(v => v.ValorTotal)))
            .OrderByDescending(s => s.Total)
            .ToList();

        return new RelatorioResultado(vendas, subtotais, vendas.Sum(v => v.ValorTotal));
    }

    public async Task<List<Aniversariante>> GerarAniversariantesAsync(int mes)
    {
        var anoAtual = DateTime.Today.Year;
        var pessoas = await db.Pessoas
            .Where(p => p.DataNascimento != null && p.DataNascimento.Value.Month == mes)
            .ToListAsync();

        return pessoas
            .Select(p => new Aniversariante(
                p.Id,
                p.Nome,
                p.DataNascimento!.Value.Day,
                p.DataNascimento.Value,
                anoAtual - p.DataNascimento.Value.Year,
                p.Telefone))
            .OrderBy(a => a.Dia)
            .ToList();
    }

    /// <summary>
    /// Texto sugerido para o atendente enviar ao cliente aniversariante (TIKAUM_SPEC.md §6,
    /// 2026-07-06) — editável na UI antes do envio; não é persistido nem enviado pelo app.
    /// </summary>
    public static string GerarMensagemAniversario(string nomeCliente)
    {
        var primeiroNome = nomeCliente
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? nomeCliente;

        return $"""
            Olá, {primeiroNome}! 🎉

            A equipe Tikaum Tattoo e Body Piercing deseja um Feliz Aniversário!

            Para comemorar, que tal um brinde especial ou 10% de desconto na sua próxima
            tatuagem, piercing ou produto? É só escolher na sua próxima visita.

            Esperamos você em breve!
            """;
    }

    public byte[] ExportarExcel(RelatorioResultado relatorio, DateTime inicio, DateTime fim)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Relatório");

        ws.Cell("A1").Value = $"Tikaum-Tech — Relatório de Vendas";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A2").Value = $"Período: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";

        var headers = new[] { "Data", "Cliente", "Usuário", "Itens", "Valor (R$)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1C1C1A");
            cell.Style.Font.FontColor = XLColor.FromHtml("#E8DDC8");
        }

        int row = 5;
        foreach (var v in relatorio.Vendas)
        {
            ws.Cell(row, 1).Value = v.DataHora.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = v.Pessoa?.Nome ?? "Avulsa";
            ws.Cell(row, 3).Value = v.Usuario ?? "";
            ws.Cell(row, 4).Value = string.Join(", ", v.Itens.Select(i =>
                i.Produto?.Nome ?? i.Servico?.Nome ?? i.DescricaoLivre ?? ""));
            ws.Cell(row, 5).Value = (double)v.ValorTotal;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        // Linha de total
        ws.Cell(row, 2).Value = $"Total ({relatorio.Vendas.Count} venda(s))";
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 5).Value = (double)relatorio.TotalGeral;
        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";

        // Subtotais por pessoa
        if (relatorio.Subtotais.Count > 1)
        {
            row += 2;
            ws.Cell(row, 1).Value = "Subtotal por cliente";
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
            foreach (var s in relatorio.Subtotais)
            {
                ws.Cell(row, 1).Value = s.NomePessoa;
                ws.Cell(row, 2).Value = $"{s.NumeroVendas} venda(s)";
                ws.Cell(row, 5).Value = (double)s.Total;
                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
