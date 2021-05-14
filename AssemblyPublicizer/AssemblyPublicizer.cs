﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

/// <summary>
/// AssemblyPublicizer - A tool to create a copy of an assembly in 
/// which all members are public (types, methods, fields, getters
/// and setters of properties).  
/// 
/// Copyright(c) 2018 CabbageCrow
/// This library is free software; you can redistribute it and/or
/// modify it under the terms of the GNU Lesser General Public
/// License as published by the Free Software Foundation; either
/// version 2.1 of the License, or(at your option) any later version.
/// 
/// Overview:
/// https://tldrlegal.com/license/gnu-lesser-general-public-license-v2.1-(lgpl-2.1)
/// 
/// This library is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
///	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU
/// Lesser General Public License for more details.
/// 
/// You should have received a copy of the GNU Lesser General Public
/// License along with this library; if not, write to the Free Software
/// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
/// USA
/// </summary>

namespace CabbageCrow.AssemblyPublicizer
{
	/// <summary>
	/// Creates a copy of an assembly in which all members are public (types, methods, fields (Minus Events), getters and setters of properties).
	/// If you use the modified assembly as your reference and compile your dll with the option "Allow unsafe code" enabled, 
	/// you can access all private elements even when using the original assembly.
	/// Without "Allow unsafe code" you get an access violation exception during runtime when accessing private members except for types.  
	/// How to enable it: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/unsafe-compiler-option
	/// arg 0 / -i|--input:		Path to the assembly (absolute or relative)
	/// arg 1 / -o|--output:	[Optional] Output path/filename
	///							Can be just a (relative) path like "subdir1\subdir2"
	///							Can be just a filename like "CustomFileName.dll"
	///							Can be a filename with path like "C:\subdir1\subdir2\CustomFileName.dll"
	/// </summary>
	class AssemblyPublicizer
	{
		static void Main(string[] args)
		{
			var suffix = "_publicized";
			Console.WriteLine(@"Info: Using suffix: " + suffix);
			var defaultOutputDir = "publicized_assemblies";

			var outputPath = Path.GetFullPath(defaultOutputDir);
			Console.WriteLine(@"Info: Writing output to: " + outputPath);

			foreach (string input in args)
			{
				if (Directory.Exists(input))
				{
					foreach (var file in Directory.GetFiles(input, "*.dll", SearchOption.AllDirectories))
					{
						ProcessAssembly(file, suffix, outputPath);
					}
				}
				else
				{
					ProcessAssembly(input, suffix, outputPath);
				}
			}

			Console.WriteLine("Completed.");
			Console.WriteLine();
			Console.WriteLine("Use the publicized library as your reference and compile your dll with the ");
			Console.WriteLine(@"option ""Allow unsafe code"" enabled.");
			Console.WriteLine(@"Without it you get an access violation exception during runtime when accessing");
			Console.WriteLine("private members except for types.");

			Exit(0);
		}

		private static void ProcessAssembly(string input, string suffix, string outputPath)
		{
			var assName = Path.GetFileNameWithoutExtension(input);
			var outputName = assName + suffix + Path.GetExtension(input);
			Console.WriteLine($"Info: Processing Assembly {assName}...");

			AssemblyDefinition assembly = null;

			if (!File.Exists(input))
			{
				Console.WriteLine();
				Console.WriteLine("ERROR! File doesn't exist or you don't have sufficient permissions.");
				Exit(30);
			}

			try
			{
				assembly = AssemblyDefinition.ReadAssembly((string)input);
			}
			catch (Exception)
			{
				Console.WriteLine();
				Console.WriteLine("ERROR! Cannot read the assembly. Please check your permissions.");
				Exit(40);
			}


			var allTypes = GetAllTypes(assembly.MainModule);
			var allMethods = allTypes.SelectMany(t => t.Methods);

			var allFields = FilterBackingEventFields(allTypes);

			int count;
			string reportString = "Changed {0} {1} to public.";

			#region Make everything public

			count = 0;
			foreach (var type in allTypes)
			{
				if (!type?.IsPublic ?? false && !type.IsNestedPublic)
				{
					count++;
					if (type.IsNested)
						type.IsNestedPublic = true;
					else
						type.IsPublic = true;
				}
			}

			Console.WriteLine(reportString, count, "types");

			count = 0;
			foreach (var method in allMethods)
			{
				if (!method?.IsPublic ?? false)
				{
					count++;
					method.IsPublic = true;
				}
			}

			Console.WriteLine(reportString, count, "methods (including getters and setters)");

			count = 0;
			foreach (var field in allFields)
			{
				if (!field?.IsPublic ?? false)
				{
					count++;
					field.IsPublic = true;
				}
			}

			Console.WriteLine(reportString, count, "fields");

			#endregion


			Console.WriteLine();

			Console.Write("Saving a copy of the modified assembly ...");

			var outputFile = Path.Combine(outputPath, outputName);

			try
			{
				if (outputPath != "" && !Directory.Exists(outputPath))
					Directory.CreateDirectory(outputPath);
				assembly.Write(outputFile);
			}
			catch (Exception)
			{
				Console.WriteLine();
				Console.WriteLine("ERROR! Cannot create/overwrite the new assembly. ");
				Console.WriteLine("Please check the path and its permissions " +
								  "and in case of overwriting an existing file ensure that it isn't currently used.");
				Exit(50);
			}

			Console.WriteLine(" OK");
			Console.WriteLine();
		}

		public static void Exit(int exitCode = 0)
		{
			Console.WriteLine();
			Console.WriteLine("Press any key to exit ...");
			Console.ReadKey();

			Environment.Exit(exitCode);
		}

		public static IEnumerable<FieldDefinition> FilterBackingEventFields(IEnumerable<TypeDefinition> allTypes)
		{
			List<string> eventNames = allTypes.SelectMany(t => t.Events).Select(eventDefinition => eventDefinition.Name).ToList();

			return allTypes.SelectMany(x => x.Fields).Where(fieldDefinition => !eventNames.Contains(fieldDefinition.Name));
		}

		/// <summary>
		/// Method which returns all Types of the given module, including nested ones (recursively)
		/// </summary>
		/// <param name="moduleDefinition"></param>
		/// <returns></returns>
		public static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition moduleDefinition)
		{
			return _GetAllNestedTypes(moduleDefinition.Types);//.Reverse();
		}

		/// <summary>
		/// Recursive method to get all nested types. Use <see cref="GetAllTypes(ModuleDefinition)"/>
		/// </summary>
		/// <param name="typeDefinitions"></param>
		/// <returns></returns>
		private static IEnumerable<TypeDefinition> _GetAllNestedTypes(IEnumerable<TypeDefinition> typeDefinitions)
		{
			//return typeDefinitions.SelectMany(t => t.NestedTypes);

			if (typeDefinitions?.Count() == 0)
				return new List<TypeDefinition>();

			var result = typeDefinitions.Concat(_GetAllNestedTypes(typeDefinitions.SelectMany(t => t.NestedTypes)));

			return result;
		}


	}
}
