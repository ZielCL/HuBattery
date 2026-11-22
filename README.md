# HuBattery

HuBattery muestra un símbolo de mouse y el porcentaje de batería junto al reloj de Windows. Funciona como un ejecutable portátil y consulta el nivel cada 30 segundos.

## Uso

1. Descarga `HuBattery.exe` desde [Releases](https://github.com/ZielCL/HuBattery/releases).
2. Abre el ejecutable y mueve el mouse si estaba en reposo.
3. Haz clic izquierdo en el indicador para actualizar. Abre el menú con clic derecho para elegir un mouse si hay varios con lectura, iniciar con Windows o salir.

Si ya tienes HuBattery abierto, sal desde su menú antes de ejecutar otra versión.

El indicador se coloca junto a la bandeja en la barra de tareas principal horizontal. Si la barra no permite ese formato, aparece un icono normal en la zona de notificación, que Windows puede dejar bajo la flecha de iconos ocultos.

## Compatibilidad

| Conexión | Lectura de batería | Estado |
| --- | --- | --- |
| Receptor USB Razer DeathAdder V3 HyperSpeed `1532:00C5` | Consulta HID de Razer | Verificado en Windows con hardware |
| Otros Razer USB del [catálogo](src/RazerBattery.cs) | Consulta HID por modelo | Implementado según referencias, pendiente de pruebas en cada modelo |
| Logitech Logi Bolt, Unifying, LIGHTSPEED y Nano | HID++ 2.0 en mouses y trackballs. HID++ 1.0 por Unifying y Nano cuando el receptor identifica el dispositivo y este ofrece una carga numérica | Beta. [IDs de receptores](src/LogitechBattery.cs). Pendiente de pruebas con esos receptores físicos |
| SteelSeries Aerox 3, 5 y 9 Wireless y Rival 3 Wireless (incluye Gen 2) | Receptores de 2,4 GHz del catálogo. Aerox puede informar en pasos de 5 %. Rival requiere una respuesta con el código de la consulta | Beta. Pendiente de pruebas en HuBattery con hardware |
| HyperX Pulsefire Haste 2 Wireless, Glorious Model O Wireless y LAMZU Maya X 8K | Consulta HID del receptor de 2,4 GHz | Beta. Pendiente de pruebas en HuBattery con hardware |
| Corsair Dark Core RGB Pro SE y Sabre V2 Pro Ultralight, Hitscan Hyperlight, Pulsar X2 V2 Mini y X2 CrazyLight Mini, ATK X1 Pro, VXE R1 Pro Max y VGN Dragonfly 4K | Lectura por receptores de 2,4 GHz identificados en el [catálogo](src/VendorMouseBattery.cs) | Beta. Pendiente de pruebas en HuBattery con hardware |
| Scyrox V8 | Receptor de 2,4 GHz `3554:F5F7` o `3554:F5F4` y cable USB `3554:F5F6`, por Compx | Beta. Pendiente de pruebas en HuBattery con hardware. [Protocolo de batería](https://github.com/Fan4Metal/mouse_tray/blob/master/mouse_tray/drivers/chipset/nordic52.py) e [IDs de S-center](https://github.com/vladkovalch/scyrox-v8-config/blob/main/scyrox_v8_config.py) |
| Mouse Bluetooth | Lee el porcentaje si Windows lo publica para el dispositivo | Pendiente de pruebas con hardware Bluetooth |
| Mouse con campo HID estándar Battery Strength | Lee el campo si existe en un reporte de características de la colección del mouse | Pendiente de pruebas con hardware compatible |

Un receptor de 2,4 GHz puede usar un protocolo propio aunque Windows reconozca el mouse. Si la barra muestra `—`, HuBattery todavía no recibió un porcentaje válido. En dispositivos Logitech que solo informan estados como «bajo» o «alto», no se inventa un porcentaje. Los equipos que usan otros receptores, interfaces o protocolos requieren una prueba y, en algunos casos, un lector adicional.

## Cómo funciona

- [MouseBattery.cs](src/MouseBattery.cs) reúne las lecturas, elimina duplicados del mismo contenedor de Windows y conserva la selección actual durante la sesión.
- [DeviceNodes.cs](src/DeviceNodes.cs) identifica los dispositivos y busca el nivel que publica Windows para ratones Bluetooth. [Guía de batería Bluetooth de Microsoft](https://learn.microsoft.com/windows-hardware/design/accessory-guidelines/bluetooth-accessory-guidelines/bluetooth-accessory-guidelines-battery-profiles-services).
- [HidBattery.cs](src/HidBattery.cs) consulta solo el campo HID Battery Strength cuando el propio descriptor del mouse lo declara. [API de valores HID de Microsoft](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getspecificvaluecaps).
- [RazerBattery.cs](src/RazerBattery.cs) envía únicamente la consulta de nivel de batería a los identificadores USB del catálogo. [Referencia de protocolo de OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c) y [referencia de identificadores USB](https://github.com/xzeldon/razer-battery-report#supported-mice).
- [LogitechBattery.cs](src/LogitechBattery.cs) identifica cada mouse o trackball en las ranuras del receptor y consulta el porcentaje con Unified Battery `0x1004`, Battery Status `0x1000` o el registro de carga HID++ 1.0 `0x0D`. [Referencias de Solaar](https://github.com/pwr-Solaar/Solaar/tree/master/lib/logitech_receiver) y [del controlador HID++ de Linux](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c).
- [VendorMouseBattery.cs](src/VendorMouseBattery.cs) verifica el VID/PID y la colección HID de cada receptor antes de consultar su batería. Valida la respuesta, incluida la suma de comprobación cuando el protocolo la define. [HaloBattery](https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md), [alloyctl](https://alloy.szymon-wilczek.me/protocol/steelseries-aerox3-wireless.html) y [ngenuity-linux-cli](https://github.com/PMarinov1994/ngenuity-linux-cli) documentan las consultas por familia.

## Compilación

En Windows 10 de 64 bits, versión 1607 o posterior, o Windows 11, ejecuta `build.cmd`. El resultado queda en `dist\HuBattery.exe`. La compilación usa el compilador de .NET Framework incluido en Windows.

Ejecuta `test.cmd` para probar los protocolos con respuestas simuladas y comprobar el transporte asíncrono de Windows. Puedes compilar en otra carpeta con `build.cmd dist\beta` mientras la versión habitual sigue abierta.

## Reportar un mouse

Abre un [issue](https://github.com/ZielCL/HuBattery/issues/new) con marca y modelo, tipo de conexión, versión de Windows y el resultado que viste. Para receptores USB, incluye `VID_xxxx&PID_xxxx` de **Administrador de dispositivos → Propiedades → Detalles → Id. de hardware**. Para Bluetooth, indica si Configuración de Windows ya muestra el porcentaje.

## Avance

- **v0.3.1-beta.1**. Añade el Scyrox V8 por receptor y cable, e incorpora el icono de HuBattery al ejecutable. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.3.1-beta.1).
- **v0.3.0-beta.1**. Añade Logitech LIGHTSPEED y Nano, lectura HID++ 1.0 cuando el dispositivo reporta un porcentaje y receptores específicos de SteelSeries, HyperX, Corsair, Glorious, LAMZU, Hitscan, Pulsar, ATK/VXE y VGN. [Descarga de la beta](https://github.com/ZielCL/HuBattery/releases/tag/v0.3.0-beta.1).
- **v0.2.0-beta.1**. Lectura experimental del porcentaje de mouses y trackballs Logitech HID++ 2.0 por Logi Bolt y Unifying.
- **v0.1.0**. Indicador portátil de un solo mouse, selección cuando hay varias lecturas y detección por Bluetooth de Windows, HID estándar y catálogo USB Razer.
