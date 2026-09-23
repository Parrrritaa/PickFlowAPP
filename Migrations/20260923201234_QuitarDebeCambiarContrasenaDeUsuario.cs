using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class QuitarDebeCambiarContrasenaDeUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DebeCambiarContrasena",
                table: "Usuarios");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DebeCambiarContrasena",
                table: "Usuarios",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
