
using Faxe.Dfs;
using Faxe.Flow;
using Faxe.Nodes.Components;

var reg = new NodeRegistry();
reg.RegisterAssembly(typeof(ValueEmitterNode).Assembly);
var compiler = new DfsCompiler(reg.Names);
var root = args[0];
var files = Directory.GetFiles(root, "*.dfs", SearchOption.AllDirectories);
int ok=0, fail=0;
var failures = new List<string>();
foreach (var f in files.OrderBy(x=>x)) {
  var dfs = File.ReadAllText(f);
  var (success, err, graph) = compiler.TryCompile(dfs);
  if (success) { ok++; Console.WriteLine("OK  " + Path.GetRelativePath(root,f) + $" nodes={graph!.Nodes.Count}"); }
  else { fail++; failures.Add(Path.GetRelativePath(root,f) + ": " + err); Console.WriteLine("FAIL " + Path.GetRelativePath(root,f) + ": " + err); }
}
Console.WriteLine($"SUMMARY ok={ok} fail={fail} total={ok+fail}");
foreach (var x in failures.Take(30)) Console.WriteLine("  " + x);
