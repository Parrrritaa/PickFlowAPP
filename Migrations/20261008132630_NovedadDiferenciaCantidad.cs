using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class NovedadDiferenciaCantidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TipoNovedad",
                table: "NovedadesPedido",
                type: "enum('Faltante','Avería','Ubicación Incorrecta','Diferencia Cantidad','Otro')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Faltante','Avería','Ubicación Incorrecta','Otro')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El ENUM anterior no admite el valor nuevo
            migrationBuilder.Sql("UPDATE NovedadesPedido SET TipoNovedad = 'Otro' WHERE TipoNovedad = 'Diferencia Cantidad';");

            migrationBuilder.AlterColumn<string>(
                name: "TipoNovedad",
                table: "NovedadesPedido",
                type: "enum('Faltante','Avería','Ubicación Incorrecta','Otro')",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "enum('Faltante','Avería','Ubicación Incorrecta','Diferencia Cantidad','Otro')")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
