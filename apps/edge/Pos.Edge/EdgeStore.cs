using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Sync.Contracts;

namespace Pos.Edge;

public sealed record EdgeBinding(Guid TenantId, Guid TerminalId, Guid DeviceId, string Server);
public sealed record QueuedSale(Guid Id, string Json, int Attempts);
public sealed record EdgeStatus(EdgeBinding? Binding, long Cursor, int Products, int Pending, int InFlight, int Conflicts, int Synced);

public sealed class EdgeStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly string _connectionString;

    public EdgeStore(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 30 }.ToString();
        using var db = Open();
        Execute(db, null, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS products (id TEXT PRIMARY KEY, version INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS taxes (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inbox (id TEXT PRIMARY KEY, sequence INTEGER UNIQUE NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sale_queue (
              id TEXT PRIMARY KEY, json TEXT NOT NULL,
              state TEXT NOT NULL CHECK(state IN ('Pending','InFlight','Conflict','Synced')),
              attempts INTEGER NOT NULL DEFAULT 0, next_attempt TEXT NOT NULL, lease_until TEXT,
              response_json TEXT, error TEXT);
            CREATE INDEX IF NOT EXISTS ix_queue_due ON sale_queue(state, next_attempt);
            """);
    }

    public Guid DeviceId
    {
        get
        {
            using var db = Open();
            Execute(db, null, "INSERT OR IGNORE INTO metadata VALUES ('device', $value)", ("$value", Guid.NewGuid().ToString()));
            return Guid.Parse(Value(db, null, "device")!);
        }
    }

    public EdgeBinding? Binding
    {
        get { using var db = Open(); var value = Value(db, null, "binding"); return value is null ? null : JsonSerializer.Deserialize<EdgeBinding>(value, JsonOptions); }
    }
    public long Cursor { get { using var db = Open(); return CursorOf(db, null); } }

    public void Initialize(SyncSnapshot snapshot, string server)
    {
        if (snapshot.SchemaVersion != 1 || snapshot.DeviceId != DeviceId || snapshot.Cursor < 0)
            throw new InvalidOperationException("Unsupported or invalid sync snapshot.");
        var binding = new EdgeBinding(snapshot.TenantId, snapshot.TerminalId, snapshot.DeviceId, server);
        var previous = Binding;
        if (previous is not null && previous != binding) throw new InvalidOperationException("This database is already bound to another tenant, terminal or server.");
        using var db = Open();
        using var tx = db.BeginTransaction();
        var storedBinding = Value(db, tx, "binding");
        if (storedBinding is not null && JsonSerializer.Deserialize<EdgeBinding>(storedBinding, JsonOptions) != binding)
            throw new InvalidOperationException("This database is already bound to another context.");
        Execute(db, tx, "DELETE FROM products; DELETE FROM taxes; DELETE FROM inbox;");
        foreach (var product in snapshot.Products) SaveProduct(db, tx, product);
        foreach (var tax in snapshot.Taxes)
            Execute(db, tx, "INSERT INTO taxes VALUES ($id, $json)", ("$id", tax.Id.ToString()), ("$json", JsonSerializer.Serialize(tax, JsonOptions)));
        Put(db, tx, "binding", JsonSerializer.Serialize(binding, JsonOptions));
        Put(db, tx, "cursor", snapshot.Cursor.ToString(CultureInfo.InvariantCulture));
        tx.Commit();
    }

    public void Apply(ChangePage page)
    {
        if (Binding is null) throw new InvalidOperationException("Bootstrap is required.");
        if (page.SchemaVersion != 1) throw new InvalidOperationException("Unsupported feed schema.");
        using var db = Open();
        using var tx = db.BeginTransaction();
        var before = CursorOf(db, tx);
        if (page.NextCursor < before) throw new InvalidOperationException("Cursor regression.");
        var last = before;
        foreach (var change in page.Changes)
        {
            if (change.SchemaVersion != 1) throw new InvalidOperationException("Unsupported event schema.");
            if (change.Sequence <= before) continue;
            if (change.Sequence <= last || change.Sequence > page.NextCursor) throw new InvalidOperationException("Unordered feed.");
            var catalogEvent = change.EventType is "ProductCreated" or "ProductUpdated" or "ProductActivated" or "ProductDeactivated";
            var inboxJson = catalogEvent ? JsonSerializer.Serialize(change, JsonOptions)
                : JsonSerializer.Serialize(new { change.Id, change.Sequence, change.EventId, change.SchemaVersion, change.EventType }, JsonOptions);
            Execute(db, tx, "INSERT OR IGNORE INTO inbox VALUES ($id, $seq, $json)",
                ("$id", change.EventId.ToString()), ("$seq", change.Sequence), ("$json", inboxJson));
            if (catalogEvent)
            {
                var product = change.Payload.Deserialize<CatalogProduct>(JsonOptions) ?? throw new InvalidOperationException("Missing product payload.");
                if (product.Id == Guid.Empty || product.Version < 1) throw new InvalidOperationException("Invalid product payload.");
                SaveProduct(db, tx, product);
            }
            last = change.Sequence;
        }
        if (last != page.NextCursor || (page.HasMore && last == before)) throw new InvalidOperationException("Invalid page checkpoint.");
        Put(db, tx, "cursor", page.NextCursor.ToString(CultureInfo.InvariantCulture));
        tx.Commit();
    }

    public void Enqueue(string json)
    {
        if (json.Length > 1_000_000) throw new InvalidOperationException("Sale command is too large.");
        var binding = Binding ?? throw new InvalidOperationException("Bootstrap before queueing a sale.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckFields(root, ["locationId", "terminalId", "customerId", "clientGeneratedId", "lines", "payments"]);
        foreach (var line in root.GetProperty("lines").EnumerateArray())
            CheckFields(line, ["productId", "quantity", "discountAmount"]);
        foreach (var payment in root.GetProperty("payments").EnumerateArray())
            CheckFields(payment, ["method", "amount", "amountTendered", "reference"]);
        var id = root.GetProperty("clientGeneratedId").GetGuid();
        if (id == Guid.Empty || root.GetProperty("terminalId").GetGuid() != binding.TerminalId)
            throw new InvalidOperationException("Command must have a UUID and match this terminal.");
        var canonical = JsonSerializer.Serialize(root, JsonOptions);
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var check = Command(db, tx, "SELECT json FROM sale_queue WHERE id = $id", ("$id", id.ToString()));
        var existing = check.ExecuteScalar() as string;
        if (existing is not null && existing != canonical) throw new InvalidOperationException("A queued UUID cannot be reused with a different payload.");
        Execute(db, tx, "INSERT OR IGNORE INTO sale_queue(id,json,state,next_attempt) VALUES ($id,$json,'Pending',$now)",
            ("$id", id.ToString()), ("$json", canonical), ("$now", Stamp(DateTimeOffset.UtcNow)));
        tx.Commit();
    }

    public QueuedSale? Claim(DateTimeOffset now)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var query = Command(db, tx, """
            SELECT id,json,attempts FROM sale_queue
            WHERE (state='Pending' AND next_attempt <= $now) OR (state='InFlight' AND lease_until <= $now)
            ORDER BY next_attempt,id LIMIT 1
            """, ("$now", Stamp(now)));
        QueuedSale? result;
        using (var reader = query.ExecuteReader())
            result = reader.Read() ? new QueuedSale(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2) + 1) : null;
        if (result is null) return null;
        Execute(db, tx, "UPDATE sale_queue SET state='InFlight', attempts=attempts+1, lease_until=$lease WHERE id=$id",
            ("$lease", Stamp(now.AddMinutes(2))), ("$id", result.Id.ToString()));
        tx.Commit();
        return result;
    }

    public void Finish(QueuedSale sale, string state, string? response = null, string? error = null, DateTimeOffset? retryAt = null)
    {
        if (state is not ("Pending" or "Conflict" or "Synced")) throw new ArgumentException("Invalid queue transition.", nameof(state));
        using var db = Open();
        Execute(db, null, """
            UPDATE sale_queue SET state=$state,response_json=$response,error=$error,next_attempt=$retry,lease_until=NULL
            WHERE id=$id AND state='InFlight' AND attempts=$attempts
            """, ("$state", state), ("$response", response), ("$error", error),
            ("$retry", Stamp(retryAt ?? DateTimeOffset.UtcNow)), ("$id", sale.Id.ToString()), ("$attempts", sale.Attempts));
    }

    public CatalogProduct? Product(Guid id)
    {
        using var db = Open();
        using var query = Command(db, null, "SELECT json FROM products WHERE id=$id", ("$id", id.ToString()));
        var json = query.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<CatalogProduct>(json, JsonOptions);
    }

    public EdgeStatus Status()
    {
        using var db = Open();
        int Count(string state) { using var cmd = Command(db, null, "SELECT count(*) FROM sale_queue WHERE state=$state", ("$state", state)); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
        using var products = Command(db, null, "SELECT count(*) FROM products");
        return new EdgeStatus(Binding, Cursor, Convert.ToInt32(products.ExecuteScalar(), CultureInfo.InvariantCulture), Count("Pending"), Count("InFlight"), Count("Conflict"), Count("Synced"));
    }

    private SqliteConnection Open() { var db = new SqliteConnection(_connectionString); db.Open(); return db; }
    private static void CheckFields(JsonElement item, string[] allowed)
    {
        if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Unsupported sale field. Only the sale contract may be queued.");
    }
    private static string Stamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static long CursorOf(SqliteConnection db, SqliteTransaction? tx) => long.Parse(Value(db, tx, "cursor") ?? "0", CultureInfo.InvariantCulture);
    private static string? Value(SqliteConnection db, SqliteTransaction? tx, string key)
    { using var cmd = Command(db, tx, "SELECT value FROM metadata WHERE key=$key", ("$key", key)); return cmd.ExecuteScalar() as string; }
    private static void Put(SqliteConnection db, SqliteTransaction tx, string key, string value) =>
        Execute(db, tx, "INSERT INTO metadata VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$key", key), ("$value", value));
    private static void SaveProduct(SqliteConnection db, SqliteTransaction tx, CatalogProduct product) =>
        Execute(db, tx, """
            INSERT INTO products VALUES ($id,$version,$json)
            ON CONFLICT(id) DO UPDATE SET version=excluded.version,json=excluded.json WHERE excluded.version > products.version
            """, ("$id", product.Id.ToString()), ("$version", product.Version), ("$json", JsonSerializer.Serialize(product, JsonOptions)));
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var parameter in parameters) cmd.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return cmd;
    }
    private static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    { using var cmd = Command(db, tx, sql, parameters); cmd.ExecuteNonQuery(); }
}
