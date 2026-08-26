using System.Globalization;
using System.Text.Json;
using Faxe.Core;
using Faxe.Core.Models;
using Microsoft.Data.Sqlite;

namespace Faxe.Persistence;

/// <summary>SQLite-backed store for tasks, templates, and users.</summary>
public sealed class FaxeStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _gate = new();
    private long _taskId;
    private long _templateId;

    public FaxeStore(string connectionString = "Data Source=faxe.db")
    {
        _db = new SqliteConnection(connectionString);
        _db.Open();
        InitSchema();
        _taskId = ScalarLong("SELECT COALESCE(MAX(id),0) FROM tasks");
        _templateId = ScalarLong("SELECT COALESCE(MAX(id),0) FROM templates");
        if (ListUsers().Count == 0)
            SaveUser(new UserRecord { Name = "admin", Password = "admin", Role = "admin" });
    }

    private void InitSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE,
              dfs TEXT NOT NULL,
              definition TEXT,
              changed INTEGER,
              last_start INTEGER,
              last_stop INTEGER,
              permanent INTEGER,
              template TEXT,
              template_vars TEXT,
              tags TEXT,
              group_name TEXT,
              group_leader INTEGER
            );
            CREATE TABLE IF NOT EXISTS templates (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE,
              dfs TEXT NOT NULL,
              definition TEXT,
              changed INTEGER,
              vars TEXT
            );
            CREATE TABLE IF NOT EXISTS users (
              name TEXT PRIMARY KEY,
              password TEXT NOT NULL,
              role TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public List<TaskRecord> GetAllTasks()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM tasks ORDER BY id";
            using var r = cmd.ExecuteReader();
            var list = new List<TaskRecord>();
            while (r.Read()) list.Add(ReadTask(r));
            return list;
        }
    }

    public TaskRecord? GetTask(string idOrName)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            if (long.TryParse(idOrName, out var id))
            {
                cmd.CommandText = "SELECT * FROM tasks WHERE id=$id OR name=$name LIMIT 1";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$name", idOrName);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM tasks WHERE name=$name LIMIT 1";
                cmd.Parameters.AddWithValue("$name", idOrName);
            }
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadTask(r) : null;
        }
    }

    public TaskRecord SaveTask(TaskRecord task)
    {
        lock (_gate)
        {
            if (task.Id == 0) task.Id = ++_taskId;
            task.Changed = FaxeTime.Now();
            task.Tags = task.Tags.OrderBy(t => t, StringComparer.Ordinal).ToList();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO tasks (id,name,dfs,definition,changed,last_start,last_stop,permanent,template,template_vars,tags,group_name,group_leader)
                VALUES ($id,$name,$dfs,$def,$changed,$ls,$lp,$perm,$tpl,$tplv,$tags,$grp,$gl)
                ON CONFLICT(id) DO UPDATE SET
                  name=excluded.name, dfs=excluded.dfs, definition=excluded.definition, changed=excluded.changed,
                  last_start=excluded.last_start, last_stop=excluded.last_stop, permanent=excluded.permanent,
                  template=excluded.template, template_vars=excluded.template_vars, tags=excluded.tags,
                  group_name=excluded.group_name, group_leader=excluded.group_leader
                """;
            BindTask(cmd, task);
            cmd.ExecuteNonQuery();
            return task;
        }
    }

    public bool DeleteTask(string idOrName)
    {
        var t = GetTask(idOrName);
        if (t is null) return false;
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM tasks WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", t.Id);
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    public void ResetTasks()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM tasks";
            cmd.ExecuteNonQuery();
            _taskId = 0;
        }
    }

    public List<TemplateRecord> GetAllTemplates()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM templates ORDER BY id";
            using var r = cmd.ExecuteReader();
            var list = new List<TemplateRecord>();
            while (r.Read()) list.Add(ReadTemplate(r));
            return list;
        }
    }

    public TemplateRecord? GetTemplate(string idOrName)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            if (long.TryParse(idOrName, out var id))
            {
                cmd.CommandText = "SELECT * FROM templates WHERE id=$id OR name=$name LIMIT 1";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$name", idOrName);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM templates WHERE name=$name LIMIT 1";
                cmd.Parameters.AddWithValue("$name", idOrName);
            }
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadTemplate(r) : null;
        }
    }

    public TemplateRecord SaveTemplate(TemplateRecord t)
    {
        lock (_gate)
        {
            if (t.Id == 0) t.Id = ++_templateId;
            t.Changed = FaxeTime.Now();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO templates (id,name,dfs,definition,changed,vars)
                VALUES ($id,$name,$dfs,$def,$changed,$vars)
                ON CONFLICT(id) DO UPDATE SET
                  name=excluded.name, dfs=excluded.dfs, definition=excluded.definition,
                  changed=excluded.changed, vars=excluded.vars
                """;
            cmd.Parameters.AddWithValue("$id", t.Id);
            cmd.Parameters.AddWithValue("$name", t.Name);
            cmd.Parameters.AddWithValue("$dfs", t.Dfs);
            cmd.Parameters.AddWithValue("$def", JsonSerializer.Serialize(t.Definition));
            cmd.Parameters.AddWithValue("$changed", t.Changed);
            cmd.Parameters.AddWithValue("$vars", JsonSerializer.Serialize(t.Vars));
            cmd.ExecuteNonQuery();
            return t;
        }
    }

    public bool DeleteTemplate(string idOrName)
    {
        var t = GetTemplate(idOrName);
        if (t is null) return false;
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM templates WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", t.Id);
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    public void ResetTemplates()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM templates";
            cmd.ExecuteNonQuery();
            _templateId = 0;
        }
    }

    public List<UserRecord> ListUsers()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT name,password,role FROM users";
            using var r = cmd.ExecuteReader();
            var list = new List<UserRecord>();
            while (r.Read())
                list.Add(new UserRecord { Name = r.GetString(0), Password = r.GetString(1), Role = r.GetString(2) });
            return list;
        }
    }

    public bool HasUserWithPw(string name, string password)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM users WHERE name=$n AND password=$p";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$p", password);
            return cmd.ExecuteScalar() is not null;
        }
    }

    public void SaveUser(UserRecord user)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO users (name,password,role) VALUES ($n,$p,$r)
                ON CONFLICT(name) DO UPDATE SET password=excluded.password, role=excluded.role
                """;
            cmd.Parameters.AddWithValue("$n", user.Name);
            cmd.Parameters.AddWithValue("$p", user.Password);
            cmd.Parameters.AddWithValue("$r", user.Role);
            cmd.ExecuteNonQuery();
        }
    }

    public bool DeleteUser(string name)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM users WHERE name=$n";
            cmd.Parameters.AddWithValue("$n", name);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public List<string> GetAllTags() =>
        GetAllTasks().SelectMany(t => t.Tags).Distinct(StringComparer.Ordinal).OrderBy(t => t).ToList();

    private long ScalarLong(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);
    }

    private static void BindTask(SqliteCommand cmd, TaskRecord task)
    {
        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.Parameters.AddWithValue("$name", task.Name);
        cmd.Parameters.AddWithValue("$dfs", task.Dfs);
        cmd.Parameters.AddWithValue("$def", JsonSerializer.Serialize(task.Definition));
        cmd.Parameters.AddWithValue("$changed", task.Changed);
        cmd.Parameters.AddWithValue("$ls", (object?)task.LastStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lp", (object?)task.LastStop ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$perm", task.Permanent ? 1 : 0);
        cmd.Parameters.AddWithValue("$tpl", task.Template);
        cmd.Parameters.AddWithValue("$tplv", JsonSerializer.Serialize(task.TemplateVars));
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(task.Tags));
        cmd.Parameters.AddWithValue("$grp", (object?)task.Group ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gl", task.GroupLeader ? 1 : 0);
    }

    private static TaskRecord ReadTask(SqliteDataReader r)
    {
        return new TaskRecord
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Name = r.GetString(r.GetOrdinal("name")),
            Dfs = r.GetString(r.GetOrdinal("dfs")),
            Definition = JsonSerializer.Deserialize<GraphDefinition>(r.GetString(r.GetOrdinal("definition"))),
            Changed = r.GetInt64(r.GetOrdinal("changed")),
            LastStart = r.IsDBNull(r.GetOrdinal("last_start")) ? null : r.GetInt64(r.GetOrdinal("last_start")),
            LastStop = r.IsDBNull(r.GetOrdinal("last_stop")) ? null : r.GetInt64(r.GetOrdinal("last_stop")),
            Permanent = r.GetInt64(r.GetOrdinal("permanent")) == 1,
            Template = r.GetString(r.GetOrdinal("template")),
            TemplateVars = JsonSerializer.Deserialize<Dictionary<string, object?>>(r.GetString(r.GetOrdinal("template_vars")))
                           ?? new Dictionary<string, object?>(),
            Tags = JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("tags"))) ?? new(),
            Group = r.IsDBNull(r.GetOrdinal("group_name")) ? null : r.GetString(r.GetOrdinal("group_name")),
            GroupLeader = r.GetInt64(r.GetOrdinal("group_leader")) == 1
        };
    }

    private static TemplateRecord ReadTemplate(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Dfs = r.GetString(r.GetOrdinal("dfs")),
        Definition = JsonSerializer.Deserialize<GraphDefinition>(r.GetString(r.GetOrdinal("definition"))),
        Changed = r.GetInt64(r.GetOrdinal("changed")),
        Vars = JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("vars"))) ?? new()
    };

    public void Dispose() => _db.Dispose();
}
