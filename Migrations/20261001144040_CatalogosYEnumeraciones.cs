using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class CatalogosYEnumeraciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Catálogos nuevos con sus filas fijas (mismos Id que Roles.Catalogo y TiposSolicitud.Catalogo)
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Nombre = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TiposSolicitud",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Nombre = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposSolicitud", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Nombre" },
                values: new object[,]
                {
                    { 1, "Administrador" },
                    { 2, "Supervisor" },
                    { 3, "Auxiliar" }
                });

            migrationBuilder.InsertData(
                table: "TiposSolicitud",
                columns: new[] { "Id", "Nombre" },
                values: new object[,]
                {
                    { 1, "Registro de empleado" },
                    { 2, "Editar empleado" },
                    { 3, "Inactivar empleado" },
                    { 4, "Editar ubicación" },
                    { 5, "Eliminar ubicación" }
                });

            // 2. Columnas de llave foránea; se llenan desde el texto anterior antes de borrarlo
            migrationBuilder.AddColumn<int>(
                name: "RolId",
                table: "Usuarios",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TipoSolicitudId",
                table: "Solicitudes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RolDestinoId",
                table: "Notificaciones",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("UPDATE Usuarios u JOIN Roles r ON r.Nombre = u.Rol SET u.RolId = r.Id;");
            migrationBuilder.Sql("UPDATE Notificaciones n JOIN Roles r ON r.Nombre = n.RolDestino SET n.RolDestinoId = r.Id;");
            migrationBuilder.Sql(@"UPDATE Solicitudes SET TipoSolicitudId = CASE TipoSolicitud
                WHEN 'RegistroEmpleado' THEN 1
                WHEN 'EdicionEmpleado' THEN 2
                WHEN 'InactivacionEmpleado' THEN 3
                WHEN 'EdicionUbicacion' THEN 4
                WHEN 'EliminacionUbicacion' THEN 5
                ELSE 0 END;");

            migrationBuilder.DropColumn(
                name: "Rol",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "TipoSolicitud",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "RolDestino",
                table: "Notificaciones");

            // 3. Columnas de pocos valores posibles pasan a ENUM de MySQL
            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Usuarios",
                type: "enum('Activo','Inactivo','Bloqueado')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Solicitudes",
                type: "enum('Pendiente','Aprobada','Rechazada')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(30)",
                oldMaxLength: 30)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Prioridad",
                table: "Pedidos",
                type: "enum('Alta','Media','Baja')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Pedidos",
                type: "enum('Pendiente','Asignado','Reasignado','Completado','Cancelado','Dividido')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(30)",
                oldMaxLength: 30)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "TipoNovedad",
                table: "NovedadesPedido",
                type: "enum('Faltante','Avería','Ubicación Incorrecta','Otro')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Tipo",
                table: "Notificaciones",
                type: "enum('Info','Warning','Success','Danger')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // 4. Índices y llaves foráneas
            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_RolId",
                table: "Usuarios",
                column: "RolId");

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_TipoSolicitudId",
                table: "Solicitudes",
                column: "TipoSolicitudId");

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_RolDestinoId",
                table: "Notificaciones",
                column: "RolDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Nombre",
                table: "Roles",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TiposSolicitud_Nombre",
                table: "TiposSolicitud",
                column: "Nombre",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notificaciones_Roles_RolDestinoId",
                table: "Notificaciones",
                column: "RolDestinoId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Solicitudes_TiposSolicitud_TipoSolicitudId",
                table: "Solicitudes",
                column: "TipoSolicitudId",
                principalTable: "TiposSolicitud",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Roles_RolId",
                table: "Usuarios",
                column: "RolId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notificaciones_Roles_RolDestinoId",
                table: "Notificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_Solicitudes_TiposSolicitud_TipoSolicitudId",
                table: "Solicitudes");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Roles_RolId",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_RolId",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_TipoSolicitudId",
                table: "Solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_Notificaciones_RolDestinoId",
                table: "Notificaciones");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Usuarios",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Activo','Inactivo','Bloqueado')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Solicitudes",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Pendiente','Aprobada','Rechazada')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Prioridad",
                table: "Pedidos",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Alta','Media','Baja')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                table: "Pedidos",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Pendiente','Asignado','Reasignado','Completado','Cancelado','Dividido')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "TipoNovedad",
                table: "NovedadesPedido",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Faltante','Avería','Ubicación Incorrecta','Otro')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Tipo",
                table: "Notificaciones",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Info','Warning','Success','Danger')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // Se recupera el texto anterior desde los catálogos antes de borrarlos
            migrationBuilder.AddColumn<string>(
                name: "Rol",
                table: "Usuarios",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TipoSolicitud",
                table: "Solicitudes",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RolDestino",
                table: "Notificaciones",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE Usuarios u JOIN Roles r ON r.Id = u.RolId SET u.Rol = r.Nombre;");
            migrationBuilder.Sql("UPDATE Notificaciones n JOIN Roles r ON r.Id = n.RolDestinoId SET n.RolDestino = r.Nombre;");
            migrationBuilder.Sql(@"UPDATE Solicitudes SET TipoSolicitud = CASE TipoSolicitudId
                WHEN 1 THEN 'RegistroEmpleado'
                WHEN 2 THEN 'EdicionEmpleado'
                WHEN 3 THEN 'InactivacionEmpleado'
                WHEN 4 THEN 'EdicionUbicacion'
                WHEN 5 THEN 'EliminacionUbicacion'
                ELSE '' END;");

            migrationBuilder.DropColumn(
                name: "RolId",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "TipoSolicitudId",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "RolDestinoId",
                table: "Notificaciones");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "TiposSolicitud");
        }
    }
}
