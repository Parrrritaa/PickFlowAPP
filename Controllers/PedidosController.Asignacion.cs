using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PickingApp.Models;
using PickingApp.Models.ViewModels;
using PickingApp.Services;

namespace PickingApp.Controllers
{
    // Asignación y reasignación de pedidos a empleados (CU-26, CU-27). Parte de PedidosController: mismas rutas (/Pedidos/...) y dependencias.
    public partial class PedidosController
    {
        // CU-26: Asignar pedido a empleado (vista interactiva)
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpGet]
        public async Task<IActionResult> Asignar(int id)
        {
            var pedido = await _context.Pedidos.DelAlcance(User)
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pedido == null) return NotFound();

            if (pedido.EstaDividido)
            {
                TempData["ErrorMessage"] = "El pedido fue dividido en partes: asigne cada parte por separado.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            // CU-27: si el pedido ya está en curso con un empleado, esta vista sirve para reasignarlo
            var empleadoActualId = pedido.EstaEnCurso ? pedido.EmpleadoId : null;

            // Mismas reglas que se aplican al asignar (activo, pedido en curso, jornada, tiempo restante),
            // evaluadas para todos los empleados en un número fijo de consultas
            // Solo el personal activo de la bodega del pedido (PickingEngine rechaza también a los de otra bodega)
            var empleados = await _context.Empleados.Where(e => e.Activo && e.BodegaId == pedido.BodegaId).ToListAsync();
            var validaciones = await _pickingEngine.ValidarAsignacionesAsync(id, empleados.Select(e => e.Id).ToList());

            bool Asignable(Empleado e) => e.Id != empleadoActualId && validaciones[e.Id].PuedeAsignar;

            // Primero los que pueden recibir el pedido, por menor tiempo estimado; el recomendado es el primero de ellos.
            // MesesExperiencia es [NotMapped], por eso el orden se aplica en memoria.
            empleados = empleados
                .OrderBy(e => !Asignable(e))
                .ThenBy(e => Asignable(e) ? validaciones[e.Id].TiempoEstimadoMinutos : 0)
                .ThenByDescending(e => e.MesesExperiencia)
                .ToList();

            return View(new AsignarPedidoViewModel
            {
                Pedido = pedido,
                Empleados = empleados,
                Validaciones = validaciones,
                TiempoEstandar = await _pickingEngine.CalcularTiempoEstandarAsync(id),
                EmpleadoRecomendadoId = empleados.FirstOrDefault(Asignable)?.Id,
                EmpleadoActualId = empleadoActualId,
                EmpleadoActualNombre = empleados.FirstOrDefault(e => e.Id == empleadoActualId)?.NombreCompleto
                    ?? (empleadoActualId.HasValue ? (await _context.Empleados.DelAlcance(User).FirstOrDefaultAsync(x => x.Id == empleadoActualId.Value))?.NombreCompleto : null)
            });
        }

