using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class CalcularDisponibilidadEmpleadoDesdePedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Quien fue marcado "Inactivo" desde la edición pasa a inactivo de verdad antes de perder la columna.
            migrationBuilder.Sql("UPDATE Empleados SET Activo = 0 WHERE EstadoDisponibilidad = 'Inactivo';");

            migrationBuilder.DropColumn(
                name: "EstadoDisponibilidad",
                table: "Empleados");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstadoDisponibilidad",
                table: "Empleados",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"UPDATE Empleados e SET e.EstadoDisponibilidad = CASE
                WHEN e.Activo = 0 THEN 'Inactivo'
                WHEN EXISTS (SELECT 1 FROM Pedidos p WHERE p.EmpleadoId = e.Id AND p.Estado IN ('Asignado', 'Reasignado', 'EnProceso')) THEN 'EnPicking'
                ELSE 'Disponible' END;");
        }
    }
}
