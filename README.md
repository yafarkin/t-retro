# T-Retro

T-Retro является внутренним веб-приложением для ретроспектив IT-команд. Сотрудник создаёт доску и приглашает команду, после чего участники собирают карточки, группируют их, голосуют за темы и фиксируют итоги обсуждения. Результатом встречи являются решения, комментарии, задачи и PDF по постоянному адресу.

Интерфейс и отчёт используют русский язык. Реализованы этапы с M0 по M5: основа приложения, встречи, приглашения, SignalR, роль фасилитатора, карточки, приватность, готовность, группировка и голосование. Следующим этапом является M6 с обсуждением, решениями, комментариями и задачами. Внутренний ИИ пока представлен локальной заглушкой без сетевых вызовов. Завершение встречи и PDF относятся к M9 и пока недоступны.

## Документы

- [Handoff](docs/handoff.md) содержит актуальный контекст для новой сессии, незавершённую работу, следующие действия и известные расхождения документов.
- [Техническое задание](docs/Retro_Spec.md) содержит полные функциональные требования, контракты, архитектуру и критерии приёмки.
- [План реализации](docs/tasks.md) содержит задачи локального MVP и итоговой поставки.
- [AGENTS.md](AGENTS.md) содержит обязательные правила разработки и сохранения контекста по командам `сохрани сессию` и `bfe`.

Если старый текст спецификации противоречит решениям в [AGENTS.md](AGENTS.md#приоритет-решений), нужно применять зафиксированные там решения и обновить документы согласованно.

## Локальный MVP

MVP должен позволить нескольким тестовым сотрудникам пройти полную встречу в двух независимых браузерных контекстах. Локальное хранение использует SQLite в файле. PostgreSQL остаётся целевой СУБД итоговой поставки, а его реализация и проверки находятся в [этапе P1](docs/tasks.md#этап-p1-postgresql-для-итоговой-поставки).

В MVP не входят production-адаптеры TWork, Jira, внутреннего ИИ и Sage. Kubernetes, GitLab CI и нагрузочная приёмка отложены до этапов итоговой поставки.

Таймер будет храниться только в памяти процесса. БД не будет содержать его состояние, `runId`, срок окончания или дедупликацию команд. Решения и комментарии являются разными сущностями. Сотрудник, который вступил во время `Vote`, получает полный текущий бюджет голосов.

## Архитектура

Backend использует .NET 10, C# 14, ASP.NET Core и Dapper. Локальная реализация хранения использует `Microsoft.Data.Sqlite`. Frontend использует Angular и Taiga UI 4.x.

| Проект | Назначение |
| --- | --- |
| `src/Domain` | Правила и доменные модели. |
| `src/Application` | Сценарии, права и порты хранения и интеграций. |
| `src/Infrastructure` | SQL, миграции, Dapper и адаптеры. |
| `src/Api` | HTTP DTO, OpenAPI, SignalR, личность и composition root. |
| `web` | Angular SPA, экраны и клиенты HTTP и SignalR. |
| `tests` | Domain, Application, Infrastructure и API-тесты. |

Решение находится в `TRetro.slnx`. Зависимости между проектами соответствуют [§10 спецификации](docs/Retro_Spec.md#10-архитектура-и-границы-данных).

## Зафиксированные версии

- .NET SDK 10.0.404, C# 14, `TargetFramework=net10.0`.
- Node 24.15.0 и npm 12.1.0.
- Angular, Angular CLI и CDK 22.3.0.
- Taiga UI core, CDK, kit и i18n 4.109.0, event-plugins 4.8.0.
- TypeScript 6.0.2, Vitest 5.1.1, Playwright 1.65.1, SignalR client 11.0.0 и Less 4.2.0.
- Dapper 2.1.100 и Microsoft.Data.Sqlite 10.0.15.

Версии Node записаны в `.nvmrc`, версия SDK в `global.json`, а frontend-зависимости зафиксированы в `web/package.json` и `web/package-lock.json`. Установка и сборка проверены без `force` и `legacy-peer-deps`.

## Подготовка инструментов

На текущей машине .NET SDK 10.0.404 установлен в `/usr/local/share/dotnet`. В `PATH` также присутствует старая установка `/Users/yafarkin/.dotnet`, которая содержит только SDK 8 и 9. Поэтому перед командами .NET нужно поставить актуальную установку первой в `PATH`:

```bash
export DOTNET_ROOT="/usr/local/share/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$PWD/.tools/nuget/packages"
```

Версия SDK закреплена в `global.json`. Локальная копия SDK внутри проекта больше не используется.

Docker на текущей машине отсутствует. Dockerfile и docker-compose можно подготовить на этапе P3, но локальные проверки не должны зависеть от Docker.

## Запуск

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

Для браузерных проверок M1, M2, M3, M4 и M5 нужно один раз установить Chromium и затем запустить сценарии:

```bash
cd web
PLAYWRIGHT_BROWSERS_PATH=../.tools/playwright npx playwright install chromium
npm run test:e2e
```

Последняя проверка прошла успешно: 59 backend-тестов, 7 unit-тестов frontend и пять E2E-сценариев в независимых браузерных контекстах. Production build Angular проходит. E2E проверяет основной маршрут на реальных SPA, API, SQLite и WebSocket, включая приглашения, присутствие, приватные карточки, готовность, группировку и голосование. Тесты перевыборов дополнительно проверяют две вкладки, сроки 59 и 61 секунда и перезапуск хоста с той же SQLite.

API-тесты M3 проверяют все сочетания флагов приватности в необработанном JSON, права автора, версии, идемпотентность команд и строки SQLite. Проверки M4 покрывают инварианты групп, конфликты версий доски, ручной перенос после предпросмотра и отключённую заглушку. Миграция `0006_voting.sql` добавляет темы, голоса и личные бюллетени. Проверки M5 покрывают 41 исходную карточку и 32 темы, бюджет 3, позднее вступление, открытый и анонимный расклад, исключение, дробные и отрицательные значения, а также гонки двух вкладок и изменения лимита. До реализации M9 переход из Discuss в Closed возвращает конфликт, чтобы встреча не завершилась без итогов, PDF и удаления приватных связей.

`npm audit` сообщает о четырёх предупреждениях в цепочке `ng-morph` и `minimatch`, которая относится к optional-зависимостям схем Taiga UI. Безопасного обновления в рамках текущих ограничений npm не предложил. Принудительный override мажорной версии не применялся. Задача на повторный разбор добавлена в план.

## Материалы по технологиям

- [Angular](https://angular.dev/) и [совместимость версий Angular, Node и TypeScript](https://angular.dev/reference/versions).
- [Taiga UI](https://taiga-ui.dev/) и [таблица совместимости Taiga UI](https://github.com/taiga-family/taiga-ui#version-compatibility).
- [ASP.NET Core](https://learn.microsoft.com/aspnet/core/) и [SignalR](https://learn.microsoft.com/aspnet/core/signalr/introduction).
- [.NET 10](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview).
- [Dapper](https://github.com/DapperLib/Dapper), [Npgsql](https://www.npgsql.org/) и [документация PostgreSQL](https://www.postgresql.org/docs/).
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) и [документация SQLite](https://www.sqlite.org/docs.html).

Ссылки на корпоративные TWork, Jira, Sage и внутренний ИИ будут добавлены только после получения официальных материалов.