        // CU-26: Asignar pedido a empleado
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Asignar(int pedidoId, int empleadoId)
        {
            await using var transaccion = await BloquearAsignacionAsync(pedidoId, empleadoId);

            var pedido = await _context.Pedidos.DelAlcance(User).FirstOrDefaultAsync(x => x.Id == pedidoId);
            if (pedido == null) return NotFound();

            // Un pedido en curso solo cambia de empleado por CU-27 (motivo obligatorio y liberación del anterior)
            if (pedido.EstaEnCurso)
            {
                TempData["ErrorMessage"] = "El pedido ya tiene un empleado asignado. Use la opción Reasignar e indique el motivo.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            // Se vuelven a validar las reglas en el servidor (activo, un pedido a la vez, jornada, tiempo restante):
            // lo que muestra la vista pudo cambiar desde que se abrió la página
            var validacion = await _pickingEngine.ValidarAsignacionAsync(pedidoId, empleadoId);

            if (!validacion.PuedeAsignar)
            {
                TempData["ErrorMessage"] = validacion.MensajeError;
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            var empleado = await _context.Empleados.DelAlcance(User).FirstOrDefaultAsync(x => x.Id == empleadoId);
            if (empleado == null) return NotFound();

            // Si genera advertencia de experiencia (umbrales de Configuración)
            if (validacion.GeneraAdvertencia)
            {
                await NotificarAdvertenciaAsignacionAsync(validacion, pedido, empleado, esReasignacion: false);
            }

            // Cambia estado, empleado, fechas y tiempos del pedido (Models/Pedido.cs) y luego los guarda con un UPDATE
            pedido.Asignar(empleadoId, DateTime.Now.AddMinutes(await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MargenInicioMinutos)),
                validacion.TiempoEstimadoMinutos, validacion.TiempoEstandarMinutos);
            pedido.AsignadoPorId = User.IdUsuario();

            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();

            if (validacion.GeneraAdvertencia)
            {
                await RegistrarAdvertenciaAsignacionAsync(validacion, pedido, esReasignacion: false);
            }
            await _auditService.RegistrarAsync(AccionesLog.AsignarPedido, "Pedidos", 
                $"Pedido {pedido.CodigoPedido} asignado al empleado {empleado.NombreCompleto}. Estimado: {Formato.Duracion(validacion.TiempoEstimadoMinutos)} (estándar {Formato.Duracion(validacion.TiempoEstandarMinutos)} × rendimiento {validacion.Rendimiento?.Factor ?? 1:0.00})");

            if (validacion.GeneraAdvertencia)
            {
                TempData["WarningMessage"] = validacion.MensajeAdvertencia;
            }
            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' asignado exitosamente a '{empleado.NombreCompleto}'.";
            TempData["ImprimirPedido"] = true; // Detalle abre la orden impresa para el empleado

            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // CU-27: Reasignar pedido a otro empleado
        [Authorize(Roles = Roles.AdministradorOSupervisor)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reasignar(int pedidoId, int nuevoEmpleadoId, string motivoReasignacion)
        {
            var motivo = motivoReasignacion?.Trim() ?? "";
            if (motivo.Length == 0)
            {
                TempData["ErrorMessage"] = "El motivo de reasignación es estrictamente obligatorio.";
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }
            if (motivo.Length > MaxLongitudMotivoReasignacion)
            {
                TempData["ErrorMessage"] = $"El motivo de reasignación no puede superar {MaxLongitudMotivoReasignacion} caracteres.";
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }

            await using var transaccion = await BloquearAsignacionAsync(pedidoId, nuevoEmpleadoId);

            var pedido = await _context.Pedidos.DelAlcance(User).FirstOrDefaultAsync(x => x.Id == pedidoId);
            if (pedido == null) return NotFound();

            if (!pedido.EstaEnCurso || !pedido.EmpleadoId.HasValue)
            {
                TempData["ErrorMessage"] = "Solo se puede reasignar un pedido que esté asignado a un empleado.";
                return RedirectToAction(nameof(Detalle), new { id = pedidoId });
            }

            if (pedido.EmpleadoId == nuevoEmpleadoId)
            {
                TempData["ErrorMessage"] = "El pedido ya está asignado a ese empleado. Seleccione un empleado distinto.";
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }

            var validacion = await _pickingEngine.ValidarAsignacionAsync(pedidoId, nuevoEmpleadoId);
            if (!validacion.PuedeAsignar)
            {
                TempData["ErrorMessage"] = validacion.MensajeError;
                return RedirectToAction(nameof(Asignar), new { id = pedidoId });
            }

            var nuevoEmpleado = await _context.Empleados.DelAlcance(User).FirstOrDefaultAsync(x => x.Id == nuevoEmpleadoId);
            if (nuevoEmpleado == null) return NotFound();

            var nombreEmpleadoAnterior = await _context.Empleados
                .Where(e => e.Id == pedido.EmpleadoId.Value)
                .Select(e => e.NombreCompleto)
                .FirstOrDefaultAsync();

            if (validacion.GeneraAdvertencia)
            {
                await NotificarAdvertenciaAsignacionAsync(validacion, pedido, nuevoEmpleado, esReasignacion: true);
            }

            pedido.Reasignar(nuevoEmpleadoId, motivo, DateTime.Now.AddMinutes(await ParametrosSistema.ObtenerAsync(_context, ParametrosSistema.MargenInicioMinutos)),
                validacion.TiempoEstimadoMinutos, validacion.TiempoEstandarMinutos);
            pedido.AsignadoPorId = User.IdUsuario();

            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();

            if (validacion.GeneraAdvertencia)
            {
                await RegistrarAdvertenciaAsignacionAsync(validacion, pedido, esReasignacion: true);
            }
            await _auditService.RegistrarAsync(AccionesLog.ReasignarPedido, "Pedidos", 
                $"Pedido {pedido.CodigoPedido} reasignado de {nombreEmpleadoAnterior ?? "(sin empleado)"} a {nuevoEmpleado.NombreCompleto}. Estimado: {Formato.Duracion(validacion.TiempoEstimadoMinutos)}. Motivo: {motivo}");

            if (validacion.GeneraAdvertencia)
            {
                TempData["WarningMessage"] = validacion.MensajeAdvertencia;
            }
            TempData["SuccessMessage"] = $"Pedido '{pedido.CodigoPedido}' reasignado exitosamente a '{nuevoEmpleado.NombreCompleto}'.";
            TempData["ImprimirPedido"] = true; // Detalle abre la orden impresa para el empleado
            return RedirectToAction(nameof(Detalle), new { id = pedidoId });
        }

        // RN-01-CU26: bloquea el pedido y el empleado (SELECT ... FOR UPDATE) hasta el commit. Una asignación simultánea
        // sobre cualquiera de los dos espera y luego valida con lo ya guardado: ni el pedido queda con dos empleados
        // ni el empleado con dos pedidos en curso. Siempre en el mismo orden (pedido, empleado) para no cruzar bloqueos.
        private async Task<IDbContextTransaction> BloquearAsignacionAsync(int pedidoId, int empleadoId)
        {
            var transaccion = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Pedidos WHERE Id = {pedidoId} FOR UPDATE");
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Empleados WHERE Id = {empleadoId} FOR UPDATE");
            return transaccion;
        }

        private const int MaxLongitudMotivoReasignacion = 250; // varchar(250) en Pedidos.MotivoReasignacion

        // CU-26 / CU-27: advertencia de experiencia (umbrales configurables); no bloquea la asignación.
        // La notificación se agrega antes del SaveChanges para guardarse junto con la asignación;
        // el LOG se escribe después, solo si la asignación quedó guardada.
        private Task NotificarAdvertenciaAsignacionAsync(AsignacionValidationResult validacion, Pedido pedido, Empleado empleado, bool esReasignacion) =>
            Avisos.AlRolAsync(_context, Roles.IdAdministrador,
                "Alerta de Asignación de Picking",
                $"El {User.DescripcionParaNotificacion()} {(esReasignacion ? "reasignó" : "asignó")} el pedido {pedido.CodigoPedido} al empleado {empleado.NombreCompleto} ({validacion.MotivoAdvertencia}).",
                TiposNotificacion.Warning);

        private Task RegistrarAdvertenciaAsignacionAsync(AsignacionValidationResult validacion, Pedido pedido, bool esReasignacion) =>
            _auditService.RegistrarAsync("Advertencia de Asignación", "Pedidos",
                $"{validacion.MensajeAdvertencia} - {(esReasignacion ? "Reasignado" : "Asignado")} por {User.Identity?.Name} ({User.FindFirstValue(ClaimTypes.Role)}) al pedido {pedido.CodigoPedido}");
    }
}
