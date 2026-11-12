using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class BodegaDeLaSolicitud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BodegaId",
                table: "Solicitudes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_BodegaId",
                table: "Solicitudes",
                column: "BodegaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Solicitudes_Bodegas_BodegaId",
                table: "Solicitudes",
                column: "BodegaId",
                principalTable: "Bodegas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Solicitudes_Bodegas_BodegaId",
                table: "Solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_BodegaId",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "BodegaId",
                table: "Solicitudes");
        }
    }
}
