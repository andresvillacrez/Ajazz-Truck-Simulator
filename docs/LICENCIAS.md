# Licencias y créditos

## Código del proyecto

El código propio del plugin (`backend.cs`, `backend.swift`, scripts, instaladores
y documentación) se distribuye bajo la **licencia MIT** incluida en
[`../LICENSE`](../LICENSE).

## Telemetría SCS

Los binarios de telemetría no son código propio:

- **Windows:** `scs-telemetry.dll`, basado en
  [RenCloud/scs-sdk-plugin](https://github.com/RenCloud/scs-sdk-plugin).
- **macOS:** `libscs-telemetry.dylib`, compilado desde
  [truckermudgeon/scs-sdk-plugin](https://github.com/truckermudgeon/scs-sdk-plugin)
  v1.12.1.

Ambos proyectos son una bifurcación del SDK de telemetría de SCS Software y se
distribuyen bajo la licencia MIT de Hans (2014). El texto completo está en
[`scs-sdk-plugin-MIT.txt`](scs-sdk-plugin-MIT.txt).

## Iconos

- Los iconos `pk-*.png` se basan en el pack de testigos
  `jp.tsuteto.ets2.sdIconPack`. Ese pack **no se incluye** en el repositorio; en
  este proyecto solo se usan las imágenes ya incorporadas al plugin.
- El icono del paquete usa el logotipo de American Truck Simulator, propiedad
  de SCS Software. Se usa únicamente para identificar el juego en el plugin.
- American Truck Simulator y Euro Truck Simulator 2 son marcas de SCS Software.
  Este proyecto no está afiliado ni patrocinado por SCS Software.

## Software de terceros

StreamDock y el software de Ajazz son productos de sus respectivos
propietarios. El plugin usa el protocolo WebSocket del SDK de StreamDock, pero
no incluye ni redistribuye su software.
