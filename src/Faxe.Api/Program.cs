using System.Text;
using System.Text.Json;
using Faxe.Api.Services;
using Faxe.Core;
using Faxe.Core.Models;
using Faxe.Dfs;
using Faxe.Flow;
using Faxe.Nodes.Components;
using Faxe.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "faxe REST API", Version = "1.0.0" });
});

builder.Services.AddSingleton(_ => new FaxeStore(builder.Configuration.GetConnectionString("Faxe") ?? "Data Source=faxe.db"));
builder.Services.AddSingleton(sp =>
{
    var registry = new NodeRegistry();
    registry.RegisterAssembly(typeof(ValueEmitterNode).Assembly);
    return registry;
});
builder.Services.AddSingleton<FlowRuntime>();
builder.Services.AddHostedService<Faxe.Api.FlowRuntimeLifetime>();
builder.Services.AddSingleton(sp =>
{
    var registry = sp.GetRequiredService<NodeRegistry>();
    return new DfsCompiler(registry.Names);
});
builder.Services.AddSingleton<FaxeService>();

var allowAnonymous = builder.Configuration.GetValue("Faxe:AllowAnonymous", true);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsOptions(ctx.Request.Method))
    {
        ctx.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        ctx.Response.Headers.Append("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
        ctx.Response.Headers.Append("Access-Control-Allow-Headers", "Authorization,Content-Type");
        ctx.Response.StatusCode = 200;
        return;
    }
    ctx.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    await next();
});

app.Use(async (ctx, next) =>
{
    if (allowAnonymous)
    {
        await next();
        return;
    }

    var store = ctx.RequestServices.GetRequiredService<FaxeStore>();
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(auth["Basic ".Length..].Trim()));
        var parts = raw.Split(':', 2);
        if (parts.Length == 2 && store.HasUserWithPw(parts[0], parts[1]))
        {
            await next();
            return;
        }
    }

    ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"faxe\"";
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
});

static IResult OkMsg(string msg) => Results.Json(new { success = true, message = msg });
static IResult ErrMsg(string msg, int code = 400) => Results.Json(new { success = false, message = msg }, statusCode: code);

// ---- users ----
app.MapGet("/v1/users", (FaxeStore store) =>
    Results.Json(store.ListUsers().Select(u => new { name = u.Name, pass = u.Password, role = u.Role })));

app.MapPost("/v1/user/add", async (HttpRequest req, FaxeStore store) =>
{
    var form = await req.ReadFormAsync();
    var name = form["name"].ToString();
    var pass = form["pass"].ToString();
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pass))
        return ErrMsg("name and pass required");
    store.SaveUser(new UserRecord { Name = name, Password = pass });
    return OkMsg("user added/updated");
});

app.MapDelete("/v1/user/delete/{username}", (string username, FaxeStore store) =>
    store.DeleteUser(username) ? OkMsg("deleted") : ErrMsg("not found", 404));

// ---- config / lang / stats ----
app.MapGet("/v1/config", () => Results.Json(new { runtime = "dotnet", version = "1.5.10" }));
app.MapGet("/v1/config_all", (IConfiguration cfg) => Results.Json(cfg.AsEnumerable().ToDictionary(kv => kv.Key, kv => kv.Value)));
app.MapGet("/v1/loglevels", () => Results.Json(new[] { "debug", "info", "notice", "warning", "error" }));
app.MapPost("/v1/loglevel/{backend}", (string backend) => OkMsg($"loglevel set for {backend}"));

app.MapGet("/v1/lang/nodes", (NodeRegistry reg) => Results.Json(reg.DescribeAll()));
app.MapGet("/v1/lang/functions", () => Results.Json(new[]
{
    "round", "bool", "str_concat", "max", "min", "abs"
}));

app.MapGet("/v1/stats", () => Results.Json(VmStats()));
app.MapGet("/v1/stats/vm", () => Results.Json(VmStats()));
app.MapGet("/v1/stats/faxe", (FlowRuntime rt, FaxeStore store) => Results.Json(new
{
    tasks = store.GetAllTasks().Count,
    running = rt.RunningNames.Count,
    nodes = rt.Registry.Names.Count
}));
app.MapGet("/v1/stats/nodes", (FlowRuntime rt) => Results.Json(rt.Registry.Names.OrderBy(x => x)));
app.MapGet("/v1/stats/lambdas", () => Results.Json(new { }));
app.MapGet("/v1/stats/s7", () => Results.Json(new { }));
app.MapGet("/v1/stats/reds", () => Results.Json(new { }));
app.MapGet("/v1/stats/msgq", () => Results.Json(new { }));
app.MapGet("/v1/stats/cpu", () => Results.Json(new { processor_count = Environment.ProcessorCount }));
app.MapGet("/v1/stats/python", () => Results.Json(new { }));
app.MapGet("/v1/stats/seq_check", () => Results.Json(new { }));

