using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class PedidoAsignadoPor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AsignadoPorId",
                table: "Pedidos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_AsignadoPorId",
                table: "Pedidos",
                column: "AsignadoPorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_Usuarios_AsignadoPorId",
                table: "Pedidos",
                column: "AsignadoPorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Pedidos ya asignados: quien los asignó sale de la última asignación o reasignación del LOG
            // ("Pedido PED-001 asignado..."); si el LOG no la tiene, queda en null y la orden dice "Sin registro"
            migrationBuilder.Sql(@"
                UPDATE Pedidos p
                SET p.AsignadoPorId = (
                    SELECT l.UsuarioId FROM LogsSistema l
                    WHERE l.Accion IN ('Asignar Pedido', 'Reasignar Pedido') AND l.UsuarioId IS NOT NULL
                      AND l.Detalle LIKE CONCAT('%Pedido ', p.CodigoPedido, ' %')
                    ORDER BY l.FechaHora DESC LIMIT 1)
                WHERE p.FechaAsignacion IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_Usuarios_AsignadoPorId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_AsignadoPorId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "AsignadoPorId",
                table: "Pedidos");
        }
    }
}
