using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikaumTech.Migrations
{
    /// <inheritdoc />
    public partial class AddItemVendaIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_itens_venda_servico_id",
                table: "itens_venda",
                newName: "ix_itens_venda_servico_id");

            migrationBuilder.RenameIndex(
                name: "IX_itens_venda_produto_id",
                table: "itens_venda",
                newName: "ix_itens_venda_produto_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_itens_venda_servico_id",
                table: "itens_venda",
                newName: "IX_itens_venda_servico_id");

            migrationBuilder.RenameIndex(
                name: "ix_itens_venda_produto_id",
                table: "itens_venda",
                newName: "IX_itens_venda_produto_id");
        }
    }
}