app.MapGet("/v1/python", () => Results.Json(Array.Empty<string>()));
app.MapGet("/v1/crate/ignore_rules", () => Results.Json(Array.Empty<object>()));
app.MapPost("/v1/crate/ignore_rule", () => OkMsg("ok"));

app.MapPost("/v1/dfs/validate", async (HttpRequest req, DfsCompiler compiler) =>
{
    var dfs = await ReadDfsBody(req);
    var (ok, err, graph) = compiler.TryCompile(dfs);
    return ok
        ? Results.Json(new { success = true, nodes = graph!.Nodes.Count, edges = graph.Edges.Count })
        : ErrMsg(err ?? "invalid");
});

// ---- tags ----
app.MapGet("/v1/tags", (FaxeStore store) => Results.Json(store.GetAllTags()));

// ---- tasks list / bulk ----
app.MapGet("/v1/tasks", (FaxeService svc) =>
    Results.Json(svc.Store.GetAllTasks().Select(t => svc.TaskToMap(t, false))));

app.MapGet("/v1/tasks/running", (FaxeService svc, FlowRuntime rt) =>
    Results.Json(svc.Store.GetAllTasks().Where(t => rt.IsRunning(t.Name)).Select(t => svc.TaskToMap(t, false))));

app.MapGet("/v1/tasks/by_tags/{tags}", (string tags, FaxeService svc) =>
{
    var set = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return Results.Json(svc.Store.GetAllTasks()
        .Where(t => set.Any(tag => t.Tags.Contains(tag)))
        .Select(t => svc.TaskToMap(t, false)));
});

app.MapGet("/v1/tasks/by_template/{template_id}", (string template_id, FaxeService svc) =>
    Results.Json(svc.Store.GetAllTasks().Where(t => t.Template == template_id).Select(t => svc.TaskToMap(t, false))));

app.MapGet("/v1/tasks/by_group/{groupname}", (string groupname, FaxeService svc) =>
    Results.Json(svc.Store.GetAllTasks().Where(t => t.Group == groupname).Select(t => svc.TaskToMap(t, false))));

app.MapGet("/v1/tasks/start_permanent", async (FaxeService svc) =>
{
    foreach (var t in svc.Store.GetAllTasks().Where(t => t.Permanent))
        await svc.StartTaskAsync(t.Name);
    return OkMsg("started permanent tasks");
});

app.MapGet("/v1/tasks/stop_all", async (FaxeService svc) =>
{
    foreach (var t in svc.Store.GetAllTasks())
        await svc.StopTaskAsync(t.Name);
    return OkMsg("stopped all");
});

app.MapGet("/v1/tasks/start/{ids}", async (string ids, FaxeService svc) =>
{
    foreach (var id in ids.Split(','))
        await svc.StartTaskAsync(id.Trim());
    return OkMsg("started");
});
app.MapGet("/v1/tasks/start/by_ids/{ids}", async (string ids, FaxeService svc) =>
{
    foreach (var id in ids.Split(','))
        await svc.StartTaskAsync(id.Trim());
    return OkMsg("started");
});
app.MapGet("/v1/tasks/start/by_tags/{tags}", async (string tags, FaxeService svc) =>
{
    var set = tags.Split(',');
    foreach (var t in svc.Store.GetAllTasks().Where(t => set.Any(tag => t.Tags.Contains(tag.Trim()))))
        await svc.StartTaskAsync(t.Name);
    return OkMsg("started");
});
app.MapGet("/v1/tasks/stop/{ids}", async (string ids, FaxeService svc) =>
{
    foreach (var id in ids.Split(','))
        await svc.StopTaskAsync(id.Trim());
    return OkMsg("stopped");
});
app.MapGet("/v1/tasks/stop/by_ids/{ids}", async (string ids, FaxeService svc) =>
{
    foreach (var id in ids.Split(','))
        await svc.StopTaskAsync(id.Trim());
    return OkMsg("stopped");
});
app.MapGet("/v1/tasks/stop/by_tags/{tags}", async (string tags, FaxeService svc) =>
{
    var set = tags.Split(',');
    foreach (var t in svc.Store.GetAllTasks().Where(t => set.Any(tag => t.Tags.Contains(tag.Trim()))))
        await svc.StopTaskAsync(t.Name);
    return OkMsg("stopped");
});

