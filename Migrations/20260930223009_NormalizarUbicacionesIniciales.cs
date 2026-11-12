using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickingApp.Migrations
{
    /// <inheritdoc />
    public partial class NormalizarUbicacionesIniciales : Migration
    {
        // Ubicaciones que creaba DbInitializer con letras en pasillo/estante/nivel ("P01", "E01", "N01"), que el
        // formulario de Bodegas no acepta: (bodega, código anterior, pasillo, estante y nivel anteriores, valores nuevos)
        private static readonly (string Bodega, string CodigoAnterior, string Pasillo, string Estante, string Nivel, int P, int E, int N)[] Sembradas =
        {
            ("BOD-01", "PAS-01-EST-01-NIV-01", "P01", "E01", "N01", 1, 1, 1),
            ("BOD-01", "PAS-01-EST-01-NIV-02", "P01", "E01", "N02", 1, 1, 2),
            ("BOD-01", "PAS-01-EST-02-NIV-01", "P01", "E02", "N01", 1, 2, 1),
            ("BOD-01", "PAS-02-EST-01-NIV-01", "P02", "E01", "N01", 2, 1, 1),
            ("BOD-01", "PAS-02-EST-02-NIV-01", "P02", "E02", "N01", 2, 2, 1),
            ("BOD-02", "PAS-A-EST-01-NIV-01", "PA", "E01", "N01", 1, 1, 1),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Solo cambia las filas que siguen exactamente como las dejó el seed. Si en esa bodega ya existe
            // una ubicación con el código nuevo, la fila se deja igual para no duplicar el código.
            foreach (var u in Sembradas)
            {
                var codigoNuevo = $"PAS{u.P:D2}-EST{u.E:D2}-NIV{u.N:D2}";

                migrationBuilder.Sql($@"
                    SET @conflicto = (SELECT COUNT(*) FROM Ubicaciones x JOIN Bodegas b ON b.Id = x.BodegaId
                                      WHERE b.Codigo = '{u.Bodega}' AND x.CodigoUbicacion = '{codigoNuevo}');");

                migrationBuilder.Sql($@"
                    UPDATE Ubicaciones x JOIN Bodegas b ON b.Id = x.BodegaId
                    SET x.Pasillo = '{u.P}', x.Estante = '{u.E}', x.Nivel = '{u.N}', x.CodigoUbicacion = '{codigoNuevo}'
                    WHERE b.Codigo = '{u.Bodega}' AND x.CodigoUbicacion = '{u.CodigoAnterior}'
                      AND x.Pasillo = '{u.Pasillo}' AND x.Estante = '{u.Estante}' AND x.Nivel = '{u.Nivel}'
                      AND @conflicto = 0;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se revierte: el formato anterior no se podía editar desde la aplicación
        }
    }
}
