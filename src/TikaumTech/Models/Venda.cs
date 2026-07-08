namespace TikaumTech.Models;

/// <summary>
/// Valores da coluna vendas.status (TIKAUM_SPEC.md §5) — venda nunca é apagada
/// nem alterada in-place: editar desabilita a original e cria uma substituta;
/// excluir apenas marca como deleted.
/// </summary>
public static class VendaStatus
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Deleted = "deleted";
}

public static class VendaAdjustedReason
{
    public const string Edit = "edit";
    public const string Delete = "delete";
}

public class Venda
{
    public int Id { get; set; }
    public int? PessoaId { get; set; }
    public DateTime DataHora { get; set; } = DateTime.Now;
    public string? Observacao { get; set; }
    public decimal ValorTotal { get; set; }

    /// <summary>
    /// Username de quem fez a venda (TIKAUM_SPEC.md §5) — snapshot em texto, não FK
    /// para o Identity: renomear/excluir a conta não altera o histórico de vendas.
    /// </summary>
    public string? Usuario { get; set; }

    // Rastreabilidade — colunas em inglês por definição explícita da spec (§5)
    public string Status { get; set; } = VendaStatus.Active;
    public int? OriginId { get; set; }
    public DateTime? AdjustedAt { get; set; }
    public string? AdjustedReason { get; set; }

    public Pessoa? Pessoa { get; set; }
    public Venda? Origin { get; set; }
    public ICollection<Venda> Substitutas { get; set; } = [];
    public ICollection<ItemVenda> Itens { get; set; } = [];

    /// <summary>Registro ativo que substituiu este (só existe quando Status = disabled).</summary>
    public Venda? SubstituidaPor => Substitutas.FirstOrDefault();
}
