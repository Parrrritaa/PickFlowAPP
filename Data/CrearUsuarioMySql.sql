-- Usuario de MySQL solo para PickFlow (ejecutar una vez, conectado como root, por ejemplo desde MySQL Workbench).
--
-- Por qué: la aplicación no debe conectarse como root. root puede leer y borrar todas las bases del servidor,
-- crear usuarios y escribir archivos en disco; si alguna vez se colara una inyección SQL, el daño llegaría a todo.
-- Con este usuario, lo peor que se podría alcanzar es la base PickingDB.
--
-- Antes de ejecutarlo, cambia CAMBIA_ESTA_CONTRASENA por una contraseña larga y propia.
-- Después pon el mismo usuario y contraseña en appsettings.Development.json:
--   "DefaultConnection": "Server=localhost;Port=3306;Database=PickingDB;User=pickflow_app;Password=...;"

-- La base se crea aquí (como root) porque pickflow_app no tiene permiso para crear bases nuevas
CREATE DATABASE IF NOT EXISTS PickingDB;

-- Solo puede conectarse desde el mismo equipo donde corre la aplicación
CREATE USER IF NOT EXISTS 'pickflow_app'@'localhost' IDENTIFIED BY 'CAMBIA_ESTA_CONTRASENA';

-- Permisos únicamente sobre PickingDB:
--   SELECT, INSERT, UPDATE, DELETE  -> el trabajo diario de la aplicación
--   CREATE, ALTER, INDEX, DROP, REFERENCES -> las migraciones que se aplican al arrancar (DbInitializer)
--     y el reinicio de autoincrementos al limpiar la base (ALTER TABLE en AuditoriaController)
--   LOCK TABLES -> los bloqueos SELECT ... FOR UPDATE al asignar pedidos
-- Sin GRANT OPTION, FILE, PROCESS, SUPER ni acceso a otras bases.
GRANT SELECT, INSERT, UPDATE, DELETE,
      CREATE, ALTER, INDEX, DROP, REFERENCES,
      LOCK TABLES
    ON PickingDB.* TO 'pickflow_app'@'localhost';

FLUSH PRIVILEGES;
