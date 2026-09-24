using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class QuitarEstadoEnProceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EnProceso nunca se asignaba; por si alguna fila lo tiene, pasa al estado equivalente.
            migrationBuilder.Sql("UPDATE Pedidos SET Estado = 'Asignado' WHERE Estado = 'EnProceso';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
