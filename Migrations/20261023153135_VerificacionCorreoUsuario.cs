using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class VerificacionCorreoUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CorreoVerificado",
                table: "Usuarios",
                type: "tinyint(1)",
                nullable: false,
                // Las cuentas que ya existían quedan verificadas; solo las nuevas pasan por el código
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorreoVerificado",
                table: "Usuarios");
        }
    }
}
