using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class BodegaDelPersonal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_BodegaId_CodigoPedido",
                table: "Pedidos",
                columns: new[] { "BodegaId", "CodigoPedido" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_BodegaId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_CodigoPedido",
                table: "Pedidos");

            migrationBuilder.AddColumn<int>(
                name: "BodegaId",
                table: "Usuarios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BodegaId",
                table: "Empleados",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_BodegaId",
                table: "Usuarios",
                column: "BodegaId");

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_BodegaId",
                table: "Empleados",
                column: "BodegaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Empleados_Bodegas_BodegaId",
                table: "Empleados",
                column: "BodegaId",
                principalTable: "Bodegas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Bodegas_BodegaId",
                table: "Usuarios",
                column: "BodegaId",
                principalTable: "Bodegas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Empleados_Bodegas_BodegaId",
                table: "Empleados");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Bodegas_BodegaId",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_BodegaId",
                table: "Usuarios");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_BodegaId",
                table: "Pedidos",
                column: "BodegaId");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_BodegaId_CodigoPedido",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Empleados_BodegaId",
                table: "Empleados");

            migrationBuilder.DropColumn(
                name: "BodegaId",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "BodegaId",
                table: "Empleados");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CodigoPedido",
                table: "Pedidos",
                column: "CodigoPedido",
                unique: true);
        }
    }
}
