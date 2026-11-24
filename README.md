# T-Retro

T-Retro является внутренним веб-приложением для ретроспектив IT-команд. Сотрудник создаёт доску и приглашает команду, после чего участники собирают карточки, группируют их, голосуют за темы и фиксируют итоги обсуждения. Результатом встречи являются решения, комментарии, задачи и PDF по постоянному адресу.

Интерфейс и отчёт используют русский язык. Реализованы этапы с M0 по M9: основа приложения, встречи, приглашения, SignalR, роль фасилитатора, карточки, приватность, готовность, группировка, голосование, обсуждение, таймер, mock-экспорт задач в Jira, завершение встречи и PDF. Следующим этапом является M10 с итоговой проверкой локального MVP. Внутренний ИИ и Jira пока представлены локальными заглушками без сетевых вызовов.

## Документы

- [Handoff](docs/handoff.md) содержит актуальный контекст для новой сессии, незавершённую работу, следующие действия и известные расхождения документов.
- [Техническое задание](docs/Retro_Spec.md) содержит полные функциональные требования, контракты, архитектуру и критерии приёмки.
- [План реализации](docs/tasks.md) содержит задачи локального MVP и итоговой поставки.
- [AGENTS.md](AGENTS.md) содержит обязательные правила разработки и сохранения контекста по командам `сохрани сессию` и `bfe`.

