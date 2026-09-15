# AvalonMCP: Контекст проекта и результаты исследований (Handover Document)

> **Назначение документа**: Фиксация всех архитектурных решений, технических открытий, состояния кодовой базы и ссылок на исследованные репозитории для передачи контекста между агентами и разработчиками.

---

## 1. Проблема и цель проекта
* **Проблема**: Официальный XAML Previewer для Avalonia в IDE (JetBrains Rider / VS Code) стал платным в рамках **Avalonia Pro** (пользователь столкнулся с ошибкой: *"Unlock Avalonia: No active subscription seats found"*).
* **Цель**: Разработать **`AvalonMCP`** — открытый MCP-сервер (Model Context Protocol), который:
  1. Позволяет **ИИ-агентам** (Antigravity, Cursor, Claude) полноценно инспектировать UI: получать скриншоты верстки, ошибки XAML и точное дерево элементов с координатами (`Bounds`).
  2. Служит бесплатной альтернативой платному превьюеру для разработчика.

---

## 2. Анализ изученных open-source проектов

1. **`AvaloniaUI/AvaloniaVSCode` (ветка `ARCHIVE`)** & **`SuessLabs/AvaloniaVS-Legacy`**
   * *Что это*: Старые официальные MIT-лицензированные плагины для VS Code и Visual Studio до перевода на Avalonia Pro.
   * *Как устроены*: Работают по принципу **Out-of-Process** через `Avalonia.Designer.HostApp.dll`. Общаются по TCP с BSON-сериализацией через библиотеку `Avalonia.Remote.Protocol`.
   * *Важное открытие*: Пакет `Avalonia.Remote.Protocol` до сих пор открыто публикуется на NuGet (версия `11.2.1`).

2. **`BoTech-Development/BoTech.DesignerForAvalonia`**
   * *Что это*: Визуальный редактор с Drag & Drop.
   * *Как устроен*: Работает **In-Process**. Загружает XAML через `AvaloniaRuntimeXamlLoader` напрямую внутри процесса окна и манипулирует живыми объектами `Control`.
   * *Плюсы/Минусы*: Дает прямой доступ к дереву и координатам элементов, но не изолирован (ошибка в XAML или UserControl крашит все приложение).

3. **`kuiperzone/AvantGarde`**
   * *Что это*: Готовый независимый бесплатный XAML-превьюер (GPLv3).
   * *Назначение*: Рекомендован разработчику для личного использования параллельно с Rider прямо сейчас без необходимости покупки подписки.

4. **`AvaloniaUI/Avalonia` (Основной репозиторий)**
   * **`Avalonia.Diagnostics` (код DevTools по F12)**: Содержит готовые алгоритмы обхода Visual/Logical Tree (`GetVisualChildren`, `GetLogicalChildren`), вычисления абсолютных координат (`TransformedBounds`) и дампа стилей/свойств.
   * **`Avalonia.Headless`**: Официальный headless-рантайм Avalonia. Позволяет рендерить верстку и производить `Measure`/`Arrange` в оперативной памяти (через Skia) без создания реальных окон Windows HWND.
   * **`Avalonia.DesignerSupport`**: Исходный код сервера `Avalonia.Designer.HostApp`.

---

## 3. Критическое техническое открытие по протоколу
* В пакете `Avalonia.Remote.Protocol` **НЕТ** сообщений для передачи Visual Tree на сторону клиента. Протокол передает только:
  * `UpdateXamlMessage` (отправка XAML)
  * `FrameMessage` (растровые пиксели готовой картинки)
  * `PointerEventMessage` / `KeyEventMessage` (ввод)
  * `UpdateXamlResultMessage` (ошибки парсинга)
* **Вывод**: Получить структуру дерева с координатами элементов через официальный Designer Host по сети *нельзя*. Поэтому необходим гибридный подход.

---

## 4. Согласованная целевая архитектура AvalonMCP

Проект строится по **двухуровневой схеме**:

```
                 [ AI Agent (Antigravity / Cursor) ]
                                 │
                   (MCP Protocol: stdio JSON-RPC)
                                 ▼
                     [ AvalonMCP Server (.NET 8) ]
                                 │
         ┌───────────────────────┴───────────────────────┐
         ▼                                               ▼
[ Уровень 1: Headless Fast Inspector ]       [ Уровень 2: Designer Bridge ]
- In-Process в чистой памяти                 - Out-of-Process (изолированный)
- Avalonia.Headless + Avalonia.Diagnostics   - Avalonia.Designer.HostApp.dll
- Вычисляет точные Bounds (x, y, w, h)       - Рендерит сложные проекты с .dll
- Skia-скриншот за ~50 мс                    - Защищен от крашей клиентского кода
```

### Набор MCP-инструментов:
1. `get_ui_tree(xaml)` — возвращает JSON-дерево с типами, именами, классами и координатами элементов (`Bounds`).
2. `render_ui_snapshot(xaml)` — быстрый рендер в Base64 PNG через Headless Skia.
3. `inspect_ui(xaml)` — комбо-инструмент: возвращает и JSON-дерево, и картинку за 1 вызов.
4. `render_project_preview(assemblyPath, xaml)` — рендер через внешний `HostApp` для сложных проектов с кастомными библиотеками.

---

## 5. Текущее состояние кодовой базы

* **Рабочая папка**: `c:\Users\adm\projects\AvalonMCP`
* **Фреймворк**: .NET 8.0 Console Application
* **Установленные пакеты**:
  * `Avalonia` (11.2.1)
  * `Avalonia.Desktop` (11.2.1)
  * `Avalonia.Remote.Protocol` (11.2.1)
  * `System.Resources.Extensions` (10.0.12)
* **Созданные файлы**:
  * [`DesignerManager.cs`](file:///c:/Users/adm/projects/AvalonMCP/DesignerManager.cs) — менеджер запуска `Avalonia.Designer.HostApp.dll` и клиент BSON/TCP протокола.
  * [`McpServer.cs`](file:///c:/Users/adm/projects/AvalonMCP/McpServer.cs) — реализация MCP JSON-RPC сервера через `stdin`/`stdout`.
  * [`Program.cs`](file:///c:/Users/adm/projects/AvalonMCP/Program.cs) — точка входа сервера.
* **Локальный путь к Designer Host в кэше**:
  `C:\Users\adm\.nuget\packages\avalonia\11.2.1\tools\netstandard2.0\designer\Avalonia.Designer.HostApp.dll`
* **Статус сборки**: Проект собирается успешно (`dotnet build`, 0 ошибок).

---

## 6. Следующие шаги для следующего агента / итерации
1. Добавить пакет `Avalonia.Headless` в `AvalonMCP.csproj`.
2. Создать сервис `HeadlessTreeDumper.cs` с логикой из `Avalonia.Diagnostics`:
   * Инициализация Headless-приложения.
   * Загрузка XAML через `AvaloniaRuntimeXamlLoader.Load`.
   * Вызов `Measure(new Size(1024, 768))` и `Arrange(new Rect(0, 0, 1024, 768))`.
   * Рекурсивный сбор дерева контролов в JSON-формат (свойство `Bounds`).
3. Зарегистрировать инструменты `get_ui_tree` и `inspect_ui` в [`McpServer.cs`](file:///c:/Users/adm/projects/AvalonMCP/McpServer.cs).
4. Протестировать на реальном XAML-файле.
