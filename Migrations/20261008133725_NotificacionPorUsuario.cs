using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class NotificacionPorUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cada aviso a un rol (o a todos) pasa a ser una copia por usuario, con el estado de lectura que tenía
            migrationBuilder.Sql(@"INSERT INTO Notificaciones (UsuarioDestinoId, Titulo, Mensaje, Tipo, Leida, FechaCreacion, Enlace)
                SELECT u.Id, n.Titulo, n.Mensaje, n.Tipo, n.Leida, n.FechaCreacion, n.Enlace
                FROM Notificaciones n JOIN Usuarios u ON n.RolDestinoId IS NULL OR u.RolId = n.RolDestinoId
                WHERE n.UsuarioDestinoId IS NULL;");
            migrationBuilder.Sql("DELETE FROM Notificaciones WHERE UsuarioDestinoId IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Notificaciones_Roles_RolDestinoId",
                table: "Notificaciones");

            migrationBuilder.DropIndex(
                name: "IX_Notificaciones_RolDestinoId",
                table: "Notificaciones");

            migrationBuilder.DropColumn(
                name: "RolDestinoId",
                table: "Notificaciones");

            migrationBuilder.AlterColumn<int>(
                name: "UsuarioDestinoId",
                table: "Notificaciones",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UsuarioDestinoId",
                table: "Notificaciones",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "RolDestinoId",
                table: "Notificaciones",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_RolDestinoId",
                table: "Notificaciones",
                column: "RolDestinoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notificaciones_Roles_RolDestinoId",
                table: "Notificaciones",
                column: "RolDestinoId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
