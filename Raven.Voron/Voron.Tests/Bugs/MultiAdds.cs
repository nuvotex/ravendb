﻿using System.Text;
using Xunit.Extensions;

namespace Voron.Tests.Bugs
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;

	using Voron.Debugging;
	using Voron.Impl;
	using Voron.Trees;

	using Xunit;

	public class MultiAdds
	{
		readonly Random _random = new Random(1234);

        private string RandomString(int size)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < size; i++)
            {
                builder.Append(Convert.ToChar(Convert.ToInt32(Math.Floor(26 * _random.NextDouble() + 65))));
            }

            return builder.ToString();
        }

		[Theory]
		[InlineData(0500)]
		[InlineData(1000)]
		[InlineData(2000)]
		[InlineData(3000)]
		[InlineData(4000)]
		[InlineData(5000)]
		public void MultiAdds_And_MultiDeletes_After_Causing_PageSplit_DoNot_Fail(int size)
		{
			using (var Env = new StorageEnvironment(StorageEnvironmentOptions.CreateMemoryOnly()))
			{
				var inputData = new List<byte[]>();
				for (int i = 0; i < size; i++)
				{
                    inputData.Add(Encoding.UTF8.GetBytes(RandomString(1024)));
				}

				using (var tx = Env.WriteTransaction())
				{
					tx.CreateTree( "foo");
					tx.Commit();
				}

				using (var tx = Env.WriteTransaction())
				{
					var tree = tx.CreateTree("foo");
					foreach (var buffer in inputData)
					{						
						Assert.DoesNotThrow(() => tree.MultiAdd("ChildTreeKey", new Slice(buffer)));
					}
					tx.Commit();
				}
				
				using (var tx = Env.WriteTransaction())
				{
					var tree = tx.CreateTree("foo");
					for (int i = 0; i < inputData.Count; i++)
					{
						var buffer = inputData[i];
						Assert.DoesNotThrow(() => tree.MultiDelete("ChildTreeKey", new Slice(buffer)));
					}

					tx.Commit();
				}
			}
		}

		[Fact]
		public void SplitterIssue()
		{
			const int DocumentCount = 10;

			using (var env = new StorageEnvironment(StorageEnvironmentOptions.CreateMemoryOnly()))
			{
				var rand = new Random();
				var testBuffer = new byte[168];
				rand.NextBytes(testBuffer);

				var multiTrees = CreateTrees(env, 1, "multitree");

				for (var i = 0; i < 50; i++)
				{
					AddMultiRecords(env, multiTrees, DocumentCount, true);

					ValidateMultiRecords(env, multiTrees, DocumentCount, i + 1);
				}
			}
		}

		[Fact]
		public void SplitterIssue2()
		{
			var storageEnvironmentOptions = StorageEnvironmentOptions.CreateMemoryOnly();
			storageEnvironmentOptions.ManualFlushing = true;
			using (var env = new StorageEnvironment(storageEnvironmentOptions))
			{
				using (var tx = env.WriteTransaction())
				{
					tx.CreateTree(  "multi");
					tx.Commit();
				}

                using (var tx = env.WriteTransaction())
                {
                   var batch = tx.CreateTree("multi");

                    batch.MultiAdd("0", "1");
                    batch.MultiAdd("1", "1");
                    batch.MultiAdd("2", "1");
                    batch.MultiAdd("3", "1");
                    batch.MultiAdd("4", "1");
                    batch.MultiAdd("5", "1");

                    tx.Commit();
                }


				using (var tx = env.ReadTransaction())
				{
					var tree = tx.CreateTree("multi");
					using (var iterator = tree.MultiRead("0"))
					{
						Assert.True(iterator.Seek(Slice.BeforeAllKeys));

						var count = 0;
						do
						{
							count++;
						} while (iterator.MoveNext());

						Assert.Equal(1, count);
					}
				}

                using (var tx = env.WriteTransaction())
                {
                    var batch = tx.CreateTree("multi");

                    batch.MultiAdd("0", "2");
                    batch.MultiAdd("1", "2");
                    batch.MultiAdd("2", "2");
                    batch.MultiAdd("3", "2");
                    batch.MultiAdd("4", "2");
                    batch.MultiAdd("5", "2");

                    tx.Commit();
                }



				using (var tx = env.ReadTransaction())
				{
					var tree = tx.CreateTree("multi");
					using (var iterator = tree.MultiRead("0"))
					{
						Assert.True(iterator.Seek(Slice.BeforeAllKeys));

						var count = 0;
						do
						{
							count++;
						} while (iterator.MoveNext());

						Assert.Equal(2, count);
					}
				}
			}
		}

		[Fact]
		public void CanAddMultiValuesUnderTheSameKeyToBatch()
		{
			using (var env = new StorageEnvironment(StorageEnvironmentOptions.CreateMemoryOnly()))
			{
				var rand = new Random();
				var testBuffer = new byte[168];
				rand.NextBytes(testBuffer);

				CreateTrees(env, 1, "multitree");

                using (var tx = env.WriteTransaction())
                {
                    var batch = tx.CreateTree("multitree0");

                    batch.MultiAdd("key", "value1");
                    batch.MultiAdd("key", "value2");

                    tx.Commit();
                }


				using (var tx = env.ReadTransaction())
				{
					var tree = tx.CreateTree("multitree0");
					using (var it = tree.MultiRead("key"))
					{
						Assert.True(it.Seek(Slice.BeforeAllKeys));

						Assert.Equal("value1", it.CurrentKey.ToString());
						Assert.True(it.MoveNext());

						Assert.Equal("value2", it.CurrentKey.ToString());
					}
				}
			}
		}

		private void ValidateMultiRecords(StorageEnvironment env, IEnumerable<string> trees, int documentCount, int i)
		{
			using (var tx = env.ReadTransaction())
			{
				for (var j = 0; j < 10; j++)
				{
					
					foreach (var treeName in trees)
					{
					    var tree = tx.CreateTree(treeName);
						using (var iterator = tree.MultiRead((j % 10).ToString()))
						{
							Assert.True(iterator.Seek(Slice.BeforeAllKeys));

							var count = 0;
							do
							{
								count++;
							}
							while (iterator.MoveNext());

							Assert.Equal((i * documentCount) / 10, count);
						}
					}
				}
			}
		}
		private void AddMultiRecords(StorageEnvironment env, IList<string> trees, int documentCount, bool sequential)
		{
		    using (var tx = env.WriteTransaction())
		    {
                var key = Guid.NewGuid().ToString();

                for (int i = 0; i < documentCount; i++)
                {
                    foreach (var tree in trees)
                    {
                        var value = sequential ? string.Format("tree_{0}_record_{1}_key_{2}", tree, i, key) : Guid.NewGuid().ToString();

                        tx.CreateTree(tree).MultiAdd((i % 10).ToString(), value);
                    }
                }
            }

		}

		private IList<string> CreateTrees(StorageEnvironment env, int number, string prefix)
		{
			var results = new List<string>();

			using (var tx = env.WriteTransaction())
			{
				for (var i = 0; i < number; i++)
				{
					results.Add(tx.CreateTree( prefix + i).Name);
				}

				tx.Commit();
			}

			return results;
		}
	}
}
