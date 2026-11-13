-- Atributos extra de Traccar (Codec 8 Extended, etc.), guardados con el mismo nombre.
-- EJECUTAR ANTES de desplegar la nueva versión del agente.
-- Ejecutar en CADA base de datos que use el agente (la de "MySql" y la de "MySqlGts").
-- Todas las columnas son NULL: los registros anteriores y otros protocolos quedan en NULL.

ALTER TABLE eventdata
  ADD COLUMN `priority` TINYINT UNSIGNED NULL,
  ADD COLUMN `event`    INT NULL,
  ADD COLUMN `ignition` TINYINT(1) NULL,
  ADD COLUMN `motion`   TINYINT(1) NULL,
  ADD COLUMN `io200`    BIGINT NULL,
  ADD COLUMN `io69`     BIGINT NULL,
  ADD COLUMN `io81`     BIGINT NULL,
  ADD COLUMN `io82`     BIGINT NULL,
  ADD COLUMN `io89`     BIGINT NULL,
  ADD COLUMN `io68`     BIGINT NULL,
  ADD COLUMN `io85`     BIGINT NULL,
  ADD COLUMN `io90`     BIGINT NULL,
  ADD COLUMN `io83`     BIGINT NULL,
  ADD COLUMN `io87`     BIGINT NULL,
  ADD COLUMN `io103`    BIGINT NULL,
  ADD COLUMN `pdop`     DOUBLE NULL,
  ADD COLUMN `hdop`     DOUBLE NULL,
  ADD COLUMN `operator` BIGINT NULL,
  ADD COLUMN `odometer` DOUBLE NULL;

-- Último valor recibido por dispositivo
ALTER TABLE device
  ADD COLUMN `priority` TINYINT UNSIGNED NULL,
  ADD COLUMN `event`    INT NULL,
  ADD COLUMN `ignition` TINYINT(1) NULL,
  ADD COLUMN `motion`   TINYINT(1) NULL,
  ADD COLUMN `io200`    BIGINT NULL,
  ADD COLUMN `io69`     BIGINT NULL,
  ADD COLUMN `io81`     BIGINT NULL,
  ADD COLUMN `io82`     BIGINT NULL,
  ADD COLUMN `io89`     BIGINT NULL,
  ADD COLUMN `io68`     BIGINT NULL,
  ADD COLUMN `io85`     BIGINT NULL,
  ADD COLUMN `io90`     BIGINT NULL,
  ADD COLUMN `io83`     BIGINT NULL,
  ADD COLUMN `io87`     BIGINT NULL,
  ADD COLUMN `io103`    BIGINT NULL,
  ADD COLUMN `pdop`     DOUBLE NULL,
  ADD COLUMN `hdop`     DOUBLE NULL,
  ADD COLUMN `operator` BIGINT NULL,
  ADD COLUMN `odometer` DOUBLE NULL;
