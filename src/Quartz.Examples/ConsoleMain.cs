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
			
				IDictionary typeMap = new Hashtable();
				int counter = 1;
			
				Console.WriteLine("Select example to run: ");
                ArrayList typeList = new ArrayList();
				foreach (Type t in types)
				{
					if (new ArrayList(t.GetInterfaces()).Contains(typeof(IExample)))
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

	    public class TypeNameComparer : IComparer
	    {
	        public int Compare(object x, object y)
	        {
	            Type t1 = (Type) x;
	            Type t2 = (Type) y;
                if (t1.Namespace.Length < t2.Namespace.Length)
                {
                    return -1;
                }

                return t1.Namespace.CompareTo(t2.Namespace);
	        }
	    }
	}
}
