using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class TrasladosDePersonal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrasladosPersonal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EmpleadoId = table.Column<int>(type: "int", nullable: true),
                    UsuarioId = table.Column<int>(type: "int", nullable: true),
                    BodegaOrigenId = table.Column<int>(type: "int", nullable: false),
                    BodegaDestinoId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrasladosPersonal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrasladosPersonal_Bodegas_BodegaDestinoId",
                        column: x => x.BodegaDestinoId,
                        principalTable: "Bodegas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrasladosPersonal_Bodegas_BodegaOrigenId",
                        column: x => x.BodegaOrigenId,
                        principalTable: "Bodegas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrasladosPersonal_Empleados_EmpleadoId",
                        column: x => x.EmpleadoId,
                        principalTable: "Empleados",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrasladosPersonal_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "TiposSolicitud",
                columns: new[] { "Id", "Nombre" },
                values: new object[] { 6, "Trasladar de bodega" });

            migrationBuilder.InsertData(
                table: "ObservacionesSolicitud",
                columns: new[] { "Id", "Activa", "Decision", "Texto", "TipoSolicitudId" },
                values: new object[,]
                {
                    { 1001, true, "Rechazar", "Motivo insuficiente", 6 },
                    { 1002, true, "Rechazar", "La bodega de destino no requiere personal", 6 },
                    { 1003, true, "Rechazar", "Se necesita en la bodega actual", 6 },
                    { 1004, true, "Aprobar", "Traslado autorizado", 6 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrasladosPersonal_BodegaDestinoId",
                table: "TrasladosPersonal",
                column: "BodegaDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_TrasladosPersonal_BodegaOrigenId",
                table: "TrasladosPersonal",
                column: "BodegaOrigenId");

            migrationBuilder.CreateIndex(
                name: "IX_TrasladosPersonal_EmpleadoId",
                table: "TrasladosPersonal",
                column: "EmpleadoId");

            migrationBuilder.CreateIndex(
                name: "IX_TrasladosPersonal_UsuarioId",
                table: "TrasladosPersonal",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrasladosPersonal");

            migrationBuilder.DeleteData(
                table: "ObservacionesSolicitud",
                keyColumn: "Id",
                keyValue: 1001);

            migrationBuilder.DeleteData(
                table: "ObservacionesSolicitud",
                keyColumn: "Id",
                keyValue: 1002);

            migrationBuilder.DeleteData(
                table: "ObservacionesSolicitud",
                keyColumn: "Id",
                keyValue: 1003);

            migrationBuilder.DeleteData(
                table: "ObservacionesSolicitud",
                keyColumn: "Id",
                keyValue: 1004);

            migrationBuilder.DeleteData(
                table: "TiposSolicitud",
                keyColumn: "Id",
                keyValue: 6);
        }
    }
}
