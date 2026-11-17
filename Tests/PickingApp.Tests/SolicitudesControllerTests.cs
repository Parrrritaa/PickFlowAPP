using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PickingApp.Controllers;
using PickingApp.Models;
using PickingApp.Models.ViewModels;

namespace PickingApp.Tests
{
    public class SolicitudesControllerTests
    {
        private static SolicitudesController Como(Escenario e, int rol, out Usuario usuario, out Ctl.SinAuditoria auditoria)
        {
            auditoria = new Ctl.SinAuditoria();
            usuario = Ctl.Usuario(e, rol, rol == Roles.IdAdministrador ? null : e.Db.Bodegas.First());
            return Ctl.Con(new SolicitudesController(e.Db, auditoria, NullLogger<SolicitudesController>.Instance), usuario);
        }

        private static Solicitud Crear(Escenario e, int tipo, object datos, Usuario? solicitante = null, string estado = EstadosSolicitud.Pendiente)
        {
            solicitante ??= Ctl.Usuario(e, Roles.IdSupervisor, e.Db.Bodegas.First());
            var solicitud = Solicitud.Nueva(tipo, solicitante.Id, datos);
            solicitud.BodegaId = e.Db.Bodegas.First().Id;
            solicitud.Estado = estado;
            e.Db.Solicitudes.Add(solicitud);
            e.Db.SaveChanges();
            return solicitud;
        }

        private static object DatosRegistro(string identificacion = "555111", int? bodegaId = null) => new DatosEmpleadoSolicitud
        {
            Identificacion = identificacion, NombreCompleto = "Empleado nuevo",
            HorarioEntrada = new TimeSpan(8, 0, 0), HorarioSalida = new TimeSpan(16, 0, 0),
            FechaIngreso = new DateTime(2026, 1, 15), BodegaId = bodegaId
        };

        private static string? Error(Controller c) => c.TempData["ErrorMessage"] as string;
        private static string? Exito(Controller c) => c.TempData["SuccessMessage"] as string;
        private static string Destino(IActionResult r) => ((RedirectToActionResult)r).ActionName!;

        // ---------- Listas ----------

        [Fact]
        public async Task LaBandejaYMisSolicitudesFiltran()
        {
            using var e = new Escenario();
            var admin = Como(e, Roles.IdAdministrador, out var adminUsuario, out _);
            var propia = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro(), adminUsuario);
            Crear(e, TiposSolicitud.Traslado, new SolicitudTraslado(), null, EstadosSolicitud.Aprobada);

            var todas = (SolicitudesListaViewModel)((ViewResult)await admin.Index(new FiltrosSolicitudes())).Model!;
            Assert.Equal(2, todas.Solicitudes.Count);

            var filtradas = (SolicitudesListaViewModel)((ViewResult)await admin.Index(new FiltrosSolicitudes
            {
                Busqueda = $"SOL-{propia.Id}", Tipo = TiposSolicitud.RegistroEmpleado, SolicitanteId = adminUsuario.Id,
                FechaDesde = DateTime.Today.AddDays(-1), FechaHasta = DateTime.Today.AddDays(1), Estado = EstadosSolicitud.Pendiente
            })).Model!;
            Assert.Single(filtradas.Solicitudes);

            var mias = (SolicitudesListaViewModel)((ViewResult)await admin.MisSolicitudes(new FiltrosSolicitudes())).Model!;
            Assert.Single(mias.Solicitudes);

            var supervisor = Como(e, Roles.IdSupervisor, out _, out _);
            var vistaSupervisor = (SolicitudesListaViewModel)((ViewResult)await supervisor.Index(new FiltrosSolicitudes())).Model!;
            Assert.True(vistaSupervisor.MostrarPestanas);
            Assert.All(vistaSupervisor.Solicitudes, s => Assert.Equal(TiposSolicitud.RegistroEmpleado, s.TipoSolicitudId));
        }

        // ---------- Revisar ----------

