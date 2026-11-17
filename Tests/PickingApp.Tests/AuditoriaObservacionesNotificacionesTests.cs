using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Tests
{
    public class AuditoriaObservacionesNotificacionesTests
    {
        private static AuditoriaController Auditoria(Escenario e, out Ctl.SinAuditoria auditoria)
        {
            auditoria = new Ctl.SinAuditoria();
            var pdf = new ReportService(new ReportesPdfTests.Entorno(), NullLogger<ReportService>.Instance);
            return Ctl.Con(new AuditoriaController(e.Db, auditoria, pdf, new DatosDemostracion(e.Db, e.Motor())), Ctl.Usuario(e));
        }

        private static void Log(Escenario e, string modulo, string detalle, DateTime? fecha = null, string usuario = "ana@prueba.com")
        {
            e.Db.LogsSistema.Add(new LogSistema
            {
                Usuario = usuario, Rol = "Administrador", Accion = "Accion", Modulo = modulo, Detalle = detalle,
                FechaHora = fecha ?? new DateTime(2026, 10, 5, 10, 0, 0)
            });
            e.Db.SaveChanges();
        }

        private static string? Error(Controller c) => c.TempData["ErrorMessage"] as string;
        private static string? Exito(Controller c) => c.TempData["SuccessMessage"] as string;

        // ---------- LOG ----------

        [Fact]
        public async Task ElLogSeFiltraYMuestraElNombreEnVezDelCorreo()
        {
            using var e = new Escenario();
            var admin = Ctl.Usuario(e);
            Log(e, "Pedidos", $"Lo hizo {admin.Correo}");
            Log(e, "Bodegas", "otro", new DateTime(2026, 11, 1));
            var c = Auditoria(e, out _);

            var todo = (AuditoriaIndexViewModel)((ViewResult)await c.Index(null, null, null, null, null)).Model!;
            Assert.Equal(2, todo.Registros.Count);

            var filtrado = (AuditoriaIndexViewModel)((ViewResult)await c.Index("Pedidos", "ana", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), 1)).Model!;
            var registro = Assert.Single(filtrado.Registros);
            Assert.Contains("Usuario de prueba (Administrador)", registro.Detalle);
            Assert.Equal("2026-10-01", filtrado.FechaDesdeFiltro);
        }

        [Fact]
        public async Task ExportarPdfExigeRangoValidoYGeneraElArchivo()
        {
            using var e = new Escenario();
            Log(e, "Pedidos", "algo");
            var c = Auditoria(e, out var auditoria);

            Assert.IsType<RedirectToActionResult>(await c.ExportarPdf(null, null, null, null));
            Assert.Contains("fecha desde", Error(c));

            await c.ExportarPdf(null, null, new DateTime(2026, 11, 2), new DateTime(2026, 11, 1));
            Assert.Contains("no puede ser posterior", Error(c));

            var archivo = Assert.IsType<FileContentResult>(await c.ExportarPdf(null, null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
            Assert.Equal("application/pdf", archivo.ContentType);
            Assert.Contains("Exportar LOG a PDF", auditoria.Acciones);
        }

        // ---------- Configuración ----------

        [Fact]
        public async Task ConfiguracionYGuardadoDeParametros()
        {
            using var e = new Escenario();
            e.Ubicacion(Escenario.Pequeno);
            var c = Auditoria(e, out var auditoria);

            var vista = Assert.IsType<ViewResult>(await c.Configuracion());
            Assert.IsType<ConfiguracionViewModel>(vista.Model);

            var (clave, parametro) = ParametrosSistema.Numericos.First();

            // Valor fuera de rango: no guarda
            await c.GuardarConfiguracion(new Dictionary<string, string> { [clave] = (parametro.Maximo + 1000).ToString() });
            Assert.Contains("No se guardaron", Error(c));

            // Valor válido y LOG desactivado
            await c.GuardarConfiguracion(new Dictionary<string, string>
            {
                [clave] = parametro.Minimo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [ParametrosSistema.LogHabilitado] = "false"
            });
            Assert.Contains("guardados", Exito(c));
            Assert.Contains(e.Db.ConfiguracionesSistema, x => x.Clave == ParametrosSistema.LogHabilitado && x.Valor == "false");
            Assert.Contains("Actualizar Configuración", auditoria.Acciones);

            // Segunda vez: actualiza las filas ya creadas
            await c.GuardarConfiguracion(new Dictionary<string, string> { [ParametrosSistema.LogHabilitado] = "true" });
            Assert.Single(e.Db.ConfiguracionesSistema, x => x.Clave == ParametrosSistema.LogHabilitado);
        }

        [Fact]
        public async Task LaDemostracionSoloSeCargaSiEstaDisponible()
        {
            using var e = new Escenario();
            var c = Auditoria(e, out _);

            var redireccion = Assert.IsType<RedirectToActionResult>(await c.CargarDatosDemostracion());

            Assert.Equal("Configuracion", redireccion.ActionName);
            Assert.NotNull(Error(c) ?? Exito(c));
        }

        [Fact]
        public async Task LaLimpiezaExigeLaFraseExacta()
        {
            using var e = new Escenario();
            var c = Auditoria(e, out _);

            Assert.IsType<ViewResult>(c.LimpiarBaseDatos());
            var vista = Assert.IsType<ViewResult>(await c.ConfirmarLimpiezaBaseDatos("limpiar"));
            Assert.Equal("LimpiarBaseDatos", vista.ViewName);
            Assert.Contains("LIMPIAR_SISTEMA", Error(c));
        }

        [Fact]
        public async Task SinPedidosNoHayHistorialQueBorrar()
        {
            using var e = new Escenario();
            var c = Auditoria(e, out _);

            await c.EliminarHistorialPedidos();

            Assert.Contains("No hay pedidos", Error(c));
        }

        // ---------- Observaciones ----------

        private static ObservacionesController Observaciones(Escenario e, out Ctl.SinAuditoria auditoria)
        {
            auditoria = new Ctl.SinAuditoria();
            return Ctl.Con(new ObservacionesController(e.Db, auditoria), Ctl.Usuario(e));
        }

        [Fact]
        public async Task CrearObservacionValidaCadaCampo()
        {
            using var e = new Escenario();
            var c = Observaciones(e, out var auditoria);
            var tipo = TiposSolicitud.Catalogo.First().Id;

            await c.Crear(9999, DecisionesSolicitud.Aprobar, "Texto válido");
            Assert.Contains("tipo de solicitud", Error(c));

            await c.Crear(tipo, "Quizás", "Texto válido");
            Assert.Contains("aprobar o para rechazar", Error(c));

            await c.Crear(tipo, DecisionesSolicitud.Aprobar, "ab");
            Assert.Contains("entre 3 y 150", Error(c));

            await c.Crear(tipo, DecisionesSolicitud.Aprobar, "  Documentos completos ");
            Assert.Contains("agregada", Exito(c));
            Assert.Equal("Documentos completos", e.Db.ObservacionesSolicitud.Single(o => o.Texto.StartsWith("Documentos")).Texto);

            await c.Crear(tipo, DecisionesSolicitud.Aprobar, "Documentos completos");
            Assert.Contains("ya existe", Error(c));
            Assert.Single(auditoria.Acciones);
        }

        [Fact]
        public async Task LasObservacionesSeActivanYDesactivanYSeListanPorTipo()
        {
            using var e = new Escenario();
            var c = Observaciones(e, out var auditoria);
            var tipo = TiposSolicitud.Catalogo.First().Id;
            await c.Crear(tipo, DecisionesSolicitud.Rechazar, "Falta soporte");
            var observacion = e.Db.ObservacionesSolicitud.Single(o => o.Texto == "Falta soporte");

            await c.CambiarEstado(observacion.Id, tipo);
            Assert.Contains("desactivada", Exito(c));
            await c.CambiarEstado(observacion.Id, tipo);
            Assert.Contains("activada", Exito(c));
            Assert.IsType<NotFoundResult>(await c.CambiarEstado(9999, null));
            Assert.Equal(3, auditoria.Acciones.Count);

            var vista = (ObservacionesIndexViewModel)((ViewResult)await c.Index(tipo)).Model!;
            Assert.Contains(vista.Observaciones, o => o.Id == observacion.Id);
            Assert.Equal(tipo, vista.TipoFiltro);
            Assert.IsType<ViewResult>(await c.Index(null));
        }

        // ---------- Notificaciones ----------

        [Fact]
        public async Task CadaUsuarioSoloVeYMarcaSusNotificaciones()
        {
            using var e = new Escenario();
            var yo = Ctl.Usuario(e);
            var otro = Ctl.Usuario(e);
            e.Db.Notificaciones.AddRange(
                new Notificacion { UsuarioDestinoId = yo.Id, Titulo = "Mía", Mensaje = "m" },
                new Notificacion { UsuarioDestinoId = yo.Id, Titulo = "Leída", Mensaje = "m", Leida = true },
                new Notificacion { UsuarioDestinoId = otro.Id, Titulo = "Ajena", Mensaje = "m" });
            e.Db.SaveChanges();
            var propia = e.Db.Notificaciones.Single(n => n.Titulo == "Mía");
            var ajena = e.Db.Notificaciones.Single(n => n.Titulo == "Ajena");
            var c = Ctl.Con(new NotificacionesController(e.Db), yo);

            var lista = Assert.IsAssignableFrom<List<Notificacion>>(((ViewResult)await c.Index()).Model);
            Assert.Equal("Mía", Assert.Single(lista).Titulo);

            Assert.IsType<NotFoundResult>(await c.MarcarLeida(ajena.Id, "fetch"));
            Assert.False(e.Db.Notificaciones.Single(n => n.Id == ajena.Id).Leida);

            Assert.IsType<NoContentResult>(await c.MarcarLeida(propia.Id, "fetch"));
            Assert.True(e.Db.Notificaciones.Single(n => n.Id == propia.Id).Leida);

            Assert.IsType<RedirectToActionResult>(await c.MarcarLeida(propia.Id));
        }
    }
}
