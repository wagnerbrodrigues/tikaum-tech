using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikaumTech.Migrations
{
    /// <inheritdoc />
    public partial class AddVendaStatusTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "adjusted_at",
                table: "vendas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "adjusted_reason",
                table: "vendas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "origin_id",
                table: "vendas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "vendas",
                type: "TEXT",
                nullable: false,
                defaultValue: "active");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_origin_id",
                table: "vendas",
                column: "origin_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_status",
                table: "vendas",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "FK_vendas_vendas_origin_id",
                table: "vendas",
                column: "origin_id",
                principalTable: "vendas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vendas_vendas_origin_id",
                table: "vendas");

            migrationBuilder.DropIndex(
                name: "ix_vendas_origin_id",
                table: "vendas");

            migrationBuilder.DropIndex(
                name: "ix_vendas_status",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "adjusted_at",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "adjusted_reason",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "origin_id",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "status",
                table: "vendas");
        }
    }
}
