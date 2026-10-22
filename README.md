# traccar-position

Agente ASP.NET Core (.NET 10) que recibe el webhook de posiciones de Traccar, las encola en
SQLite (`/app/data/queue.db`) y las escribe en MySQL (`urbano` o `gts` según la cuenta).

## Requisitos en el servidor

- Docker y docker-compose 1.29.2 (comando `docker-compose`, con guion).
- Redes externas ya creadas:
  - `app-net` — donde está el contenedor `mysql-gts`.
  - `traccar-net` — donde está el contenedor `traccar`.

## Instalación

```bash
git clone <URL_DEL_REPO> traccar-position
cd traccar-position

# Configuración (no se versiona)
cp TraccarAgent/appsettings.json.example TraccarAgent/appsettings.json
nano TraccarAgent/appsettings.json   # poner usuario y contraseña de MySQL

# Carpeta persistente para la cola SQLite (el contenedor corre con uid/gid 10001)
mkdir -p data && sudo chown 10001:10001 data

docker-compose build
docker-compose up -d
```

En Traccar (`traccar.xml`) el forward de posiciones debe apuntar a:

```
http://traccar-position:5008/webhook/traccar
```

## Actualización

```bash
git pull && docker-compose build && docker-compose down && docker-compose up -d
```

## Pruebas con curl (desde el propio servidor)

El puerto solo está publicado en `127.0.0.1:5008`.

```bash
# Health (no toca la base de datos)
curl -i http://127.0.0.1:5008/health

# Estado de la cola
curl -i http://127.0.0.1:5008/queue/status

# Enviar una posición de prueba
curl -i -X POST http://127.0.0.1:5008/webhook/traccar \
  -H "Content-Type: application/json" \
  -d '{
        "position": {
          "id": 1, "deviceId": 1, "protocol": "teltonika",
          "serverTime": "2026-10-22T15:00:00Z", "deviceTime": "2026-10-22T15:00:00Z",
          "fixTime": "2026-10-22T15:00:00Z", "valid": true,
          "latitude": -12.0464, "longitude": -77.0428, "altitude": 150,
          "speed": 0, "course": 0, "accuracy": 0,
          "attributes": { "sat": 10, "ignition": false }
        },
        "device": {
          "id": 1, "name": "PRUEBA", "uniqueId": "123456789012345", "status": "online",
          "attributes": { "accountID": "CUENTA", "deviceID": "DISPOSITIVO" }
        }
      }'

# Pausar / reanudar los workers (los webhooks se siguen acumulando en SQLite)
curl -i -X POST http://127.0.0.1:5008/queue/pause
curl -i -X POST http://127.0.0.1:5008/queue/resume
```

Si `Webhook:SecretToken` tiene valor, añade `-H "Authorization: Bearer <token>"` al POST del webhook.

## Logs

```bash
docker-compose logs -f --tail=200 traccar-position
docker inspect --format '{{json .State.Health}}' traccar-position
```

Los logs se rotan (json-file, 10 MB × 3 archivos).
