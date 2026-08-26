using Faxe.Core;
using Faxe.Core.Models;
using Faxe.Dfs;
using Faxe.Flow;
using Faxe.Persistence;

namespace Faxe.Api.Services;

/// <summary>Application façade for task/template lifecycle and graph control.</summary>
public sealed class FaxeService
{
    private readonly FaxeStore _store;
    private readonly FlowRuntime _runtime;
    private readonly DfsCompiler _compiler;

    public FaxeService(FaxeStore store, FlowRuntime runtime, DfsCompiler compiler)
    {
        _store = store;
        _runtime = runtime;
        _compiler = compiler;
    }

    public FlowRuntime Runtime => _runtime;
    public FaxeStore Store => _store;
    public DfsCompiler Compiler => _compiler;

    public object TaskToMap(TaskRecord t, bool full = true)
    {
        t.IsRunning = _runtime.IsRunning(t.Name);
        var map = new Dictionary<string, object?>
        {
            ["id"] = t.Id,
            ["name"] = t.Name,
            ["running"] = t.IsRunning,
            ["permanent"] = t.Permanent,
            ["changed"] = FaxeTime.ToIso8601(t.Changed),
            ["last_start"] = FaxeTime.ToIso8601(t.LastStart),
            ["last_stop"] = FaxeTime.ToIso8601(t.LastStop),
            ["tags"] = t.Tags
        };
        if (!string.IsNullOrEmpty(t.Template))
        {
            map["template"] = t.Template;
            map["template_vars"] = t.TemplateVars;
        }
        if (t.Group is not null)
        {
            map["group"] = t.Group;
            map["group_leader"] = t.GroupLeader;
        }
        if (full)
            map["dfs"] = t.Dfs;
        return map;
    }

    public object TemplateToMap(TemplateRecord t) => new
    {
        id = t.Id,
        name = t.Name,
        changed = FaxeTime.ToIso8601(t.Changed),
        dfs = t.Dfs,
        vars = t.Vars
    };

    public (TaskRecord? Task, string? Error) RegisterTask(string name, string dfs)
    {
        var (ok, err, graph) = _compiler.TryCompile(dfs);
        if (!ok || graph is null) return (null, err ?? "compile failed");
        if (_store.GetTask(name) is not null) return (null, $"task '{name}' already exists");
        var task = new TaskRecord
        {
            Name = name,
            Dfs = dfs,
            Definition = graph
        };
        return (_store.SaveTask(task), null);
    }

    public (TaskRecord? Task, string? Error) UpdateTask(string idOrName, string dfs)
    {
        var existing = _store.GetTask(idOrName);
        if (existing is null) return (null, "not_found");
        var (ok, err, graph) = _compiler.TryCompile(dfs);
        if (!ok || graph is null) return (null, err ?? "compile failed");
        var wasRunning = _runtime.IsRunning(existing.Name);
        if (wasRunning)
            _runtime.StopAsync(existing).GetAwaiter().GetResult();
        existing.Dfs = dfs;
        existing.Definition = graph;
        _store.SaveTask(existing);
        if (wasRunning)
            _runtime.StartAsync(existing).GetAwaiter().GetResult();
        return (existing, null);
    }

    public async Task<(bool Ok, string? Error)> StartTaskAsync(string idOrName, bool permanent = false)
    {
        var task = _store.GetTask(idOrName);
        if (task is null) return (false, "not_found");
        if (permanent)
        {
            task.Permanent = true;
            _store.SaveTask(task);
        }
        await _runtime.StartAsync(task);
        _store.SaveTask(task);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> StopTaskAsync(string idOrName, bool permanent = false)
    {
        var task = _store.GetTask(idOrName);
        if (task is null) return (false, "not_found");
        await _runtime.StopAsync(task);
        if (permanent) task.Permanent = false;
        _store.SaveTask(task);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteTaskAsync(string idOrName, bool force = false)
    {
        var task = _store.GetTask(idOrName);
        if (task is null) return (false, "not_found");
        if (_runtime.IsRunning(task.Name))
        {
            if (!force) return (false, "task is running");
            await _runtime.StopAsync(task);
        }
        _store.DeleteTask(idOrName);
        return (true, null);
    }

    public (TemplateRecord? Template, string? Error) RegisterTemplate(string name, string dfs)
    {
        var (ok, err, graph) = _compiler.TryCompile(dfs);
        if (!ok || graph is null) return (null, err ?? "compile failed");
        if (_store.GetTemplate(name) is not null) return (null, $"template '{name}' already exists");
        var t = new TemplateRecord { Name = name, Dfs = dfs, Definition = graph };
        return (_store.SaveTemplate(t), null);
    }

    public (TaskRecord? Task, string? Error) TaskFromTemplate(string templateId, string taskName, IReadOnlyDictionary<string, object?>? vars = null)
    {
        var tpl = _store.GetTemplate(templateId);
        if (tpl is null) return (null, "not_found");
        var (ok, err, graph) = _compiler.TryCompile(tpl.Dfs, vars);
        if (!ok || graph is null) return (null, err ?? "compile failed");
        var task = new TaskRecord
        {
            Name = taskName,
            Dfs = tpl.Dfs,
            Definition = graph,
            Template = tpl.Name,
            TemplateVars = vars is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(vars, StringComparer.Ordinal)
        };
        return (_store.SaveTask(task), null);
    }
}
