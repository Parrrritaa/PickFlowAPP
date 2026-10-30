using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Models;
using PickingApp.Services;

namespace PickingApp.Tests
{
    // Validación del Excel de pedidos (ExcelService.ValidarArchivoExcelAsync); no guarda nada.
    public class ExcelTests
    {
        // Columnas A–F: CodigoPedido, Bodega, CodigoProducto, NombreProducto, Cantidad, Prioridad
        private static MemoryStream Archivo(params object[][] filas) =>
            Libro(new[] { "CodigoPedido", "Bodega", "CodigoProducto", "NombreProducto", "Cantidad", "Prioridad" }, filas);

        private static MemoryStream Libro(string[] encabezados, object[][] filas)
        {
            using var libro = new XLWorkbook();
            var hoja = libro.AddWorksheet("Pedidos");
            for (var c = 0; c < encabezados.Length; c++) hoja.Cell(1, c + 1).Value = encabezados[c];
            for (var f = 0; f < filas.Length; f++)
                for (var c = 0; c < filas[f].Length; c++) hoja.Cell(f + 2, c + 1).Value = XLCellValue.FromObject(filas[f][c]);

            var stream = new MemoryStream();
            libro.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        private static object[] Fila(Ubicacion u, string pedido, string prioridad = "Media", string bodega = "BOD-01") =>
            new object[] { pedido, bodega, u.Producto!.Codigo, u.Producto.Nombre, 2, prioridad };

        private static Task<ExcelValidationResult> ValidarAsync(Escenario e, MemoryStream archivo, int? bodegaDelUsuario = null,
            params ResolucionExcel[] resoluciones) =>
            new ExcelService(e.Db, NullLogger<ExcelService>.Instance).ValidarArchivoExcelAsync(archivo, resoluciones, bodegaDelUsuario);

        // Bodega activa con una ubicación activa del producto indicado
        private static Bodega OtraBodega(Escenario e, string codigo, string nombre, params Producto[] productos)
        {
            var bodega = new Bodega { Codigo = codigo, Nombre = nombre };
            for (var i = 0; i < productos.Length; i++)
            {
                var nivel = (i + 1).ToString();
                e.Db.Ubicaciones.Add(new Ubicacion
                {
                    Bodega = bodega, Pasillo = "9", Estante = "9", Nivel = nivel,
                    CodigoUbicacion = Ubicacion.GenerarCodigo("9", "9", nivel), Producto = productos[i]
                });
            }
            if (productos.Length == 0) e.Db.Bodegas.Add(bodega);
            e.Db.SaveChanges();
            return bodega;
        }

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
        public async Task LaUbicacionLaEligeElSistemaEntreLasActivasDeLaBodega()
        {
            using var e = new Escenario();
            var segunda = e.Ubicacion(Escenario.Pequeno);
            // Mismo producto en una ubicación de menor código pero inactiva, y en otra activa de menor código
            var inactiva = new Ubicacion { BodegaId = segunda.BodegaId, Pasillo = "0", Estante = "0", Nivel = "1",
                CodigoUbicacion = "PAS00-EST00-NIV01", Producto = segunda.Producto, Activa = false };
            var primera = new Ubicacion { BodegaId = segunda.BodegaId, Pasillo = "0", Estante = "1", Nivel = "1",
                CodigoUbicacion = "PAS00-EST01-NIV01", Producto = segunda.Producto };
            e.Db.Ubicaciones.AddRange(inactiva, primera);
            e.Db.SaveChanges();

            var r = await ValidarAsync(e, Archivo(Fila(segunda, "PED-905")));

            Assert.True(r.IsValid, string.Join(" / ", r.Errors));
            Assert.Equal(primera.Id, r.PedidosValidados.Single().Detalles.Single().UbicacionId);
        }

        [Fact]
        public async Task UnProductoQueLaBodegaNoTieneQuedaComoNovedadYElRestoSeCarga()
        {
            using var e = new Escenario();
            var disponible = e.Ubicacion(Escenario.Pequeno);
            // Este producto solo está en Norte
            var soloEnNorte = new Producto { Codigo = "PROD-950", Nombre = "Solo en Norte" };
            OtraBodega(e, "BOD-02", "Norte", soloEnNorte);
            var filaNorte = new object[] { "PED-906", "BOD-01", "PROD-950", "Solo en Norte", 3, "Media" };

            var r = await ValidarAsync(e, Archivo(Fila(disponible, "PED-906"), filaNorte));

            Assert.True(r.IsValid, string.Join(" / ", r.Errors));
            var pedido = r.PedidosValidados.Single();
            Assert.Single(pedido.Detalles);
            var novedad = pedido.Novedades.Single();
            Assert.Equal(TiposNovedad.NoDisponible, novedad.TipoNovedad);
            Assert.Contains("PROD-950", novedad.Descripcion);
            Assert.Contains("Disponible en: Norte", novedad.Descripcion);
            Assert.Equal(1, r.FilasNoDisponibles);
            Assert.Contains(r.NoDisponibles, m => m.Contains("PED-906"));
        }

        [Fact]
        public async Task SinNingunProductoDisponibleElPedidoSeRechazaYSeRecomiendanBodegas()
        {
            using var e = new Escenario();
            var p1 = new Producto { Codigo = "PROD-951", Nombre = "Uno" };
            var p2 = new Producto { Codigo = "PROD-952", Nombre = "Dos" };
            OtraBodega(e, "BOD-02", "Norte", p1, p2);
            OtraBodega(e, "BOD-03", "Sur", new Producto { Codigo = "PROD-953", Nombre = "Tres" });
            e.Db.Productos.Add(new Producto { Codigo = "PROD-954", Nombre = "Ninguna lo tiene" });
            e.Db.SaveChanges();

            var r = await ValidarAsync(e, Archivo(
                new object[] { "PED-907", "BOD-01", "PROD-951", "Uno", 1, "Media" },
                new object[] { "PED-907", "BOD-01", "PROD-952", "Dos", 1, "Media" },
                new object[] { "PED-908", "BOD-01", "PROD-954", "Ninguna lo tiene", 1, "Media" }));

            Assert.False(r.IsValid);
            Assert.Empty(r.PedidosValidados);
            Assert.Contains(r.Errors, m => m.Contains("PED-907") && m.Contains("Norte (2 de 2)") && !m.Contains("Sur"));
            Assert.Contains(r.Errors, m => m.Contains("PED-908") && m.Contains("Ninguna bodega maneja"));
        }

        [Fact]
        public async Task UnaBodegaInactivaNoTieneProductosDisponibles()
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);
            e.Db.Bodegas.Single(b => b.Codigo == "BOD-01").Activa = false;
            e.Db.SaveChanges();

