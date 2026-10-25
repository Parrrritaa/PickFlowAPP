using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoObservacionesSolicitud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ObservacionId",
                table: "Solicitudes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ObservacionesSolicitud",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TipoSolicitudId = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "enum('Aprobar','Rechazar')", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Texto = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Activa = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservacionesSolicitud", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObservacionesSolicitud_TiposSolicitud_TipoSolicitudId",
                        column: x => x.TipoSolicitudId,
                        principalTable: "TiposSolicitud",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "ObservacionesSolicitud",
                columns: new[] { "Id", "Activa", "Decision", "Texto", "TipoSolicitudId" },
                values: new object[,]
                {
                    { 1, true, "Rechazar", "Identificación duplicada", 1 },
                    { 2, true, "Rechazar", "Datos incompletos o incorrectos", 1 },
                    { 3, true, "Rechazar", "No se requiere personal adicional", 1 },
                    { 4, true, "Aprobar", "Datos verificados", 1 },
                    { 5, true, "Aprobar", "Incorporación autorizada", 1 },
                    { 6, true, "Rechazar", "Los cambios no están justificados", 2 },
                    { 7, true, "Rechazar", "Datos incorrectos", 2 },
                    { 8, true, "Rechazar", "Afecta pedidos en curso", 2 },
                    { 9, true, "Aprobar", "Cambios verificados", 2 },
                    { 10, true, "Rechazar", "Motivo insuficiente", 3 },
                    { 11, true, "Rechazar", "Tiene pedidos en curso", 3 },
                    { 12, true, "Rechazar", "Requiere revisión con Recursos Humanos", 3 },
                    { 13, true, "Aprobar", "Inactivación justificada", 3 },
                    { 14, true, "Rechazar", "Ubicación en uso por pedidos activos", 4 },
                    { 15, true, "Rechazar", "Datos de ubicación incorrectos", 4 },
                    { 16, true, "Rechazar", "El producto no corresponde", 4 },
                    { 17, true, "Aprobar", "Cambios verificados", 4 },
                    { 18, true, "Rechazar", "Ubicación en uso", 5 },
                    { 19, true, "Rechazar", "Tiene producto asignado", 5 },
                    { 20, true, "Rechazar", "Se recomienda inactivar en vez de eliminar", 5 },
                    { 21, true, "Aprobar", "Eliminación verificada", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_ObservacionId",
                table: "Solicitudes",
                column: "ObservacionId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservacionesSolicitud_TipoSolicitudId_Decision_Texto",
                table: "ObservacionesSolicitud",
                columns: new[] { "TipoSolicitudId", "Decision", "Texto" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Solicitudes_ObservacionesSolicitud_ObservacionId",
                table: "Solicitudes",
                column: "ObservacionId",
                principalTable: "ObservacionesSolicitud",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Solicitudes_ObservacionesSolicitud_ObservacionId",
                table: "Solicitudes");

            migrationBuilder.DropTable(
                name: "ObservacionesSolicitud");

            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_ObservacionId",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "ObservacionId",
                table: "Solicitudes");
        }
    }
}
