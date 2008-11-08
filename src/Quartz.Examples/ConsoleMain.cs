using System;
using System.Collections.Generic;
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
			
				IDictionary<int, Type> typeMap = new Dictionary<int, Type>();
				int counter = 1;
			
				Console.WriteLine("Select example to run: ");
                List<Type> typeList = new List<Type>();
				foreach (Type t in types)
				{
					if (new List<Type>(t.GetInterfaces()).Contains(typeof(IExample)))
					{
					    typeList.Add(t);
					}
				}

                // sort for easier readability
                typeList.Sort(new TypeNameComparer());

			    foreach (Type t in typeList)
			    {
                    string counterString = string.Format("[{0}]", counter).PadRight(4);
                    Console.WriteLine("{0} {1} {2}", counterString, t.Namespace.Substring(t.Namespace.LastIndexOf(".") + 1), t.Name);
                    typeMap.Add(counter++, t);
                }

				Console.WriteLine();
				Console.Write("> ");
				int num = Convert.ToInt32(Console.ReadLine());
				Type eType = typeMap[num];
				IExample example = ObjectUtils.InstantiateType<IExample>(eType);
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

	    public class TypeNameComparer : IComparer<Type>
	    {
	        public int Compare(Type t1, Type t2)
	        {
	            if (t1.Namespace.Length > t2.Namespace.Length)
                {
                    return 1;
                }
	            
                if (t1.Namespace.Length < t2.Namespace.Length)
	            {
	                return -1;
	            }
	            
                return t1.Namespace.CompareTo(t2.Namespace);
	        }
	    }
	}
}
