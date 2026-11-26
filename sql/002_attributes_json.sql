-- Telemetría: position.attributes completo en una columna JSON.
-- EJECUTAR ANTES de desplegar la nueva versión del agente.
-- Ejecutar en CADA base de datos que use el agente (la de "MySql" y la de "MySqlGts").
--
-- Requiere MySQL 8.0.12 o superior por ALGORITHM=INSTANT (verificar con SELECT VERSION();).
-- Con INSTANT la tabla no se reconstruye; si no es posible, el ALTER falla sin hacer cambios.

-- Histórico: atributos de cada posición, tal cual llegan
ALTER TABLE eventdata
  ADD COLUMN `attributes` JSON NULL,
  ALGORITHM=INSTANT;

-- Último valor conocido de cada atributo, y marca de unidades con telemetría
ALTER TABLE device
  ADD COLUMN `attributes` JSON NULL,
  ADD COLUMN `telemetria` TINYINT(1) NOT NULL DEFAULT 0,
  ALGORITHM=INSTANT;

-- "telemetria" se marca a mano al instalar el equipo (el agente no la modifica):
-- UPDATE device SET telemetria = 1 WHERE accountID = 'CUENTA' AND deviceID = 'DISPOSITIVO';
