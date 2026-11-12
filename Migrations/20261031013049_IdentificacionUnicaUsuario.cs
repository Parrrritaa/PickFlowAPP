using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class IdentificacionUnicaUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Editar usuario guardaba '' cuando se borraba la identificación: varios '' chocarían con el índice único
            migrationBuilder.Sql("UPDATE Usuarios SET Identificacion = NULL WHERE TRIM(Identificacion) = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Identificacion",
                table: "Usuarios",
                column: "Identificacion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usuarios_Identificacion",
                table: "Usuarios");
        }
    }
}
