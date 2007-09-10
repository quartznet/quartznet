using System;
using System.Collections;

using MbUnit.Framework;

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
				Assert.AreEqual(col1[i], col2[i], string.Format("Collection items differ at index {0}: {1} vs {2}", i, col1[i], col2[i]));
			}
		}
	}
}
