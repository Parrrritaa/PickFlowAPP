using Microsoft.AspNetCore.Mvc;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;

namespace PickingApp.Tests
{
    public class ProductosControllerTests
    {
        private static ProductosController Como(Escenario e, out Ctl.SinAuditoria auditoria)
        {
            auditoria = new Ctl.SinAuditoria();
            return Ctl.Con(new ProductosController(e.Db, auditoria), Ctl.Usuario(e));
        }

        private static string? Error(Controller c) => c.TempData["ErrorMessage"] as string;
        private static string? Exito(Controller c) => c.TempData["SuccessMessage"] as string;

        [Fact]
        public async Task IndexMuestraCategoriasYProductos()
        {
            using var e = new Escenario();
            e.Ubicacion(Escenario.Pequeno);

            var vista = Assert.IsType<ViewResult>(await Como(e, out _).Index());

            var modelo = Assert.IsType<ProductosIndexViewModel>(vista.Model);
            Assert.NotEmpty(modelo.Categorias);
            Assert.Single(modelo.Productos);
        }

        [Theory]
        [InlineData("", "2")]
        [InlineData("Nueva", "0")]
        [InlineData("Nueva", "abc")]
        [InlineData("Nueva", "601")]
        public async Task GuardarCategoriaRechazaDatosInvalidos(string nombre, string minutos)
        {
            using var e = new Escenario();
            var c = Como(e, out _);
            var antes = e.Db.CategoriasProducto.Count();

            await c.GuardarCategoria(null, nombre, minutos, null);

            Assert.NotNull(Error(c));
            Assert.Equal(antes, e.Db.CategoriasProducto.Count());
        }

        [Fact]
        public async Task GuardarCategoriaRechazaDescripcionLargaYNombreRepetido()
        {
            using var e = new Escenario();
            var c = Como(e, out _);

            await c.GuardarCategoria(null, "Nueva", "2", new string('x', 151));
            Assert.Contains("descripción", Error(c));

            var existente = e.Db.CategoriasProducto.First().Nombre;
            await c.GuardarCategoria(null, existente, "2", null);
            Assert.Contains("Ya existe", Error(c));
        }

        [Fact]
        public async Task GuardarCategoriaCreaYEdita()
        {
            using var e = new Escenario();
            var c = Como(e, out var auditoria);

            await c.GuardarCategoria(null, "  Frágil ", "3", " delicado ");
            var creada = e.Db.CategoriasProducto.Single(x => x.Nombre == "Frágil");
            Assert.Equal(3, creada.MinutosPorUnidad);
            Assert.Equal("delicado", creada.Descripcion);

            await c.GuardarCategoria(creada.Id, "Frágil", "5", null);
            Assert.Equal(5, e.Db.CategoriasProducto.Single(x => x.Id == creada.Id).MinutosPorUnidad);
            Assert.Equal(new[] { "Crear Categoría", "Editar Categoría" }, auditoria.Acciones);

            await c.GuardarCategoria(9999, "Otra", "5", null);
            Assert.Contains("ya no existe", Error(c));
        }

        [Fact]
        public async Task HacerPredeterminadaDejaSoloUna()
        {
            using var e = new Escenario();
            var c = Como(e, out _);

            await c.HacerPredeterminada(Escenario.Grande);

            Assert.Equal(new[] { Escenario.Grande }, e.Db.CategoriasProducto.Where(x => x.EsPredeterminada).Select(x => x.Id).ToArray());
            await c.HacerPredeterminada(9999);
            Assert.Contains("ya no existe", Error(c));
        }

        [Fact]
        public async Task EliminarCategoriaNoPermiteLaPredeterminadaYDejaSusProductosSinCategoria()
        {
            using var e = new Escenario();
            var c = Como(e, out _);
            var predeterminada = e.Db.CategoriasProducto.Single(x => x.EsPredeterminada).Id;

            await c.EliminarCategoria(predeterminada);
            Assert.Contains("por defecto", Error(c));

            await c.EliminarCategoria(9999);
            Assert.Contains("ya no existe", Error(c));

            var ubicacion = e.Ubicacion(Escenario.Grande);
            await c.EliminarCategoria(Escenario.Grande);

            Assert.Null(e.Db.Productos.Single(p => p.Id == ubicacion.ProductoId).CategoriaProductoId);
            Assert.Contains("1 producto", Exito(c));
            Assert.DoesNotContain(e.Db.CategoriasProducto, x => x.Id == Escenario.Grande);

            await c.EliminarCategoria(Escenario.Pequeno);
            Assert.DoesNotContain("producto(s)", Exito(c));
        }

        [Fact]
        public async Task GuardarClasificacionSoloCuentaLosCambiosReales()
        {
            using var e = new Escenario();
            var c = Como(e, out _);
            var uno = e.Ubicacion(Escenario.Pequeno).Producto!;
            var dos = e.Ubicacion(null).Producto!;

            await c.GuardarClasificacion(new Dictionary<int, string?>
            {
                [uno.Id] = Escenario.Pequeno.ToString(), // sin cambio
                [dos.Id] = Escenario.Grande.ToString()
            });
            Assert.Equal("Se actualizó la categoría de 1 producto.", Exito(c));
            Assert.Equal(Escenario.Grande, e.Db.Productos.Single(p => p.Id == dos.Id).CategoriaProductoId);

            // Valor vacío o inexistente = categoría por defecto (null)
            await c.GuardarClasificacion(new Dictionary<int, string?> { [uno.Id] = "", [dos.Id] = "9999" });
            Assert.Equal("Se actualizó la categoría de 2 productos.", Exito(c));
            Assert.All(e.Db.Productos, p => Assert.Null(p.CategoriaProductoId));
        }

        [Fact]
        public async Task EditarProductoValidaYAplicaCambios()
        {
            using var e = new Escenario();
            var c = Como(e, out var auditoria);
            var p = e.Ubicacion(Escenario.Pequeno).Producto!;

            await c.EditarProducto(9999, "x", "1");
            Assert.Contains("ya no existe", Error(c));

            await c.EditarProducto(p.Id, "  ", "1");
            Assert.Contains("nombre", Error(c));

            await c.EditarProducto(p.Id, "Nombre", "0");
            Assert.Contains("caja", Error(c));

            await c.EditarProducto(p.Id, p.Nombre, p.UnidadesPorCaja.ToString());
            Assert.Contains("no tenía cambios", Exito(c));
            Assert.Empty(auditoria.Acciones);

            await c.EditarProducto(p.Id, "Nombre nuevo", "12");
            var guardado = e.Db.Productos.Single(x => x.Id == p.Id);
            Assert.Equal("Nombre nuevo", guardado.Nombre);
            Assert.Equal(12, guardado.UnidadesPorCaja);
            Assert.Single(auditoria.Acciones);
        }
    }
}