        [Fact]
        public async Task RevisarMuestraCadaTipoDeSolicitud()
        {
            using var e = new Escenario();
            var ubicacion = e.Ubicacion(Escenario.Pequeno);
            var empleado = e.Empleado();
            var c = Como(e, Roles.IdAdministrador, out _, out _);
            var bodega = e.Db.Bodegas.First();

            var solicitudes = new[]
            {
                Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro()),
                Crear(e, TiposSolicitud.EdicionEmpleado, new SolicitudEdicionEmpleado
                {
                    Motivo = "Corrección", NuevosDatos = new DatosEmpleadoSolicitud { Id = empleado.Id, NombreCompleto = "Otro" },
                    Cambios = { new CambioSolicitud { Campo = CamposEmpleado.Nombre, Anterior = "Antes", Nuevo = "Otro" } }
                }),
                Crear(e, TiposSolicitud.EdicionEmpleado, new SolicitudEdicionEmpleado { NuevosDatos = new DatosEmpleadoSolicitud { Id = 9999 } }),
                Crear(e, TiposSolicitud.InactivacionEmpleado, new SolicitudInactivacionEmpleado { EmpleadoId = empleado.Id, Motivo = "Renuncia" }),
                Crear(e, TiposSolicitud.InactivacionEmpleado, new SolicitudInactivacionEmpleado { EmpleadoId = 9999, NombreCompleto = "Ya no está" }),
                Crear(e, TiposSolicitud.EdicionUbicacion, new SolicitudEdicionUbicacion
                {
                    UbicacionId = ubicacion.Id, Motivo = "Mover",
                    Cambios = { new CambioSolicitud { Campo = CamposUbicacion.Estado, Anterior = "Inactiva", Nuevo = "Activa" } }
                }),
                Crear(e, TiposSolicitud.EdicionUbicacion, new SolicitudEdicionUbicacion { UbicacionId = 9999, CodigoUbicacion = "X" }),
                Crear(e, TiposSolicitud.EliminacionUbicacion, new SolicitudEliminacionUbicacion { UbicacionId = ubicacion.Id, Motivo = "Sobra" }),
                Crear(e, TiposSolicitud.EliminacionUbicacion, new SolicitudEliminacionUbicacion { UbicacionId = 9999 }),
                Crear(e, TiposSolicitud.Traslado, new SolicitudTraslado { EsEmpleado = true, PersonaId = empleado.Id, NombreCompleto = "N", BodegaOrigenId = bodega.Id, BodegaDestinoId = 999, Motivo = "m" }),
                Crear(e, TiposSolicitud.Traslado, new SolicitudTraslado { EsEmpleado = false, NombreCompleto = "Aux" }, null, EstadosSolicitud.Aprobada),
            };

