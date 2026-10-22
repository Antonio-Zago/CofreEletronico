using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CofreEletronico.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    // Token de concorrência de estantes (xmin, coluna de sistema do PostgreSQL). O gerador de SQL do Npgsql ignora
    // AddColumn/DropColumn de "xmin": o script desta migration não altera a tabela (só registra o histórico).
    public partial class AdicionarConcorrenciaEstantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "estantes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "estantes");
        }
    }
}
