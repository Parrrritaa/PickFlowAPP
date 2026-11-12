# PickFlow - Documentación de Base de Datos (PickingDB)
### Triggers, Procedimientos Almacenados, Vistas y Diccionario de Datos

Este documento explica de forma clara cómo está organizada la base de datos de nuestro proyecto **PickFlow**, qué hace cada trigger, para qué sirven los procedimientos y vistas, y el detalle de cada una de las 17 tablas del sistema.

---

## 1. Triggers (Disparadores automáticos)

Los triggers son sensores que vigilan la base de datos. Se disparan solos cuando pasa algo específico y hacen tareas repetitivas para que nadie tenga que hacerlas a mano.

### Trigger 1: Cronómetro automático al completar un pedido (`trg_pedidos_before_update_completar`)
* **Tabla que vigila:** `Pedidos` (se activa justo antes de guardar la actualización).
* **Para qué sirve:**
  * En el momento exacto en que un pedido cambia su estado a `'Completado'`, el trigger se activa solo.
  * Si no venía la fecha de finalización, le pone la fecha y hora exacta de ese instante (`NOW`).
  * Marca la casilla de confirmación física de entrega.
  * Calcula cuántos minutos tardó el operario en recoger los productos (comparando la fecha en que empezó contra la fecha en que terminó). Le pone mínimo 1 minuto para que nunca queden pedidos en cero.
* **Beneficio:** Garantiza que ningún pedido terminado quede con tiempos vacíos o sin calcular.

---

### Trigger 2: Alarma cuando se reporta un problema en un pedido (`trg_novedades_pedido_after_insert`)
* **Tabla que vigila:** `NovedadesPedido` (se activa justo después de que se inserta una novedad).
* **Para qué sirve:**
  * Si un operario está haciendo picking y reporta que falta un producto o que algo está dañado, el trigger busca qué supervisor asignó ese pedido.
  * Le inserta una notificación automática en su campana de avisos (`Notificaciones`) para que sepa de una vez qué pasó y pueda ir a resolverlo.
  * También guarda una fila en `LogsSistema` para que quede en el historial de auditoría quién reportó el problema y a qué hora.

---

### Trigger 3: Candado de seguridad para cuentas de usuario (`trg_usuarios_control_bloqueo`)
* **Tabla que vigila:** `Usuarios` (se activa antes de actualizar al usuario).
* **Para qué sirve:**
  * Si una persona intenta entrar y se equivoca 3 veces seguidas con su clave, el trigger cambia el estado de la cuenta a `'Bloqueado'` y anota la fecha del bloqueo.
  * Si más adelante el administrador revisa el caso y lo vuelve a poner como `'Activo'`, el trigger resetea el contador de fallos a cero y borra la fecha de bloqueo para que la cuenta quede limpia.

---

## 2. Procedimientos Almacenados (Funciones del sistema)

Son procesos que agrupan varias acciones en una sola llamada segura. Si algo sale mal a mitad de camino, cancelan todo (`ROLLBACK`) para no dejar datos a medias.

### Procedimiento 1: Asignar un pedido a un empleado (`sp_AsignarPedidoAEmpleado`)
* **Qué recibe:** El ID del pedido, el ID del empleado que lo va a recoger y el ID del supervisor que lo está asignando.
* **Qué valida antes de hacer nada:**
  1. Que el pedido exista y esté pendiente o reasignado.
  2. Que el empleado exista y esté activo en el sistema.
  3. Que el empleado trabaje en la misma bodega donde están los productos del pedido (evita asignar por error pedidos de una bodega a trabajadores de otra).
* **Qué hace:** Suma los minutos estimados según los productos del pedido y ajusta el tiempo según la experiencia del trabajador (si lleva más de 2 años rinde más rápido; si es nuevo se le da margen). Luego cambia el pedido a `'Asignado'` y guarda el log de quién lo asignó.

---

### Procedimiento 2: Terminar un pedido y cerrar novedades (`sp_CerrarPedidoConNovedades`)
* **Qué recibe:** El ID del pedido, el ID del usuario que lo cierra, una opción para resolver novedades abiertas (1 o 0) y una nota de cierre.
* **Qué hace:**
  * Revisa que el pedido esté en curso.
  * Si el pedido tenía problemas reportados (novedades abiertas) y el usuario autorizó cerrarlas, las marca como atendidas con la nota de cierre.
  * Pasa el pedido a `'Completado'` (aquí el Trigger 1 calcula automáticamente el tiempo real).
  * Si el pedido era parte de un pedido grande que se dividió (ej: Parte 1 y Parte 2), revisa si las demás partes ya terminaron. Si ya todas terminaron, completa también el pedido original.