Если старый текст спецификации противоречит решениям в [AGENTS.md](AGENTS.md#приоритет-решений), нужно применять зафиксированные там решения и обновить документы согласованно.

## Локальный MVP

MVP должен позволить нескольким тестовым сотрудникам пройти полную встречу в двух независимых браузерных контекстах. Локальное хранение использует SQLite в файле. PostgreSQL остаётся целевой СУБД итоговой поставки, а его реализация и проверки находятся в [этапе P1](docs/tasks.md#этап-p1-postgresql-для-итоговой-поставки).

В MVP не входят production-адаптеры TWork, Jira, внутреннего ИИ и Sage. Kubernetes и нагрузочная приёмка отложены до этапов итоговой поставки. Сборка и тесты уже запускаются в GitHub Actions.

Таймер хранится только в памяти процесса. БД не содержит его состояние, `runId`, срок окончания или дедупликацию команд. Решения и комментарии являются разными сущностями. Сотрудник, который вступил во время `Vote`, получает полный текущий бюджет голосов.

## Архитектура

Backend использует .NET 10, C# 14, ASP.NET Core и Dapper. Локальная реализация хранения использует `Microsoft.Data.Sqlite`. Frontend использует Angular и Taiga UI 4.x.

| Проект | Назначение |
| --- | --- |
| `src/Domain` | Правила и доменные модели. |
| `src/Application` | Сценарии, права и порты хранения и интеграций. |
| `src/Infrastructure` | SQL, миграции, Dapper, PDF и адаптеры. |
| `src/Api` | HTTP DTO, OpenAPI, SignalR, личность и composition root. |
| `web` | Angular SPA, экраны и клиенты HTTP и SignalR. |
| `tests` | Domain, Application, Infrastructure и API-тесты. |

Решение находится в `TRetro.slnx`. Зависимости между проектами соответствуют [§10 спецификации](docs/Retro_Spec.md#10-архитектура-и-границы-данных).

## Завершение и PDF

Миграция `0009_completion.sql` добавляет `meeting_totals`, `topic_totals` и `reports`. Завершение выполняется в одной SQLite-транзакции: сохраняет итоги и PDF, удаляет приватные связи согласно независимым флагам, очищает готовность, закрывает приглашение и переводит встречу в `Closed`. Ошибка генерации откатывает всю операцию. Повтор исходной команды возвращает тот же сохранённый отчёт.

`GET /api/meetings/{id}/report.pdf` при каждом запросе проверяет личность и действующее членство. Все допущенные участники получают одинаковые байты с `application/pdf` и `Cache-Control: private, no-store`. После завершения состояние читается из сохранённых итогов; готовность и личный бюджет не выдаются. Экспорт задачи в mock Jira остаётся доступным в `Closed` и не перегенерирует PDF.

PDF генерируется локально через PDFsharp без сетевых вызовов. Шрифты DejaVu Sans Regular и Bold с кириллицей встроены в приложение и PDF. Длинные тексты переносятся, страницы нумеруются. Лицензии находятся в [PDFsharp-LICENSE.txt](src/Infrastructure/Reporting/PDFsharp-LICENSE.txt) и [DejaVu-LICENSE.txt](src/Infrastructure/Reporting/Fonts/DejaVu-LICENSE.txt).

## Зафиксированные версии

- .NET SDK 10.0.404, C# 14, `TargetFramework=net10.0`.
- Node 24.15.0 и npm 12.1.0.
- Angular, Angular CLI и CDK 22.3.0.
- Taiga UI core, CDK, kit и i18n 4.109.0, event-plugins 4.8.0.
- TypeScript 6.0.2, Vitest 5.1.1, Playwright 1.65.1, SignalR client 11.0.0 и Less 4.2.0.
- Dapper 2.1.100 и Microsoft.Data.Sqlite 10.0.15.
- PDFsharp 6.2.4 с MIT-лицензией и System.Security.Cryptography.Pkcs 10.0.15. Версия PDFsharp зафиксирована: более новая ветка 6.5 перешла на AGPL/коммерческую лицензию.
- DejaVu Sans 2.37 и PdfPig 0.1.16 для проверки текста PDF в тестах.

Версии Node записаны в `.nvmrc`, версия SDK в `global.json`, а frontend-зависимости зафиксированы в `web/package.json` и `web/package-lock.json`. Установка и сборка проверены без `force` и `legacy-peer-deps`.

## Подготовка инструментов

Скрипты используют системную установку .NET SDK из `/usr/local/share/dotnet` или уже настроенный `DOTNET_ROOT`. Для ручных команд при необходимости задайте переменные окружения:

```bash
export DOTNET_ROOT="/usr/local/share/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$PWD/.tools/nuget/packages"
```

Версия SDK закреплена в `global.json`. Локальная копия SDK внутри проекта больше не используется.

Docker на текущей машине отсутствует. Dockerfile и docker-compose можно подготовить на этапе P3, но локальные проверки не должны зависеть от Docker.

## Скрипты macOS и Ubuntu

Из корня репозитория запустите:

```bash
./tools/build.sh
./tools/run.sh
```

`tools/build.sh` поддерживает macOS и Ubuntu Linux. Он проверяет ОС и мажорные версии .NET SDK, Node.js и npm из `global.json`, `.nvmrc` и `web/package.json`. Затем выполняет restore, чистую сборку и тесты backend, `npm ci`, production build, unit-тесты frontend и все E2E. Playwright Chromium устанавливается в `.tools/playwright`; на Ubuntu системные зависимости устанавливаются через `playwright install --with-deps chromium`. E2E используют отдельную SQLite в `.tools/e2e`, не затрагивая рабочую базу. Скрипт совместим с системным Bash 3.2 и не требует Docker.

`tools/run.sh` требует предварительного успешного `tools/build.sh`, применяет миграции и одновременно запускает API на `http://127.0.0.1:5166` и Angular на `http://127.0.0.1:4200`. Нажатие Ctrl+C останавливает оба процесса. Рабочая база остаётся в `src/Api/data/t-retro.db`.

В Development SignalR подключается напрямую к API на порт 5166. CORS с credentials разрешён только для `localhost:4200` и `127.0.0.1:4200`; Vite проксирует HTTP `/api`, `/health` и `/openapi`, но не выступает WebSocket-прокси. При закрытии страницы приложение останавливает SignalR через `pagehide`. Production использует same-origin без dev-CORS.

Тот же `tools/build.sh` запускается в GitHub Actions из [.github/workflows/ci.yml](.github/workflows/ci.yml) на `ubuntu-latest` x86_64 для каждого push и pull request. Workflow использует `actions/setup-dotnet`, `actions/setup-node` и отдельно устанавливает npm 12.1.0. Секреты и внешняя БД для текущих SQLite-проверок не нужны. [Успешный запуск на Ubuntu](https://github.com/yafarkin/t-retro/actions/runs/45240085578) подтвердил все шаги сборки и тестов для `0d7d324`.

## Ручной запуск

Выполните миграции из корня репозитория:

```bash
dotnet restore TRetro.slnx
dotnet run --project src/Api --launch-profile http -- --migrate
```

Команда создаёт `src/Api/data/t-retro.db`. Повторный запуск миграций безопасен.

Запустите API:

```bash
dotnet run --project src/Api --launch-profile http
```

API доступен на `http://localhost:5166`. OpenAPI находится на `http://localhost:5166/openapi/v1.json`.

В другом терминале запустите Angular:

```bash
cd web
npm ci
npm start
```

Откройте `http://localhost:4200`. Для локального входа можно использовать `employee-a` и `Сотрудник А`. Во втором независимом браузерном профиле нужно использовать другой subject, например `employee-b`. Angular проксирует `/api`, `/health` и `/openapi` на порт 5166.

Проверки процесса доступны без входа:

- `GET /health/live` проверяет процесс без обращения к БД.
- `GET /health/startup` подтверждает запуск приложения.
- `GET /health/ready` проверяет БД и последнюю версию схемы.

## Конфигурация

Основные параметры находятся в `src/Api/appsettings.json` и переопределяются стандартными переменными ASP.NET Core:

- `ConnectionStrings__Default` задаёт строку подключения SQLite.
- `Database__Provider` сейчас поддерживает только `SQLite`.
- `DevAuthentication__Enabled` включает локальный вход только в Development и Testing.
- `Integrations__Ai__Enabled` включает локальную заглушку предложений группировки. В Development и Testing она включена по умолчанию, в основном конфиге выключена. Адрес и ключ внешнего сервиса не используются.
- `Integrations__Jira__Enabled` включает mock Jira. В Development и Testing она включена, в основном конфиге выключена. `Integrations__Jira__Outcome` задаёт исход `Created`, `Rejected`, `Unknown` или `Unavailable`; `Integrations__Jira__DelayMs` имитирует задержку ответа. Сетевые вызовы не выполняются.

Production отклоняет SQLite и тестовую личность. Поведение изменится после реализации PostgreSQL и TWork. Секреты должны передаваться извне и не попадать в репозиторий.

## Сборка и тесты

Backend:

```bash
dotnet restore TRetro.slnx
dotnet build TRetro.slnx --no-restore
dotnet test TRetro.slnx --no-build --no-restore
```

Frontend:

```bash
cd web
npm ci
npm run build
npm test -- --watch=false
```

Для браузерных проверок M1–M9 нужно один раз установить Chromium и затем запустить сценарии:

```bash
cd web
PLAYWRIGHT_BROWSERS_PATH=../.tools/playwright npx playwright install chromium
npm run test:e2e
```

Последняя проверка прошла успешно: 85 backend-тестов (Domain 25, Application 8, Infrastructure 7, API 45), 11 unit-тестов frontend и девять E2E-сценариев в независимых браузерных контекстах. Backend собирается без предупреждений, production build Angular также проходит без предупреждений. E2E проверяет основной маршрут на реальных SPA, API, SQLite и WebSocket, включая приглашения, присутствие, приватные карточки, готовность, группировку, голосование, обсуждение, таймер, mock-экспорт, завершение и одинаковый PDF из новой сессии. Тесты перевыборов дополнительно проверяют две вкладки, сроки 59 и 61 секунда и перезапуск хоста с той же SQLite.

API-тесты M3 проверяют все сочетания флагов приватности в необработанном JSON, права автора, версии, идемпотентность команд и строки SQLite. Проверки M4 покрывают инварианты групп, конфликты версий доски, ручной перенос после предпросмотра и отключённую заглушку. Миграция `0006_voting.sql` добавляет темы, голоса и личные бюллетени. Проверки M5 покрывают 41 исходную карточку и 32 темы, бюджет 3, позднее вступление, открытый и анонимный расклад, исключение и гонки команд. Миграция `0007_discussion.sql` добавляет очередь, текущую тему, комментарии, решения и задачи в раздельных таблицах. Проверки M6 покрывают сортировку 2, 2 и 0, навигацию, CRUD и исторического исполнителя. Таймер M7 не добавляет миграций или записей в БД: состояние, `runId` и дедупликация находятся только в памяти. Проверки M7 покрывают идемпотентность, истечение, отмену, рестарт, reconnect и звук. Миграция `0008_jira_exports.sql` добавляет намерения и исходы mock-экспорта. Проверки M8 покрывают постоянный `externalRequestId`, один mock-POST при повторах и гонках, блокировку задачи, успех, отказ, недоступность, потерю ответа и порт сверки.

Проверки M9 покрывают четыре сочетания `anonymousCards` и `anonymousVotes`, сохранённые итоги и строки SQLite, физическое удаление связей и записей создания карточек, нулевые темы, решения, комментарии, задачи и назначения. Отдельно проверяются откат при ошибке PDF, повтор и конфликт команды, пустое ретро, доступ участника из новой сессии, запрет постороннему и исключённому, одинаковые байты PDF, кириллица и многостраничная карточка. [CompletionApiTests](tests/Api.Tests/CompletionApiTests.cs) открывает реальные PDF через PdfPig, а [M9 E2E](web/e2e/m9.spec.ts) проходит весь маршрут в двух браузерных контекстах.

`npm audit` сообщает о четырёх предупреждениях в цепочке `ng-morph` и `minimatch`, которая относится к optional-зависимостям схем Taiga UI. Безопасного обновления в рамках текущих ограничений npm не предложил. Принудительный override мажорной версии не применялся. Задача на повторный разбор добавлена в план.

## Материалы по технологиям

- [Angular](https://angular.dev/) и [совместимость версий Angular, Node и TypeScript](https://angular.dev/reference/versions).
- [Taiga UI](https://taiga-ui.dev/) и [таблица совместимости Taiga UI](https://github.com/taiga-family/taiga-ui#version-compatibility).
- [ASP.NET Core](https://learn.microsoft.com/aspnet/core/) и [SignalR](https://learn.microsoft.com/aspnet/core/signalr/introduction).
- [.NET 10](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview).
- [Dapper](https://github.com/DapperLib/Dapper), [Npgsql](https://www.npgsql.org/) и [документация PostgreSQL](https://www.postgresql.org/docs/).
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) и [документация SQLite](https://www.sqlite.org/docs.html).
- [PDFsharp 6.2.4 на NuGet](https://www.nuget.org/packages/PDFsharp/6.2.4), [документация](https://docs.pdfsharp.net/) и [MIT-лицензия этой версии](https://raw.githubusercontent.com/empira/PDFsharp/v6.2.4/LICENSE).
- [DejaVu Sans 2.37](https://github.com/dejavu-fonts/dejavu-fonts/releases/tag/version_2_37) и [PdfPig](https://www.nuget.org/packages/PdfPig/0.1.16).

Ссылки на корпоративные TWork, Jira, Sage и внутренний ИИ будут добавлены только после получения официальных материалов.
