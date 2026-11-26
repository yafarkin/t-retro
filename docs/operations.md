# Логи и проверки состояния

## Логирование

Приложение использует `Microsoft.Extensions.Logging.ILogger` и стандартные провайдеры ASP.NET Core. Локальный запуск показывает сообщения в консоли. Уровни задают [appsettings.json](../src/Api/appsettings.json) и [appsettings.Development.json](../src/Api/appsettings.Development.json). Отдельного форматтера JSON в конфигурации нет.

[ApiExceptionHandler](../src/Api/ApiExceptionHandler.cs) пишет общий сбой API с `traceId`, обработчик readiness пишет предупреждение при ошибке проверки, а [FacilitatorElectionWorker](../src/Infrastructure/Background/FacilitatorElectionWorker.cs) сообщает о сбоях фоновой обработки. Serilog и корпоративный sink Sage не подключены. Они остаются в задаче [P2.4](tasks.md#этап-p2-корпоративные-интеграции). [Состав пакетов API](../src/Api/Api.csproj) и [регистрация приложения](../src/Api/Program.cs).

Для подключения согласовать с владельцем Sage формат события, адрес назначения, авторизацию, обязательные поля и правила фильтрации. Подключить Serilog и согласованный sink в `Api`, сохранив имеющиеся интерфейсы `ILogger` внутри приложения. Проверить одну запись события с уровнем, временем, результатом и `traceId`, а для запросов также с длительностью. Перед включением на стенде проверить логи ASP.NET Core, reverse proxy, SignalR и внешних клиентов. Для HTTP использовать имя маршрута или очищенный путь. Тела, query string с токенами, полные приглашения, тексты записей, индивидуальные голоса и сырые ответы интеграций в журнал не передавать. Подробные сообщения ASP.NET Core в Development сами по себе не подтверждают фильтрацию по этим правилам.

## Health endpoints

Три анонимных маршрута определены в [Program.cs](../src/Api/Program.cs). При успехе они возвращают HTTP 200 и `{"status":"ok"}`. Неготовность startup или readiness возвращает HTTP 503 и `{"status":"unavailable"}`.

| Маршрут | Что проверяет | Настройка будущего Deployment |
| --- | --- | --- |
| `GET /health/live` | HTTP-процесс отвечает. Проверка не обращается к БД. | `livenessProbe` |
| `GET /health/startup` | ASP.NET Core вызвал `ApplicationStarted`. Проверка не запускает миграции. | `startupProbe` |
| `GET /health/ready` | БД доступна, а применённая версия схемы равна версии мигратора приложения. Ошибка чтения или несовпадение дают 503. | `readinessProbe` |

SQLite и PostgreSQL хранят версии в `schema_migrations`. Миграции запускаются отдельной командой `--migrate`. PostgreSQL использует блокировку на время транзакции, чтобы две команды миграции не применили один шаг одновременно. Jira и ИИ не влияют на результат проверок состояния. Манифесты Kubernetes остаются в плане. [SQLite](../src/Infrastructure/Persistence/Sqlite/SqliteSchemaVersionReader.cs) и [PostgreSQL](../src/Infrastructure/Persistence/PostgreSql/PostgreSqlDatabase.cs). [Задачи P3.2 и P3.3](tasks.md#этап-p3-контейнер-kubernetes-и-github-actions)

Локальная проверка после `./tools/run.sh`:

```sh
for probe in live startup ready; do
  curl -fsS -w ' HTTP %{http_code}\n' "http://127.0.0.1:5166/health/$probe"
done
```

[ApiSmokeTests](../tests/Api.Tests/ApiSmokeTests.cs) проверяет HTTP 503 у readiness до миграций, HTTP 200 после миграций и доступность live и startup без входа. Проверка замены pod и недоступности внешней PostgreSQL остаётся на этапе P4.
