namespace TikaumTech.Models;

public class Pessoa
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public DateTime? DataNascimento { get; set; }
    /// <summary>Coluna `telefone` no banco; a UI chama de "Celular" (TIKAUM_SPEC.md §5).</summary>
    public string? Telefone { get; set; }
    /// <summary>Desempate para homônimos — aparece na busca e nos autocompletes. Validado via <see cref="Services.CpfValidador"/> quando preenchido.</summary>
    public string? Cpf { get; set; }
    public string? Observacoes { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;

    public ICollection<Venda> Vendas { get; set; } = [];

    /// <summary>Linha secundária nos autocompletes (CPF · celular) — distingue homônimos na busca.</summary>
    public string Identificacao =>
        string.Join(" · ", new[] { Cpf, Telefone }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
