using Faxe.Core;
using Faxe.Core.Data;
using Faxe.Dfs;
using Faxe.Flow;
using Faxe.Nodes.Components;

namespace Faxe.Tests;

public class DfsCompilerTests
{
    private static DfsCompiler CreateCompiler()
    {
        var reg = new NodeRegistry();
        reg.RegisterAssembly(typeof(ValueEmitterNode).Assembly);
        return new DfsCompiler(reg.Names);
    }

    [Fact]
    public void Compiles_value_emitter_pipeline()
    {
        var dfs = """
            |value_emitter()
            .every(3s)
            .mode('monotonic_int')
            .type(point)
            |default()
            .fields('some')
            .field_values('value')
            |delete().fields('wasined')
            |debug()
            """;

        var (ok, err, graph) = CreateCompiler().TryCompile(dfs);
        Assert.True(ok, err);
        Assert.NotNull(graph);
        Assert.Equal(4, graph!.Nodes.Count);
        Assert.Equal(3, graph.Edges.Count);
        Assert.Equal("value_emitter", graph.Nodes[0].Type);
        Assert.Equal("3s", graph.Nodes[0].Options["every"]);
        Assert.Equal("monotonic_int", graph.Nodes[0].Options["mode"]);
    }

    [Fact]
    public void Compiles_where_lambda()
    {
        var dfs = """
            def in =
                |value_emitter()
                .every(300ms)
                .type(point)
                |where()
                .lambda(lambda: "val" > 3)
                |debug()
            """;
        var (ok, err, graph) = CreateCompiler().TryCompile(dfs);
        Assert.True(ok, err);
        Assert.Contains(graph!.Nodes, n => n.Type == "where");
        var where = graph.Nodes.First(n => n.Type == "where");
        Assert.IsType<LambdaExpression>(where.Options["lambda"]);
    }

    [Fact]
    public void Rejects_unknown_node()
    {
        var (ok, err, _) = CreateCompiler().TryCompile("|totally_unknown_xyz()|debug()");
        Assert.False(ok);
        Assert.Contains("totally_unknown_xyz", err);
    }
}

public class FlowDataTests
{
    [Fact]
    public void Nested_path_get_set_delete()
    {
        var p = new DataPoint { Ts = FaxeTime.Now() };
        FlowData.Set(p, "axis.z.cur", 42);
        Assert.Equal(42, Convert.ToInt32(FlowData.Get(p, "axis.z.cur")));
        FlowData.Delete(p, "axis.z.cur");
        Assert.Null(FlowData.Get(p, "axis.z.cur"));
    }
}

public class LambdaTests
{
    [Fact]
    public void Evaluates_comparison_against_field()
    {
        var p = new DataPoint { Ts = 1, Fields = { ["val"] = 5.0 } };
        Assert.True(LambdaEval.ExecuteBool(p, "\"val\" > 3"));
        Assert.False(LambdaEval.ExecuteBool(p, "\"val\" > 10"));
    }

    [Fact]
    public void Evaluates_round_and_arithmetic()
    {
        var p = new DataPoint { Ts = 1, Fields = { ["val"] = 1.234 } };
        var v = LambdaEval.Execute(p, "round(\"val\" * 1000)");
        Assert.Equal(1234.0, Convert.ToDouble(v));
    }
}

public class FlowRuntimeTests
{
    [Fact]
    public async Task Runs_value_emitter_to_debug()
    {
        var reg = new NodeRegistry();
        reg.RegisterAssembly(typeof(ValueEmitterNode).Assembly);
        var compiler = new DfsCompiler(reg.Names);
        var graphDef = compiler.Compile("""
            |value_emitter().every(50ms).mode('monotonic_int').type(point)|debug()
            """);
        var runtime = new FlowRuntime(reg);
        var task = new Core.Models.TaskRecord
        {
            Name = "t1",
            Dfs = "x",
            Definition = graphDef
        };
        await runtime.StartAsync(task);
        await Task.Delay(200);
        Assert.True(runtime.IsRunning("t1"));
        await runtime.StopAsync(task);
        Assert.False(runtime.IsRunning("t1"));
    }
}
