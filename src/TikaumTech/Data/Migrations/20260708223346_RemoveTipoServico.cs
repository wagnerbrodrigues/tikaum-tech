using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikaumTech.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTipoServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tipo",
                table: "servicos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "servicos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
