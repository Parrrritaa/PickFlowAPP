-- Base de datos del proyecto PickFlow
-- Aquí están los 3 triggers, los 3 procedimientos almacenados y las vistas que armamos para el sistema.

USE PickingDB;

-- ==============================================================================
-- 1. TRIGGERS (Disparadores automáticos)
-- ==============================================================================

-- ------------------------------------------------------------------------------
-- TRIGGER 1: Cronómetro automático cuando se termina un pedido
-- Tabla: Pedidos (antes de actualizar)
-- Qué hace: Cuando un pedido cambia su estado a 'Completado', este trigger revisa
-- que no quede sin fecha de fin, le pone que la entrega física fue confirmada
-- y calcula solo cuántos minutos se demoró el operario haciendo el picking.
-- Así nadie tiene que calcular tiempos a mano ni quedan pedidos en blanco.
-- ------------------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_pedidos_before_update_completar;
DELIMITER $$
CREATE TRIGGER trg_pedidos_before_update_completar
BEFORE UPDATE ON Pedidos
FOR EACH ROW
BEGIN
    -- Solo actuamos si el pedido acaba de pasar a Completado
    IF NEW.Estado = 'Completado' AND OLD.Estado <> 'Completado' THEN
        
        -- Si no mandaron fecha de finalización, ponemos la fecha y hora de este instante
        IF NEW.FechaFinalizacion IS NULL THEN
            SET NEW.FechaFinalizacion = NOW(6);
        END IF;

        -- Marcamos la confirmación física de entrega
        SET NEW.ConfirmacionFisica = 1;

        -- Calculamos los minutos reales (inicio vs fin). Dejamos mínimo 1 minuto para evitar ceros
        IF NEW.TiempoProcesamientoMinutos IS NULL OR NEW.TiempoProcesamientoMinutos <= 0 THEN
            SET NEW.TiempoProcesamientoMinutos = ROUND(
                GREATEST(
                    1.0,
                    TIMESTAMPDIFF(
                        SECOND,
                        COALESCE(NEW.FechaInicioProceso, NEW.FechaAsignacion, OLD.FechaCarga),
                        NEW.FechaFinalizacion
                    ) / 60.0
                ),
                1
            );
        END IF;
    END IF;
END$$
DELIMITER ;


-- ------------------------------------------------------------------------------
-- TRIGGER 2: Control de calidad de las novedades
-- Tabla: NovedadesPedido (antes de insertar)
-- Qué hace: Antes de guardar una novedad le quita los espacios sobrantes a la
-- descripción y, si queda vacía, rechaza el registro con un mensaje claro.
-- Además deja la novedad coherente: si viene atendida y sin fecha de atención le
-- pone la fecha actual, y si viene sin atender limpia los datos de atención.
-- (El tipo no se revisa aquí: la columna TipoNovedad es un ENUM y MySQL ya
-- rechaza cualquier valor que no esté en la lista.)
-- Los avisos y el registro en LogsSistema NO van aquí: la aplicación ya avisa a
-- administradores y supervisores de la bodega y ya guarda el log; hacerlo también
-- aquí los duplicaría.
-- ------------------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_novedades_pedido_after_insert;
DROP TRIGGER IF EXISTS trg_novedades_pedido_before_insert;
DELIMITER $$
CREATE TRIGGER trg_novedades_pedido_before_insert
BEFORE INSERT ON NovedadesPedido
FOR EACH ROW
BEGIN
    SET NEW.Descripcion = TRIM(NEW.Descripcion);
    IF NEW.Descripcion IS NULL OR NEW.Descripcion = '' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'La descripción de la novedad es obligatoria.';
    END IF;

    IF NEW.Atendida = 1 THEN
        IF NEW.FechaAtencion IS NULL THEN
            SET NEW.FechaAtencion = NOW(6);
        END IF;
    ELSE
        SET NEW.AtendidaPor = NULL;
        SET NEW.FechaAtencion = NULL;
        SET NEW.ObservacionesAtencion = NULL;
    END IF;
END$$
DELIMITER ;


