namespace Bench;

using System.Text;
using System.Text.Json;

/// PROTOTYPE. A synthetic log corpus shaped like what an ASP.NET Core service
/// actually emits, because the trigram index is only as expensive as the text it
/// indexes — message length and trigram diversity decide the answer.
sealed class Corpus
{
    public sealed record Entry(
        long Id,
        Guid ProjectId,
        DateTime EventTime,
        DateTime ReceiptTime,
        short Level,
        string? LoggerName,
        string? Instance,
        string? TraceId,
        string? SpanId,
        string MessageTemplate,
        string RenderedMessage,
        string? Exception,
        string? Properties);

    sealed record Template(string LoggerName, short Level, string Text, string[] Props);

    // Levels: 0 Verbose, 1 Debug, 2 Information, 3 Warning, 4 Error, 5 Fatal.
    static readonly Template[] Templates =
    [
        new("Microsoft.AspNetCore.Hosting.Diagnostics", 2,
            "Request starting {Protocol} {Method} {Scheme}://{Host}{Path}{QueryString}",
            ["Protocol", "Method", "Scheme", "Host", "Path", "QueryString"]),
        new("Microsoft.AspNetCore.Hosting.Diagnostics", 2,
            "Request finished {Protocol} {Method} {Scheme}://{Host}{Path} - {StatusCode} {ContentLength} {ContentType} {Elapsed}ms",
            ["Protocol", "Method", "Scheme", "Host", "Path", "StatusCode", "ContentLength", "ContentType", "Elapsed"]),
        new("Microsoft.AspNetCore.Routing.EndpointMiddleware", 1,
            "Executing endpoint '{EndpointName}'", ["EndpointName"]),
        new("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", 1,
            "Route matched with {RouteData}. Executing controller action with signature {ActionSignature}",
            ["RouteData", "ActionSignature"]),
        new("Microsoft.EntityFrameworkCore.Database.Command", 1,
            "Executed DbCommand ({Elapsed}ms) [Parameters=[{Parameters}], CommandType='Text', CommandTimeout='30'] {CommandText}",
            ["Elapsed", "Parameters", "CommandText"]),
        new("Microsoft.EntityFrameworkCore.Database.Command", 3,
            "Executed DbCommand ({Elapsed}ms) [Parameters=[{Parameters}], CommandType='Text', CommandTimeout='30'] {CommandText}",
            ["Elapsed", "Parameters", "CommandText"]),
        new("Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler", 3,
            "Bearer was not authenticated. Failure message: {FailureMessage}", ["FailureMessage"]),
        new("Acme.Web.Security.SignInService", 3,
            "User {UserName} failed login from {RemoteIp} using {UserAgent}", ["UserName", "RemoteIp", "UserAgent"]),
        new("Acme.Web.Security.SignInService", 2,
            "User {UserId} signed in from {RemoteIp}", ["UserId", "RemoteIp"]),
        new("Acme.Orders.OrderService", 2,
            "Order {OrderId} placed by customer {CustomerId} for {Amount} EUR", ["OrderId", "CustomerId", "Amount"]),
        new("Acme.Orders.OrderService", 3,
            "Order {OrderId} could not be reserved: only {Available} of {Requested} units of {Sku} in stock",
            ["OrderId", "Available", "Requested", "Sku"]),
        new("Acme.Orders.OrderService", 4,
            "Order {OrderId} failed to persist after {Attempts} attempts", ["OrderId", "Attempts"]),
        new("Acme.Billing.InvoiceJob", 2,
            "Generated {Count} invoices in {Elapsed}ms", ["Count", "Elapsed"]),
        new("Acme.Billing.PaymentGatewayClient", 4,
            "Payment gateway returned {StatusCode} for transaction {TransactionId}", ["StatusCode", "TransactionId"]),
        new("Acme.Billing.PaymentGatewayClient", 3,
            "Retrying {Operation} in {Delay}ms after transient failure", ["Operation", "Delay"]),
        new("Acme.Integration.WebhookDispatcher", 2,
            "Dispatched webhook {WebhookId} to {TargetUrl}, response {StatusCode} in {Elapsed}ms",
            ["WebhookId", "TargetUrl", "StatusCode", "Elapsed"]),
        new("Acme.Integration.WebhookDispatcher", 4,
            "Webhook {WebhookId} to {TargetUrl} gave up after {Attempts} attempts", ["WebhookId", "TargetUrl", "Attempts"]),
        new("System.Net.Http.HttpClient.Catalog.ClientHandler", 0,
            "Sending HTTP request {Method} {Uri}", ["Method", "Uri"]),
        new("System.Net.Http.HttpClient.Catalog.ClientHandler", 0,
            "Received HTTP response headers after {Elapsed}ms - {StatusCode}", ["Elapsed", "StatusCode"]),
        new("Quartz.Core.JobRunShell", 1, "Job {JobKey} executed in {Elapsed}ms", ["JobKey", "Elapsed"]),
        new("Acme.Infrastructure.Cache.RedisCache", 1, "Cache {Outcome} for key {CacheKey}", ["Outcome", "CacheKey"]),
        new("Acme.Infrastructure.Startup", 2, "Application started. Listening on {Urls}", ["Urls"]),
        new("Acme.Infrastructure.Health", 3, "Health check {CheckName} degraded: {Reason}", ["CheckName", "Reason"]),
        new("Acme.Infrastructure.Health", 5, "Host terminated unexpectedly: {Reason}", ["Reason"]),
        // Plain sentences with no placeholders — VISION.md insists these are
        // complete entries, not degraded ones.
        new("Acme.Infrastructure.Maintenance", 2, "Nightly maintenance window opened", []),
        new("Acme.Infrastructure.Maintenance", 2, "Disk full on /dev/sda1", []),
        new("Acme.Orders.OrderService", 1, "Order projection rebuilt from scratch", []),
    ];

