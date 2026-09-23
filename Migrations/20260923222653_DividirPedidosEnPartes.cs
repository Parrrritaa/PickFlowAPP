using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class DividirPedidosEnPartes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumeroParte",
                table: "Pedidos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PedidoPadreId",
                table: "Pedidos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_PedidoPadreId",
                table: "Pedidos",
                column: "PedidoPadreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_Pedidos_PedidoPadreId",
                table: "Pedidos",
                column: "PedidoPadreId",
                principalTable: "Pedidos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_Pedidos_PedidoPadreId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_PedidoPadreId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "NumeroParte",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PedidoPadreId",
                table: "Pedidos");
        }
    }
}
