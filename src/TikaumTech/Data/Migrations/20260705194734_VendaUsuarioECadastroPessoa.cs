using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikaumTech.Migrations
{
    /// <inheritdoc />
    public partial class VendaUsuarioECadastroPessoa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Corrigido à mão: o scaffold gerou RenameColumn(email → data_nascimento),
            // o que manteria e-mails antigos como "datas de nascimento" (lixo que quebra
            // a leitura de DateTime). A intenção da spec §5 é REMOVER email e criar
            // data_nascimento vazia — drop + add.
            migrationBuilder.DropColumn(
                name: "email",
                table: "pessoas");

            migrationBuilder.AddColumn<DateTime>(
                name: "data_nascimento",
                table: "pessoas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cpf",
                table: "pessoas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "usuario",
                table: "vendas",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "usuario",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "cpf",
                table: "pessoas");

            migrationBuilder.DropColumn(
                name: "data_nascimento",
                table: "pessoas");

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "pessoas",
                type: "TEXT",
                nullable: true);
        }
    }
}