            foreach (var s in solicitudes)
            {
                var resultado = await c.Revisar(s.Id);
                Assert.True(resultado is ViewResult, $"Tipo {s.TipoSolicitudId} ({s.Estado}) devolvió {resultado.GetType().Name}");
            }
            Assert.IsType<NotFoundResult>(await c.Revisar(9999));
        }

        [Fact]
        public async Task RevisarEnCadenaRecorreLasPendientes()
        {
            using var e = new Escenario();
            var primera = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro("1"));
            var segunda = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro("2"));
            var respondida = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro("3"), null, EstadosSolicitud.Aprobada);
            var c = Como(e, Roles.IdAdministrador, out _, out _);

            var inicio = Assert.IsType<RedirectToActionResult>(await c.RevisarPendientes(null));
            Assert.Equal(primera.Id, inicio.RouteValues!["id"]);

            var vista = (SolicitudDetalleViewModel)((ViewResult)await c.Revisar(primera.Id, true, null)).Model!;
            Assert.True(vista.EnCadena);
            Assert.Equal(segunda.Id, vista.SiguienteId);

            // Una ya respondida salta a la siguiente pendiente
            Assert.IsType<RedirectToActionResult>(await c.Revisar(respondida.Id, true, null));

            e.Db.Solicitudes.RemoveRange(e.Db.Solicitudes.Where(s => s.Estado == EstadosSolicitud.Pendiente));
            e.Db.SaveChanges();
            await c.RevisarPendientes(null);
            Assert.Contains("No hay solicitudes pendientes", Exito(c));
        }

        [Fact]
        public async Task ElSupervisorSoloGestionaRegistrosDeEmpleado()
        {
            using var e = new Escenario();
            var c = Como(e, Roles.IdSupervisor, out var supervisor, out _);
            var ajena = Crear(e, TiposSolicitud.Traslado, new SolicitudTraslado());
            var propia = Crear(e, TiposSolicitud.Traslado, new SolicitudTraslado { NombreCompleto = "X" }, supervisor);

            Assert.Equal("Index", Destino(await c.Revisar(ajena.Id)));
            Assert.Contains("solo tienen permiso", Error(c));
            Assert.Equal("Detalle", Destino(await c.Revisar(propia.Id)));

            Assert.IsType<ViewResult>(await c.Detalle(propia.Id));
            Assert.IsType<NotFoundResult>(await c.Detalle(ajena.Id));

            await c.Aprobar(ajena.Id, null, null);
            Assert.Contains("solo tienen permiso", Error(c));
            await c.Rechazar(ajena.Id, null, null);
            Assert.Contains("solo tienen permiso", Error(c));
        }

        [Fact]
        public async Task ElAdministradorAbreDesdeDetalleLasSolicitudesAjenas()
        {
            using var e = new Escenario();
            var c = Como(e, Roles.IdAdministrador, out _, out _);
            var ajena = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro());

            Assert.Equal("Revisar", Destino(await c.Detalle(ajena.Id)));
            Assert.IsType<NotFoundResult>(await c.Detalle(9999));
        }

        // ---------- Aprobar y rechazar ----------

        [Fact]
        public async Task AprobarRegistraAlEmpleadoYAvisaAlSolicitante()
        {
            using var e = new Escenario();
            var c = Como(e, Roles.IdAdministrador, out _, out var auditoria);
            var solicitud = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro("777", e.Db.Bodegas.First().Id));

            var resultado = await c.Aprobar(solicitud.Id, null, null);

            Assert.Equal("Index", Destino(resultado));
            Assert.Equal(EstadosSolicitud.Aprobada, e.Db.Solicitudes.Single(s => s.Id == solicitud.Id).Estado);
            Assert.Contains(e.Db.Empleados, x => x.Identificacion == "777");
            Assert.Contains(e.Db.Notificaciones, n => n.UsuarioDestinoId == solicitud.UsuarioSolicitanteId);
            Assert.Single(auditoria.Acciones);
        }

        [Fact]
        public async Task AprobarConUnImpedimentoDejaLaSolicitudPendiente()
        {
            using var e = new Escenario();
            var existente = e.Empleado();
            var c = Como(e, Roles.IdAdministrador, out _, out _);
            // La identificación ya está registrada: la regla de negocio lo impide
            var solicitud = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro(existente.Identificacion, e.Db.Bodegas.First().Id));

            var resultado = await c.Aprobar(solicitud.Id, null, null, true, null);

            Assert.Equal("Revisar", Destino(resultado));
            Assert.NotNull(Error(c));
            Assert.Equal(EstadosSolicitud.Pendiente, e.Db.Solicitudes.Single(s => s.Id == solicitud.Id).Estado);
        }

        [Fact]
        public async Task LaObservacionSeValidaAlAprobarYAlRechazar()
        {
            using var e = new Escenario();
            var c = Como(e, Roles.IdAdministrador, out _, out _);
            var solicitud = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro());

            await c.Aprobar(9999, null, null);
            Assert.Contains("no se encuentra disponible", Error(c));
            await c.Rechazar(9999, null, null);
            Assert.Contains("no se encuentra disponible", Error(c));

            await c.Rechazar(solicitud.Id, null, null);
            Assert.Contains("elija una observación", Error(c));
            await c.Rechazar(solicitud.Id, ObservacionSolicitud.ValorOtro, new string('a', 401));
            Assert.Contains("hasta 400", Error(c));
            await c.Rechazar(solicitud.Id, ObservacionSolicitud.ValorOtro, "corto");
            Assert.NotNull(Error(c));
            await c.Rechazar(solicitud.Id, "9999", null);
            Assert.Contains("no corresponde", Error(c));
            Assert.Equal(EstadosSolicitud.Pendiente, e.Db.Solicitudes.Single().Estado);
        }

        [Fact]
        public async Task RechazarGuardaLaObservacionYPasaALaSiguienteEnCadena()
        {
            using var e = new Escenario();
            var c = Como(e, Roles.IdAdministrador, out _, out var auditoria);
            var primera = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro("1"));
            var segunda = Crear(e, TiposSolicitud.RegistroEmpleado, DatosRegistro("2"));
            var catalogo = ObservacionesIniciales.Catalogo.First(o => o.Decision == DecisionesSolicitud.Rechazar && o.TipoSolicitudId == TiposSolicitud.RegistroEmpleado);

            var siguiente = Assert.IsType<RedirectToActionResult>(await c.Rechazar(primera.Id, catalogo.Id.ToString(), null, true, null));

            Assert.Equal("Revisar", siguiente.ActionName);
            Assert.Equal(segunda.Id, siguiente.RouteValues!["id"]);
            var guardada = e.Db.Solicitudes.Single(s => s.Id == primera.Id);
            Assert.Equal(EstadosSolicitud.Rechazada, guardada.Estado);
            Assert.Equal(catalogo.Id, guardada.ObservacionId);

            // Última de la cadena: vuelve a la bandeja con el aviso
            var fin = await c.Rechazar(segunda.Id, ObservacionSolicitud.ValorOtro, "Faltan los documentos del empleado", true, null);
            Assert.Equal("Index", Destino(fin));
            Assert.Contains("No quedan solicitudes pendientes", Exito(c));
            Assert.Equal(2, auditoria.Acciones.Count);
        }

        // ---------- Traslados solicitados ----------

        [Fact]
        public async Task SolicitarTrasladoValidaAntesDeCrear()
        {
            using var e = new Escenario();
            var empleado = e.Empleado();
            var destino = new Bodega { Codigo = "BOD-02", Nombre = "Destino" };
            var inactiva = new Bodega { Codigo = "BOD-03", Nombre = "Cerrada", Activa = false };
            e.Db.Bodegas.AddRange(destino, inactiva);
            e.Db.SaveChanges();
            var c = Como(e, Roles.IdSupervisor, out _, out _);

            Assert.IsType<NotFoundResult>(await c.SolicitarTraslado(true, 9999, destino.Id, "m"));
            Assert.IsType<NotFoundResult>(await c.SolicitarTraslado(false, 9999, destino.Id, "m"));

            await c.SolicitarTraslado(true, empleado.Id, inactiva.Id, "m");
            Assert.Contains("bodega activa", Error(c));
            await c.SolicitarTraslado(true, empleado.Id, destino.Id, " ");
            Assert.Contains("motivo", Error(c));

            await c.SolicitarTraslado(true, empleado.Id, destino.Id, "Más demanda");
            Assert.Single(e.Db.Solicitudes);
            Assert.Contains("Requiere aprobación", Exito(c));

            await c.SolicitarTraslado(true, empleado.Id, destino.Id, "Otra vez");
            Assert.Single(e.Db.Solicitudes);
            Assert.NotNull(Error(c));
        }

        [Fact]
        public async Task SolicitarTrasladoDeUnAuxiliarDeLaBodega()
        {
            using var e = new Escenario();
            var bodega = e.Db.Bodegas.First();
            var destino = new Bodega { Codigo = "BOD-02", Nombre = "Destino" };
            e.Db.Bodegas.Add(destino);
            var auxiliar = Ctl.Usuario(e, Roles.IdAuxiliar, bodega);
            e.Db.SaveChanges();
            var c = Como(e, Roles.IdSupervisor, out _, out _);

            await c.SolicitarTraslado(false, auxiliar.Id, destino.Id, "Refuerzo");

            Assert.Contains(e.Db.Solicitudes, s => s.TipoSolicitudId == TiposSolicitud.Traslado);
        }
    }
}
