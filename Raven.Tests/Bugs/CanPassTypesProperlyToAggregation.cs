using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Raven.Client.Document;
using Raven.Client.Indexes;
using Raven.Database.Indexing;
using Xunit;

namespace Raven.Tests.Bugs
{
	public class CanPassTypesProperlyToAggregation 
	{
		[Fact]
		public void WillGenerateDecimalCast()
		{
			Expression<Func<IEnumerable<Coin>, IEnumerable<object>>> query = x => from y in x
			                                                                      group y by y.Denomination
			                                                                      into g
			                                                                      select
			                                                                      	new
			                                                                      	{
			                                                                      		Denomination = g.Key,
			                                                                      		Cost = g.Sum(z => z.Cost)
			                                                                      	};


			var code = IndexDefinitionHelper.PruneToFailureLinqQueryAsStringToWorkableCode(query, new DocumentConvention(), "docs", false);

			Assert.Equal(@"docs
	.GroupBy(y => y.Denomination)
	.Select(g => new {Denomination = g.Key, Cost = g.Sum(z => (double)z.Cost)})", code);
		} 
	}

	public class Coin
	{
		public int Denomination { get; set; }
		public double Cost { get; set; }
	}
}