---

### Procedimiento 3: Reporte de productividad por bodega (`sp_GenerarResumenProductividadBodega`)
* **Qué recibe:** El ID de la bodega y dos fechas (inicio y fin).
* **Qué devuelve:** Dos tablas de resultados listas para mostrar:
  1. **Resumen general de la bodega:** Total de pedidos cargados, cuántos se completaron, cuántos se cancelaron, cuántos siguen en curso, total de unidades físicas recogidas y tiempo promedio de picking.
  2. **Ranking de operarios:** Cada trabajador con su cantidad de pedidos terminados, unidades recogidas, promedio de minutos y su porcentaje de eficiencia frente al estándar.

---

## 3. Vistas (Consultas listas para usar)

Las vistas nos ahorran escribir consultas SQL largas con muchos `JOIN`. Funcionan como tablas virtuales que podemos consultar con un simple `SELECT * FROM nombre_vista`.

1. **`vw_PedidosDetalleCompleto`:** Muestra la lista de pedidos con los nombres de la bodega, el empleado asignado, el supervisor, cuántos productos tiene el pedido y si tiene problemas pendientes.
2. **`vw_RendimientoEmpleados`:** La tabla de posiciones de los empleados: cuántos pedidos llevan completados, promedio de tiempo real vs estándar y porcentaje de eficiencia.
3. **`vw_InventarioUbicaciones`:** El mapa físico del almacén: pasillo, estante, nivel, qué producto tiene guardado, su categoría y si la ubicación está ocupada o libre.
4. **`vw_NovedadesPendientes`:** La lista de alertas rojas para supervisores: solo los problemas abiertos, con el detalle y cuántos minutos llevan esperando atención.
5. **`vw_EstadoOperativoBodegas`:** El tablero en vivo de cada bodega: cuántos operarios están trabajando hoy, pedidos pendientes, pedidos en curso y pedidos terminados en el día.
6. **`vw_HistorialSolicitudes`:** Seguimiento de solicitudes administrativas (aprobadas o rechazadas), con el solicitante, el administrador que respondió y el motivo.
7. **`vw_AuditoriaReciente`:** Bitácora limpia de seguridad para ver quién hizo qué, cuándo y desde qué IP.

---

## 4. Diccionario de Datos

Aquí está la descripción completa de las **17 tablas** de nuestra base de datos (`PickingDB`).

### 1. `Bodegas`
Guarda las sedes o almacenes físicos donde opera PickFlow.
* `Id` (`INT`, PK, Auto-incremental): Identificador único de la bodega.
* `Codigo` (`VARCHAR(20)`, UNIQUE): Código corto o acrónimo (ej: `BOD-01`).
* `Nombre` (`VARCHAR(100)`): Nombre de la sede.
* `Descripcion` (`VARCHAR(250)`, Opcional): Dirección o notas sobre la bodega.
* `Activa` (`TINYINT(1)`, Por defecto `1`): Indica si la bodega está funcionando o inactiva.
* `FechaCreacion` (`DATETIME(6)`): Fecha en que se registró la bodega.

### 2. `CategoriasProducto`
Clasifica los productos según su tamaño o peso para saber cuánto tiempo toma recogerlos.
* `Id` (`INT`, PK, Auto-incremental): Identificador de la categoría.
* `Nombre` (`VARCHAR(50)`, UNIQUE): Nombre (Pequeño, Mediano, Grande, Voluminoso).
* `Descripcion` (`VARCHAR(150)`, Opcional): Ejemplos de artículos que entran aquí.
* `MinutosPorUnidad` (`DOUBLE`): Minutos base estimados para recoger una unidad.
* `EsPredeterminada` (`TINYINT(1)`, Por defecto `0`): Se usa si un producto nuevo no tiene categoría asignada.

### 3. `ConfiguracionesSistema`
Valores generales de configuración de la aplicación (clave y valor).
* `Id` (`INT`, PK, Auto-incremental): Identificador de la configuración.
* `Clave` (`VARCHAR(100)`, UNIQUE): Nombre de la variable o ajuste.
* `Valor` (`VARCHAR(255)`): Contenido del ajuste.

