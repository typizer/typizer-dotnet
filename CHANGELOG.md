# Изменения

Формат — [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/), версии — [SemVer](https://semver.org/lang/ru/).
Пакеты `Typizer` и `Typizer.Extensions.DependencyInjection` выходят с одной версией.

## [0.2.0] — 2026-11-16

Совместимо с 0.1.0: прежние вызовы работают без изменений.

### Добавлено

- Причина `code_mismatch` у `FieldResult.Reason`: значение поля расходится со штрихкодом или QR-кодом
  на документе. Сервис сам читает коды (платёжный QR по ГОСТ Р 56042-2014, QR чека, «Честный знак»), чтобы точнее
  заполнять поля вашей схемы; ответ — по-прежнему только поля схемы, отдельного списка кодов в прогоне
  нет.

## [0.1.0] — 2026-11-14

### Добавлено

- `TypizerClient` для всех 49 публичных операций Typizer API: прогоны, файлы, типизаторы и их версии,
  каталог схем, пакеты, вебхуки, баланс и расход.
- `ExtractAsync(file, schema)` — документ и схема → данные: создание прогона, серверное ожидание и
  опрос, `TypizerRunFailedException` и `TypizerWaitTimeoutException`.
- Документ с диска, из памяти, из потока (в том же запросе), по URL или `file_id`.
- `Run.Fields`, `Run.NeedsReview`, `Run.FieldsToReview()`, `Run.GetOutput<T>()`.
- Повторы при сетевых ошибках, 408, 429, 500, 502–504 с `Retry-After` и экспонентой; `Idempotency-Key`
  на каждый `POST`; таймаут попытки 75 с.
- Типизированные исключения по кодам ошибок API.
- Постраничные списки: `await foreach`, `await` (первая страница), `AsPages()`.
- `TypizerWebhooks.Verify` / `IsValid` (Standard Webhooks) для ASP.NET Core, `HttpHeaders`,
  `NameValueCollection` и словарей.
- Прокси, свой `HttpClient`/`HttpMessageHandler`, журнал SDK без ключа и персональных данных.
- `Typizer.Extensions.DependencyInjection`: `services.AddTypizer(…)` через `IHttpClientFactory` и
  `ILogger<TypizerClient>`.
- .NET 8 и .NET Framework 4.6.2+ (`netstandard2.0`).

[0.1.0]: https://github.com/typizer/typizer-dotnet/releases/tag/v0.1.0