-- ------------------------------------------------------------------------------
-- TRIGGER 3: Candado de seguridad para usuarios
-- Tabla: Usuarios (antes de actualizar)
-- Qué hace:
-- 1) Si una cuenta activa llega al máximo de intentos fallidos, la bloquea.
--    El máximo se lee de ConfiguracionesSistema (clave MaxIntentosFallidos), el
--    mismo parámetro que el administrador cambia en Configuración: 5 por defecto,
--    y si el valor guardado no es un entero entre 3 y 10 se usa 5.
--    Los administradores (RolId = 1) nunca se bloquean, igual que en la aplicación,
--    para que siempre quede alguien que pueda desbloquear cuentas.
-- 2) Si el administrador desbloquea la cuenta (vuelve a 'Activo'), reinicia los
--    contadores de intentos y borra la fecha de bloqueo.
-- Cuando el bloqueo viene de la aplicación, la cuenta ya llega 'Bloqueado' y el
-- trigger no hace nada; protege los cambios hechos por fuera de la aplicación.
-- ------------------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_usuarios_control_bloqueo;
DELIMITER $$
CREATE TRIGGER trg_usuarios_control_bloqueo
BEFORE UPDATE ON Usuarios
FOR EACH ROW
BEGIN
    DECLARE v_Texto VARCHAR(255);
    DECLARE v_MaxIntentos INT DEFAULT 5;

    IF NEW.Estado = 'Activo' AND OLD.Estado = 'Activo'
       AND NEW.IntentosFallidos > OLD.IntentosFallidos
       AND NEW.RolId <> 1 THEN

        SELECT Valor INTO v_Texto
          FROM ConfiguracionesSistema
         WHERE Clave = 'MaxIntentosFallidos'
         LIMIT 1;

        IF v_Texto REGEXP '^[0-9]+$' AND CAST(v_Texto AS UNSIGNED) BETWEEN 3 AND 10 THEN
            SET v_MaxIntentos = CAST(v_Texto AS UNSIGNED);
        END IF;

        IF NEW.IntentosFallidos >= v_MaxIntentos THEN
            SET NEW.Estado = 'Bloqueado';
            SET NEW.FechaBloqueo = NOW(6);
        END IF;
    END IF;

    -- Si el admin lo desbloquea, limpiamos los intentos y la fecha de bloqueo
    IF NEW.Estado = 'Activo' AND OLD.Estado = 'Bloqueado' THEN
        SET NEW.IntentosFallidos = 0;
        SET NEW.FechaBloqueo = NULL;
        SET NEW.IntentosCodigo = 0;
    END IF;
END$$
DELIMITER ;


-- ==============================================================================
-- 2. PROCEDIMIENTOS ALMACENADOS (Funciones para tareas del negocio)
-- ==============================================================================

