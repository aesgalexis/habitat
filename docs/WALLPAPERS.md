# Fondos de HABITAT

> **Referencia pendiente de reconciliar · 24 de octubre de 2026.** No se encontraron la biblioteca de fondos ni los controles de importación descritos en esta copia. El contenido conservado debajo describe una referencia anterior, incluidas sus versiones, etiquetas y pruebas; no acredita funciones disponibles, recursos incluidos ni pruebas ejecutables aquí. Puede pertenecer a otra rama. Consulta [PROJECT_STATUS.md](PROJECT_STATUS.md) antes de utilizarlo. Las etiquetas bilingües conservadas tampoco sustituyen la [regla de interfaz en inglés](LOCALIZATION.md).

En **Preferencias… → Apariencia → Fondo de pantalla** (en inglés,
**Preferences… → Appearance → Wallpaper**) se selecciona el fondo global.
Las instalaciones nuevas mantienen el fondo transparente, **Híbrido al 15 %**.
Las preferencias existentes conservan su tema y opacidad.

- **Original · transparente** utiliza el escritorio visible a través de HABITAT.
- **Bruma**, **Atardecer** y **Bosque** son tres fondos de color integrados.
- **Grafito**, **Dunas**, **Marea**, **Olivo** y **Órbita** son cinco ilustraciones
  incluidas con la aplicación, disponibles sin conexión. Grafito es un paisaje a lápiz;
  Órbita está pensado para tema oscuro y los demás para claro. Híbrido elige el contraste.
  Admiten encuadre y atenuación, y no ocupan plazas de la biblioteca personal.
  Los [archivos y prompts de la colección](WALLPAPER_ART.md) documentan su creación.
- **Añadir imágenes…** admite varios JPG/PNG de una vez, hasta 40 imágenes guardadas.
  Se conserva el nombre del archivo en su idioma original.
- **Rellenar** recorta sin deformar, centrando las fotos propias y conservando el
  motivo de las ilustraciones en ventanas estrechas. **Ajustar** muestra la imagen completa
  sobre una base gris neutra. **Atenuación** aplica una capa clara u oscura según el tema.
- **Restaurar original · Híbrido, 15 %** restaura el aspecto inicial y conserva la biblioteca.
- **Quitar imagen** retira la imagen seleccionada de la biblioteca al guardar.
  El archivo original del usuario nunca se modifica.

La previsualización es inmediata; los ajustes se guardan al cerrar Preferencias.
Cancelar el selector conserva lo anterior. Los errores de importación se muestran
dentro de Preferencias. Si falta un fondo al cargar, se usa la transparencia y se
informa del problema. Un fallo al guardar restaura las preferencias anteriores.

Las imágenes se procesan localmente, sin subirlas a ningún servicio. Se validan
mediante el decodificador de Windows (JPG/PNG, hasta 25 MB y 60 megapíxeles),
se respeta la orientación EXIF y se genera una copia PNG con lado máximo de 3200
píxeles en `AppDataPaths.WallpapersDirectory`. Los archivos se identifican mediante
GUID, nunca mediante rutas proporcionadas por el JSON de preferencias.
La selección, encuadre, atenuación, nombres y luminosidad se guardan en
`Preferences.Wallpaper`. Los archivos retirados solo se eliminan después del
guardado correcto; las importaciones de una edición fallida se descartan.

`WallpaperSurface` coloca una capa decorativa detrás del contenido y conserva la
estructura inicial de los destinos. No participa en foco ni eventos de entrada.
`WallpaperService` importa las imágenes fuera del hilo de interfaz y carga
miniaturas/vistas con decodificación limitada. Los cambios rápidos de selección
descartan las cargas atrasadas. Híbrido utiliza la luminosidad mediana de la imagen
importada; en modo transparente conserva el detector de escritorio existente.
La atenuación ayuda con imágenes de luminosidad irregular, y se puede elegir
manualmente Día o Noche.

Pruebas: `WallpaperChecks` cubre migración, persistencia y normalización.
`--wallpaper-ui-only` ejecuta comprobaciones nativas de importación, recuperación,
cancelación, controles en ambos idiomas y restauración con datos sintéticos
aislados bajo `artifacts`. `--all-ui-checks` también las incluye.

Nota técnica: Windows escala antes de aplicar la orientación EXIF, por lo que
las dimensiones de `BitmapTransform` usan el espacio de la imagen original:
[documentación de BitmapTransform](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.bitmaptransform).
