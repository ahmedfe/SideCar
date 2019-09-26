﻿// Copyright (c) Daniel Crenna & Contributors. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SideCar.Configuration;
using SideCar.Extensions;
using SideCar.Models;

namespace SideCar.DataAccess
{
	public class PhysicalFilePackageCompiler : IPackageCompiler
	{
		private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

		private readonly IOptionsSnapshot<SideCarOptions> _options;
		private readonly ILogger<PhysicalFilePackageCompiler> _logger;

		public PhysicalFilePackageCompiler(IOptionsSnapshot<SideCarOptions> options, ILogger<PhysicalFilePackageCompiler> logger)
		{
			_options = options;
			_logger = logger;
		}

		public async Task<PackageResult> CompilePackageAsync(Assembly assembly, string buildHash, CancellationToken cancellationToken = default)
		{
			await Semaphore.WaitAsync(cancellationToken);

			try
			{
				var packageHash = assembly.ComputePackageHash(buildHash);
				var sdkDir = Path.GetFullPath(Path.Combine(_options.Value.BuildLocation, $"mono-wasm-{buildHash}"));
				var assemblyLocation = assembly.Location;

				var assemblyDir = Path.GetDirectoryName(assemblyLocation);
                if(string.IsNullOrWhiteSpace(assemblyDir))
                    throw new DirectoryNotFoundException(assemblyDir);

				var outputDir = Path.GetFullPath(Path.Combine(_options.Value.PackageLocation, $"mono-wasm-{packageHash}"));
				Directory.CreateDirectory(outputDir);
				Directory.CreateDirectory(_options.Value.PackageLocation);

				var sb = new StringBuilder();
				sb.Append($" --search-path=\"{assemblyDir}\""); // Add specified path 'x' to list of paths used to resolve assemblies
				sb.Append($" --mono-sdkdir=\"{sdkDir}\"");      // Set the mono sdk directory to 'x'
				sb.Append($" --copy=always");                   // Set the type of copy to perform (always|ifnewest)
				sb.Append($" --out=\"{outputDir}\"");           // Set the output directory to 'x' (default to the current directory)
				sb.Append($" \"{assemblyLocation}\"");          // Include {target}.dll as one of the root assemblies

                ShadowCopyReferencesRecursive(assembly, assemblyDir);

				var args = sb.ToString();

				var fileName = Path.Combine(sdkDir, "packager.exe");
				var process = new Process
				{
					StartInfo = new ProcessStartInfo(fileName, args)
					{
						UseShellExecute = false,
						WorkingDirectory = "",
						RedirectStandardError = true,
						RedirectStandardOutput = true
					}
				};

				_logger?.LogDebug("Compiling package {PackageHash}", packageHash);
				_logger?.LogDebug("-------------------------------", packageHash);
				_logger?.LogDebug("SDK Location: {SdkDir}", sdkDir);
				_logger?.LogDebug("Assembly Location: {AssemblyLocation}", assemblyLocation);
				_logger?.LogDebug("Output Location: {OutputLocation}", outputDir);
				_logger?.LogDebug("-------------------------------", packageHash);
				_logger?.LogDebug("{Command}", $"packager.exe {args}");

				process.Start();

				var output = await process.StandardOutput.ReadToEndAsync();
				var errors = await process.StandardError.ReadToEndAsync();

				if (!string.IsNullOrWhiteSpace(errors))
					_logger?.LogWarning(errors);

				if (!string.IsNullOrWhiteSpace(output))
					_logger?.LogDebug(output);

				var result = new PackageResult
				{
					Successful = true,
					Errors = errors,
					Output = output
				};

				return result;
			}
			finally
			{
				Semaphore.Release();
			}
		}

		private static void ShadowCopyReferencesRecursive(Assembly assembly, string assemblyDir)
		{
			foreach (var assemblyRef in assembly.GetReferencedAssemblies())
			{
				if (assemblyRef.Name == "netstandard")
					continue;
				var reference = Assembly.Load(assemblyRef);

				var referenceInfo = new FileInfo(reference.Location);
				var targetFile = Path.Combine(assemblyDir, Path.GetFileName(reference.Location));
				var targetFileInfo = new FileInfo(targetFile);
				if (!targetFileInfo.Exists || targetFileInfo.LastWriteTimeUtc < referenceInfo.LastWriteTimeUtc)
					File.Copy(reference.Location, targetFile, true);

				ShadowCopyReferencesRecursive(reference, assemblyDir);
			}
		}
	}
}