### 4. `DetallesPedido`
Cada producto y cantidad que forma parte de un pedido de picking.
* `Id` (`INT`, PK, Auto-incremental): Identificador de la línea.
* `PedidoId` (`INT`, FK -> `Pedidos`): Pedido al que pertenece esta línea.
* `CodigoProducto` (`VARCHAR(50)`): Código SKU del producto.
* `NombreProducto` (`VARCHAR(150)`): Nombre del artículo.
* `Cantidad` (`INT`, Por defecto `1`): Número de unidades a recoger.
* `UbicacionId` (`INT`, FK -> `Ubicaciones`): De qué pasillo/estante se debe sacar.

### 5. `Empleados`
Los operarios físicos de bodega que hacen el trabajo de recolección.
* `Id` (`INT`, PK, Auto-incremental): Identificador del empleado.
* `Identificacion` (`VARCHAR(30)`, UNIQUE): Cédula del operario (solo números).
* `NombreCompleto` (`VARCHAR(100)`): Nombre y apellido.
* `Correo` (`VARCHAR(100)`, Opcional): Correo de contacto.
* `HorarioEntrada` (`TIME(6)`): Hora a la que inicia su turno.
* `HorarioSalida` (`TIME(6)`): Hora a la que finaliza su turno.
* `FechaIngreso` (`DATETIME(6)`): Fecha en que empezó a trabajar en la empresa.
* `Activo` (`TINYINT(1)`, Por defecto `1`): Si está disponible para recibir pedidos.
* `BodegaId` (`INT`, Opcional, FK -> `Bodegas`): Bodega física donde labora.
* `FechaReinicioPromedio` (`DATETIME(6)`, Opcional): Fecha desde la cual se mide su rendimiento.

### 6. `LogsSistema`
Bitácora de auditoría que guarda cada movimiento importante hecho en la aplicación.
* `Id` (`INT`, PK, Auto-incremental): Identificador del log.
* `FechaHora` (`DATETIME(6)`): Momento exacto del evento.
* `Usuario` (`VARCHAR(100)`): Nombre o identificador del usuario.
* `UsuarioId` (`INT`, Opcional, FK -> `Usuarios`): ID de la cuenta en el sistema.
* `Rol` (`VARCHAR(50)`): Rol que tenía el usuario al momento de la acción.
* `Accion` (`VARCHAR(100)`): Qué hizo (Crear, Editar, Asignar, Login, etc.).
* `Modulo` (`VARCHAR(100)`): Sección afectada (Pedidos, Empleados, Novedades, etc.).
* `Detalle` (`LONGTEXT`): Explicación detallada de lo sucedido.
* `DireccionIp` (`VARCHAR(50)`, Opcional): IP desde donde se conectó.

### 7. `Notificaciones`
Avisos dirigidos a cada usuario (aparecen en la campana de la app).
* `Id` (`INT`, PK, Auto-incremental): Identificador del mensaje.
* `UsuarioDestinoId` (`INT`, FK -> `Usuarios`): Para qué usuario es el aviso.
* `Titulo` (`VARCHAR(150)`): Encabezado de la alerta.
* `Mensaje` (`VARCHAR(500)`): Explicación del aviso.
* `Tipo` (`ENUM`: Info, Warning, Success, Danger): Nivel de urgencia visual.
* `Leida` (`TINYINT(1)`, Por defecto `0`): 1 si el usuario ya la abrió, 0 si está pendiente.
* `FechaCreacion` (`DATETIME(6)`): Cuándo se generó.
* `Enlace` (`VARCHAR(255)`, Opcional): Ruta a la que lleva al hacer clic.

### 8. `NovedadesPedido`
Problemas o reportes que encuentra el operario durante la recolección física.
* `Id` (`INT`, PK, Auto-incremental): Identificador de la novedad.
* `PedidoId` (`INT`, FK -> `Pedidos`): En qué pedido ocurrió el problema.
* `Descripcion` (`VARCHAR(500)`): Qué pasó exactamente.
* `TipoNovedad` (`ENUM`: Faltante, Avería, Ubicación Incorrecta, Otro): Categoría del problema.
* `FechaRegistro` (`DATETIME(6)`): Fecha y hora en que se reportó.
* `RegistradoPor` (`VARCHAR(100)`): Nombre de la persona que reportó.
* `RegistradoPorId` (`INT`, Opcional, FK -> `Usuarios`): ID de la cuenta que lo registró.
* `Atendida` (`TINYINT(1)`, Por defecto `0`): 1 si el supervisor ya la resolvió, 0 si sigue abierta.
* `AtendidaPor` (`VARCHAR(100)`, Opcional): Nombre del supervisor que la atendió.
* `FechaAtencion` (`DATETIME(6)`, Opcional): Cuándo se resolvió.
* `ObservacionesAtencion` (`VARCHAR(500)`, Opcional): Qué solución se dio.