-- ------------------------------------------------------------------------------
-- PROCEDIMIENTO 1: Asignar un pedido a un empleado
-- Qué hace: Valida que el pedido exista y esté pendiente, que el operario esté activo
-- y que trabaje en la misma bodega del pedido. Luego calcula el tiempo estimado
-- según los productos y la experiencia del trabajador, y actualiza el pedido.
-- ------------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS sp_AsignarPedidoAEmpleado;
DELIMITER $$
CREATE PROCEDURE sp_AsignarPedidoAEmpleado (
    IN  p_PedidoId       INT,
    IN  p_EmpleadoId     INT,
    IN  p_AsignadoPorId  INT,
    OUT p_CodigoResultado INT,         -- 0 si todo salió bien, mayor a 0 si hubo error
    OUT p_MensajeResultado VARCHAR(255)
)
sp_block: BEGIN
    DECLARE v_BodegaPedido INT;
    DECLARE v_EstadoPedido VARCHAR(30);
    DECLARE v_CodigoPedido VARCHAR(50);
    DECLARE v_BodegaEmpleado INT;
    DECLARE v_EmpleadoActivo TINYINT(1);
    DECLARE v_NombreEmpleado VARCHAR(100);
    DECLARE v_NombreUsuario VARCHAR(100);
    DECLARE v_TiempoEstandar DOUBLE DEFAULT 0;
    DECLARE v_TiempoEstimado DOUBLE DEFAULT 0;
    DECLARE v_FactorRendimiento DOUBLE DEFAULT 1.0;
    DECLARE v_MesesExperiencia INT DEFAULT 0;

    -- Si algo truena en la base de datos, deshacemos los cambios
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        SET p_CodigoResultado = 99;
        SET p_MensajeResultado = 'Ocurrió un error inesperado al intentar asignar el pedido.';
    END;

    -- 1. Revisamos que el pedido exista y esté listo para asignar
    SELECT BodegaId, Estado, CodigoPedido
      INTO v_BodegaPedido, v_EstadoPedido, v_CodigoPedido
      FROM Pedidos
     WHERE Id = p_PedidoId;

    IF v_BodegaPedido IS NULL THEN
        SET p_CodigoResultado = 1;
        SET p_MensajeResultado = 'El pedido no existe.';
        LEAVE sp_block;
    END IF;

    IF v_EstadoPedido NOT IN ('Pendiente', 'Reasignado') THEN
        SET p_CodigoResultado = 2;
        SET p_MensajeResultado = CONCAT('No se puede asignar. El pedido está en estado: ', v_EstadoPedido);
        LEAVE sp_block;
    END IF;

    -- 2. Revisamos los datos del empleado (bodega, estado y experiencia)
    SELECT BodegaId, Activo, NombreCompleto,
           GREATEST(0, (YEAR(CURDATE()) - YEAR(FechaIngreso)) * 12 + (MONTH(CURDATE()) - MONTH(FechaIngreso)))
      INTO v_BodegaEmpleado, v_EmpleadoActivo, v_NombreEmpleado, v_MesesExperiencia
      FROM Empleados
     WHERE Id = p_EmpleadoId;

    IF v_BodegaEmpleado IS NULL THEN
        SET p_CodigoResultado = 3;
        SET p_MensajeResultado = 'El empleado no existe.';
        LEAVE sp_block;
    END IF;

    IF v_EmpleadoActivo = 0 THEN
        SET p_CodigoResultado = 4;
        SET p_MensajeResultado = 'El empleado está marcado como inactivo.';
        LEAVE sp_block;
    END IF;

    -- No dejamos asignar a un empleado que trabaja en otra bodega diferente
    IF v_BodegaEmpleado <> v_BodegaPedido THEN
        SET p_CodigoResultado = 5;
        SET p_MensajeResultado = 'El empleado no pertenece a la misma bodega de este pedido.';
        LEAVE sp_block;
    END IF;

    -- 3. Calculamos el tiempo base según los productos y categorías
    SELECT COALESCE(SUM(
        CEIL(d.Cantidad / GREATEST(1, COALESCE(p.UnidadesPorCaja, 1))) * 
        COALESCE(c.MinutosPorUnidad, 1.5)
    ), 5.0)
      INTO v_TiempoEstandar
      FROM DetallesPedido d
      LEFT JOIN Productos p ON d.CodigoProducto = p.Codigo
      LEFT JOIN CategoriasProducto c ON p.CategoriaProductoId = c.Id
     WHERE d.PedidoId = p_PedidoId;

    -- Si tiene más de 2 años rinde más rápido, si es nuevo (menos de 6 meses) tarda un poco más
    IF v_MesesExperiencia >= 24 THEN
        SET v_FactorRendimiento = 0.85;
    ELSEIF v_MesesExperiencia >= 6 THEN
        SET v_FactorRendimiento = 1.00;
    ELSE
        SET v_FactorRendimiento = 1.20;
    END IF;

    SET v_TiempoEstimado = ROUND(v_TiempoEstandar * v_FactorRendimiento, 1);

    -- 4. Guardamos todo en una sola transacción
    START TRANSACTION;

        UPDATE Pedidos
           SET EmpleadoId = p_EmpleadoId,
               AsignadoPorId = p_AsignadoPorId,
               Estado = 'Asignado',
               FechaAsignacion = NOW(6),
               FechaInicioProceso = DATE_ADD(NOW(6), INTERVAL 3 MINUTE),
               TiempoEstandarMinutos = v_TiempoEstandar,
               TiempoEstimadoMinutos = v_TiempoEstimado
         WHERE Id = p_PedidoId;

        -- Dejamos registro en los logs
        SELECT NombreCompleto INTO v_NombreUsuario FROM Usuarios WHERE Id = p_AsignadoPorId;

        INSERT INTO LogsSistema (
            FechaHora, Usuario, UsuarioId, Rol, Accion, Modulo, Detalle, DireccionIp
        ) VALUES (
            NOW(6),
            COALESCE(v_NombreUsuario, 'Supervisor'),
            p_AsignadoPorId,
            'Supervisor',
            'Asignación de Pedido',
            'Pedidos',
            CONCAT('Pedido ', v_CodigoPedido, ' asignado a ', v_NombreEmpleado, ' (Tiempo est: ', v_TiempoEstimado, ' min)'),
            '127.0.0.1'
        );

    COMMIT;

    SET p_CodigoResultado = 0;
    SET p_MensajeResultado = CONCAT('Pedido asignado correctamente a ', v_NombreEmpleado);
