# План — Инсталлятор для EnglishStudio

**Создано:** 2026-05-29
**Контекст:** `EnglishStudio.App` — WPF `net10.0-windows`, `WinExe`. Зависимости: EF Core + SQLite (`e_sqlite3.dll`),
NAudio, **Whisper.net + Whisper.net.Runtime** (нативные DLL whisper.cpp, x64). Решение собирается, но
способа распространения нет. Личное использование, но удобно иметь ставящийся билд (другой ПК, переустановка).

**Особенности рантайма (влияют на инсталлятор):**
- **БД** `dictionary.db` + IELTS-таблицы создаются при первом запуске в `%AppData%\EnglishStudio\` миграциями — **в инсталлятор не кладём**.
- **Whisper-модели** (`ggml-base.en` ~142 МБ, `ggml-medium.en` ~1.5 ГБ) скачиваются lazy при первом использовании в `%AppData%\EnglishStudio\Models\` — **в инсталлятор не кладём** (иначе +1.6 ГБ).
- **Аудио словаря** (UK/US MP3) качается lazy split-zip — не кладём.
- **Listening MP3** (Cambridge 15-20) — embedded resource в `Modules.Ielts.Listening`, извлекается seed-сервисом в `%AppData%` → попадают в билд автоматически как часть сборки, отдельно класть не надо.
- **Speaking Cambridge .txt** — импортируется `CambridgeSpeakingImportService.ImportIfPossibleAsync()` из внешнего пути. **ПРОВЕРИТЬ** перед релизом: если источник вне репозитория (`Downloads`), на чистой машине Speaking будет пустым → нужно либо embed контента, либо graceful-degradation. Зафиксировать в acceptance.

**Цель:** один `.exe`-инсталлятор, ставящий приложение **без прав администратора** (per-user), с ярлыками и
аптайм-аптдейтами по желанию.

---

## 1. Выбор технологии

| Вариант | Плюсы | Минусы | Решение |
|---|---|---|---|
| **Inno Setup** | Простой Pascal-скрипт, per-user install без admin, маленький бутстрап, легко кастомизировать UI | Внешний инструмент (не NuGet) | **ОСНОВНОЙ** |
| WiX Toolset v4/v5 | MSI, корпоративное развёртывание, GPO | Крутая кривая, XML-многословность, для personal-use избыточно | Альтернатива, описать кратко |
| MSIX | Современный, авто-апдейты | Требует подписи сертификатом, песочница ломает доступ к произвольному `%AppData%` пути и subprocess `claude` | Не подходит (CLI subprocess + произвольный FS) |

**Решение:** Inno Setup как основной путь. WiX — в разделе 5 как опция.

---

## 2. Стратегия публикации .NET

Перед упаковкой — `dotnet publish`. Два варианта рантайма:

| Режим | Команда | Размер | Когда |
|---|---|---|---|
| **Self-contained x64** (рекомендуется) | `dotnet publish src/EnglishStudio.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false` | ~150-200 МБ | пользователю не нужен установленный .NET 10 |
| Framework-dependent | `... --self-contained false` | ~10-20 МБ | если .NET 10 Desktop Runtime гарантированно стоит |

**Брать self-contained** — переустановки/другой ПК без возни с рантаймом.

**Не использовать** `PublishSingleFile=true` и `PublishTrimmed=true`:
- SingleFile + нативные whisper/sqlite DLL → распаковка во временную папку, риск с subprocess и нативными зависимостями.
- Trimming ломает EF Core reflection и Whisper native loading.

После publish проверить, что в выходной папке присутствуют:
- `EnglishStudio.App.exe` + все `*.dll`
- `runtimes/win-x64/native/e_sqlite3.dll`
- `runtimes/win-x64/native/whisper.dll` (+ ggml-* нативные, идут от Whisper.net.Runtime)
- WPF/desktop рантайм (при self-contained)

---

## 3. Inno Setup скрипт

Создать `installer/EnglishStudio.iss`. Инструмент: Inno Setup 6.x (поставить локально, в репозиторий не коммитим сам компилятор).

```ini
#define AppName "EnglishStudio"
#define AppVersion "1.0.0"            ; брать из общего свойства, см. раздел 4
#define AppPublisher "tvorec"
#define PublishDir "..\src\EnglishStudio.App\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppId={{PUT-A-FIXED-GUID-HERE}     ; ОДИН раз сгенерировать GUID и НЕ менять между версиями (для апгрейда)
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest            ; per-user, без UAC
OutputDir=output
OutputBaseFilename=EnglishStudio-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\EnglishStudio.App.exe

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}";       Filename: "{app}\EnglishStudio.App.exe"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\EnglishStudio.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Run]
Filename: "{app}\EnglishStudio.App.exe"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; ВНИМАНИЕ: пользовательские данные (БД, модели, аудио) в %AppData%\EnglishStudio НЕ удаляем при деинсталляции,
; чтобы не потерять прогресс. Если нужна полная очистка — отдельная галочка/скрипт.
```

**Нюансы:**
- `AppId` GUID фиксированный → апгрейд поверх старой версии работает.
- `PrivilegesRequired=lowest` + установка в `{localappdata}\Programs` → без админа.
- `%AppData%\EnglishStudio` (прогресс, модели) **не трогаем** при uninstall.

---

## 4. Версионирование

- Завести `Directory.Build.props` в корне (если ещё нет) с общими `<Version>`, `<AssemblyVersion>`, `<FileVersion>` для App.
- Скрипт сборки (`installer/build.ps1`) читает версию из `Directory.Build.props` → передаёт в Inno как `/DAppVersion=`.
- Вести `CHANGELOG.md` (semver). Bump версии — ручной перед релизом.

`installer/build.ps1` (последовательность):
1. `dotnet publish` (раздел 2, Release, win-x64, self-contained).
2. Проверка наличия нативных DLL в publish (fail-fast если нет `e_sqlite3.dll`/`whisper.dll`).
3. Вызов `ISCC.exe installer\EnglishStudio.iss /DAppVersion=<v>` (путь к ISCC найти/параметризовать).
4. Итог: `installer/output/EnglishStudio-Setup-<v>.exe`.

---

## 5. Альтернатива — WiX (кратко)

Если понадобится MSI (корпоративное развёртывание/GPO):
- WiX v5, проект `installer/wix/EnglishStudio.wxs`.
- `HeatDirectory` (harvest) publish-папки → компоненты.
- `Package` с `Scope="perUser"`.
- Сложнее в поддержке; для personal-use не оправдано. Документировать как «при необходимости».

---

## 6. Фазы

| Фаза | Содержание | Время |
|---|---|---|
| 1 | `Directory.Build.props` с версией; настроить и проверить self-contained publish; чек-лист нативных DLL | 1.5 ч |
| 2 | **Проверить Speaking-контент**: запустить publish-билд на «чистом» профиле/в чистой `%AppData%`, убедиться что все секции работают, выявить отсутствие Cambridge .txt; решить (embed/degradation) | 1 ч |
| 3 | Написать `installer/EnglishStudio.iss` + сгенерировать фиксированный AppId GUID | 2 ч |
| 4 | `installer/build.ps1` (publish → проверка → ISCC) | 1 ч |
| 5 | Тест: установка на чистой машине/VM без .NET и без admin → запуск → миграции создают БД → Whisper качается → все 7 модулей работают | 1.5 ч |
| 6 | Тест апгрейда поверх (тот же AppId, новая версия) + uninstall (данные в %AppData% сохранились) | 1 ч |
| **Итого** | | **~8 ч** |

---

## 7. Acceptance

- `installer/build.ps1` из чистого клона выдаёт `EnglishStudio-Setup-<v>.exe`
- Установка **без прав администратора** в `%LocalAppData%\Programs\EnglishStudio`
- На машине **без установленного .NET 10** приложение запускается (self-contained)
- Первый запуск: миграции создают `%AppData%\EnglishStudio\*.db`, seed отрабатывает, все 7 модулей открываются
- Whisper-модель скачивается при первом обращении к произношению/Speaking (не в инсталляторе)
- **Speaking-секция не пустая** на чистой машине (либо контент embed, либо явное сообщение пользователю)
- Ярлыки в меню Пуск и (опц.) на рабочем столе
- Апгрейд поверх предыдущей версии не дублирует и сохраняет прогресс
- Uninstall удаляет программу, но НЕ удаляет `%AppData%\EnglishStudio`

---

## 8. Риски

| Риск | Митигация |
|---|---|
| Нативные DLL (sqlite/whisper) не попали в publish | Fail-fast чек в build.ps1; не использовать SingleFile/Trim |
| Speaking пуст на чистой машине (Cambridge .txt вне репо) | Фаза 2 — обязательная проверка; embed контента или graceful message |
| Размер инсталлятора большой из-за self-contained | Допустимо для personal-use; при желании — framework-dependent + проверка рантайма в `[Code]` секции Inno |
| Whisper качает 1.5 ГБ при первом Speaking — пользователь не понимает паузу | UI уже показывает прогресс; в README/первом запуске предупредить |
| Антивирус/SmartScreen блокирует неподписанный .exe | Для personal-use ок; при распространении — code signing (отдельная задача) |
| `claude` CLI subprocess отсутствует на целевой машине | AI-оценка уже деградирует с сообщением «Claude CLI не найден»; проверить, что инсталлятор это не ломает |
