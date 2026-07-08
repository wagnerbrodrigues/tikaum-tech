namespace TikaumTech.Models;

public class ItemVenda
{
    public int Id { get; set; }
    public int VendaId { get; set; }
    public int? ProdutoId { get; set; }
    public int? ServicoId { get; set; }
    public string? DescricaoLivre { get; set; }
    public decimal Quantidade { get; set; } = 1;
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }

    public Venda Venda { get; set; } = null!;
    public Produto? Produto { get; set; }
    public Servico? Servico { get; set; }
}