    static readonly string[] Paths =
    [
        "/api/orders", "/api/orders/4711", "/api/orders/9d3f1c/items", "/api/customers/8821",
        "/api/catalog/products", "/api/catalog/products/SKU-44192", "/api/billing/invoices",
        "/api/auth/token", "/healthz", "/metrics", "/api/webhooks/incoming/stripe",
        "/api/search", "/api/reports/monthly", "/swagger/index.html", "/api/orders/4711/cancel",
    ];

    static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.4 Safari/605.1.15",
        "curl/8.7.1", "PostmanRuntime/7.43.0", "python-requests/2.32.3",
        "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
    ];

    static readonly string[] SqlCommands =
    [
        "SELECT o.\"Id\", o.\"CustomerId\", o.\"PlacedAt\", o.\"Status\", o.\"Total\" FROM \"Orders\" AS o WHERE o.\"CustomerId\" = @__customerId_0 ORDER BY o.\"PlacedAt\" DESC LIMIT @__p_1",
        "INSERT INTO \"OrderItems\" (\"Id\", \"OrderId\", \"Sku\", \"Quantity\", \"UnitPrice\") VALUES (@p0, @p1, @p2, @p3, @p4)",
        "UPDATE \"Inventory\" SET \"Available\" = \"Available\" - @p0 WHERE \"Sku\" = @p1 AND \"Available\" >= @p0",
        "SELECT COUNT(*)::int FROM \"Invoices\" AS i WHERE i.\"IssuedAt\" >= @__from_0 AND i.\"IssuedAt\" < @__to_1",
        "SELECT c.\"Id\", c.\"Email\", c.\"DisplayName\", a.\"Street\", a.\"City\", a.\"PostalCode\", a.\"Country\" FROM \"Customers\" AS c LEFT JOIN \"Addresses\" AS a ON c.\"Id\" = a.\"CustomerId\" WHERE c.\"Email\" = @__email_0",
    ];

    static readonly string[] Exceptions =
    [
        """
        System.NullReferenceException: Object reference not set to an instance of an object.
           at Acme.Orders.OrderService.Reserve(Order order, CancellationToken ct) in /src/Acme.Orders/OrderService.cs:line 142
           at Acme.Orders.OrderService.PlaceAsync(PlaceOrderCommand command, CancellationToken ct) in /src/Acme.Orders/OrderService.cs:line 88
           at Acme.Web.Controllers.OrdersController.Post(PlaceOrderRequest request, CancellationToken ct) in /src/Acme.Web/Controllers/OrdersController.cs:line 51
           at lambda_method42(Closure, Object, Object[])
           at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskOfIActionResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)
        """,
        """
        Npgsql.NpgsqlException (0x80004005): Exception while reading from stream
         ---> System.TimeoutException: Timeout during reading attempt
           at Npgsql.Internal.NpgsqlReadBuffer.<Ensure>g__EnsureLong|54_0(NpgsqlReadBuffer buffer, Int32 count, Boolean async, Boolean readingNotifications)
           at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)
           at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)
           at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReader(RelationalCommandParameterObject parameterObject)
           --- End of inner exception stack trace ---
        """,
        """
        System.Text.Json.JsonException: '<' is an invalid start of a value. Path: $ | LineNumber: 0 | BytePositionInLine: 0.
           at System.Text.Json.ThrowHelper.ReThrowWithPath(ReadStack& state, JsonReaderException ex)
           at System.Text.Json.Serialization.JsonConverter`1.ReadCore(Utf8JsonReader& reader, ReadStack& state, JsonSerializerOptions options)
           at Acme.Billing.PaymentGatewayClient.ParseAsync(HttpResponseMessage response) in /src/Acme.Billing/PaymentGatewayClient.cs:line 96
        """,
        """
        System.IO.IOException: No space left on device
           at System.IO.RandomAccess.WriteAtOffset(SafeFileHandle handle, ReadOnlySpan`1 buffer, Int64 fileOffset)
           at System.IO.Strategies.BufferedFileStreamStrategy.Flush(Boolean flushToDisk)
           at Serilog.Sinks.File.FileSink.EmitOrOverflow(LogEvent logEvent)
        """,
    ];

    static readonly string[] Outcomes = ["hit", "miss"];
    static readonly string[] Methods = ["GET", "POST", "PUT", "DELETE", "PATCH"];
    static readonly string[] ContentTypes = ["application/json; charset=utf-8", "text/plain", "application/problem+json"];

    readonly Random _random;
    readonly Guid[] _projects;
    readonly string[][] _instances;
    long _nextId;

    public Corpus(int projectCount, int seed = 20260806)
    {
        _random = new Random(seed);
        _projects = new Guid[projectCount];
        _instances = new string[projectCount][];
        for (var i = 0; i < projectCount; i++)
        {
            var bytes = new byte[16];
            new Random(seed + i).NextBytes(bytes);
            _projects[i] = new Guid(bytes);

            // One to four replicas per project — the reason `instance` exists.
            var replicas = 1 + (i % 4);
            _instances[i] = new string[replicas];
            for (var r = 0; r < replicas; r++)
                _instances[i][r] = $"svc-{i:D2}-{r}{Convert.ToHexString(bytes, 0, 2).ToLowerInvariant()}";
        }
    }

    public IReadOnlyList<Guid> Projects => _projects;

    /// Entries come out in ascending receipt time, which is how they actually
    /// arrive; generating them in random order would flatter every btree.
    public IEnumerable<Entry> Generate(long count, DateTime from, DateTime to)
    {
        var span = (to - from).TotalSeconds;
        for (long i = 0; i < count; i++)
        {
            var receipt = from.AddSeconds(span * i / count);
            var projectIndex = WeightedProject();
            var template = PickTemplate();

            // Most entries are delivered within a couple of seconds; a few
            // arrive late, which is exactly the case ADR 0009 cares about.
            var lag = _random.NextDouble() < 0.01
                ? TimeSpan.FromSeconds(_random.Next(60, 7200))
                : TimeSpan.FromMilliseconds(_random.Next(10, 2500));
            var eventTime = receipt - lag;

            var props = new Dictionary<string, object?>();
            var rendered = Render(template, props);

            var withException = template.Level >= 4 ? _random.NextDouble() < 0.55 : _random.NextDouble() < 0.004;
            var exception = withException ? Exceptions[_random.Next(Exceptions.Length)] : null;

            // Enrichers attach properties no placeholder collected — stored and
            // displayed, deliberately not searchable (ADR 0010).
            props["MachineName"] = _instances[projectIndex][0];
            props["ThreadId"] = _random.Next(4, 64);
            props["ProcessId"] = 1;
            props["EnvironmentName"] = "Production";
            props["Application"] = $"acme-service-{projectIndex:D2}";

            var traced = _random.NextDouble() < 0.75;

            yield return new Entry(
                Id: ++_nextId,
                ProjectId: _projects[projectIndex],
                EventTime: eventTime,
                ReceiptTime: receipt,
                Level: template.Level,
                LoggerName: template.LoggerName,
                Instance: _instances[projectIndex][_random.Next(_instances[projectIndex].Length)],
                TraceId: traced ? RandomHex(32) : null,
                SpanId: traced ? RandomHex(16) : null,
                MessageTemplate: template.Text,
                RenderedMessage: rendered,
                Exception: exception,
                Properties: JsonSerializer.Serialize(props));
        }
    }

    /// Traffic is not spread evenly over projects: a couple of them produce most
    /// of the entries, which is what a real installation looks like.
    int WeightedProject()
    {
        var r = _random.NextDouble();
        if (r < 0.45) return _random.Next(0, Math.Min(2, _projects.Length));
        if (r < 0.80) return _random.Next(0, Math.Min(6, _projects.Length));
        return _random.Next(_projects.Length);
    }

    Template PickTemplate()
    {
        // Level mix aimed at a normal production service: mostly Information,
        // a healthy amount of Debug, few errors.
        var r = _random.NextDouble();
        short wanted = r switch
        {
            < 0.05 => 0,
            < 0.25 => 1,
            < 0.86 => 2,
            < 0.96 => 3,
            < 0.995 => 4,
            _ => 5,
        };
        var candidates = Templates.Where(t => t.Level == wanted).ToArray();
        if (candidates.Length == 0) candidates = Templates;
        return candidates[_random.Next(candidates.Length)];
    }

    string Render(Template template, Dictionary<string, object?> props)
    {
        if (template.Props.Length == 0) return template.Text;

        var builder = new StringBuilder(template.Text);
        foreach (var name in template.Props)
        {
            var value = ValueFor(name);
            props[name] = value;
            builder.Replace("{" + name + "}", value?.ToString() ?? "null");
        }
        return builder.ToString();
    }

    object? ValueFor(string name) => name switch
    {
        "Protocol" => "HTTP/1.1",
        "Method" => Methods[_random.Next(Methods.Length)],
        "Scheme" => "https",
        "Host" => "api.acme.example",
        "Path" => Paths[_random.Next(Paths.Length)],
        "QueryString" => _random.NextDouble() < 0.35 ? $"?page={_random.Next(1, 40)}&size=50" : "",
        "StatusCode" => _random.NextDouble() < 0.9 ? 200 : new[] { 400, 401, 404, 409, 422, 500, 502 }[_random.Next(7)],
        "ContentLength" => _random.Next(0, 90000),
        "ContentType" => ContentTypes[_random.Next(ContentTypes.Length)],
        "Elapsed" => Math.Round(_random.NextDouble() * 2400, 4),
        "Delay" => _random.Next(50, 5000),
        "EndpointName" => "Acme.Web.Controllers.OrdersController.Post (Acme.Web)",
        "RouteData" => "{action = \"Post\", controller = \"Orders\"}",
        "ActionSignature" => "System.Threading.Tasks.Task`1[Microsoft.AspNetCore.Mvc.IActionResult] Post(Acme.Web.Contracts.PlaceOrderRequest, System.Threading.CancellationToken)",
        "CommandText" => SqlCommands[_random.Next(SqlCommands.Length)],
        "Parameters" => $"@__customerId_0='{_random.Next(1000, 99999)}', @__p_1='50'",
        "FailureMessage" => "IDX10223: Lifetime validation failed. The token is expired.",
        "UserName" => $"user{_random.Next(1, 5000)}@example.com",
        "UserId" => _random.Next(1, 90000),
        "RemoteIp" => $"203.0.113.{_random.Next(1, 255)}",
        "UserAgent" => UserAgents[_random.Next(UserAgents.Length)],
        "OrderId" => Guid.NewGuid().ToString(),
        "CustomerId" => _random.Next(1000, 99999),
        "Amount" => Math.Round(_random.NextDouble() * 990 + 10, 2),
        "Available" => _random.Next(0, 5),
        "Requested" => _random.Next(5, 40),
        "Sku" => $"SKU-{_random.Next(10000, 99999)}",
        "Attempts" => _random.Next(2, 8),
        "Count" => _random.Next(1, 4000),
        "TransactionId" => $"txn_{RandomHex(18)}",
        "Operation" => "CapturePayment",
        "WebhookId" => Guid.NewGuid().ToString(),
        "TargetUrl" => $"https://hooks.partner{_random.Next(1, 30)}.example/acme/{RandomHex(8)}",
        "Uri" => $"https://catalog.internal/api/products/SKU-{_random.Next(10000, 99999)}",
        "JobKey" => $"DEFAULT.{new[] { "InvoiceJob", "ReminderJob", "CleanupJob" }[_random.Next(3)]}",
        "Outcome" => Outcomes[_random.Next(Outcomes.Length)],
        "CacheKey" => $"catalog:product:SKU-{_random.Next(10000, 99999)}",
        "Urls" => "https://0.0.0.0:8443, http://0.0.0.0:8080",
        "CheckName" => new[] { "database", "redis", "gateway" }[_random.Next(3)],
        "Reason" => "Connection refused after 3 attempts",
        _ => "unset",
    };

    string RandomHex(int length)
    {
        var bytes = new byte[length / 2];
        _random.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
