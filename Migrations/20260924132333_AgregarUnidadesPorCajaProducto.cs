using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class AgregarUnidadesPorCajaProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnidadesPorCaja",
                table: "Productos",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnidadesPorCaja",
                table: "Productos");
        }
    }
}
