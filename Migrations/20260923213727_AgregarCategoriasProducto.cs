using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCategoriasProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoriaProductoId",
                table: "Productos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TiempoEstandarMinutos",
                table: "Pedidos",
                type: "double",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoriasProducto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Nombre = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Descripcion = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinutosPorUnidad = table.Column<double>(type: "double", nullable: false),
                    EsPredeterminada = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriasProducto", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "CategoriasProducto",
                columns: new[] { "Id", "Descripcion", "EsPredeterminada", "MinutosPorUnidad", "Nombre" },
                values: new object[,]
                {
                    { 1, "Mouse, memorias USB, cables", false, 0.29999999999999999, "Pequeño" },
                    { 2, "Teclados, routers, audífonos", true, 1.5, "Mediano" },
                    { 3, "Monitores, impresoras", false, 4.0, "Grande" },
                    { 4, "Torres de PC, UPS", false, 10.0, "Voluminoso / pesado" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Productos_CategoriaProductoId",
                table: "Productos",
                column: "CategoriaProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasProducto_Nombre",
                table: "CategoriasProducto",
                column: "Nombre",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Productos_CategoriasProducto_CategoriaProductoId",
                table: "Productos",
                column: "CategoriaProductoId",
                principalTable: "CategoriasProducto",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Productos_CategoriasProducto_CategoriaProductoId",
                table: "Productos");

            migrationBuilder.DropTable(
                name: "CategoriasProducto");

            migrationBuilder.DropIndex(
                name: "IX_Productos_CategoriaProductoId",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "CategoriaProductoId",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "TiempoEstandarMinutos",
                table: "Pedidos");
        }
    }
}
