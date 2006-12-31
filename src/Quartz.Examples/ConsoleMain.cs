using System;
using System.Collections;
using System.Reflection;
using Quartz.Util;

namespace Quartz.Examples
{
	/// <summary>
	/// Console main runner.
	/// </summary>
	public class ConsoleMain
	{
		[STAThread]
		public static void Main(string[] args)
		{
			try
			{
				Assembly asm = Assembly.GetExecutingAssembly();
				Type[] types = asm.GetTypes();
			
				Hashtable typeMap = new Hashtable();
				int counter = 1;
			
				Console.WriteLine("Select example to run: ");
				foreach (Type t in types)
				{
					if (new ArrayList(t.GetInterfaces()).Contains(typeof(IExample)))
					{
						Console.WriteLine("[" + counter + "] " + t.Name);
						typeMap.Add(counter++, t);
					}
				}
				Console.WriteLine();
				Console.Write("> ");
				int num = Convert.ToInt32(Console.ReadLine());
				Type eType = (Type) typeMap[num];
				IExample example = (IExample) ObjectUtils.InstantiateType(eType);
				example.Run();
				Console.WriteLine("Example run successfully.");
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error running example: " + ex.Message);
				Console.WriteLine(ex.ToString());
				
			}
			Console.Read();
		}	
	}
}
