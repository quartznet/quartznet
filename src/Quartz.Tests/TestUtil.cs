using System;
using System.Collections;

using NUnit.Framework;

namespace Quartz.Tests
{
	/// <summary>
	/// Utility class for tests.
	/// </summary>
	public class TestUtil
	{
		private TestUtil()
		{
		}

		public static void AssertCollectionEquality(IList col1, IList col2)
		{
			Assert.AreEqual(col1.Count, col2.Count, "Collection sizes differ");
			for (int i = 0; i < col1.Count; ++i)
			{
				Assert.AreEqual(col1[i], col2[i], "Collection items differ at index " + i + ": " + col1[i] + " vs " + col2[i]);
			}
		}
	}
}