END$$
DELIMITER ;


-- ------------------------------------------------------------------------------
-- PROCEDIMIENTO 2: Terminar un pedido y resolver novedades
-- Qué hace: Pone el pedido en 'Completado'. Si tiene problemas o novedades abiertas,
-- permite cerrarlas de una vez con una nota. Y si el pedido era parte de uno dividido,
-- se fija si las demás partes ya terminaron para cerrar también el pedido padre.
-- ------------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS sp_CerrarPedidoConNovedades;
DELIMITER $$
CREATE PROCEDURE sp_CerrarPedidoConNovedades (
    IN  p_PedidoId                 INT,
    IN  p_UsuarioId                INT,
    IN  p_AtenderNovedadesAbiertas TINYINT(1),   -- 1 para cerrar novedades pendientes, 0 para no
    IN  p_ObservacionCierre        VARCHAR(250),
    OUT p_CodigoResultado          INT,
    OUT p_MensajeResultado         VARCHAR(255)
)
sp_cierre: BEGIN
    DECLARE v_EstadoActual VARCHAR(30);
    DECLARE v_CodigoPedido VARCHAR(50);
    DECLARE v_PedidoPadreId INT;
    DECLARE v_NombreUsuario VARCHAR(100);
    DECLARE v_NovedadesAbiertas INT DEFAULT 0;
    DECLARE v_PartesIncompletas INT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        SET p_CodigoResultado = 99;
        SET p_MensajeResultado = 'Hubo un error al intentar cerrar el pedido.';
    END;

    -- 1. Buscamos el pedido y revisamos que esté en curso
    SELECT Estado, CodigoPedido, PedidoPadreId
      INTO v_EstadoActual, v_CodigoPedido, v_PedidoPadreId
      FROM Pedidos
     WHERE Id = p_PedidoId;

    IF v_EstadoActual IS NULL THEN
        SET p_CodigoResultado = 1;
        SET p_MensajeResultado = 'El pedido no existe.';
        LEAVE sp_cierre;
    END IF;

    IF v_EstadoActual NOT IN ('Asignado', 'Reasignado') THEN
        SET p_CodigoResultado = 2;
        SET p_MensajeResultado = CONCAT('Solo se pueden cerrar pedidos que estén en curso (estado actual: ', v_EstadoActual, ').');
        LEAVE sp_cierre;
    END IF;

    -- 2. Vemos si tiene novedades abiertas
    SELECT COUNT(*) INTO v_NovedadesAbiertas
      FROM NovedadesPedido
     WHERE PedidoId = p_PedidoId AND Atendida = 0;

    -- Si hay novedades y el usuario no autorizó cerrarlas, paramos aquí
    IF v_NovedadesAbiertas > 0 AND p_AtenderNovedadesAbiertas = 0 THEN
        SET p_CodigoResultado = 3;
        SET p_MensajeResultado = CONCAT('El pedido aún tiene ', v_NovedadesAbiertas, ' novedad(es) abierta(s) sin atender.');
        LEAVE sp_cierre;
    END IF;

    SELECT NombreCompleto INTO v_NombreUsuario FROM Usuarios WHERE Id = p_UsuarioId;

    START TRANSACTION;

        -- Si había novedades pendientes, las marcamos como atendidas
        IF v_NovedadesAbiertas > 0 AND p_AtenderNovedadesAbiertas = 1 THEN
            UPDATE NovedadesPedido
               SET Atendida = 1,
                   AtendidaPor = COALESCE(v_NombreUsuario, 'Administrador'),
                   FechaAtencion = NOW(6),
                   ObservacionesAtencion = COALESCE(p_ObservacionCierre, 'Atendida al cerrar el pedido')
             WHERE PedidoId = p_PedidoId AND Atendida = 0;
        END IF;

        -- Pasamos el pedido a Completado (el Trigger 1 se encargará del cronómetro y la confirmación física)
        UPDATE Pedidos
           SET Estado = 'Completado',
               FechaFinalizacion = NOW(6),
               ConfirmacionFisica = 1
         WHERE Id = p_PedidoId;

        -- Si era parte de un pedido dividido, revisamos si todas las partes ya terminaron
        IF v_PedidoPadreId IS NOT NULL THEN
            SELECT COUNT(*) INTO v_PartesIncompletas
              FROM Pedidos
             WHERE PedidoPadreId = v_PedidoPadreId
               AND Estado NOT IN ('Completado', 'Cancelado');

            -- Si ya terminaron todas las partes, cerramos también el pedido original
            IF v_PartesIncompletas = 0 THEN
                UPDATE Pedidos
                   SET Estado = 'Completado',
                       ConfirmacionFisica = 1,
                       FechaFinalizacion = NOW(6)
                 WHERE Id = v_PedidoPadreId;
            END IF;
        END IF;

        -- Guardamos en la bitácora
        INSERT INTO LogsSistema (
            FechaHora, Usuario, UsuarioId, Rol, Accion, Modulo, Detalle, DireccionIp
        ) VALUES (
            NOW(6),
            COALESCE(v_NombreUsuario, 'Sistema'),
            p_UsuarioId,
            'Operación',
            'Cierre de Pedido',
            'Pedidos',
            CONCAT('Pedido ', v_CodigoPedido, ' completado con éxito.'),
            '127.0.0.1'
        );

    COMMIT;

    SET p_CodigoResultado = 0;
    SET p_MensajeResultado = CONCAT('El pedido ', v_CodigoPedido, ' se completó sin problemas.');
