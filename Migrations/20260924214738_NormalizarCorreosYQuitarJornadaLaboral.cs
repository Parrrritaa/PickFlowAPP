using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class NormalizarCorreosYQuitarJornadaLaboral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los correos se comparan tal cual: se guardan en minúsculas y sin espacios
            migrationBuilder.Sql("UPDATE Usuarios SET Correo = LOWER(TRIM(Correo));");
            migrationBuilder.Sql("UPDATE Empleados SET Correo = LOWER(TRIM(Correo)) WHERE Correo IS NOT NULL;");

            // JornadaLaboral pasa a calcularse desde HorarioEntrada/HorarioSalida
            migrationBuilder.DropColumn(
                name: "JornadaLaboral",
                table: "Empleados");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JornadaLaboral",
                table: "Empleados",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE Empleados SET JornadaLaboral = CONCAT(TIME_FORMAT(HorarioEntrada, '%H:%i'), ' - ', TIME_FORMAT(HorarioSalida, '%H:%i'));");
        }
    }
}
