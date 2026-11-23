# Handoff

Файл содержит актуальный контекст для продолжения работы в новой сессии. Его нужно полностью обновлять по запросу пользователя `сохрани сессию` или `bfe`, а также при любом запросе сохранить контекст перед сменой сессии.

Обновлено 23 ноября 2026 года после реализации M9.

## Как обновлять файл

Перед обновлением нужно проверить фактическое состояние файлов, статусы задач, результаты последних проверок и незавершённую работу. Новый снимок должен содержать текущее состояние, выполненные работы, незавершённую работу, следующие действия, блокировки, принятые решения, результаты проверок и ссылки на все нужные файлы.

При обновлении нужно проверить [спецификацию](Retro_Spec.md), [план](tasks.md), [README](../README.md) и [AGENTS.md](../AGENTS.md) на непротиворечивость, полноту и актуальность. Найденные расхождения нужно исправить или явно записать, если требуется решение пользователя.

## Текущее состояние

Этапы с M0 по M9 завершены и отмечены в [плане](tasks.md). Локальный MVP теперь проходит полный маршрут от создания встречи до `Closed` и сохранённого PDF. Следующий этап — M10: итоговая проверка MVP, документация приёмки и повторный разбор frontend-зависимостей.

Последний коммит `1acacfd Update session handoff after M8`. Изменения M9 ещё не коммитились: пользователь не запрашивал коммит этого этапа. До начала M9 рабочее дерево было чистым.