app.MapGet("/v1/tasks/update", async (FaxeService svc) =>
{
    foreach (var t in svc.Store.GetAllTasks())
        svc.UpdateTask(t.Name, t.Dfs);
    await Task.CompletedTask;
    return OkMsg("updated");
});
app.MapGet("/v1/tasks/update_by_tags/{tags}", (string tags, FaxeService svc) =>
{
    var set = tags.Split(',');
    foreach (var t in svc.Store.GetAllTasks().Where(t => set.Any(tag => t.Tags.Contains(tag.Trim()))))
        svc.UpdateTask(t.Name, t.Dfs);
    return OkMsg("updated");
});
app.MapGet("/v1/tasks/update_by_template/{template}", (string template, FaxeService svc) =>
{
    foreach (var t in svc.Store.GetAllTasks().Where(t => t.Template == template))
        svc.UpdateTask(t.Name, t.Dfs);
    return OkMsg("updated");
});
app.MapGet("/v1/tasks/reset", (FaxeStore store) => { store.ResetTasks(); return OkMsg("reset"); });

app.MapPost("/v1/tasks/import", async (HttpRequest req, FaxeService svc) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    if (doc.RootElement.ValueKind != JsonValueKind.Array) return ErrMsg("expected array");
    foreach (var el in doc.RootElement.EnumerateArray())
    {
        var name = el.GetProperty("name").GetString()!;
        var dfs = el.GetProperty("dfs").GetString()!;
        if (svc.Store.GetTask(name) is null)
            svc.RegisterTask(name, dfs);
        else
            svc.UpdateTask(name, dfs);
    }
    return OkMsg("imported");
});

// ---- single task ----
app.MapMethods("/v1/task", new[] { "POST", "PUT" }, async (HttpRequest req, FaxeService svc) =>
{
    var form = await req.ReadFormAsync();
    var name = form["name"].ToString();
    var dfs = form["dfs"].ToString();
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dfs))
        return ErrMsg("name and dfs required");
    if (svc.Store.GetTask(name) is null)
    {
        var (t, err) = svc.RegisterTask(name, dfs);
        return err is null ? Results.Json(svc.TaskToMap(t!)) : ErrMsg(err);
    }
    var (u, uerr) = svc.UpdateTask(name, dfs);
    return uerr is null ? Results.Json(svc.TaskToMap(u!)) : ErrMsg(uerr);
});

app.MapPost("/v1/task/register", async (HttpRequest req, FaxeService svc) =>
{
    var form = await req.ReadFormAsync();
    var (t, err) = svc.RegisterTask(form["name"].ToString(), form["dfs"].ToString());
    return err is null ? Results.Json(svc.TaskToMap(t!)) : ErrMsg(err);
});

app.MapPost("/v1/task/start_temp", async (HttpRequest req, FaxeService svc) =>
{
    var form = await req.ReadFormAsync();
    var dfs = form["dfs"].ToString();
    var name = "temp-" + Guid.NewGuid().ToString("N");
    var (t, err) = svc.RegisterTask(name, dfs);
    if (err is not null) return ErrMsg(err);
    await svc.StartTaskAsync(name);
    return Results.Json(svc.TaskToMap(t!));
});

app.MapGet("/v1/task/{task_id}", (string task_id, FaxeService svc) =>
{
    var t = svc.Store.GetTask(task_id);
    return t is null ? ErrMsg("not_found", 404) : Results.Json(svc.TaskToMap(t));
});

app.MapGet("/v1/graph/{task_id}", (string task_id, FaxeService svc, FlowRuntime rt) =>
{
    var t = svc.Store.GetTask(task_id);
    if (t is null) return ErrMsg("not_found", 404);
    var g = rt.Get(t.Name);
    return Results.Json(g?.ToGraphMap() ?? t.Definition);
});

app.MapPost("/v1/task/update/{task_id}", async (string task_id, HttpRequest req, FaxeService svc) =>
{
    var dfs = await ReadDfsBody(req);
    var (t, err) = svc.UpdateTask(task_id, dfs);
    return err is null ? Results.Json(svc.TaskToMap(t!)) : ErrMsg(err, err == "not_found" ? 404 : 400);
});

app.MapPost("/v1/task/ping/{task_id}", (string task_id) => OkMsg("pong"));

