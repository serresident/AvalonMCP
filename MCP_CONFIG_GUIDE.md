# Руководство по подключению AvalonMCP к клиентам (Cursor, Claude, Antigravity)

Сервер **AvalonMCP** работает по стандартному протоколу Model Context Protocol (MCP) через `stdio` транспорт. Ниже приведены готовые конфигурации для популярных клиентов.

---

## 1. Подключение в Cursor IDE

Откройте `Cursor Settings` -> `Features` -> `MCP Servers` -> `Add new MCP server`:
* **Name**: `AvalonMCP`
* **Type**: `command`
* **Command**:
```json
{
  "command": "dotnet",
  "args": [
    "C:\\Users\\adm\\projects\\AvalonMCP\\bin\\Debug\\net8.0\\AvalonMCP.dll"
  ]
}
```

Либо добавьте в файл конфигурации `~/.cursor/mcp.json`:
```json
{
  "mcpServers": {
    "avalon": {
      "command": "dotnet",
      "args": [
        "C:\\Users\\adm\\projects\\AvalonMCP\\bin\\Debug\\net8.0\\AvalonMCP.dll"
      ]
    }
  }
}
```

---

## 2. Подключение в Claude Desktop

Откройте `%APPDATA%\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "avalon": {
      "command": "dotnet",
      "args": [
        "C:\\Users\\adm\\projects\\AvalonMCP\\bin\\Debug\\net8.0\\AvalonMCP.dll"
      ]
    }
  }
}
```

---

## 3. Доступные инструменты (Tools)

После подключения ассистенту станут доступны следующие инструменты:

| Инструмент | Параметры | Описание |
|---|---|---|
| `lint_ui` | `xaml`, `width`, `height`, `theme`, `assemblyPath` | **Самый быстрый инструмент (5-15 мс)**: мгновенно проверяет верстку на схлопывание (0x0), обрезку текста, наложение элементов и выравнивание без генерации картинок. |
| `inspect_ui` | `xaml`, `width`, `height`, `theme`, `assemblyPath`, `annotateErrors` | Возвращает вычисленное Visual Tree (JSON) с диагностикой верстки и аннотированный PNG-скриншот с красными маркерами ошибок. |
| `render_ui_snapshot` | `xaml`, `width`, `height`, `theme`, `assemblyPath`, `annotateErrors` | Рендерит AXAML и возвращает изображение `image/png` в формате MCP с опциональным оверлеем ошибок. |
| `get_ui_tree` | `xaml`, `width`, `height`, `theme`, `assemblyPath` | Возвращает структурированное дерево элементов с `bounds`, `margin`, `classes` и `text`. |
| `discover_project` | `path` | Находит `.sln`, `.csproj` и все `.axaml` файлы в указанной директории. |
| `build_project` | `path`, `timeoutSeconds` | Запускает компиляцию целевого проекта и возвращает структурированный отчет об ошибках. |
| `update_and_render_xaml` | `assemblyPath`, `xaml` | Отправляет XAML в изолированный `Avalonia.Designer.HostApp` через TCP/BSON. |

---

## 4. Пример промпта для агента:

> *"Проверь верстку экрана LoginView.axaml в проекте. Вызови `inspect_ui`, передай XAML и путь к собранной dll, проверь, не вылезают ли кнопки за пределы контейнера, и покажи скриншот в темной теме (`theme: "dark"`)."*
