using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class SelloSeguridadUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelloSeguridad",
                table: "Usuarios",
                type: "varchar(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Las cuentas existentes reciben un sello propio en lugar de quedar todas con el mismo valor vacío
            migrationBuilder.Sql("UPDATE Usuarios SET SelloSeguridad = UUID();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelloSeguridad",
                table: "Usuarios");
        }
    }
}
