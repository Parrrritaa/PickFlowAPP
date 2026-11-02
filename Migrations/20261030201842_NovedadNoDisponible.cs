using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class NovedadNoDisponible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TipoNovedad",
                table: "NovedadesPedido",
                type: "enum('Faltante','Avería','Ubicación Incorrecta','Diferencia Cantidad','Otro','No disponible')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Faltante','Avería','Ubicación Incorrecta','Diferencia Cantidad','Otro')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TipoNovedad",
                table: "NovedadesPedido",
                type: "enum('Faltante','Avería','Ubicación Incorrecta','Diferencia Cantidad','Otro')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Faltante','Avería','Ubicación Incorrecta','Diferencia Cantidad','Otro','No disponible')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
