ALTER TABLE device
  ADD COLUMN protocol       varchar(24)        NULL,
  ADD COLUMN lastAccuracy   double             NULL,
  ADD COLUMN satellites     tinyint unsigned   NULL,
  ADD COLUMN rssi           tinyint unsigned   NULL,
  ADD COLUMN motionState    tinyint(1)         NULL,
  ADD COLUMN validFix       tinyint(1)         NULL,
  ADD COLUMN status         varchar(16)        NULL,
  ADD COLUMN calendarId     int(10) unsigned   NULL,
  ADD COLUMN lastPositionId int(10) unsigned   NULL,
  ADD COLUMN motionTime     int(10) unsigned   NULL,
  ADD COLUMN motionLat      double             NULL,
  ADD COLUMN motionLon      double             NULL;