            var r = await ValidarAsync(e, Archivo(Fila(a, "PED-909")));

            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, m => m.Contains("PED-909") && m.Contains("ninguno de sus productos"));
        }

        [Fact]
        public async Task UnProductoNoRegistradoEsUnErrorDeLaFila()
        {
            using var e = new Escenario();

            var r = await ValidarAsync(e, Archivo(new object[] { "PED-910", "BOD-01", "PROD-NADA", "No existe", 1, "Media" }));

            Assert.False(r.IsValid);
            Assert.Equal(1, r.FilasConProductoNoRegistrado);
            Assert.Contains(r.Errors, m => m.StartsWith("Fila 2") && m.Contains("PROD-NADA"));
        }

        [Fact]
        public async Task ElMismoCodigoEnDosBodegasSonDosPedidosYElDuplicadoEsPorBodega()
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);
            OtraBodega(e, "BOD-02", "Norte", a.Producto!);
            // PED-911 ya existe en Norte, no en BOD-01
            e.Db.Pedidos.Add(new Pedido { CodigoPedido = "PED-911", BodegaId = e.Db.Bodegas.Single(b => b.Codigo == "BOD-02").Id });
            e.Db.SaveChanges();

            var enDos = await ValidarAsync(e, Archivo(Fila(a, "PED-912"), Fila(a, "PED-912", bodega: "BOD-02")));
            var duplicado = await ValidarAsync(e, Archivo(Fila(a, "PED-911"), Fila(a, "PED-911", bodega: "BOD-02")));

            Assert.True(enDos.IsValid, string.Join(" / ", enDos.Errors));
            Assert.Equal(2, enDos.TotalPedidos);
            Assert.Equal(1, duplicado.FilasConPedidoDuplicado);
            Assert.Contains(duplicado.Errors, m => m.Contains("PED-911") && m.Contains("Norte"));
            Assert.Single(duplicado.PedidosValidados);
        }

        [Fact]
        public async Task ElSupervisorSoloCargaPedidosDeSuBodega()
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);
            var norte = OtraBodega(e, "BOD-02", "Norte", a.Producto!);

            var r = await ValidarAsync(e, Archivo(Fila(a, "PED-913"), Fila(a, "PED-914", bodega: "BOD-02")), bodegaDelUsuario: norte.Id);

            Assert.False(r.IsValid);
            Assert.Equal(1, r.FilasDeOtraBodega);
            Assert.Contains(r.Errors, m => m.Contains("PED-913") && m.Contains("Solo puede cargar pedidos de su bodega"));
        }

        [Fact]
        public async Task UnNombreDistintoSeCargaConElRegistradoAlConfirmarlo()
        {
            using var e = new Escenario();
            var a = e.Ubicacion(Escenario.Pequeno);
            var fila = new object[] { "PED-915", "BOD-01", a.Producto!.Codigo, "Otro nombre", 1, "Media" };

            var sinConfirmar = await ValidarAsync(e, Archivo(fila));
            var clave = sinConfirmar.Incidencias.Single().Clave;
            var confirmado = await ValidarAsync(e, Archivo(fila), null,
                new ResolucionExcel { Clave = clave, Opcion = ResolucionExcel.OpcionNombreRegistrado });

            Assert.False(sinConfirmar.IsValid);
            Assert.True(confirmado.IsValid, string.Join(" / ", confirmado.Errors));
            Assert.Equal(a.Producto.Nombre, confirmado.PedidosValidados.Single().Detalles.Single().NombreProducto);
        }

        [Fact]
        public async Task ElFormatoAnteriorConPasilloSeExplica()
        {
            using var e = new Escenario();

            var r = await ValidarAsync(e, Libro(new[] { "CodigoPedido", "Bodega", "Pasillo", "Estante", "Nivel", "CodigoProducto" }, Array.Empty<object[]>()));

            Assert.False(r.IsValid);
            Assert.Contains("formato anterior", r.Errors.Single());
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
