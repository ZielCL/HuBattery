# HuBattery

[![Descargar la beta para Windows](https://img.shields.io/badge/Descargar-beta-2563EB?style=flat-square)](https://github.com/ZielCL/HuBattery/releases/download/v0.4.1-beta.2/HuBattery.exe)
[![Windows 10 y 11](https://img.shields.io/badge/Windows-10%20y%2011-0F766E?style=flat-square)](#uso)
[![Estado de la compilación](https://github.com/ZielCL/HuBattery/actions/workflows/portable.yml/badge.svg?branch=main)](https://github.com/ZielCL/HuBattery/actions/workflows/portable.yml)
[![Estrellas en GitHub](https://img.shields.io/github/stars/ZielCL/HuBattery?style=flat-square&label=Estrellas&logo=github&color=CA8A04)](https://github.com/ZielCL/HuBattery/stargazers)
[![Reportar un dispositivo](https://img.shields.io/badge/Reportar-dispositivo-9333EA?style=flat-square)](#reportar-un-dispositivo)

HuBattery muestra la batería del mouse y los audífonos junto al reloj de Windows. Funciona como un ejecutable portátil y consulta el nivel cada 30 segundos.

![Vista previa de HuBattery con un mouse al 78 % y audífonos al 64 % junto al reloj de Windows](assets/hubattery-barra-de-tareas.png)

*Vista previa del indicador con porcentajes de ejemplo.*

[Ver versiones](https://github.com/ZielCL/HuBattery/releases) · [Ver mouses](#compatibilidad) · [Ver audífonos](#audífonos)

## Uso

1. Conecta el mouse o los audífonos por el receptor, cable o Bluetooth, según el modelo.
2. Abre `HuBattery.exe` con doble clic. Enciende los audífonos o mueve el mouse si estaba en reposo.
3. Haz clic izquierdo en el indicador para actualizar. Usa el menú de clic derecho para elegir un mouse o unos audífonos si hay varias lecturas del mismo tipo.

Si ya tienes HuBattery abierto, sal desde su menú antes de ejecutar otra versión.

El indicador muestra un mouse y unos audífonos a la vez junto a la bandeja en la barra de tareas principal horizontal. En la bandeja compacta, abre el menú para ver ambas lecturas. Windows puede agrupar el icono en la sección de iconos ocultos.

## Actualizaciones

Desde **v0.4.1-beta.1**, HuBattery consulta GitHub una vez cuando se abre tras un nuevo arranque de Windows. La primera apertura registra el arranque actual. Activa **Iniciar con Windows** desde el menú del indicador para que la comprobación se haga al iniciar sesión.

- **Al arrancar Windows.** Si HuBattery encuentra una versión nueva, añade **Instalar actualización** al menú de clic derecho del indicador sin mostrar una ventana.
- **En cualquier momento.** Haz clic derecho en el indicador y elige **Buscar actualizaciones**.

Si confirmas la instalación, HuBattery descarga y verifica el ejecutable, reemplaza el archivo en su ubicación actual y vuelve a abrirse. Las versiones anteriores a v0.4.1-beta.1 necesitan descargar el ejecutable actualizado una vez.

## Compatibilidad

**Probado con hardware en HuBattery**. Razer DeathAdder V3 HyperSpeed por receptor USB `1532:00C5`. El resto de las entradas están disponibles en beta y quedan pendientes de pruebas físicas en HuBattery.

| Marca | Modelos o receptores incluidos | Conexión |
| --- | --- | --- |
| Razer | DeathAdder V3 HyperSpeed y otros modelos del [catálogo Razer](src/RazerBattery.cs) | Receptor o cable USB según el modelo |
| Logitech | G305 y otros mouses o trackballs que reportan porcentaje mediante los [receptores Bolt, Unifying, LIGHTSPEED y Nano del catálogo](src/LogitechBattery.cs) | Receptor USB HID++ |
| SteelSeries | Aerox 3, 5 y 9 Wireless y Rival 3 Wireless, incluida la Gen 2 | Receptor USB de 2,4 GHz |
| HyperX | Pulsefire Haste 2 Wireless | Receptor USB de 2,4 GHz |
| Glorious | Model O Wireless | Receptor USB de 2,4 GHz |
| LAMZU | Maya X 8K | Receptor USB de 2,4 GHz |
| Corsair | Dark Core RGB Pro SE y Sabre V2 Pro Ultralight | Receptor USB de 2,4 GHz |
| Hitscan | Hyperlight | Receptor USB de 2,4 GHz |
| Pulsar | X2 V2 Mini y X2 CrazyLight Mini | Receptor USB de 2,4 GHz |
| ATK | X1 Pro | Receptor USB de 2,4 GHz |
| VXE | R1 Pro Max | Receptor USB de 2,4 GHz |
| VGN | Dragonfly 4K | Receptor USB de 2,4 GHz |
| Scyrox | V8 | Receptores `3554:F5F7` y `3554:F5F4`, y cable `3554:F5F6` |
| Zaopin | Z2 Mini | Receptor USB de 2,4 GHz y cable USB |
| Dareu | A950 Air y A950 Wing | Receptor USB de 2,4 GHz y cable USB |
| G-Wolves | Lycan, Fenrir Pro y Fenrir Asym 8K | Receiver RS y receptores y cables USB incluidos en el catálogo |

La compatibilidad depende del modelo y de los [IDs USB de su conexión](src/VendorMouseBattery.cs). Aerox puede reportar en pasos de 5 % y Rival requiere una respuesta que incluya el código de la consulta. Para HID++ 1.0, HuBattery consulta mouses y trackballs identificados por un receptor Unifying o Nano cuando el dispositivo devuelve un porcentaje.

HuBattery también puede leer mouses de otras marcas por **Bluetooth** si Windows publica el porcentaje, o mediante **HID estándar** si el dispositivo declara el campo Battery Strength en un reporte de características. Ambas vías siguen pendientes de pruebas con hardware en HuBattery. Si el indicador muestra `—`, todavía no se ha recibido un porcentaje válido.

## Audífonos

Esta primera beta de audio está pendiente de pruebas con unidades físicas en HuBattery.

| Marca o conexión | Modelos o condición de lectura |
| --- | --- |
| Bluetooth | Audífonos cuyo nivel de batería publica Windows. También pueden aparecer otros dispositivos de audio Bluetooth, como parlantes portátiles. |
| SteelSeries | Arctis Nova 5, 5X, 7, 7 Gen 2, 7X y 7P por los [receptores USB incluidos](src/UsbHeadsetBattery.cs). Algunas revisiones Nova 7 reportan niveles en pasos de 25 %. |
| HyperX | Cloud Alpha Wireless `03F0:098D`, Cloud Alpha 2 `03F0:08BE` y Cloud III Wireless `03F0:05B7` o `03F0:0C9D`, por receptor o estación USB. |
| Corsair | Void Elite Wireless `1B1C:0A55`, por su receptor USB. |

## Cómo funciona

- [PeripheralBattery.cs](src/PeripheralBattery.cs) reúne las lecturas y elimina duplicados del mismo tipo dentro de un contenedor de Windows. El menú conserva la selección de cada tipo durante la sesión.
- [DeviceNodes.cs](src/DeviceNodes.cs) identifica los dispositivos y lee el nivel que publica Windows para Bluetooth. HuBattery distingue el audio por el servicio Bluetooth o por el contenedor del dispositivo de audio. [Guía de batería Bluetooth](https://learn.microsoft.com/windows-hardware/design/accessory-guidelines/bluetooth-accessory-guidelines/bluetooth-accessory-guidelines-battery-profiles-services) e [IDs de contenedores de audio](https://learn.microsoft.com/windows-hardware/drivers/audio/audio-endpoint-container-id).
- [HidBattery.cs](src/HidBattery.cs) consulta solo el campo HID Battery Strength cuando el propio descriptor del mouse lo declara. [API de valores HID de Microsoft](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getspecificvaluecaps).
- [RazerBattery.cs](src/RazerBattery.cs) envía únicamente la consulta de nivel de batería a los identificadores USB del catálogo. [Referencia de protocolo de OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c) y [referencia de identificadores USB](https://github.com/xzeldon/razer-battery-report#supported-mice).
- [LogitechBattery.cs](src/LogitechBattery.cs) identifica cada mouse o trackball en las ranuras del receptor y consulta el porcentaje con Unified Battery `0x1004`, Battery Status `0x1000` o el registro de carga HID++ 1.0 `0x0D`. [Referencias de Solaar](https://github.com/pwr-Solaar/Solaar/tree/master/lib/logitech_receiver) y [del controlador HID++ de Linux](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c).
- [VendorMouseBattery.cs](src/VendorMouseBattery.cs) verifica el VID/PID y la colección HID de cada receptor antes de consultar su batería. Valida la respuesta, incluida la suma de comprobación cuando el protocolo la define. [HaloBattery](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md), [alloyctl](https://alloy.szymon-wilczek.me/protocol/steelseries-aerox3-wireless.html) y [ngenuity-linux-cli](https://github.com/PMarinov1994/ngenuity-linux-cli) documentan las consultas por familia.
- El soporte de Scyrox, Zaopin, Dareu y G-Wolves se apoya en el [lector Compx/Nordic](https://github.com/Fan4Metal/mouse_tray/blob/master/mouse_tray/drivers/chipset/nordic52.py), los [IDs de Scyrox S-center](https://github.com/vladkovalch/scyrox-v8-config/blob/main/scyrox_v8_config.py) y la [documentación de las variantes de G-Wolves](https://github.com/maximelonguepe/gwolves-battery-overlay/blob/main/docs/PROTOCOL.md).
- [UsbHeadsetBattery.cs](src/UsbHeadsetBattery.cs) limita las consultas de batería a los IDs y colecciones HID de los receptores catalogados. [HeadsetControl](https://github.com/Sapd/HeadsetControl), [HaloBattery](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md) y [alphabat](https://github.com/csmith/alphabat) documentan las lecturas de referencia.

## Compilación

En Windows 10 de 64 bits, versión 1607 o posterior, o Windows 11, ejecuta `build.cmd`. El resultado queda en `dist\HuBattery.exe`. La compilación usa el compilador de .NET Framework incluido en Windows.

Ejecuta `test.cmd` para probar los protocolos con respuestas simuladas y comprobar el transporte asíncrono de Windows. Puedes compilar en otra carpeta con `build.cmd dist\beta` mientras la versión habitual sigue abierta.

## Reportar un dispositivo

Abre un [issue](https://github.com/ZielCL/HuBattery/issues/new) con marca y modelo, tipo de conexión, versión de Windows y el resultado que viste. Para receptores USB, incluye `VID_xxxx&PID_xxxx` de **Administrador de dispositivos → Propiedades → Detalles → Id. de hardware**. Para Bluetooth, indica si Configuración de Windows ya muestra el porcentaje.

## Avance

- **v0.4.1-beta.2**. Añade la comprobación de versiones al arrancar Windows y la búsqueda e instalación desde el menú del indicador. Mejora la limpieza de archivos temporales tras instalar. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.4.1-beta.2).
- **v0.4.0-beta.1**. Añade audífonos Bluetooth y receptores USB seleccionados de SteelSeries, HyperX y Corsair. El indicador puede mostrar el mouse y los audífonos simultáneamente. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.4.0-beta.1).
- **v0.3.2-beta.1**. Añade Zaopin Z2 Mini, Dareu A950 Air y A950 Wing, y receptores y cables de G-Wolves Lycan y Fenrir. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.3.2-beta.1).
- **v0.3.1-beta.1**. Añade el Scyrox V8 por receptor y cable, e incorpora el icono de HuBattery al ejecutable. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.3.1-beta.1).
- **v0.3.0-beta.1**. Añade Logitech LIGHTSPEED y Nano, lectura HID++ 1.0 cuando el dispositivo reporta un porcentaje y receptores específicos de SteelSeries, HyperX, Corsair, Glorious, LAMZU, Hitscan, Pulsar, ATK/VXE y VGN. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.3.0-beta.1).
- **v0.2.0-beta.1**. Lectura experimental del porcentaje de mouses y trackballs Logitech HID++ 2.0 por Logi Bolt y Unifying.
- **v0.1.0**. Indicador portátil de un solo mouse, selección cuando hay varias lecturas y detección por Bluetooth de Windows, HID estándar y catálogo USB Razer.