END$$
DELIMITER ;


-- ------------------------------------------------------------------------------
-- PROCEDIMIENTO 3: Reporte de productividad por bodega y fechas
-- Qué hace: Le das el ID de una bodega y un rango de fechas.
-- Te devuelve 2 tablas listas para mostrar:
-- 1) Los números generales de la bodega (pedidos completados, cancelados, pendientes y tiempos).
-- 2) El ranking de empleados (cuántos pedidos hizo cada uno y su porcentaje de eficiencia).
-- ------------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS sp_GenerarResumenProductividadBodega;
DELIMITER $$
CREATE PROCEDURE sp_GenerarResumenProductividadBodega (
    IN p_BodegaId     INT,
    IN p_FechaInicio  DATETIME,
    IN p_FechaFin     DATETIME
)
BEGIN
    -- TABLA 1: Resumen de la bodega
    SELECT 
        b.Id AS BodegaId,
        b.Codigo AS CodigoBodega,
        b.Nombre AS NombreBodega,
        COUNT(p.Id) AS TotalPedidos,
        SUM(CASE WHEN p.Estado = 'Completado' THEN 1 ELSE 0 END) AS PedidosCompletados,
        SUM(CASE WHEN p.Estado = 'Cancelado' THEN 1 ELSE 0 END) AS PedidosCancelados,
        SUM(CASE WHEN p.Estado IN ('Asignado', 'Reasignado') THEN 1 ELSE 0 END) AS PedidosEnCurso,
        SUM(CASE WHEN p.Estado = 'Pendiente' THEN 1 ELSE 0 END) AS PedidosPendientes,
        COALESCE(SUM(dp.TotalUnidades), 0) AS TotalUnidadesRecogidas,
        ROUND(AVG(CASE WHEN p.Estado = 'Completado' THEN p.TiempoProcesamientoMinutos END), 1) AS PromedioTiempoMinutos,
        ROUND(AVG(CASE WHEN p.Estado = 'Completado' THEN p.TiempoEstandarMinutos END), 1) AS PromedioEstandarMinutos,
        COUNT(n.Id) AS TotalNovedadesReportadas
    FROM Bodegas b
    LEFT JOIN Pedidos p ON b.Id = p.BodegaId AND p.FechaCarga BETWEEN p_FechaInicio AND p_FechaFin
    LEFT JOIN (
        SELECT PedidoId, SUM(Cantidad) AS TotalUnidades 
        FROM DetallesPedido 
        GROUP BY PedidoId
    ) dp ON p.Id = dp.PedidoId
    LEFT JOIN NovedadesPedido n ON p.Id = n.PedidoId
    WHERE b.Id = p_BodegaId
    GROUP BY b.Id, b.Codigo, b.Nombre;

    -- TABLA 2: Ranking de operarios en esa bodega y fechas
    SELECT 
        e.Id AS EmpleadoId,
        e.Identificacion,
        e.NombreCompleto,
        COUNT(p.Id) AS PedidosCompletados,
        COALESCE(SUM(dp.TotalUnidades), 0) AS UnidadesRecogidas,
        ROUND(AVG(p.TiempoProcesamientoMinutos), 1) AS PromedioMinutosPorPedido,
        ROUND(AVG(p.TiempoEstandarMinutos), 1) AS PromedioEstandarMinutos,
        ROUND(AVG(p.TiempoEstandarMinutos / NULLIF(p.TiempoProcesamientoMinutos, 0)) * 100, 1) AS EficienciaPorcentaje
    FROM Empleados e
    JOIN Pedidos p ON e.Id = p.EmpleadoId
    LEFT JOIN (
        SELECT PedidoId, SUM(Cantidad) AS TotalUnidades 
        FROM DetallesPedido 
        GROUP BY PedidoId
    ) dp ON p.Id = dp.PedidoId
    WHERE e.BodegaId = p_BodegaId
      AND p.Estado = 'Completado'
      AND p.FechaFinalizacion BETWEEN p_FechaInicio AND p_FechaFin
    GROUP BY e.Id, e.Identificacion, e.NombreCompleto
    ORDER BY PedidosCompletados DESC, EficienciaPorcentaje DESC;
