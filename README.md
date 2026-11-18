# Typizer — .NET SDK для Typizer API

Официальный .NET SDK для [Typizer](https://typizer.ru) — сервиса, который извлекает данные из
документов (счета, УПД, накладные, договоры, сканы и фото) в JSON по вашей схеме.

Один вызов — «документ и схема → данные»: `await client.ExtractAsync(file, schema)` сам создаёт
прогон, дожидается результата и возвращает его с проверкой каждого поля. Всё асинхронное, с
`CancellationToken`; повторы с `Idempotency-Key`, таймауты, проверка подписи вебхуков и
`IHttpClientFactory` — из коробки. Полная документация — [docs.typizer.ru](https://docs.typizer.ru).

## Установка

```sh
dotnet add package Typizer
# для ASP.NET Core и Generic Host: services.AddTypizer(…)
dotnet add package Typizer.Extensions.DependencyInjection
```

Работает в .NET 8 и новее и в .NET Framework 4.6.2+ (сборка `netstandard2.0`). Зависимостей нет,
кроме `System.Text.Json` — и то только для .NET Framework: в .NET 8 он уже есть.

## Ключ API

1. Зарегистрируйтесь в кабинете [my.typizer.ru](https://my.typizer.ru).
2. Откройте раздел «Ключи API» и создайте ключ со скоупом `runs:write` (для запуска прогонов) —
   подробнее о скоупах в [«Ключах API»](https://docs.typizer.ru/how-to/api-keys/).
3. Ключ (`tz_live_…`) показывается один раз. Сохраните его, например в переменную окружения или в
   секреты приложения (`dotnet user-secrets`, Key Vault, Vault):

```sh
export TYPIZER_API_KEY="tz_live_…"
```

`new TypizerClient()` без аргументов берёт ключ из `TYPIZER_API_KEY`; если ключа нет, клиент
бросает `TypizerConfigurationException` сразу при создании. Ключ — секрет сервера: не передавайте
его в браузер или мобильное приложение. В `ToString()` клиента, в сообщениях исключений и в журнале
ключа нет — только маска `tz_live_…a1b2`.

## Быстрый старт

```csharp
using Typizer;

using var client = new TypizerClient(Environment.GetEnvironmentVariable("TYPIZER_API_KEY")!);

const string schema = """
{
  "type": "object",
  "properties": {
    "invoice_number": { "type": "string" },
    "invoice_date": { "type": "string", "format": "date" },
    "seller": {
      "type": "object",
      "properties": {
        "name": { "type": "string" },
        "inn": { "type": "string", "x-typizer-check": "inn" }
      }
    },
    "total": { "type": "number" }
  },
  "required": ["invoice_number", "total"]
}
""";

Run run = await client.ExtractAsync(TypizerFile.FromPath("invoice.pdf"), schema);

Console.WriteLine(run.Output); // {"invoice_number":"А-1024","invoice_date":"2026-11-05",…}

// Числовой «уверенности» API не отдаёт: у каждого поля — статус проверки и место в документе
if (run.NeedsReview)
{
    foreach (var (path, field) in run.FieldsToReview())
        Console.WriteLine($"{path}: {field.Status} {field.Reason}"); // seller.inn: warn checksum_mismatch
}
```

`ExtractAsync` ждёт результат до 5 минут (`ExtractOptions.Timeout`): сначала сервер держит запрос до
60 секунд, потом SDK опрашивает прогон с паузой от 1 до 5 секунд. Не дождался —
`TypizerWaitTimeoutException`: прогон при этом продолжается (и будет оплачен), его можно дождаться
через `client.Runs.WaitAsync(e.RunId)` или вебхук. Прогон завершился ошибкой —
`TypizerRunFailedException` с кодом (`document_unreadable`, `too_many_pages`, …) и самим прогоном.

Схему можно передать JSON-текстом, `JsonElement`, `JsonNode`, словарём или объектом — порядок
свойств сохраняется (по нему идут столбцы CSV/XLSX и элементы XML). Результат — `JsonElement` как
есть, или сразу ваш тип:

```csharp
using Typizer;

using var client = new TypizerClient();
Run run = await client.ExtractAsync(TypizerFile.FromPath("invoice.pdf"), new ExtractOptions { Catalog = "schet-na-oplatu@1" });

Invoice? invoice = run.GetOutput<Invoice>(); // invoice_number → InvoiceNumber
Console.WriteLine($"{invoice?.InvoiceNumber}: {invoice?.Total} ₽");

record Invoice(string InvoiceNumber, decimal Total);
```

Документ передаётся явно, одним из способов:

| Источник                                                  | Как уходит                                 |
| --------------------------------------------------------- | ------------------------------------------ |
| `TypizerFile.FromPath("invoice.pdf")`, `FileInfo`         | в том же запросе (`multipart/form-data`)   |
| `TypizerFile.FromBytes(bytes, "invoice.pdf")`, `byte[]`   | в том же запросе                           |
| `TypizerFile.FromStream(stream, "invoice.pdf")`, `Stream` | в том же запросе                           |
| `TypizerFile.FromUrl("https://…")`, `Uri`                 | Typizer скачает документ сам               |
| `TypizerFile.FromId("file_…")`                            | уже загруженный `client.Files.UploadAsync` |

Имя файла нужно только для `Content-Type` части запроса; сервер всё равно определяет тип по
содержимому. Поток с `Seek` (файл, `MemoryStream`) читается с текущей позиции и перематывается при
повторе; поток без `Seek` (сеть, архив) SDK сначала читает в память — не больше 50 МиБ. Файл больше
50 МиБ (лимит API) SDK отклоняет до отправки той же `TypizerFileTooLargeException`, что вернул бы
сервер.

### Проверки полей

Поле схемы может нести проверку `x-typizer-check`: официальную (`"inn"`), свою (`"my/contract-no@1"`)
или встроенную — объект прямо в схеме (`{"kind": "rules", …}`); SDK передаёт схему как есть. Поле, не
прошедшее проверку, — `warn`: `run.Warnings()` отдаёт путь, готовую фразу по-русски (`Message`) и код
причины (`Reason`). Все проверки поля и их итоги — в `field.Checks`; у прогона разовой схемы
`run.ChecksUsed` перечисляет проверки на точных версиях, у типизатора они в `SavedTypizer.Checks`.

Свои проверки — `client.Validators`: `CreateAsync` (тесты — контракт версии: красный тест не даст
сохранить), `UpdateAsync` (новые правила — новая версия, `Bump`), `GetAsync("my/contract-no")`,
`ListAsync`, `ArchiveAsync`. `TestAsync` прогоняет значения через проверку (ссылку или спецификацию),
ничего не сохраняя; `SuggestAsync` предлагает спецификацию по описанию и примерам. Спецификация — JSON
как есть (текст, `JsonElement`, словарь), SDK не закрывает её в типы.

Готовые проверки каталога (ИНН, IBAN, VIN и другие) подключаются ссылкой без установки:
`"x-typizer-check": "iban@1"`. Список — `client.Catalog.Validators.ListAsync()`, своя копия для
правки — `client.Catalog.Validators.ForkAsync("iban@1")`.

```csharp
using Typizer;

using var client = new TypizerClient();

await client.Validators.CreateAsync(new ValidatorCreateOptions
{
    Name = "Номер договора",
    Slug = "contract-no",
    Spec = """
    {
      "kind": "rules",
      "applies_to": "string",
      "normalize": { "strip": ["spaces"], "case": "upper" },
      "rules": [{ "type": "pattern", "pattern": "ДГ-\\d{4}/\\d{4}" }],
      "message": "Номер договора не по шаблону ДГ-ГГГГ/NNNN"
    }
    """,
    Tests = new[] { ValidatorTest.Pass("ДГ-2026/0153"), ValidatorTest.Fail("ДГ-26/153") },
});

ValidatorTestResult test = await client.Validators.TestAsync(new[] { "ДГ-2026/0001", "ДГ-26/1" }, reference: "my/contract-no@1");
foreach (ValidatorTestValueResult result in test.Results)
    Console.WriteLine($"{result.Value}: {(result.Pass ? "проходит" : result.Message)}");

string schema = """
{
  "type": "object",
  "properties": {
    "contract_no": { "type": "string", "x-typizer-check": "my/contract-no@1" },
    "seller_inn": { "type": "string", "x-typizer-check": "inn" }
  }
}
""";
Run run = await client.ExtractAsync(TypizerFile.FromPath("contract.pdf"), schema);

foreach (FieldWarning warning in run.Warnings())
    Console.WriteLine($"{warning.Path}: {warning.Message}"); // contract_no: Номер договора не по шаблону ДГ-ГГГГ/NNNN
```

## ASP.NET Core и DI

`Typizer.Extensions.DependencyInjection` регистрирует `TypizerClient` как типизированный клиент
`IHttpClientFactory` (соединения и DNS — по правилам фабрики), настройки берёт из
`IOptions<TypizerClientOptions>`, а журнал SDK отправляет в `ILogger<TypizerClient>`:

```csharp
using Typizer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTypizer(o => o.ApiKey = builder.Configuration["Typizer:ApiKey"]);

var app = builder.Build();

// Загрузка счёта из формы → данные по схеме каталога
app.MapPost("/invoices", async (IFormFile file, TypizerClient typizer, CancellationToken ct) =>
{
    await using Stream stream = file.OpenReadStream();
    Run run = await typizer.ExtractAsync(TypizerFile.FromStream(stream, file.FileName),
        new ExtractOptions { Catalog = "schet-faktura@1" }, ct);
    return Results.Ok(run.Output);
}).DisableAntiforgery();

app.Run();
```

`AddTypizer` возвращает `IHttpClientBuilder` — к нему добавляются свои обработчики
(`AddHttpMessageHandler`) или `ConfigurePrimaryHttpMessageHandler` (корпоративный сертификат,
ГОСТ-TLS). Повторы у SDK свои; добавлять поверх них политику повторов Polly не нужно.

Без DI — один клиент на приложение: `TypizerClient` потокобезопасен и держит пул соединений.

## Схема из каталога

В [каталоге](https://typizer.ru/catalog/) — готовые схемы для частых документов: счёт-фактура, УПД,
счёт на оплату, акт сверки и другие. Ссылка — slug (`schet-faktura`), с мажорной (`@1`) или точной
(`@1.2.0`) версией; схемы сообщества — `издатель/slug`:

```csharp
using Typizer;

using var client = new TypizerClient();

Run run = await client.ExtractAsync(TypizerFile.FromPath("upd.pdf"), new ExtractOptions { Catalog = "upd@1" });

// Что есть в каталоге
await foreach (CatalogEntryBrief entry in client.Catalog.ListAsync(new CatalogListOptions { Category = CatalogCategory.Invoices }))
    Console.WriteLine($"{entry.Ref}: {entry.Title}");

CatalogEntry upd = await client.Catalog.GetAsync("upd@1");
Console.WriteLine(upd.Version.Schema); // сама схема
```

## Типизатор

Схему удобно сохранить как типизатор и запускать его по `id`; результат можно сразу получить в XML
для 1С, CSV или XLSX:

```csharp
using Typizer;

using var client = new TypizerClient();

SavedTypizer invoices = await client.Typizers.CreateAsync(new TypizerCreateOptions
{
    Name = "Счета поставщиков",
    Schema = File.ReadAllText("invoice.schema.json"),
    DefaultFormat = OutputFormat.Xml,
});

Run run = await client.ExtractAsync(TypizerFile.FromPath("invoice.pdf"), invoices); // или new ExtractOptions { Typizer = "tz_…" }

using TypizerDownload xml = await client.Runs.GetOutputAsync(run.Id, OutputFormat.Xml);
await xml.SaveToAsync(Path.Combine("out", xml.FileName ?? run.Id + ".xml"));

// Прогон без ожидания: вернётся queued, результат — через WaitAsync или вебхук
Run queued = await client.Runs.CreateAsync(TypizerFile.FromPath("invoice-2.pdf"), new RunCreateOptions { Typizer = invoices.Id });
Run done = await client.Runs.WaitAsync(queued.Id, TimeSpan.FromMinutes(10));
```

Списки (`Runs.ListAsync`, `Typizers.ListAsync`, `Catalog.ListAsync`, …) постраничные, новые первыми.
`await foreach` перебирает все элементы всех страниц — следующая страница запрашивается, когда
кончилась текущая; `await` отдаёт первую страницу:

```csharp
using Typizer;

using var client = new TypizerClient();

await foreach (Run run in client.Runs.ListAsync(new RunListOptions { Status = RunStatus.Failed, Limit = 100 }))
    Console.WriteLine($"{run.Id}: {run.Error?.Code}");

Page<Run> page = await client.Runs.ListAsync(new RunListOptions { Limit = 20 });
Console.WriteLine($"{page.Count} прогонов, дальше — {page.NextCursor}");
```

Без `await foreach` (C# 7.3 в старых проектах .NET Framework) — страницами:

```cs
Page<Run> page = await client.Runs.ListAsync().GetPageAsync();
while (page != null)
{
    foreach (Run run in page) Handle(run);
    page = await page.GetNextPageAsync();
}
```

## Ошибки

Ошибка API — исключение `TypizerApiException` (или его подкласс) с полями из ответа
`application/problem+json` (RFC 9457):
- `Code` — машиночитаемый код (`invalid_request`, `insufficient_balance`, `rate_limited`, …; константы — `ErrorCodes`);
- `StatusCode` — статус HTTP; `Detail` — описание случая, `Param` — виноватое поле запроса;
- `RequestId` — приложите его к обращению в поддержку (support@typizer.ru);
- `DocUrl` — страница кода: `https://docs.typizer.ru/errors/<code>`; `RetryAfter` — из `Retry-After`;
  `IsRetryable` — поможет ли повтор позже.

Текст исключения: `[insufficient_balance] Not enough pages on the balance: 1 needed, 0 available (HTTP 402, request_id=req_…, https://docs.typizer.ru/errors/insufficient_balance)`.

| Исключение                              | Когда                                                                                       |
| --------------------------------------- | ------------------------------------------------------------------------------------------- |
| `TypizerBadRequestException`            | 400: неверный запрос, id или схема (`invalid_schema`)                                       |
| `TypizerAuthenticationException`        | 401: ключа нет, он неверен или отозван                                                      |
| `TypizerInsufficientBalanceException`   | 402: не хватает страниц на балансе                                                          |
| `TypizerPermissionDeniedException`      | 403: у ключа нет скоупа или доступа                                                         |
| `TypizerNotFoundException`              | 404                                                                                         |
| `TypizerConflictException`              | 409; `TypizerIdempotencyConflictException` — тот же ключ идемпотентности с другим запросом  |
| `TypizerFileTooLargeException`          | 413: файл больше 50 МиБ                                                                     |
| `TypizerUnsupportedFileTypeException`   | 415                                                                                         |
| `TypizerUnprocessableDocumentException` | 422: документ не читается или данные не извлеклись                                          |
| `TypizerRateLimitException`             | 429 после всех повторов                                                                     |
| `TypizerInternalServerException`        | 500 после всех повторов                                                                     |
| `TypizerServiceUnavailableException`    | 502–504 после всех повторов                                                                 |
| `TypizerConnectionException`            | ответа нет: DNS, соединение, TLS, прокси; `TypizerTimeoutException` — истёк таймаут попытки |
| `TypizerRunFailedException`             | `ExtractAsync`: прогон завершился `failed` (`Code`, `Run`)                                  |
| `TypizerWaitTimeoutException`           | `ExtractAsync`/`WaitAsync` не дождались (`RunId`, `Run`)                                    |
| `TypizerWebhookVerificationException`   | подпись вебхука не прошла проверку (`Reason`)                                               |

Все они наследуют `TypizerException`. Ошибки в аргументах (нет схемы, две схемы сразу, пустой id)
— обычные `ArgumentException` до отправки запроса; отмена — `OperationCanceledException`.

```csharp
using Typizer;

using var client = new TypizerClient();

try
{
    Run run = await client.ExtractAsync(new Uri("https://typizer.ru/samples/invoice.pdf"), new ExtractOptions { Catalog = "schet-faktura" });
}
catch (TypizerInsufficientBalanceException)
{
    Console.WriteLine("Пополните баланс в кабинете my.typizer.ru");
}
catch (TypizerRunFailedException e) when (e.Code == "document_unreadable")
{
    Console.WriteLine("Документ не читается — попросите скан получше");
}
catch (TypizerApiException e)
{
    Console.WriteLine($"{e.Code}: {e.Detail} ({e.DocUrl}, request_id {e.RequestId})");
}
catch (TypizerConnectionException e)
{
    Console.WriteLine("Сеть: " + e.Message);
}
```

## Повторы и идемпотентность

SDK сам повторяет запрос (по умолчанию до 2 раз, `TypizerClientOptions.MaxRetries`) при сетевой
ошибке, таймауте, `408`, `429`, `500`, `502`–`504`: ждёт ровно `Retry-After`, если он пришёл (и не
длиннее минуты — иначе сразу бросает ошибку с заполненным `RetryAfter`), а без него — 0,5 → 1 → 2 → 4
→ 8 секунд со случайной поправкой. Остальные ошибки от повтора не исчезнут: сначала исправьте причину.

Каждый `POST` уходит с заголовком `Idempotency-Key` (UUID v4 на вызов), и повтор идёт с тем же
ключом — поэтому повтор после обрыва связи или `503` не создаст второй прогон и не спишет страницы
дважды. Свой ключ нужен для повторов между процессами — «тот же счёт из той же очереди»:

```csharp
using Typizer;

using var client = new TypizerClient();
string orderId = "42";

Run run = await client.ExtractAsync(TypizerFile.FromPath("invoice.pdf"), new ExtractOptions
{
    Catalog = "schet-faktura@1",
    IdempotencyKey = $"order-{orderId}-invoice", // повтор задания из очереди вернёт тот же прогон
    Metadata = new Dictionary<string, string> { ["order_id"] = orderId },
});

// Для остальных методов — RequestOptions: свой ключ, таймаут, повторы, заголовки
SavedTypizer typizer = await client.Typizers.CreateAsync("Счета", File.ReadAllText("invoice.schema.json"),
    new RequestOptions { IdempotencyKey = "typizer-invoices-v1", MaxRetries = 5 });
```

Повтор ответа виден в «сыром» ответе: методы `…WithResponseAsync` возвращают значение вместе со
статусом и заголовками (`RequestId`, `IdempotentReplayed`, `RateLimit`, `Location`). Повтор создания
конечной точки вебхуков с тем же ключом отдаёт `Secret = null` — секрет приходит только в первом
ответе.

Таймаут одной HTTP-попытки — 75 секунд (`TypizerClientOptions.Timeout`); при создании прогона с
серверным ожиданием SDK сам продлевает его до `wait + 15` секунд. Другие таймауты для части вызовов —
`client.WithOptions(o => o.Timeout = …)`: копия клиента на тех же соединениях.

## Вебхуки

Вместо ожидания результат можно получать вебхуком: укажите `WebhookUrl` в `ExtractOptions`/
`RunCreateOptions` или создайте конечную точку:

```csharp
using Typizer;

using var client = new TypizerClient();

WebhookEndpoint endpoint = await client.WebhookEndpoints.CreateAsync(
    "https://your-server.example/typizer/webhook",
    new[] { WebhookEventType.RunSucceeded, WebhookEventType.RunFailed });
Console.WriteLine(endpoint.Secret); // whsec_… — ключ проверки подписи, храните как секрет

await client.WebhookEndpoints.TestAsync(endpoint.Id); // пришлёт тестовый run.succeeded
```

Вебхуки подписываются по спецификации [Standard Webhooks](https://www.standardwebhooks.com)
(HMAC-SHA256). `TypizerWebhooks.Verify(payload, headers, secret)` проверяет подпись и метку времени
(не дальше 5 минут) и возвращает событие, а при неверной подписи бросает
`TypizerWebhookVerificationException`; `TypizerWebhooks.IsValid(…)` — то же, но возвращает
`true`/`false`. Клиент и ключ API для этого не нужны. Тело передавайте ровно в тех байтах, в которых
оно пришло, а не пересобранным из разобранного JSON. Обработчик в ASP.NET Core:

```csharp
using Typizer;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
string secret = builder.Configuration["Typizer:WebhookSecret"]!;

app.MapPost("/typizer/webhook", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string payload = await reader.ReadToEndAsync(); // сырое тело, как пришло

    WebhookEvent evt;
    try
    {
        evt = TypizerWebhooks.Verify(payload, request.Headers, secret);
    }
    catch (TypizerWebhookVerificationException)
    {
        return Results.BadRequest();
    }

    // evt.Id (он же заголовок webhook-id) — ключ дедупликации: доставка может прийти повторно
    if (evt.Type == WebhookEventType.RunSucceeded && evt.Run is { } run)
    {
        Console.WriteLine($"{run.Id}: {run.Output} (заказ {run.Metadata.GetValueOrDefault("order_id")})");
    }
    return Results.Ok();
});

app.Run();
```

В MVC-контроллере, где тело уже прочитал `[FromBody]`, включите `Request.EnableBuffering()` в
middleware и перемотайте `Request.Body` в начало. В ASP.NET на .NET Framework заголовки —
`Request.Headers` (`NameValueCollection`), тело — `Request.InputStream`. Подробнее — в
[«Приёме вебхуков»](https://docs.typizer.ru/how-to/webhooks/). Никогда не обрабатывайте тело вебхука,
не проверив подпись.

## Прокси и свой HTTP-клиент

Прокси подхватывается так же, как у .NET по умолчанию: в .NET 8 — из `HTTPS_PROXY`/`HTTP_PROXY`/
`NO_PROXY` (в Windows — ещё и системный), в .NET Framework — системный прокси Windows. Явный прокси,
в том числе с логином и паролем:

```csharp
using Typizer;

using var client = new TypizerClient(new TypizerClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("TYPIZER_API_KEY"),
    Proxy = TypizerProxy.FromUrl("http://user:password@proxy.local:3128"), // или new WebProxy(…)
});
Console.WriteLine((await client.GetBalanceAsync()).PagesAvailable);
```

Прокси с инспекцией TLS (свой корневой сертификат): лучше всего добавить корневой сертификат в
доверенные сертификаты ОС. Если так нельзя — свой `HttpMessageHandler`, который доверяет только ему.
Отключить проверку TLS целиком SDK не предлагает:

```csharp
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Typizer;

X509Certificate2 corporateRoot = X509Certificate2.CreateFromPemFile("corporate-root.pem");
var handler = new SocketsHttpHandler
{
    SslOptions = new SslClientAuthenticationOptions
    {
        RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
        {
            if (errors == SslPolicyErrors.None) return true;
            if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null || chain is null) return false;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(corporateRoot);
            return chain.Build(new X509Certificate2(certificate));
        },
    },
};

using var client = new TypizerClient(new TypizerClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("TYPIZER_API_KEY"),
    HttpMessageHandler = handler, // так же подключается обработчик с ГОСТ-TLS
});
```

Свой `HttpClient` (например, общий на приложение) передаётся в `TypizerClientOptions.HttpClient` или
вторым аргументом конструктора — SDK его не закрывает. Свои таймауты и повторы SDK ведёт сам, поэтому
`HttpClient.Timeout` лучше оставить больше `Timeout` клиента (по умолчанию у `HttpClient` — 100 с).

## Логи

По умолчанию SDK молчит. Журнал — строка на HTTP-попытку (`Debug`), повторы (`Information`) и
объявленные плановые работы (`Warning`, один раз на клиент):

```
typizer: POST /v1/extract -> 200 in 1.84s (request_id=req_…, attempt 1/3)
typizer: retrying POST /v1/extract in 1.0s after 429 rate_limited (attempt 2/3, request_id=req_…)
```

В ASP.NET Core с `AddTypizer` он идёт в `ILogger<TypizerClient>` (категория `Typizer.TypizerClient`,
уровень — как обычно, в `appsettings.json`). Без DI — функция в настройках:

```csharp
using Typizer;

using var client = new TypizerClient(new TypizerClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("TYPIZER_API_KEY"),
    Logger = (level, message) => Console.Error.WriteLine($"{level}: {message}"),
});
```

Ключа, тел запросов и ответов, результатов, имён файлов, `metadata` и подписей вебхуков в журнале нет
ни на одном уровне: в документах клиентов персональные данные (152-ФЗ). Телеметрии SDK не отправляет.

## .NET Framework

Сборка `netstandard2.0` работает в .NET Framework 4.6.2 и новее (WinForms, WPF, WCF, ASP.NET MVC 5,
Windows-службы). API только асинхронный; внутри SDK везде `ConfigureAwait(false)`, поэтому в старом
синхронном коде вызов `client.GetBalanceAsync().GetAwaiter().GetResult()` не взаимоблокируется — но
лучше сделать обработчик `async`. Примеры выше используют C# 11 (`"""…"""`, `using var`); в C# 7.3
пишите схему обычной строкой, а списки перебирайте страницами (см. «Типизатор»).

## Документация

- [Документация Typizer](https://docs.typizer.ru)
- [Справочник API](https://docs.typizer.ru/reference/api/) — REST-операция за каждым методом SDK
- [Коды ошибок](https://docs.typizer.ru/reference/errors/)
- [Первый вызов API](https://docs.typizer.ru/tutorials/first-api-call/)
- [Изменения](https://github.com/typizer/typizer-dotnet/blob/main/CHANGELOG.md)

Вопросы и проблемы — support@typizer.ru.

## Лицензия

MIT — см. файл [LICENSE](https://github.com/typizer/typizer-dotnet/blob/main/LICENSE).
