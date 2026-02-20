// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Reflection;

namespace A2v10.Xaml.SchemaGenerator;

internal static class Program
{
	static int Main(string[] args)
	{
		if (args.Length < 1)
		{
			Console.Error.WriteLine(
				"Usage: A2v10.Xaml.SchemaGenerator <path-to-dll> [output-json-path]");
			return 1;
		}

		string dllPath = Path.GetFullPath(args[0]);
		string outputPath = args.Length >= 2
			? Path.GetFullPath(args[1])
			: Path.Combine(Directory.GetCurrentDirectory(), "a2v10-xaml-schema.json");

		if (!File.Exists(dllPath))
		{
			Console.Error.WriteLine($"Assembly not found: {dllPath}");
			return 1;
		}

		try
		{
			string dllDir = Path.GetDirectoryName(dllPath)!;
			AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
			{
				var name = new AssemblyName(resolveArgs.Name);
				string candidate = Path.Combine(dllDir, name.Name + ".dll");
				if (File.Exists(candidate))
					return Assembly.LoadFrom(candidate);
				return null;
			};

			Console.WriteLine($"Loading assembly: {dllPath}");
			var assembly = Assembly.LoadFrom(dllPath);
			Console.WriteLine(
				$"Loaded: {assembly.GetName().Name} v{assembly.GetName().Version}");

			var generator = new SchemaGenerator(assembly);
			string json = generator.Generate();

			string? outputDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
				Directory.CreateDirectory(outputDir);

			File.WriteAllText(outputPath, json);
			Console.WriteLine($"Schema written to: {outputPath}");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			Console.Error.WriteLine(ex.StackTrace);
			return 1;
		}
	}
}
