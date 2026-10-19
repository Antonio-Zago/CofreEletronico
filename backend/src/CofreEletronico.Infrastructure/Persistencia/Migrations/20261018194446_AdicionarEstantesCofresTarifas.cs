using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CofreEletronico.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarEstantesCofresTarifas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estantes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    segredo_hmac = table.Column<string>(type: "text", nullable: false),
                    nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endereco = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    device_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    online = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_heartbeat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estantes", x => x.id);
                    table.CheckConstraint("ck_estantes_device_id", "device_id ~ '^est-[a-z0-9]{6}$'");
                    table.CheckConstraint("ck_estantes_latitude", "latitude >= -90 AND latitude <= 90");
                    table.CheckConstraint("ck_estantes_longitude", "longitude >= -180 AND longitude <= 180");
                });

            migrationBuilder.CreateTable(
                name: "tarifas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tamanho = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_por_hora = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    tempo_minimo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarifas", x => x.id);
                    table.CheckConstraint("ck_tarifas_tamanho", "tamanho IN ('pequeno', 'medio', 'grande')");
                    table.CheckConstraint("ck_tarifas_tempo_minimo", "tempo_minimo > 0");
                    table.CheckConstraint("ck_tarifas_valor_por_hora", "valor_por_hora > 0");
                });

            migrationBuilder.CreateTable(
                name: "cofres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    estante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    canal_rele = table.Column<int>(type: "integer", nullable: false),
                    tamanho = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cofres", x => x.id);
                    table.CheckConstraint("ck_cofres_canal_rele", "canal_rele >= 0 AND canal_rele <= 15");
                    table.CheckConstraint("ck_cofres_numero", "numero > 0");
                    table.CheckConstraint("ck_cofres_status", "status IN ('livre', 'reservado', 'ocupado', 'manutencao')");
                    table.CheckConstraint("ck_cofres_tamanho", "tamanho IN ('pequeno', 'medio', 'grande')");
                    table.ForeignKey(
                        name: "fk_cofres_estantes_estante_id",
                        column: x => x.estante_id,
                        principalTable: "estantes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cofres_estante_id_canal_rele",
                table: "cofres",
                columns: new[] { "estante_id", "canal_rele" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cofres_estante_id_numero",
                table: "cofres",
                columns: new[] { "estante_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_estantes_device_id",
                table: "estantes",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tarifas_tamanho",
                table: "tarifas",
                column: "tamanho",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cofres");

            migrationBuilder.DropTable(
                name: "tarifas");

            migrationBuilder.DropTable(
                name: "estantes");
        }
    }
}
