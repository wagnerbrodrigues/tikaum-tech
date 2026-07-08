namespace TikaumTech.Models;

public class Produto
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public decimal PrecoPadrao { get; set; }
    public bool Ativo { get; set; } = true;

    public ICollection<ItemVenda> ItensVenda { get; set; } = [];
}
