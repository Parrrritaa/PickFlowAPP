using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class NovedadRegistradaPorUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RegistradoPorId",
                table: "NovedadesPedido",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovedadesPedido_RegistradoPorId",
                table: "NovedadesPedido",
                column: "RegistradoPorId");

            migrationBuilder.AddForeignKey(
                name: "FK_NovedadesPedido_Usuarios_RegistradoPorId",
                table: "NovedadesPedido",
                column: "RegistradoPorId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Las novedades anteriores solo tienen el nombre: se enlazan cuando ese nombre es de un único usuario
            migrationBuilder.Sql(@"
                UPDATE NovedadesPedido n
                JOIN (SELECT NombreCompleto, MIN(Id) AS Id FROM Usuarios GROUP BY NombreCompleto HAVING COUNT(*) = 1) u
                    ON u.NombreCompleto = n.RegistradoPor
                SET n.RegistradoPorId = u.Id
                WHERE n.RegistradoPorId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NovedadesPedido_Usuarios_RegistradoPorId",
                table: "NovedadesPedido");

            migrationBuilder.DropIndex(
                name: "IX_NovedadesPedido_RegistradoPorId",
                table: "NovedadesPedido");

            migrationBuilder.DropColumn(
                name: "RegistradoPorId",
                table: "NovedadesPedido");
        }
    }
}
