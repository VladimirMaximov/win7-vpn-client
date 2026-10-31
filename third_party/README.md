# Сторонние компоненты

Сборка скачивает только фиксированные версии. URL и SHA-256 архивов и бинарников находятся в [`dependencies.json`](dependencies.json). Архивы и извлечённые файлы хранятся в `.deps/` и не коммитятся.

| Компонент | Версия / вариант | Лицензия | Исходники |
|---|---|---|---|
| Mihomo | 1.19.33, Windows amd64-v1 | [GPL-3.0](licenses/LICENSE-mihomo.txt) | [Тег v1.19.33](https://github.com/MetaCubeX/mihomo/tree/v1.19.33) |
| Xray | 26.10.29, win7-64 | [MPL-2.0](licenses/LICENSE-Xray.txt) | [Тег v26.10.29](https://github.com/XTLS/Xray-core/tree/v26.10.29) |
| YamlDotNet | 3.9.0, net35 | [MIT](licenses/LICENSE-YamlDotNet.txt) | [Тег v3.9.0](https://github.com/aaubry/YamlDotNet/tree/v3.9.0) |

Для кросс-сборки отдельно загружается `Microsoft.NETFramework.ReferenceAssemblies.net35` 1.0.3 с NuGet. Это reference assemblies для компилятора, не среда выполнения и не часть распространяемого EXE.

Движки не модифицируются и запускаются отдельными процессами. При обновлении компонента необходимо сверить поддержку Windows 7, обновить манифест и ожидаемые хеши `Assets.cs`, пересобрать и проверить приложение. Копии лицензий включаются в EXE; исходники движков доступны по закреплённым тегам выше. При публикации сборок необходимо соблюдать условия соответствующих лицензий.
