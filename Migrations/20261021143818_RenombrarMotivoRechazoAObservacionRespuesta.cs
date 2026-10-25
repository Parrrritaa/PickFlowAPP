using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class RenombrarMotivoRechazoAObservacionRespuesta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MotivoRechazo",
                table: "Solicitudes",
                newName: "ObservacionRespuesta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ObservacionRespuesta",
                table: "Solicitudes",
                newName: "MotivoRechazo");
        }
    }
}