### 9. `ObservacionesSolicitud`
Catálogo de frases predeterminadas para justificar aprobaciones o rechazos.
* `Id` (`INT`, PK, Auto-incremental): Identificador de la opción.
* `TipoSolicitudId` (`INT`, FK -> `TiposSolicitud`): Para qué tipo de trámite aplica.
* `Decision` (`ENUM`: Aprobar, Rechazar): Si es frase para aceptar o rechazar.
* `Texto` (`VARCHAR(255)`): El motivo redactado.
* `Activa` (`TINYINT(1)`, Por defecto `1`): Si está visible en el formulario.

### 10. `Pedidos`
Cabecera principal de las órdenes de picking que se cargan al sistema.
* `Id` (`INT`, PK, Auto-incremental): Identificador del pedido.
* `CodigoPedido` (`VARCHAR(50)`, UNIQUE): Código visible (ej: `PED-001`).
* `BodegaId` (`INT`, FK -> `Bodegas`): Bodega donde debe recogerse.
* `Prioridad` (`ENUM`: Alta, Media, Baja): Nivel de urgencia.
* `Estado` (`ENUM`: Pendiente, Asignado, Reasignado, Completado, Cancelado, Dividido): Estado actual del pedido.
* `EmpleadoId` (`INT`, Opcional, FK -> `Empleados`): Quién lo está recogiendo.
* `FechaCarga` (`DATETIME(6)`): Fecha en que se subió desde Excel.
* `FechaAsignacion` (`DATETIME(6)`, Opcional): Hora en que se le entregó al operario.
* `AsignadoPorId` (`INT`, Opcional, FK -> `Usuarios`): Supervisor que asignó la orden.
* `FechaInicioProceso` (`DATETIME(6)`, Opcional): Hora real en que inició la recolección.
* `FechaFinalizacion` (`DATETIME(6)`, Opcional): Hora en que se terminó el picking.
* `TiempoEstimadoMinutos` (`DOUBLE`, Opcional): Minutos que calculó el algoritmo.
* `TiempoEstandarMinutos` (`DOUBLE`, Opcional): Tiempo base según los tipos de productos.
* `TiempoProcesamientoMinutos` (`DOUBLE`, Opcional): Minutos reales que se demoró el operario.
* `MotivoCancelacion` (`VARCHAR(250)`, Opcional): Por qué se canceló si fue el caso.
* `MotivoReasignacion` (`VARCHAR(250)`, Opcional): Por qué se cambió de empleado.
* `ConfirmacionFisica` (`TINYINT(1)`, Por defecto `0`): Marca si los paquetes ya fueron verificados.
* `PedidoPadreId` (`INT`, Opcional, FK -> `Pedidos`): ID del pedido original si este es una parte dividida.
* `NumeroParte` (`INT`, Opcional): Número de parte (Parte 1, Parte 2, etc.).

### 11. `Productos`
Catálogo de artículos o referencias que se comercializan (SKUs).
* `Id` (`INT`, PK, Auto-incremental): Identificador del producto.
* `Codigo` (`VARCHAR(50)`, UNIQUE): Código o SKU del producto.
* `Nombre` (`VARCHAR(150)`): Nombre del artículo.
* `CategoriaProductoId` (`INT`, Opcional, FK -> `CategoriasProducto`): Categoría de tamaño/peso.
* `UnidadesPorCaja` (`INT`, Por defecto `1`): Cantidad de unidades que vienen juntas por caja.

### 12. `Roles`
Perfiles de usuario en el sistema.
* `Id` (`INT`, PK, Auto-incremental): 1 = Administrador, 2 = Supervisor, 3 = Auxiliar.
* `Nombre` (`VARCHAR(30)`, UNIQUE): Nombre del rol.

### 13. `Solicitudes`
Peticiones enviadas por supervisores o auxiliares para que el administrador las revise.
* `Id` (`INT`, PK, Auto-incremental): Identificador de la solicitud.
* `TipoSolicitudId` (`INT`, FK -> `TiposSolicitud`): Qué trámite es.
* `UsuarioSolicitanteId` (`INT`, FK -> `Usuarios`): Quién pide el cambio.
* `BodegaId` (`INT`, Opcional, FK -> `Bodegas`): Bodega de donde proviene la petición.
* `Estado` (`ENUM`: Pendiente, Aprobada, Rechazada): Estado de la solicitud.
* `FechaSolicitud` (`DATETIME(6)`): Fecha en que se radicó.
* `DatosJson` (`LONGTEXT`): Datos del formulario guardados en formato JSON.
* `UsuarioRespondeId` (`INT`, Opcional, FK -> `Usuarios`): Administrador que revisó.
* `FechaRespuesta` (`DATETIME(6)`, Opcional): Cuándo se tomó la decisión.
* `ObservacionId` (`INT`, Opcional, FK -> `ObservacionesSolicitud`): Frase estándar elegida.
* `ObservacionRespuesta` (`VARCHAR(500)`, Opcional): Comentario libre de respuesta.