END$$
DELIMITER ;


-- ==============================================================================
-- 3. VISTAS (Consultas ya armadas para no repetir código largo)
-- ==============================================================================

-- ------------------------------------------------------------------------------
-- VISTA 1: Pedidos con toda la información junta
-- Para qué sirve: Muestra el pedido con el nombre de la bodega, quién lo recogió,
-- quién lo asignó, cuántos productos tiene y si tiene novedades pendientes.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_PedidosDetalleCompleto AS
SELECT 
    p.Id AS PedidoId,
    p.CodigoPedido,
    p.BodegaId,
    b.Codigo AS CodigoBodega,
    b.Nombre AS NombreBodega,
    p.Prioridad,
    p.Estado,
    p.EmpleadoId,
    e.Identificacion AS IdentificacionEmpleado,
    e.NombreCompleto AS NombreEmpleado,
    p.AsignadoPorId,
    u.NombreCompleto AS AsignadoPorNombre,
    p.FechaCarga,
    p.FechaAsignacion,
    p.FechaInicioProceso,
    p.FechaFinalizacion,
    p.TiempoEstimadoMinutos,
    p.TiempoEstandarMinutos,
    p.TiempoProcesamientoMinutos,
    p.ConfirmacionFisica,
    p.PedidoPadreId,
    p.NumeroParte,
    COALESCE(resumen.TotalLineas, 0) AS TotalLineas,
    COALESCE(resumen.TotalUnidades, 0) AS TotalUnidades,
    COALESCE(nov.TotalNovedades, 0) AS TotalNovedades,
    COALESCE(nov.NovedadesPendientes, 0) AS NovedadesPendientes
FROM Pedidos p
INNER JOIN Bodegas b ON p.BodegaId = b.Id
LEFT JOIN Empleados e ON p.EmpleadoId = e.Id
LEFT JOIN Usuarios u ON p.AsignadoPorId = u.Id
LEFT JOIN (
    SELECT PedidoId, COUNT(Id) AS TotalLineas, SUM(Cantidad) AS TotalUnidades
    FROM DetallesPedido
    GROUP BY PedidoId
) resumen ON p.Id = resumen.PedidoId
LEFT JOIN (
    SELECT PedidoId, COUNT(Id) AS TotalNovedades, SUM(CASE WHEN Atendida = 0 THEN 1 ELSE 0 END) AS NovedadesPendientes
    FROM NovedadesPedido
    GROUP BY PedidoId
) nov ON p.Id = nov.PedidoId;