app.MapDelete("/v1/task/delete/{task_id}", async (string task_id, FaxeService svc) =>
{
    var (ok, err) = await svc.DeleteTaskAsync(task_id);
    return ok ? OkMsg("deleted") : ErrMsg(err ?? "error", err == "not_found" ? 404 : 400);
});

app.MapDelete("/v1/task/delete/{task_id}/force", async (string task_id, FaxeService svc) =>
{
    var (ok, err) = await svc.DeleteTaskAsync(task_id, force: true);
    return ok ? OkMsg("deleted") : ErrMsg(err ?? "error", 404);
});

app.MapPost("/v1/task/add_tags/{task_id}", async (string task_id, HttpRequest req, FaxeService svc) =>
{
    var t = svc.Store.GetTask(task_id);
    if (t is null) return ErrMsg("not_found", 404);
    var form = await req.ReadFormAsync();
    var tags = form["tags"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var tag in tags)
        if (!t.Tags.Contains(tag)) t.Tags.Add(tag);
    svc.Store.SaveTask(t);
    return Results.Json(svc.TaskToMap(t));
});

app.MapPost("/v1/task/remove_tags/{task_id}", async (string task_id, HttpRequest req, FaxeService svc) =>
{
    var t = svc.Store.GetTask(task_id);
    if (t is null) return ErrMsg("not_found", 404);
    var form = await req.ReadFormAsync();
    var tags = form["tags"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    t.Tags = t.Tags.Where(x => !tags.Contains(x)).ToList();
    svc.Store.SaveTask(t);
    return Results.Json(svc.TaskToMap(t));
});

app.MapGet("/v1/task/start/{task_id}/{permanent?}", async (string task_id, string? permanent, FaxeService svc) =>
{
    var perm = string.Equals(permanent, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(permanent, "permanent", StringComparison.OrdinalIgnoreCase);
    var (ok, err) = await svc.StartTaskAsync(task_id, perm);
    return ok ? OkMsg("started") : ErrMsg(err ?? "error", 404);
});

app.MapGet("/v1/task/stop/{task_id}/{permanent?}", async (string task_id, string? permanent, FaxeService svc) =>
{
    var perm = string.Equals(permanent, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(permanent, "permanent", StringComparison.OrdinalIgnoreCase);
    var (ok, err) = await svc.StopTaskAsync(task_id, perm);
    return ok ? OkMsg("stopped") : ErrMsg(err ?? "error", 404);
});

app.MapGet("/v1/task/start_debug/{task_id}/{duration_minutes?}", (string task_id, int? duration_minutes) => OkMsg("debug started"));
app.MapGet("/v1/task/stop_debug/{task_id}", (string task_id) => OkMsg("debug stopped"));
app.MapGet("/v1/task/start_metrics_trace/{task_id}/{duration_minutes?}", (string task_id, int? duration_minutes) => OkMsg("metrics started"));
app.MapGet("/v1/task/stop_metrics_trace/{task_id}", (string task_id) => OkMsg("metrics stopped"));

app.MapGet("/v1/task/start_group/{task_id}/{concurrency}/{permanent?}", async (string task_id, int concurrency, string? permanent, FaxeService svc) =>
{
    var leader = svc.Store.GetTask(task_id);
    if (leader is null) return ErrMsg("not_found", 404);
    leader.Group = leader.Name;
    leader.GroupLeader = true;
    svc.Store.SaveTask(leader);
    await svc.StartTaskAsync(leader.Name, permanent is not null);
    for (var i = 1; i < concurrency; i++)
    {
        var copyName = $"{leader.Name}--{i}";
        if (svc.Store.GetTask(copyName) is null)
        {
            var copy = new TaskRecord
            {
                Name = copyName,
                Dfs = leader.Dfs,
                Definition = leader.Definition,
                Group = leader.Name,
                GroupLeader = false,
                Tags = leader.Tags.ToList()
            };
            svc.Store.SaveTask(copy);
        }
        await svc.StartTaskAsync(copyName);
    }
    return OkMsg("group started");
});

app.MapGet("/v1/task/stop_group/{groupname}/{permanent?}", async (string groupname, string? permanent, FaxeService svc) =>
{
    foreach (var t in svc.Store.GetAllTasks().Where(t => t.Group == groupname))
        await svc.StopTaskAsync(t.Name, permanent is not null);
    return OkMsg("group stopped");
});

app.MapDelete("/v1/task/delete_group/{groupname}", async (string groupname, FaxeService svc) =>
{
    foreach (var t in svc.Store.GetAllTasks().Where(t => t.Group == groupname).ToList())
        await svc.DeleteTaskAsync(t.Name, force: true);
    return OkMsg("group deleted");
});

app.MapGet("/v1/task/group_size/{groupname}/{group_size}", async (string groupname, int group_size, FaxeService svc) =>
{
    var members = svc.Store.GetAllTasks().Where(t => t.Group == groupname).OrderBy(t => t.Name).ToList();
    var leader = members.FirstOrDefault(t => t.GroupLeader) ?? members.FirstOrDefault();
    if (leader is null) return ErrMsg("not_found", 404);
    while (members.Count < group_size)
    {
        var i = members.Count;
        var copyName = $"{leader.Name}--{i}";
        var copy = new TaskRecord
        {
            Name = copyName,
            Dfs = leader.Dfs,
            Definition = leader.Definition,
            Group = groupname,
            GroupLeader = false
        };
        svc.Store.SaveTask(copy);
        members.Add(copy);
    }
    while (members.Count > group_size)
    {
        var last = members.Last(t => !t.GroupLeader);
        await svc.DeleteTaskAsync(last.Name, force: true);
        members.Remove(last);
    }
    return OkMsg("group resized");
});

// ---- templates ----
app.MapGet("/v1/templates", (FaxeService svc) =>
    Results.Json(svc.Store.GetAllTemplates().Select(svc.TemplateToMap)));
app.MapGet("/v1/templates/reset", (FaxeStore store) => { store.ResetTemplates(); return OkMsg("reset"); });
app.MapPost("/v1/templates/import", async (HttpRequest req, FaxeService svc) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    foreach (var el in doc.RootElement.EnumerateArray())
        svc.RegisterTemplate(el.GetProperty("name").GetString()!, el.GetProperty("dfs").GetString()!);
    return OkMsg("imported");
});

app.MapPost("/v1/template/register", async (HttpRequest req, FaxeService svc) =>
{
    var form = await req.ReadFormAsync();
    var (t, err) = svc.RegisterTemplate(form["name"].ToString(), form["dfs"].ToString());
    return err is null ? Results.Json(svc.TemplateToMap(t!)) : ErrMsg(err);
});

app.MapGet("/v1/template/{template_id}", (string template_id, FaxeService svc) =>
{
    var t = svc.Store.GetTemplate(template_id);
    return t is null ? ErrMsg("not_found", 404) : Results.Json(svc.TemplateToMap(t));
});

app.MapDelete("/v1/template/delete/{template_id}", (string template_id, FaxeStore store) =>
    store.DeleteTemplate(template_id) ? OkMsg("deleted") : ErrMsg("not_found", 404));

app.MapPost("/v1/task/from_template/{template_id}/{task_name}", (string template_id, string task_name, FaxeService svc) =>
{
    var (t, err) = svc.TaskFromTemplate(template_id, task_name);
    return err is null ? Results.Json(svc.TaskToMap(t!)) : ErrMsg(err, err == "not_found" ? 404 : 400);
});

app.MapPost("/v1/import/dfs", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return ErrMsg("multipart expected");
    var form = await req.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null) return ErrMsg("file required");
    Directory.CreateDirectory("dfs_import");
    var path = Path.Combine("dfs_import", file.FileName);
    await using (var fs = File.Create(path))
        await file.CopyToAsync(fs);
    return OkMsg($"stored {file.FileName}");
});

app.MapPost("/v1/import/python", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return ErrMsg("multipart expected");
    var form = await req.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null) return ErrMsg("file required");
    Directory.CreateDirectory("python");
    var path = Path.Combine("python", file.FileName);
    await using (var fs = File.Create(path))
        await file.CopyToAsync(fs);
    return OkMsg($"stored {file.FileName}");
});

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

static object VmStats() => new
{
    machine_name = Environment.MachineName,
    os = Environment.OSVersion.ToString(),
    processors = Environment.ProcessorCount,
    working_set = Environment.WorkingSet,
    dotnet = Environment.Version.ToString()
};

static async Task<string> ReadDfsBody(HttpRequest req)
{
    if (req.HasFormContentType)
    {
        var form = await req.ReadFormAsync();
        if (!string.IsNullOrEmpty(form["dfs"])) return form["dfs"].ToString();
        if (!string.IsNullOrEmpty(form["data"])) return form["data"].ToString();
    }
    using var reader = new StreamReader(req.Body);
    return await reader.ReadToEndAsync();
}

public partial class Program;
