using BenchmarkDotNet.Running;

// Run all parser-comparison benchmarks (pass a --filter to narrow). Mirrors
// bench/Starling.JsEngineBench/Program.cs.
BenchmarkSwitcher.FromAssembly(typeof(Starling.HtmlParserBench.HtmlParserComparisonBench).Assembly).Run(args);