Docker отсутствует и для проверок не используется. .NET SDK 10.0.404 находится в `/usr/local/share/dotnet`, настройка `PATH` описана в [README](../README.md#подготовка-инструментов).

## Что сделано в M9

- Добавлена миграция [0009_completion.sql](../src/Infrastructure/Persistence/Migrations/SQLite/0009_completion.sql): `meeting_totals`, `topic_totals`, `reports`. На встречу создаётся один набор итогов и один PDF.
- [MeetingCompletionContracts](../src/Application/MeetingCompletionContracts.cs) содержит модели итогов, порты и создание безопасного снимка. В нём есть колонки, участники, карточки, группы, темы, очередь, решения, комментарии, задачи, назначения и разрешённые открытые связи.
- [SqliteMeetingCompletionRepository](../src/Infrastructure/Persistence/Sqlite/SqliteMeetingCompletionRepository.cs) выполняет согласованное чтение, генерацию PDF, сохранение итогов и байтов, удаление приватных связей, очистку готовности, закрытие приглашения и переход в `Closed` в одной immediate-транзакции SQLite.
- При `anonymousCards=true` удаляются все `card_authors` и записи `command_deduplication` типа `CreateCard`. При `anonymousVotes=true` удаляются `votes` и `personal_ballots`, а суммы остаются в итогах. Флаги независимы.
- Ошибка PDF откатывает транзакцию и оставляет `Discuss`. Повтор исходной команды после успеха возвращает прежнюю версию и тот же PDF. Другое тело или тип с тем же `operationId` даёт конфликт.
- [MeetingBoardService](../src/Application/MeetingBoardService.cs) использует реальное завершение вместо `CompletionUnavailable` и отменяет таймер только после commit. Таймер и его команды по-прежнему не сохраняются в БД.
- [SqliteMeetingRepository](../src/Infrastructure/Persistence/Sqlite/SqliteMeetingRepository.cs) в `Closed` читает сохранённый payload из `meeting_totals`, а не текущие карточки и голоса. Актуальные статусы Jira отдельно читаются из `exports`; поздний экспорт не меняет PDF.
- [MeetingAccessService](../src/Application/MeetingAccessService.cs) возвращает темы и обсуждение в `Closed`, но не возвращает готовность, личный бюджет или таймер.
- [MeetingReportService](../src/Application/MeetingReportService.cs) и `GET /api/meetings/{id}/report.pdf` проверяют личность и членство при каждом GET. Ответ имеет `application/pdf` и `Cache-Control: private, no-store`; байты одинаковы для допущенных участников.
- [MeetingSettingsPage](../web/src/app/meeting-settings-page.html) имеет отдельное read-only представление `Closed` с итогами, рейтингом, открытым раскладом, решениями, комментариями, задачами и ссылкой на PDF. Команд редактирования там нет, mock-экспорт Jira остаётся доступным фасилитатору.

## PDF и лицензии

- Выбран [PDFsharp 6.2.4](https://www.nuget.org/packages/PDFsharp/6.2.4): цель `net10.0`, локальная генерация без сети, MIT-лицензия. Версия 6.5.0 не используется, поскольку перешла на AGPL/коммерческую лицензию.
- Транзитивный `System.Security.Cryptography.Pkcs` обновлён до 10.0.15 прямой ссылкой, предупреждения NU1902/NU1903 устранены.
- [PdfSharpMeetingReportGenerator](../src/Infrastructure/Reporting/PdfSharpMeetingReportGenerator.cs) формирует русский отчёт с итогами, колонками, карточками, группами, рейтингом включая нули, статусами, решениями, комментариями, задачами и открытыми сведениями по флагам. Длинные строки переносятся, страницы нумеруются, метаданные не содержат авторства участников.
- [EmbeddedFontResolver](../src/Infrastructure/Reporting/EmbeddedFontResolver.cs) использует встроенные DejaVu Sans Regular/Bold 2.37 с кириллицей, не зависящие от ОС.
- Лицензии: [PDFsharp](../src/Infrastructure/Reporting/PDFsharp-LICENSE.txt), [DejaVu](../src/Infrastructure/Reporting/Fonts/DejaVu-LICENSE.txt). Выбор и ссылки зафиксированы в [README](../README.md#завершение-и-pdf).

## Проверки

Стандартные команды описаны в [README](../README.md#сборка-и-тесты). Последний полный регресс прошёл:

- `dotnet restore`, `dotnet build` без предупреждений и `dotnet test`: 85 backend-тестов — Domain 25, Application 8, Infrastructure 7, API 45.
- `npm ci`, production build без предупреждений Angular и 11 unit-тестов frontend.
- Все девять E2E [M1](../web/e2e/m1.spec.ts)–[M9](../web/e2e/m9.spec.ts) прошли на реальных SPA, API, SQLite и WebSocket.
- [CompletionApiTests](../tests/Api.Tests/CompletionApiTests.cs) проверяют четыре сочетания флагов, итоги, физическое удаление, PDF и его текст через PdfPig, нулевые темы, решения, комментарии, задачи, откат ошибки, повторы, конфликт операции, пустое ретро, многостраничную кириллицу и доступ из новой сессии.
- [PdfSharpMeetingReportGeneratorTests](../tests/Infrastructure.Tests/PdfSharpMeetingReportGeneratorTests.cs) проверяют пустой PDF, сигнатуру и JSON round-trip сохранённого состояния.
- [M9 E2E](../web/e2e/m9.spec.ts) проходит встречу двумя участниками до `Closed` и сравнивает байты PDF в двух контекстах и новой сессии участника.

`npm audit` по-прежнему сообщает о четырёх предупреждениях в optional-цепочке `ng-morph` и `minimatch`: 2 moderate и 2 high. Override не применялся, задача M10.8 открыта.

## Принятые решения и ограничения

- Локальный MVP использует SQLite. PostgreSQL остаётся на P1; SQLite-проверки не заменяют PostgreSQL-приёмку.
- Приложение рассчитано на один процесс/pod. Таймер, `runId`, срок и дедупликация команд таймера хранятся только в памяти и не попадают в БД.
- Решения и комментарии раздельны. Поздний участник `Vote` получает полный текущий бюджет без пересчёта `N`.
- Внутренний ИИ и Jira остаются локальными mock-адаптерами без сети. Корпоративные контракты TWork, Jira, ИИ и Sage не предоставлены; URL, заголовки, проекты и аутентификацию нельзя выдумывать.
- Скрытые тексты, авторство и технические связи нельзя возвращать в HTTP, SignalR, HTML, PDF или логах. В анонимном сохранённом payload авторство и индивидуальные голоса отсутствуют.
- Коммиты выполняются только по явной просьбе пользователя.

## Незавершённая работа

M9 завершён, незавершённой реализации внутри этапа нет. Изменения M9 ожидают явно запрошенного коммита. Следующий этап M10 ещё не начат.

В M10 нужно:

1. Сверить существующее покрытие с M10.1–M10.3 и дополнить только реальные пробелы Domain, приватности и SQLite.
2. Выполнить M10.4–M10.5: полный маршрут и применимые сценарии 01–15, 17 и локальную часть 18 из [§14](Retro_Spec.md#14-приёмка-и-самопроверка-постановки). Для 16 после рестарта таймер должен отсутствовать; PostgreSQL-проверки остаются открытыми.
3. Выполнить чистую сборку M10.6, актуализировать результаты и документацию M10.7.
4. Повторно разобрать M10.8 и предупреждения `npm audit` без непроверенного мажорного override.

Внешних блокировок для M10 нет. P1–P4 по-прежнему требуют внешнюю PostgreSQL, корпоративные контракты и соответствующую инфраструктуру.

## Файлы для новой сессии

- [docs/tasks.md](tasks.md) — статусы M0–M10 и последующих этапов.
- [AGENTS.md](../AGENTS.md) — обязательные правила разработки и сохранения контекста.
- [docs/Retro_Spec.md](Retro_Spec.md) — исходные требования, особенно §9 и §14.
- [README.md](../README.md) — запуск, версии, PDF и результаты проверок.
- [src/Application/MeetingCompletionContracts.cs](../src/Application/MeetingCompletionContracts.cs) — модели и безопасная проекция итогов.
- [src/Infrastructure/Persistence/Sqlite/SqliteMeetingCompletionRepository.cs](../src/Infrastructure/Persistence/Sqlite/SqliteMeetingCompletionRepository.cs) — транзакция завершения.
- [src/Infrastructure/Reporting/PdfSharpMeetingReportGenerator.cs](../src/Infrastructure/Reporting/PdfSharpMeetingReportGenerator.cs) — генерация PDF.
- [src/Api/MeetingEndpoints.cs](../src/Api/MeetingEndpoints.cs) — постоянный маршрут отчёта.
- [web/src/app/meeting-settings-page.html](../web/src/app/meeting-settings-page.html) — Closed UI.
- [tests/Api.Tests/CompletionApiTests.cs](../tests/Api.Tests/CompletionApiTests.cs) — интеграционные проверки M9.
- [web/e2e/m9.spec.ts](../web/e2e/m9.spec.ts) — полный браузерный маршрут.
- [docs/handoff.md](handoff.md) — этот снимок.

В начале новой сессии нужно прочитать handoff и AGENTS, затем сверить снимок с фактическими файлами и `git status`.