### 14. `TiposSolicitud`
Catálogo de trámites permitidos.
* `Id` (`INT`, PK, Auto-incremental): 1=Registro empleado, 2=Editar empleado, 3=Inactivar empleado, 4=Editar ubicación, 5=Eliminar ubicación.
* `Nombre` (`VARCHAR(50)`, UNIQUE): Nombre del trámite.

### 15. `TrasladosPersonal`
Historial de empleados o usuarios que fueron cambiados de una bodega a otra.
* `Id` (`INT`, PK, Auto-incremental): Identificador del movimiento.
* `EmpleadoId` (`INT`, Opcional, FK -> `Empleados`): Operario trasladado.
* `UsuarioId` (`INT`, Opcional, FK -> `Usuarios`): Usuario trasladado.
* `BodegaOrigenId` (`INT`, FK -> `Bodegas`): Bodega de donde salió.
* `BodegaDestinoId` (`INT`, FK -> `Bodegas`): Bodega a la que llegó.
* `Fecha` (`DATETIME(6)`): Fecha en que se autorizó el traslado.

### 16. `Ubicaciones`
Posiciones físicas en la estantería del almacén.
* `Id` (`INT`, PK, Auto-incremental): Identificador de la ubicación.
* `BodegaId` (`INT`, FK -> `Bodegas`): En qué bodega física está.
* `CodigoUbicacion` (`VARCHAR(50)`): Código de posición (ej: `PAS01-EST02-NIV03`).
* `Pasillo` (`VARCHAR(20)`): Número de pasillo.
* `Estante` (`VARCHAR(20)`): Número de estante o columna.
* `Nivel` (`VARCHAR(20)`): Altura o nivel del estante.
* `Activa` (`TINYINT(1)`, Por defecto `1`): Si se puede usar para guardar productos.
* `ProductoId` (`INT`, Opcional, FK -> `Productos`): Producto que tiene asignado en esa posición.

### 17. `Usuarios`
Cuentas de acceso que pueden iniciar sesión en el sistema web.
* `Id` (`INT`, PK, Auto-incremental): Identificador del usuario.
* `NombreCompleto` (`VARCHAR(100)`): Nombres y apellidos.
* `Identificacion` (`VARCHAR(30)`, Opcional, UNIQUE): Cédula del usuario.
* `Correo` (`VARCHAR(100)`, UNIQUE): Correo con el que inicia sesión.
* `PasswordHash` (`LONGTEXT`): Contraseña encriptada con BCrypt (nunca en texto plano).
* `SelloSeguridad` (`VARCHAR(36)`): Token que cierra sesiones anteriores al cambiar la clave.
* `RolId` (`INT`, FK -> `Roles`): Rol de permisos (Admin, Supervisor o Auxiliar).
* `BodegaId` (`INT`, Opcional, FK -> `Bodegas`): Bodega a la que está limitado (NULL si es Admin).
* `Estado` (`ENUM`: Activo, Inactivo, Bloqueado): Estado de la cuenta.
* `IntentosFallidos` (`INT`, Por defecto `0`): Veces consecutivas que erró la contraseña.
* `FechaBloqueo` (`DATETIME(6)`, Opcional): Momento en que se bloqueó.
* `FechaRegistro` (`DATETIME(6)`): Cuándo se dio de alta la cuenta.
* `UltimoAcceso` (`DATETIME(6)`, Opcional): Última vez que inició sesión.
* `CodigoVerificacionHash` (`VARCHAR(60)`, Opcional): Código de 6 dígitos para recuperar clave.
* `CodigoVerificacionExpira` (`DATETIME(6)`, Opcional): Fecha de vencimiento del código.
* `IntentosCodigo` (`INT`, Por defecto `0`): Intentos fallidos con el código temporal.
* `PasswordPendienteHash` (`LONGTEXT`, Opcional): Clave nueva a la espera de confirmación.
* `CorreoVerificado` (`TINYINT(1)`, Por defecto `1`): Indica si confirmó su correo.
