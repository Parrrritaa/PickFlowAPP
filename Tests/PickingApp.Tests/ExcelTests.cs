using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Validación del Excel de pedidos (ExcelService.ValidarArchivoExcelAsync); no guarda nada.
    public class ExcelTests
    {
        // Columnas A–I: CodigoPedido, Bodega, Pasillo, Estante, Nivel, CodigoProducto, NombreProducto, Cantidad, Prioridad
        private static MemoryStream Archivo(params object[][] filas)
        {
            using var libro = new XLWorkbook();
            var hoja = libro.AddWorksheet("Pedidos");
            var encabezados = new[] { "CodigoPedido", "Bodega", "Pasillo", "Estante", "Nivel", "CodigoProducto", "NombreProducto", "Cantidad", "Prioridad" };
            for (var c = 0; c < encabezados.Length; c++) hoja.Cell(1, c + 1).Value = encabezados[c];
            for (var f = 0; f < filas.Length; f++)
                for (var c = 0; c < filas[f].Length; c++) hoja.Cell(f + 2, c + 1).Value = XLCellValue.FromObject(filas[f][c]);

            var stream = new MemoryStream();
            libro.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        private static object[] Fila(Ubicacion u, string pedido, string prioridad = "Media", string? codigoProducto = null) =>
            new object[] { pedido, "BOD-01", u.Pasillo, u.Estante, u.Nivel, codigoProducto ?? u.Producto!.Codigo, u.Producto!.Nombre, 2, prioridad };

        private static Task<ExcelValidationResult> ValidarAsync(Escenario e, MemoryStream archivo) =>
            new ExcelService(e.Db, NullLogger<ExcelService>.Instance).ValidarArchivoExcelAsync(archivo, Array.Empty<ResolucionExcel>(), puedeEditarUbicaciones: true);

        [Fact]
        public async Task CodigosQueSoloDifierenEnMayusculasSonElMismoPedido()
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);
            var b = e.Ubicacion(Escenario.Pequeno);

            var r = await ValidarAsync(e, Archivo(Fila(a, "PED-900"), Fila(b, "ped-900")));

            Assert.True(r.IsValid, string.Join(" / ", r.Errors));
            Assert.Equal(1, r.TotalPedidos);
            Assert.Equal(2, r.PedidosValidados.Single().Detalles.Count);
        }

        [Theory]
        [InlineData("alta", Prioridades.Alta)]
        [InlineData("BAJA", Prioridades.Baja)]
        [InlineData("", Prioridades.Media)]
        [InlineData("Urgente", Prioridades.Media)]
        public async Task LaPrioridadSeReconoceSinImportarMayusculas(string enArchivo, string esperada)
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);

            var r = await ValidarAsync(e, Archivo(Fila(a, "PED-901", enArchivo)));

            Assert.Equal(esperada, r.PedidosValidados.Single().Prioridad);
        }

        [Fact]
        public async Task UnPedidoConProductosEnDosBodegasSeRechaza()
        {
            using var e = new Escenario();
            var enBod01 = e.Ubicacion(Escenario.Pequeno);
            var otra = new Bodega { Codigo = "BOD-02", Nombre = "Otra bodega" };
            var enBod02 = new Ubicacion
            {
                Bodega = otra, Pasillo = "1", Estante = "1", Nivel = "1",
                CodigoUbicacion = Ubicacion.GenerarCodigo("1", "1", "1"),
                Producto = new Producto { Codigo = "PROD-900", Nombre = "Producto en otra bodega" }
            };
            e.Db.Ubicaciones.Add(enBod02);
            e.Db.SaveChanges();
            var filaBod02 = new object[] { "PED-902", "BOD-02", "1", "1", "1", "PROD-900", "Producto en otra bodega", 2, "Media" };

            var dosBodegas = await ValidarAsync(e, Archivo(Fila(enBod01, "PED-902"), filaBod02));
            // Cada bodega en su propio pedido sí es válido
            var separados = await ValidarAsync(e, Archivo(Fila(enBod01, "PED-903"), filaBod02.Select((v, i) => i == 0 ? "PED-904" : v).ToArray()));

            Assert.False(dosBodegas.IsValid);
            Assert.Contains(dosBodegas.Errors, m => m.Contains("PED-902") && m.Contains("BOD-01") && m.Contains("BOD-02"));
            Assert.True(separados.IsValid, string.Join(" / ", separados.Errors));
            Assert.Equal(2, separados.TotalPedidos);
        }

        [Fact]
        public async Task UnArchivoIlegibleNoMuestraDetallesTecnicos()
        {
            using var e = new Escenario();

            var r = await ValidarAsync(e, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("esto no es un Excel")));

            Assert.False(r.IsValid);
            Assert.StartsWith("No fue posible leer el archivo seleccionado. Verifique que sea un Excel (.xlsx) válido", r.Errors.Single());
        }

        [Fact]
        public async Task UnCodigoDemasiadoLargoEsUnErrorDelArchivo()
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);

            var r = await ValidarAsync(e, Archivo(Fila(a, "PED-" + new string('9', 60))));

            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, m => m.Contains("hasta 50 caracteres"));
        }
    }
}
