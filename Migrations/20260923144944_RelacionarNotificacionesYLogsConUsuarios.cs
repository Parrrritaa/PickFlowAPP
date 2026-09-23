using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class RelacionarNotificacionesYLogsConUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UsuarioId",
                table: "LogsSistema",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_UsuarioDestinoId",
                table: "Notificaciones",
                column: "UsuarioDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_LogsSistema_UsuarioId",
                table: "LogsSistema",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_LogsSistema_Usuarios_UsuarioId",
                table: "LogsSistema",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notificaciones_Usuarios_UsuarioDestinoId",
                table: "Notificaciones",
                column: "UsuarioDestinoId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Asocia los logs existentes al usuario cuyo correo actual coincide; los demás quedan en NULL
            // y conservan el correo/rol en texto.
            migrationBuilder.Sql(
                "UPDATE LogsSistema l JOIN Usuarios u ON u.Correo = l.Usuario SET l.UsuarioId = u.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LogsSistema_Usuarios_UsuarioId",
                table: "LogsSistema");

            migrationBuilder.DropForeignKey(
                name: "FK_Notificaciones_Usuarios_UsuarioDestinoId",
                table: "Notificaciones");

            migrationBuilder.DropIndex(
                name: "IX_Notificaciones_UsuarioDestinoId",
                table: "Notificaciones");

            migrationBuilder.DropIndex(
                name: "IX_LogsSistema_UsuarioId",
                table: "LogsSistema");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "LogsSistema");
        }
    }
}