-- ------------------------------------------------------------------------------
-- VISTA 2: Rendimiento y tiempos de los empleados
-- Para qué sirve: Muestra la lista de operarios, cuántos pedidos completaron,
-- cuántas unidades recogieron y qué porcentaje de eficiencia tienen frente al tiempo estándar.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_RendimientoEmpleados AS
SELECT 
    e.Id AS EmpleadoId,
    e.Identificacion,
    e.NombreCompleto,
    e.BodegaId,
    b.Nombre AS NombreBodega,
    e.Activo,
    e.FechaIngreso,
    CONCAT(DATE_FORMAT(e.HorarioEntrada, '%H:%i'), ' - ', DATE_FORMAT(e.HorarioSalida, '%H:%i')) AS JornadaLaboral,
    COUNT(p.Id) AS PedidosCompletados,
    COALESCE(SUM(det.TotalUnidades), 0) AS TotalUnidadesRecogidas,
    ROUND(AVG(p.TiempoProcesamientoMinutos), 2) AS PromedioTiempoMinutos,
    ROUND(AVG(p.TiempoEstandarMinutos), 2) AS PromedioEstandarMinutos,
    ROUND(
        (AVG(p.TiempoEstandarMinutos) / NULLIF(AVG(p.TiempoProcesamientoMinutos), 0)) * 100,
        1
    ) AS PorcentajeEficiencia
FROM Empleados e
LEFT JOIN Bodegas b ON e.BodegaId = b.Id
LEFT JOIN Pedidos p ON e.Id = p.EmpleadoId 
                    AND p.Estado = 'Completado' 
                    AND (e.FechaReinicioPromedio IS NULL OR p.FechaFinalizacion >= e.FechaReinicioPromedio)
LEFT JOIN (
    SELECT PedidoId, SUM(Cantidad) AS TotalUnidades
    FROM DetallesPedido
    GROUP BY PedidoId
) det ON p.Id = det.PedidoId
GROUP BY e.Id, e.Identificacion, e.NombreCompleto, e.BodegaId, b.Nombre, e.Activo, e.FechaIngreso, e.HorarioEntrada, e.HorarioSalida;


-- ------------------------------------------------------------------------------
-- VISTA 3: Mapa de inventario y ubicaciones
-- Para qué sirve: Muestra cada posición física (pasillo, estante y nivel) junto con
-- qué producto tiene guardado, su categoría y si está ocupada o libre.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_InventarioUbicaciones AS
SELECT 
    u.Id AS UbicacionId,
    u.BodegaId,
    b.Codigo AS CodigoBodega,
    b.Nombre AS NombreBodega,
    u.CodigoUbicacion,
    u.Pasillo,
    u.Estante,
    u.Nivel,
    u.Activa AS UbicacionActiva,
    u.ProductoId,
    p.Codigo AS CodigoProducto,
    p.Nombre AS NombreProducto,
    p.UnidadesPorCaja,
    c.Id AS CategoriaId,
    c.Nombre AS CategoriaNombre,
    c.MinutosPorUnidad AS MinutosEstimadosPorToma,
    CASE WHEN u.ProductoId IS NOT NULL THEN 'Ocupada' ELSE 'Libre' END AS EstadoOcupacion
FROM Ubicaciones u
INNER JOIN Bodegas b ON u.BodegaId = b.Id
LEFT JOIN Productos p ON u.ProductoId = p.Id
LEFT JOIN CategoriasProducto c ON p.CategoriaProductoId = c.Id;


-- ------------------------------------------------------------------------------
-- VISTA 4: Novedades que están esperando atención
-- Para qué sirve: Lista rápida de problemas abiertos en los pedidos para que el
-- supervisor sepa cuáles atender primero y cuántos minutos llevan esperando.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_NovedadesPendientes AS
SELECT 
    n.Id AS NovedadId,
    n.PedidoId,
    p.CodigoPedido,
    p.BodegaId,
    b.Nombre AS NombreBodega,
    p.Prioridad AS PrioridadPedido,
    p.Estado AS EstadoPedido,
    n.TipoNovedad,
    n.Descripcion,
    n.FechaRegistro,
    n.RegistradoPor,
    n.RegistradoPorId,
    TIMESTAMPDIFF(MINUTE, n.FechaRegistro, NOW(6)) AS MinutosTranscurridos
