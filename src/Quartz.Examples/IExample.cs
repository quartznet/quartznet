using System;

namespace Quartz.Examples
{
	/// <summary>
	/// Interface for examples.
	/// </summary>
	public interface IExample
	{
		string Name
		{
			get;
		}
		
		void Run();
	}
}
