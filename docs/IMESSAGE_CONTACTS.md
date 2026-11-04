# Distinguir contactos con iMessage

Estado del 28 de octubre de 2026: restaurado el envío por Phone Link sin exigir una etiqueta de protocolo. El clasificador descrito aquí es un prototipo local opcional; todavía no modifica la agenda ni los iconos de HABITAT.

## Resultado de la investigación

Microsoft confirma que [Phone Link permite mensajes, incluidos iMessage, con iPhone](https://www.microsoft.com/en-us/windows/learning-center/how-to-connect-your-phone-to-a-computer). Conservamos la ruta que ya permitió enviar desde HABITAT. La verificación de destinatario, texto y una única pulsación de Send sigue activa, igual que la conservación del foco y la recuperación tras resultados inciertos.

La versión instalada que inspeccionamos ofrece controles genéricos y nuestro lector devuelve `Unknown`. No equivale a SMS ni demuestra iMessage. El icono azul actual selecciona la ruta; no es una detección individual. Si el servicio no se expone, Phone Link/iPhone decide el transporte y HABITAT no puede garantizar que nunca se use SMS. Una indicación explícita de SMS sigue bloqueada antes de Send.

Tampoco basta leer el campo Bluetooth `sms-gsm`: el proyecto [BlueFerry documenta observaciones de su implementación](https://github.com/erikwb/blueferry/blob/main/PROTOCOL.md#map-receive-behavior) donde SMS e iMessage llegan con el mismo tipo. En sus pruebas de envío iOS elige el transporte. Son observaciones en dispositivos concretos, no una garantía universal de Apple; impiden tratar esa etiqueta como prueba fiable.

## Una clasificación masiva viable

Una copia local de la base de Mensajes contiene el servicio utilizado en cada mensaje. El proyecto [imessage-exporter](https://github.com/ReagentX/imessage-exporter) documenta esa estructura y admite copias de iOS en Windows. El prototipo consulta `message.service`, la fecha y la dirección del remitente en `handle.id`, sin seleccionar cuerpos ni adjuntos.

Propuesta para la agenda:

| Evidencia por dirección exacta | Estado propuesto | Interpretación |
| --- | --- | --- |
| Recibimos un mensaje con servicio iMessage | **iMessage used** | Evidencia histórica con fecha; puede haber cambiado |
| Solo encontramos SMS, MMS o RCS | **Unknown** | No prueba que esa persona carezca de iMessage |
| No hay mensajes o no se puede cruzar la dirección | **Unknown** | Falta información |
| Hay iMessage antiguo y SMS reciente | **iMessage used** | Conservar ambas observaciones; no certificar disponibilidad actual |

Se cruza cada teléfono internacional o correo por separado. No se propaga la evidencia a todos los números de una persona, ni se deduce por marca del teléfono, etiqueta de contacto, grupo, nombre o país supuesto. El prototipo utiliza solo mensajes entrantes: `handle_id` es su remitente; no asigna mensajes salientes o de grupos a participantes deducidos. Los campos de servicio de la tabla `handle` tampoco sirven por sí solos.

Esto permite analizar cientos de direcciones en una ejecución sin enviar mensajes de prueba. No descubre automáticamente la disponibilidad actual de las personas con las que nunca hubo conversaciones. No hemos encontrado una consulta pública de Phone Link para verificar esa disponibilidad en lote; [Microsoft indica que Phone Link no exporta una API de este tipo](https://learn.microsoft.com/en-gb/answers/questions/1301943/phone-link-api).

## Prototipo y comprobación

[scripts/probe-imessage-history.py](../scripts/probe-imessage-history.py) recibe una base SQLite de Mensajes ya exportada, cerrada y descifrada, y una lista JSON de las direcciones que se desea cruzar. No busca copias automáticamente, extrae backups, solicita contraseñas ni lee la base en uso de otra aplicación. Rechaza esquemas sin los metadatos necesarios y bases con transacciones pendientes. Abre la fuente en modo de solo lectura e inmutable; no crea diarios ni cambia la fuente.

```powershell
python scripts/probe-imessage-history.py --database artifacts/private/sms.db --addresses artifacts/private/addresses.json --output artifacts/private/imessage-evidence.json
```

La lista de entrada tiene formato `["+34600111222", "person@example.com"]`. El informe contiene únicamente esas direcciones normalizadas, conteos por servicio, fecha y evidencia histórica; todas conservan `currentCapability: "unknown"`. Tiene datos personales aunque no incluya cuerpos. El archivo de salida debe ser nuevo; no se sobrescribe otro informe. Es una herramienta de investigación, no un importador conectado a HABITAT.

`python tests/imessage-history.checks.py` prueba 400 direcciones sintéticas, ausencia de falsos negativos, historial mixto, dirección exacta, correos, exclusión de mensajes salientes, conservación de la base por hash, ausencia de cuerpos/archivos auxiliares, esquemas incompatibles y conservación de un informe existente. Solo crea datos bajo `artifacts/checks`.

## Límite de la propuesta

Necesita que la copia contenga los mensajes. [Apple explica que las copias de ordenador excluyen datos ya sincronizados y almacenados en iCloud, incluidos Mensajes](https://support.apple.com/en-us/108771). No se ha creado ni leído una copia real del usuario y no se han cambiado ajustes de iCloud. No podemos prometer cobertura de todos sus contactos.

El siguiente paso práctico sería validar el formato y la cobertura con una copia elegida por el usuario y, si aporta suficiente información, integrar una importación opcional de estos metadatos. La interfaz distinguiría evidencia histórica de ausencia de datos. El envío restaurado no depende de esta futura importación.