FROM NovedadesPedido n
INNER JOIN Pedidos p ON n.PedidoId = p.Id
INNER JOIN Bodegas b ON p.BodegaId = b.Id
WHERE n.Atendida = 0;


-- ------------------------------------------------------------------------------
-- VISTA 5: Resumen en vivo de cómo va cada bodega hoy
-- Para qué sirve: Tablero general que dice cuántos empleados están trabajando,
-- cuántos pedidos están pendientes, cuántos en curso y cuántos se terminaron hoy.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_EstadoOperativoBodegas AS
SELECT 
    b.Id AS BodegaId,
    b.Codigo AS CodigoBodega,
    b.Nombre AS NombreBodega,
    b.Activa,
    (SELECT COUNT(*) FROM Empleados e WHERE e.BodegaId = b.Id AND e.Activo = 1) AS EmpleadosActivos,
    (SELECT COUNT(*) FROM Pedidos p WHERE p.BodegaId = b.Id AND p.Estado = 'Pendiente') AS PedidosPendientes,
    (SELECT COUNT(*) FROM Pedidos p WHERE p.BodegaId = b.Id AND p.Estado IN ('Asignado', 'Reasignado')) AS PedidosEnCurso,
    (SELECT COUNT(*) FROM Pedidos p WHERE p.BodegaId = b.Id AND p.Estado = 'Completado' AND DATE(p.FechaFinalizacion) = CURDATE()) AS CompletadosHoy,
    (SELECT COUNT(*) FROM NovedadesPedido np JOIN Pedidos p ON np.PedidoId = p.Id WHERE p.BodegaId = b.Id AND np.Atendida = 0) AS NovedadesSinAtender,
    (SELECT COUNT(*) FROM Ubicaciones u WHERE u.BodegaId = b.Id AND u.Activa = 1) AS TotalUbicacionesActivas
FROM Bodegas b;


-- ------------------------------------------------------------------------------
-- VISTA 6: Historial de solicitudes administrativas
-- Para qué sirve: Muestra quién pidió crear o mover empleados/ubicaciones, si se aprobó
-- o rechazó, quién respondió y cuánto tardaron en darle respuesta.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_HistorialSolicitudes AS
SELECT 
    s.Id AS SolicitudId,
    s.TipoSolicitudId,
    ts.Nombre AS TipoSolicitudNombre,
    s.BodegaId,
    b.Nombre AS NombreBodega,
    s.Estado AS EstadoSolicitud,
    s.FechaSolicitud,
    s.UsuarioSolicitanteId,
    us.NombreCompleto AS SolicitanteNombre,
    us.Correo AS SolicitanteCorreo,
    s.UsuarioRespondeId,
    ur.NombreCompleto AS RespondeNombre,
    s.FechaRespuesta,
    TIMESTAMPDIFF(HOUR, s.FechaSolicitud, s.FechaRespuesta) AS HorasParaRespuesta,
    COALESCE(obs.Texto, s.ObservacionRespuesta) AS ObservacionFinal
FROM Solicitudes s
INNER JOIN TiposSolicitud ts ON s.TipoSolicitudId = ts.Id
LEFT JOIN Bodegas b ON s.BodegaId = b.Id
INNER JOIN Usuarios us ON s.UsuarioSolicitanteId = us.Id
LEFT JOIN Usuarios ur ON s.UsuarioRespondeId = ur.Id
LEFT JOIN ObservacionesSolicitud obs ON s.ObservacionId = obs.Id;


-- ------------------------------------------------------------------------------
-- VISTA 7: Registro reciente de auditoría
-- Para qué sirve: Muestra las acciones que hicieron los usuarios en el sistema,
-- asociando el nombre completo del usuario y su rol para revisar seguridad.
-- ------------------------------------------------------------------------------
CREATE OR REPLACE VIEW vw_AuditoriaReciente AS
SELECT 
    l.Id AS LogId,
    l.FechaHora,
    l.Modulo,
    l.Accion,
    l.Usuario AS IdentificadorUsuario,
    u.NombreCompleto AS NombreUsuarioRegistrado,
    l.Rol,
    l.Detalle,
    l.DireccionIp
FROM LogsSistema l
LEFT JOIN Usuarios u ON l.UsuarioId = u.Id;
